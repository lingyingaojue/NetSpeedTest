using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using NetSpeedTest.Models;

namespace NetSpeedTest.Services;

/// <summary>
/// 核心测速引擎（下载/上传/Ping/一键测速 + 多URL并发）
/// </summary>
public class SpeedTestService
{
    private readonly HttpClient _httpClient;
    private readonly NetworkInfoService _networkInfo;
    private readonly SpeedTestOptions _options;
    private readonly ConcurrentDictionary<string, IPAddress[]> _dnsCache = new();

    public SpeedTestService(HttpClient httpClient, NetworkInfoService networkInfo, SpeedTestOptions options)
    {
        _httpClient = httpClient;
        _networkInfo = networkInfo;
        _options = options;
    }

    /// <summary>
    /// 多 URL 并发下载测速
    /// </summary>
    /// <param name="urls">要测速的 URL 列表</param>
    /// <param name="threadCount">并发线程数 (1~256)</param>
    /// <param name="adapterName">网卡名称</param>
    /// <param name="profileName">配置名称</param>
    /// <param name="gateway">网关 IP</param>
    /// <param name="adapterId">网卡 ID（用于采集系统级速率）</param>
    /// <param name="onUrlProgress">单 URL 进度回调</param>
    /// <param name="onDownloadProgress">下载/网卡接收速率回调 (seconds, rateMbps, totalBytes)</param>
    /// <param name="onUploadProgress">上传/网卡发送速率回调 (seconds, rateMbps, totalBytes)</param>
    /// <param name="onActiveThreadCount">活跃线程数回调</param>
    /// <param name="onLatency">内网延迟回调</param>
    /// <param name="onWanLatency">外网延迟回调</param>
    /// <param name="onAverageSpeed">10秒后平均网速回调</param>
    /// <param name="ct">取消令牌</param>
    public async Task<SpeedTestResult> RunMultiUrlTestAsync(
        List<string> urls,
        int threadCount,
        List<NetworkAdapterInfo> adapters,
        string profileName,
        string? gateway = null,
        Action<string, string, double, double, long>? onUrlProgress = null,
        Action<double, double, long>? onDownloadProgress = null,
        Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null,
        Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null,
        Action<double>? onWanLatency = null,
        Action<double>? onJitter = null,
        Action<double>? onAverageSpeed = null,
        Action<double>? onAverageDownload = null,
        Action<double>? onAverageUpload = null,
        Action<double>? onAverageTotal = null,
        Action<long>? onTotalBytes = null,
        Action<PacketLossSample>? onPacketLoss = null,
        int adaptiveThreadCap = 0,
        CancellationToken ct = default,
        HttpClient? client = null)
    {
        if (urls == null || urls.Count == 0)
            throw new ArgumentException("URL 列表不能为空");
        if (adapters == null || adapters.Count == 0)
            throw new ArgumentException("至少需要一个活跃网卡");

        threadCount = Math.Clamp(threadCount, 2, 1024);

        var overall = Stopwatch.StartNew();
        var urlDetails = new List<UrlTestDetail>();
        var allRateSamples = new List<double>();
        var globalLock = new object();
        int activeThreads = 0;
        var totalBytesDownloaded = new LongRef();
        var nicState = new NicState();

        // 内部取消令牌：方法返回时取消所有后台任务
        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;

        var adaptiveMaxBase = _options.AdaptiveThreadsEnabled
            ? (adaptiveThreadCap > 0 ? Math.Max(8, adaptiveThreadCap) : Math.Max(8, GetAutomaticAdaptiveMax()))
            : 0;
        var useAdaptive = adaptiveMaxBase > 0;
        var workerCount = useAdaptive ? adaptiveMaxBase : threadCount;
        var startThreads = useAdaptive ? Math.Clamp(_options.AdaptiveStartThreads, 1, adaptiveMaxBase) : 0;
        AdaptiveController? adaptive = useAdaptive
            ? new AdaptiveController(adaptiveMaxBase, startThreads, _options.TestTimeoutSec, onActiveThreadCount)
            : null;

        // 信号量控制并发度
        using var semaphore = new SemaphoreSlim(workerCount, workerCount);

        var http = CreateEgressClient(adapters, client, out var isAdapterBound, out var ownsHttpClient);
        using var ownedClient = ownsHttpClient ? http : null;

        var nicMonitorTask = StartNicMonitor(overall, ctLinked, adapters, nicState,
            onDownloadProgress, onUploadProgress, onAdapterRates,
            onAverageDownload, onAverageUpload, onAverageTotal, onAverageSpeed,
            totalBytesDownloaded, onTotalBytes, tc: threadCount, adaptive: adaptive, throughputMode: 1);

        var sourceIp = isAdapterBound ? GetAdapterSourceIp(adapters) : null;
        var gwTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss, sourceIp, http);

        var urlBalancer = new UrlBalancer(urls, useFastestAfterProbe: true);

        // 单次下载迭代：先轮转探测所有 URL，之后自动选择最优 URL；超时/失败自动避让
        async Task RunOneDownloadAsync(int workerId, CancellationToken requestCt, CancellationToken globalCt)
        {
            var url = urlBalancer.GetUrlForWorker(workerId);
            var detail = new UrlTestDetail { Url = url, Host = GetHostFromUrl(url) };
            lock (globalLock) { urlDetails.Add(detail); }

            long prevBytes = 0;
            try
            {
                var result = await TestDownloadAsync(url,
                    (elapsed, rate, bytes) =>
                    {
                        long delta = bytes - prevBytes;
                        prevBytes = bytes;
                        if (delta > 0) Interlocked.Add(ref totalBytesDownloaded.Value, delta);
                        detail.BytesDownloaded = bytes;
                        detail.AvgMbps = rate;
                        detail.DurationSeconds = elapsed;
                        onUrlProgress?.Invoke(url, detail.Host, elapsed, rate, bytes);
                    },
                    requestCt, http);
                detail.AvgMbps = result.avgMbps;
                detail.PeakMbps = result.peakMbps;
                detail.BytesDownloaded = result.totalBytes;
                detail.DurationSeconds = result.duration;
                detail.RateHistory = result.history;
                lock (globalLock) { allRateSamples.AddRange(result.history.Select(p => p.RateMbps)); }
                urlBalancer.ReportSuccess(url, result.avgMbps, result.duration);
                Logger.Log($"[D-URL] nic={adapters[0].Name} url={url} ok={result.totalBytes}B {result.avgMbps:F2}Mbps");
            }
            catch (OperationCanceledException)
            {
                if (requestCt.IsCancellationRequested && !globalCt.IsCancellationRequested)
                {
                    detail.IsTrimmed = true;
                    return;
                }

                detail.IsFailed = true;
                detail.ErrorMessage = "URL 请求超时";
                Logger.Log($"[D-URL] nic={adapters[0].Name} url={url} TIMEOUT");
                urlBalancer.ReportTimeout(url);
                await Task.Delay(200, globalCt);
            }
            catch (Exception ex)
            {
                detail.IsFailed = true;
                detail.ErrorMessage = ex.Message;
                Logger.Log($"[D-URL] nic={adapters[0].Name} url={url} FAIL {ex.Message}");
                urlBalancer.ReportFailure(url);
                await Task.Delay(500, globalCt);
            }
        }

        // 线程池：固定模式每线程循环下载；自适应模式由控制器动态放行
        var tasks = new List<Task>(); var rampBatch = Math.Max(1, threadCount / 256);
        if (adaptive != null)
        {
            adaptive.StartWorkers(ctLinked, async (workerId, requestCt) =>
            {
                await RunOneDownloadAsync(workerId, requestCt, ctLinked);
            });
        }
        else
        {
            for (int i = 0; i < threadCount; i++)
            {
                var idx = i;
                if (ct.IsCancellationRequested) break;

                tasks.Add(Task.Run(async () =>
                {
                    try { await semaphore.WaitAsync(ctLinked); } catch { return; }
                    var current = Interlocked.Increment(ref activeThreads);
                    try
                    {
                        onActiveThreadCount?.Invoke(current);
                        while (!ctLinked.IsCancellationRequested)
                        {
                            try { await RunOneDownloadAsync(idx, ctLinked, ctLinked); }
                            catch (OperationCanceledException) { break; }
                        }
                    }
                    finally
                    {
                        try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                        current = Interlocked.Decrement(ref activeThreads);
                        onActiveThreadCount?.Invoke(current);
                    }
                }));

                if (_options.ThreadRampUpMs > 0 && (i + 1) % rampBatch == 0 && i + 1 < threadCount)
                {
                    try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        // 等待所有 URL 测速完成
        if (adaptive != null) { await adaptive.WaitAsync(); }

        await Task.WhenAll(tasks);

        overall.Stop();

        // 汇总结果
        var successful = urlDetails.Where(d => !d.IsFailed).ToList();
        var totalBytes = successful.Sum(d => d.BytesDownloaded);
        var (peakAggMbps, minAggMbps) = allRateSamples.Count > 0 ? (allRateSamples.Max(), allRateSamples.Min()) : (0, 0);

        // 去重：合并同URL多轮明细（线程循环可能多次访问同一URL）
        var dedupedDetails = urlDetails
            .GroupBy(d => d.Url)
            .Select(g =>
            {
                var considered = g.Where(d => !d.IsTrimmed).ToList();
                if (considered.Count == 0) return null;
                var succeeded = considered.Where(d => !d.IsFailed && d.BytesDownloaded > 0).ToList();
                var first = considered[0];
                return new UrlTestDetail
                {
                    Url = first.Url,
                    Host = first.Host,
                    AvgMbps = succeeded.Count > 0 ? succeeded.Average(d => d.AvgMbps) : 0,
                    PeakMbps = succeeded.Count > 0 ? succeeded.Max(d => d.PeakMbps) : 0,
                    BytesDownloaded = succeeded.Sum(d => d.BytesDownloaded),
                    DurationSeconds = succeeded.Count > 0 ? succeeded.Sum(d => d.DurationSeconds) : 0,
                    IsFailed = considered.All(d => d.IsFailed),
                    ErrorMessage = considered.FirstOrDefault(d => d.IsFailed)?.ErrorMessage
                };
            })
            .Where(d => d != null)
            .Select(d => d!)
            .ToList();

        // 停止所有后台报告任务
        internalCts.Cancel();
        await Task.WhenAll(gwTasks.gatewayTask ?? Task.CompletedTask, gwTasks.wanTask, gwTasks.jitterTask, gwTasks.lossTask, nicMonitorTask);

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

        // 网卡级平均速率
        var totalSec = Math.Max(overall.Elapsed.TotalSeconds, 0.1);
        double nicDlAvg, nicUlAvg;
        if (nicState.R)
        {
            var effSec = Math.Max(totalSec - _options.AverageDelaySec, 0.1);
            var dropSec = _options.CompensationEnabled ? nicState.TotalDropDuration : 0;
            var adjSec = Math.Max(effSec - dropSec, 0.1);
            nicDlAvg = Math.Max(0, (nicState.AR - nicState.BR) * 8.0 / (adjSec * 1_000_000.0));
            nicUlAvg = Math.Max(0, (nicState.AS - nicState.BS) * 8.0 / (adjSec * 1_000_000.0));
        }
        else
        {
            nicDlAvg = Math.Max(0, (nicState.AR - nicState.FR) * 8.0 / (totalSec * 1_000_000.0));
            nicUlAvg = Math.Max(0, (nicState.AS - nicState.FS) * 8.0 / (totalSec * 1_000_000.0));
        }

        return new SpeedTestResult
        {
            Timestamp = DateTime.Now,
            DownloadMbps = nicDlAvg,
            PeakMbps = peakAggMbps,
            UploadMbps = nicUlAvg,
            LatencyMs = 0,
            JitterMs = 0,
            PacketLoss = 0,
            NodeName = profileName,
            NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")),
            BytesDownloaded = totalBytes,
            BytesUploaded = 0,
            DurationSeconds = overall.Elapsed.TotalSeconds,
            ThreadCount = adaptive != null ? Math.Max(1, adaptive.Peak) : threadCount,
            UrlDetails = dedupedDetails
        };
    }

    /// <summary>
    /// 测速准备：DNS 预解析 + HTTP 连接预热
    /// </summary>
    public async Task PrepareUrlsAsync(List<string> urls, CancellationToken ct, Action<int, string> report)
    {
        if (urls.Count == 0) { report(100, ""); return; }

        report(10, "解析测速地址...");
        var hosts = urls.Select(GetHostFromUrl).Distinct().ToList();
        try
        {
            await Task.WhenAll(hosts.Select(h => Task.Run(async () =>
            {
                try { await Dns.GetHostAddressesAsync(h, ct); } catch { }
            }, ct)));
        }
        catch { }
        ct.ThrowIfCancellationRequested();
        report(45, $"{hosts.Count} 个地址已解析");

        report(50, "建立服务器连接...");
        try
        {
            await Task.WhenAll(urls.Select(async url =>
            {
                try
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts2.Token);
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    using var _ = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                }
                catch { }
            }));
        }
        catch { }
        ct.ThrowIfCancellationRequested();
        report(85, "连接已建立");

        report(95, "准备就绪");
        try { await Task.Delay(200, ct); } catch { }
        report(100, "");
    }

    // ========== 基础测速方法 ==========

    /// <summary>
    /// 从 URL 提取主机名
    /// </summary>
    private static string GetHostFromUrl(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    private static IPAddress? GetAdapterSourceIp(List<NetworkAdapterInfo> adapters)
    {
        if (adapters == null || adapters.Count == 0) return null;
        var ipText = adapters[0].IPAddress;
        return IPAddress.TryParse(ipText, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip : null;
    }


    private static IPAddress? ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try { return Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
        catch (Exception ex) { Logger.Log($"DNS resolve failed for {host}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// UDP 探测单次发送的等待上限（毫秒）。
    /// </summary>
    private const int UdpProbeTimeoutMs = 1000;

    /// <summary>
    /// UDP 探测结果。用于区分“主机有响应”与“真的无响应”。
    /// </summary>
    internal enum UdpProbeOutcome
    {
        /// <summary>在超时前收到返回数据报。</summary>
        Response = 0,

        /// <summary>
        /// 收到 ICMP Port Unreachable 导致连接被重置。对已连接 UDP socket 而言这是
        /// 主机可达的确定性证据，必须计为成功，否则会误判为 100% 丢包 / 无延迟。
        /// </summary>
        PortUnreachable = 1,

        /// <summary>超时且没有任何响应。</summary>
        Timeout = 2
    }

    /// <summary>
    /// 判定已完成的 UDP 接收任务的探测结果（无网络依赖，供单测覆盖）。
    /// </summary>
    internal static UdpProbeOutcome ClassifyUdpProbeResult(Task receiveTask)
    {
        if (receiveTask == null || !receiveTask.IsCompleted) return UdpProbeOutcome.Timeout;

        if (receiveTask.IsCompletedSuccessfully) return UdpProbeOutcome.Response;

        if (receiveTask.IsFaulted && receiveTask.Exception != null)
        {
            foreach (var inner in receiveTask.Exception.Flatten().InnerExceptions)
            {
                if (inner is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset)
                    return UdpProbeOutcome.PortUnreachable;
            }
        }

        return UdpProbeOutcome.Timeout;
    }

    /// <summary>
    /// 发送一次 UDP 探测并等待结果（数据报或 ICMP Port Unreachable），两者都算主机有响应。
    /// </summary>
    internal static async Task<UdpProbeOutcome> SendUdpProbeAsync(UdpClient udp, byte[] probe, int timeoutMs, CancellationToken ct)
    {
        await udp.SendAsync(probe, probe.Length);
        var receiveTask = udp.ReceiveAsync();
        using var probeCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, probeCts.Token);
        var winner = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
        if (winner != receiveTask)
        {
            // 超时：接收任务仍可能稍后因 ICMP 而失败，挂观察者避免未观察异常。
            _ = receiveTask.ContinueWith(static t => _ = t.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return UdpProbeOutcome.Timeout;
        }

        // 关键：必须 await 以观察结果。ICMP Port Unreachable 会以 ConnectionReset 形式抛出。
        try
        {
            await receiveTask;
            return UdpProbeOutcome.Response;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
        {
            return UdpProbeOutcome.PortUnreachable;
        }
        catch (OperationCanceledException)
        {
            return UdpProbeOutcome.Timeout;
        }
    }

    /// <summary>
    /// 延迟测试：UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 五层回退
    /// </summary>
    private async Task<double> TestGatewayLatencyAsync(string host, CancellationToken ct, IPAddress? ip = null, IPAddress? sourceIp = null, HttpClient? probeClient = null)
    {
        var latencies = new List<double>();

        // 第一层：UDP 探测（端口 33434，主机回 ICMP Port Unreachable）
        try
        {
            var ipv4 = ip ?? (await Dns.GetHostAddressesAsync(host, ct)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                using var udp = sourceIp != null ? new UdpClient(new IPEndPoint(sourceIp, 0)) : new UdpClient();
                udp.Connect(ipv4, 33434);
                udp.Client.SendTimeout = 1000;
                udp.Client.ReceiveTimeout = 1000;
                var probe = new byte[] { 0x00 };
                for (int i = 0; i < 5; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        var outcome = await SendUdpProbeAsync(udp, probe, UdpProbeTimeoutMs, ct);
                        // 数据报与 ICMP Port Unreachable 都证明主机可达，均应计入延迟。
                        if (outcome != UdpProbeOutcome.Timeout)
                            latencies.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                    if (ct.IsCancellationRequested) break;
                    if (i < 4) try { await Task.Delay(50, ct); } catch { break; }
                }
            }
        }
        catch { }
        if (latencies.Count > 0) { Logger.Log($"延迟({host}): UDP={latencies.Average():F1}ms"); return latencies.Average(); }

        // 第二层：ICMP Ping
        if (sourceIp == null)
        {
        const int count = 10;
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, 1000);
                if (reply.Status == IPStatus.Success && reply.RoundtripTime > 0)
                    latencies.Add(reply.RoundtripTime);
            }
            catch { }
            if (i < count - 1)
                try { await Task.Delay(100, ct); } catch { break; }
        }
        if (latencies.Count > 0) { Logger.Log($"延迟({host}): ICMP={latencies.Average():F1}ms"); return latencies.Average(); }
        }

        // 第三层：TCP 连接 443
        for (int i = 0; i < 5; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var tcp = sourceIp != null ? new TcpClient(new IPEndPoint(sourceIp, 0)) : new TcpClient();
                var sw = Stopwatch.StartNew();
                await tcp.ConnectAsync(host, 443, ct);
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch { }
            if (i < 4)
                try { await Task.Delay(200, ct); } catch { break; }
        }
        if (latencies.Count > 0) { Logger.Log($"延迟({host}): TCP443={latencies.Average():F1}ms"); return latencies.Average(); }

        // 第四层：HTTPS HEAD
        for (int i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, "https://" + host);
                var sw = Stopwatch.StartNew();
                using var resp = await (probeClient ?? _httpClient).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch { }
            if (i < 2)
                try { await Task.Delay(300, ct); } catch { break; }
        }
        if (latencies.Count > 0) { Logger.Log($"延迟({host}): HTTPS_HEAD={latencies.Average():F1}ms"); return latencies.Average(); }

        // 第五层：HTTP HEAD（网关/路由器端口 80）
        for (int i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, "http://" + host);
                var sw = Stopwatch.StartNew();
                using var resp = await (probeClient ?? _httpClient).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch { }
            if (i < 2)
                try { await Task.Delay(300, ct); } catch { break; }
        }
        var final = latencies.Count > 0 ? latencies.Average() : 0;
        Logger.Log(final > 0 ? $"延迟({host}): HTTP_HEAD={final:F1}ms" : $"延迟({host}): 四层全失败");
        return final;
    }

    /// <summary>
    /// 下载测速（流式读取，最少 5 秒，最多 60 秒自动停止）
    /// 返回：(avgMbps, peakMbps, totalBytes, duration, rateHistory)
    /// </summary>
    public async Task<(double avgMbps, double peakMbps, long totalBytes, double duration, List<RateDataPoint> history)>
        TestDownloadAsync(
            string url,
            Action<double, double, long>? onProgress = null,
            CancellationToken ct = default,
            HttpClient? client = null)
    {
        const int bufferSize = 64 * 1024;

        var stopwatch = Stopwatch.StartNew();
        long totalBytes = 0;
        var rateSamples = new List<double>();
        var history = new List<RateDataPoint>();

        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(TimeSpan.FromSeconds(10));
        // F-05：手动跟随重定向，逐跳校验目标为公网地址。
        using var response = await SendWithValidatedRedirectsAsync(
            client ?? _httpClient, url, TimeSpan.FromSeconds(10), headerCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            double lastReportTime = 0;
            long lastReportBytes = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromSeconds(15));
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), readCts.Token);
                if (bytesRead == 0)
                    break;

                totalBytes += bytesRead;
                var elapsed = stopwatch.Elapsed.TotalSeconds;

                if (elapsed - lastReportTime >= 0.2)
                {
                    var dur = elapsed - lastReportTime;
                    var bytes = totalBytes - lastReportBytes;
                    var rateMbps = (bytes * 8.0) / (dur * 1_000_000.0);

                    rateSamples.Add(rateMbps);
                    history.Add(new RateDataPoint { TimeSeconds = elapsed, RateMbps = rateMbps });

                    onProgress?.Invoke(elapsed, rateMbps, totalBytes);

                    lastReportTime = elapsed;
                    lastReportBytes = totalBytes;
                }
            }

            stopwatch.Stop();

            var avgMbps = rateSamples.Count > 0 ? rateSamples.Average() : 0;
            var peakMbps = rateSamples.Count > 0 ? rateSamples.Max() : 0;

            return (avgMbps, peakMbps, totalBytes, stopwatch.Elapsed.TotalSeconds, history);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private int GetAutomaticAdaptiveMax()
    {
        // 自适应线程硬上限：1024
        return 1024;
    }

    // ====== 共享监控方法 ======

    private sealed class NicState {
        public long FR, FS, AR, AS, BR, BS;
        public bool R;
        public volatile bool IsCompensating;
        public double PeakRate, BelowThresholdSec, DropStartTime;
        public long DropStartBytes;
        public double TotalDropDuration;
        public long TotalDropBytes;
    }

    /// <summary>
    /// URL 负载均衡器：探索阶段 worker 轮转覆盖全部 URL；全部探测后切换到最优 URL；不健康 URL 自动避让。
    /// </summary>
    internal sealed class UrlBalancer
    {
        private sealed class UrlHealth
        {
            public bool Attempted;
            public bool Assigned;
            public int Success;
            public int Fail;
            public int ConsecutiveFail;
            public int Timeouts;
            public double AvgMbps;
            public DateTime CooldownUntilUtc;
        }

        private readonly object _sync = new();
        private readonly List<string> _urls;
        private readonly Dictionary<string, UrlHealth> _health = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _nextUrlIndex = new();
        private readonly bool _useFastestAfterProbe;

        public UrlBalancer(IEnumerable<string> urls, bool useFastestAfterProbe)
        {
            _useFastestAfterProbe = useFastestAfterProbe;
            _urls = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var u in _urls) _health[u] = new UrlHealth();
        }

        public string GetUrlForWorker(int workerId)
        {
            lock (_sync)
            {
                if (_urls.Count == 0) return string.Empty;
                if (!_nextUrlIndex.TryGetValue(workerId, out var cursor))
                    cursor = Math.Abs(workerId) % _urls.Count;

                // 探索阶段：每个 URL 至少被测一次后再进入“选择最快节点”阶段。
                var allAttempted = _useFastestAfterProbe && _urls.All(u => _health[u].Attempted);
                if (_useFastestAfterProbe && !allAttempted)
                {
                    var unvisited = _urls.Where(u => !_health[u].Assigned).ToList();
                    if (unvisited.Count > 0)
                    {
                        var nextUnvisited = unvisited
                            .OrderBy(u => (_urls.IndexOf(u) - cursor + _urls.Count) % _urls.Count)
                            .First();
                        _health[nextUnvisited].Assigned = true;
                        _nextUrlIndex[workerId] = (cursor + 1) % _urls.Count;
                        return nextUnvisited;
                    }
                }

                // 每个 worker 每次取完后前进到下一条 URL，避免 worker 数少于 URL 数时某些 URL 永远分不到线程。
                var preferred = _urls[cursor % _urls.Count];
                _nextUrlIndex[workerId] = (cursor + 1) % _urls.Count;
                if (!allAttempted && IsHealthy(preferred)) return preferred;

                var healthy = _urls.Where(IsHealthy).ToList();
                if (healthy.Count > 0)
                {
                    return healthy.OrderByDescending(u => Score(u)).First();
                }

                return preferred;
            }
        }

        public void ReportSuccess(string url, double avgMbps, double duration)
        {
            lock (_sync)
            {
                if (!_health.TryGetValue(url, out var h)) return;
                h.Assigned = true;
                h.Attempted = true;
                h.Success++;
                h.ConsecutiveFail = 0;
                h.Timeouts = 0;
                h.AvgMbps = h.AvgMbps <= 0 ? avgMbps : h.AvgMbps * 0.7 + avgMbps * 0.3;
                h.CooldownUntilUtc = DateTime.MinValue;
            }
        }

        public void ReportFailure(string url)
        {
            lock (_sync)
            {
                if (!_health.TryGetValue(url, out var h)) return;
                h.Assigned = true;
                h.Attempted = true;
                h.Fail++;
                h.ConsecutiveFail++;
                if (h.ConsecutiveFail >= 2) h.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(10);
            }
        }

        public void ReportTimeout(string url)
        {
            lock (_sync)
            {
                if (!_health.TryGetValue(url, out var h)) return;
                h.Assigned = true;
                h.Attempted = true;
                h.Timeouts++;
                h.ConsecutiveFail++;
                h.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(10);
            }
        }

        private bool IsHealthy(string url)
        {
            if (!_health.TryGetValue(url, out var h)) return false;
            return DateTime.UtcNow >= h.CooldownUntilUtc;
        }

        private double Score(string url)
        {
            if (!_health.TryGetValue(url, out var h)) return -1;
            var failPenalty = h.Fail * 10 + h.Timeouts * 100;
            return h.AvgMbps * 10 - failPenalty + h.Success;
        }

        /// <summary>
        /// 本次测速的每个 URL 结果快照。
        /// 上传路径以字节计数器作为速率来源，本身没有逐 URL 速率，
        /// 但必须能看出哪些 URL 被服务器拒收或超时，否则状态会退化成“测速完成”。
        /// </summary>
        public List<UrlTestDetail> BuildDetails()
        {
            lock (_sync)
            {
                return _urls.Select(url =>
                {
                    var h = _health[url];
                    var failed = h.Assigned && h.Success == 0 && (h.Fail > 0 || h.Timeouts > 0);
                    return new UrlTestDetail
                    {
                        Url = url,
                        Host = GetHostFromUrl(url),
                        AvgMbps = h.AvgMbps,
                        PeakMbps = 0,
                        BytesDownloaded = 0,
                        DurationSeconds = 0,
                        IsFailed = failed,
                        ErrorMessage = failed
                            ? h.Timeouts > 0 && h.Fail == 0
                                ? $"Timeout x{h.Timeouts}"
                                : $"Rejected/failed x{h.Fail + h.Timeouts}"
                            : null
                    };
                }).ToList();
            }
        }
    }

    private sealed class LongRef { public long Value; }

    /// <summary>
    /// 自适应并发控制器：动态调整实际并发线程数。
    /// </summary>
    /// <summary>
    /// 自适应并发控制器：粗扫 + 二分精调 + 稳态微调，硬上限严格受 MaxBase 约束。
    /// </summary>
    /// <summary>
    /// 分级动态线程池控制器。
    /// 容量阶梯：128 / 256 / 512 / 1024（多网卡时按份额折算）。
    /// 实际线程每次 +2，脉冲间隔 100~2000ms 动态调整。
    /// </summary>
    internal sealed class AdaptiveController
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _wake;
        private readonly Action<int>? _onActive;
        private readonly Queue<int> _freeSlots = new();
        private readonly CancellationTokenSource?[] _slotCancel;
        private readonly List<int> _capacities = new();
        private readonly List<Task> _workers = new();
        private readonly Queue<double> _recentRates = new();
        private readonly int _testTimeoutSec;

        private Func<int, CancellationToken, Task>? _body;
        private CancellationTokenSource? _workerCts;
        private Task? _pulseTask;
        private int _workerSeq = -1;

        private int _current;
        private int _target;
        private int _peak;
        private int _capacity;
        private int _capacityIndex;
        private int _bestTarget = 1;
        private double _bestRate;
        private int _evalCount;
        private int _noGainCount;
        private int _declineCount;
        // F-07：PulseLoop 与 UI 观察线程并发访问，必须保证可见性。
        private volatile bool _increaseAllowed = true;
        private DateTime _nextCapacityEvalUtc;
        private DateTime _nextReevaluateUtc;
        private DateTime _cooldownUntilUtc;
        private double _pulseIntervalMs = 200;

        private int _lastReported = -1;
        private DateTime _lastReportUtc;

        public int MaxBase { get; }
        public int Current => Volatile.Read(ref _current);
        public int Peak => Volatile.Read(ref _peak);
        public int Target => Volatile.Read(ref _target);
        public int Capacity => Volatile.Read(ref _capacity);

        public AdaptiveController(int maxBase, int startThreads, int testTimeoutSec, Action<int>? onActive)
        {
            MaxBase = Math.Max(1, maxBase);
            _testTimeoutSec = Math.Max(10, testTimeoutSec);
            _wake = new SemaphoreSlim(0, MaxBase);
            _slotCancel = new CancellationTokenSource?[MaxBase];
            _onActive = onActive;

            foreach (var level in new[] { 128, 256, 512, 1024 })
            {
                if (level < MaxBase) _capacities.Add(level);
            }
            _capacities.Add(MaxBase);
            _capacities = _capacities.Distinct().OrderBy(x => x).ToList();
            _capacity = _capacities[0];
            _target = Math.Clamp(startThreads, 1, _capacity);
            _bestTarget = _target;
            for (var i = 0; i < MaxBase; i++) _freeSlots.Enqueue(i);
        }

        public void StartWorkers(CancellationToken global, Func<int, CancellationToken, Task> body)
        {
            _body = body;
            _workerCts = CancellationTokenSource.CreateLinkedTokenSource(global);
            StartWorkerBatch(_capacity);
            _pulseTask = Task.Run(() => PulseLoopAsync(_workerCts.Token));
        }

        public void Stop()
        {
            try { _workerCts?.Cancel(); } catch { }
        }

        public async Task WaitAsync()
        {
            if (_pulseTask != null)
            {
                try { await _pulseTask; } catch { }
            }

            Task[] snapshot;
            lock (_sync) { snapshot = _workers.ToArray(); }
            if (snapshot.Length > 0)
            {
                try { await Task.WhenAll(snapshot); } catch { }
            }
        }

        private void StartWorkerBatch(int count)
        {
            var token = _workerCts?.Token ?? CancellationToken.None;
            for (var i = 0; i < count; i++)
            {
                var workerId = Interlocked.Increment(ref _workerSeq);
                var task = Task.Run(() => WorkerLoopAsync(workerId, token));
                lock (_sync) { _workers.Add(task); }
            }
        }

        private async Task WorkerLoopAsync(int workerId, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                int slot;
                try { slot = await AcquireAsync(ct); }
                catch (OperationCanceledException) { break; }

                var slotCts = CreateSlotCancellation(slot, ct);
                try
                {
                    if (_body != null) await _body(workerId, slotCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) break;
                    // 槽位被主动裁剪：释放后回到等待队列
                    continue;
                }
                catch
                {
                    try { await Task.Delay(200, ct); } catch { break; }
                }
                finally
                {
                    slotCts.Dispose();
                    Release(slot);
                }
            }
        }

        private async Task PulseLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay((int)Math.Clamp(_pulseIntervalMs, 100, 2000), ct);
                }
                catch (OperationCanceledException) { break; }

                // F-07：volatile 读取，避免看到过期的允许位与容量。
                if (!_increaseAllowed) continue;
                SetTarget(Math.Min(Volatile.Read(ref _target) + 2, Volatile.Read(ref _capacity)));
            }
        }

        public async Task<int> AcquireAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (_sync)
                {
                    if (_current < _target)
                    {
                        var slot = _freeSlots.Dequeue();
                        _current++;
                        if (_current > _peak) _peak = _current;
                        _slotCancel[slot] = new CancellationTokenSource();
                        ReportActiveLocked();
                        return slot;
                    }
                }
                await _wake.WaitAsync(ct);
            }
        }

        public CancellationTokenSource CreateSlotCancellation(int slot, CancellationToken global)
        {
            lock (_sync)
            {
                _slotCancel[slot] ??= new CancellationTokenSource();
                return CancellationTokenSource.CreateLinkedTokenSource(global, _slotCancel[slot]!.Token);
            }
        }

        public void Release(int slot)
        {
            lock (_sync)
            {
                if (_current <= 0) return;
                _current--;
                _freeSlots.Enqueue(slot);
                var old = _slotCancel[slot];
                _slotCancel[slot] = null;
                try { old?.Dispose(); } catch { }
                ReportActiveLocked();
                try { _wake.Release(); } catch (SemaphoreFullException) { }
            }
        }

        public void SetTarget(int target)
        {
            List<int>? trimSlots = null;
            var wakeCount = 0;
            lock (_sync)
            {
                // F-07：容量与目标必须在同一临界区内读取，保证裁剪决策自洽。
                _target = Math.Clamp(target, 1, _capacity);
                if (_target > _current) wakeCount = _target - _current;
                else if (_target < _current)
                {
                    var busy = new List<int>();
                    for (var i = 0; i < _slotCancel.Length; i++)
                        if (_slotCancel[i] != null) busy.Add(i);
                    trimSlots = busy.OrderByDescending(x => x).Take(_current - _target).ToList();
                }
            }

            if (wakeCount > 0)
            {
                try { _wake.Release(wakeCount); } catch (SemaphoreFullException) { }
            }

            if (trimSlots != null && trimSlots.Count > 0)
            {
                _ = TrimSlotsAsync(trimSlots);
            }
        }

        private async Task TrimSlotsAsync(List<int> slots)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                CancelSlot(slots[i]);
                if ((i + 1) % 8 == 0 && i + 1 < slots.Count)
                {
                    await Task.Delay(200);
                }
            }
        }

        private void CancelSlot(int slot)
        {
            lock (_sync)
            {
                var cts = _slotCancel[slot];
                if (cts != null)
                {
                    try { cts.Cancel(); } catch { }
                }
            }
        }

        public void Observe(double throughput, bool compensating, double elapsed)
        {
            var now = DateTime.UtcNow;
            // F-07：_recentRates 会被 NIC 监控线程写入、被 PulseLoop/裁剪路径读取，
            // 队列的入队出队与快照都必须在 _sync 内完成，否则并发枚举会抛
            // "Collection was modified"。
            lock (_sync)
            {
                _recentRates.Enqueue(throughput);
                while (_recentRates.Count > 10) _recentRates.Dequeue();
            }

            if (compensating)
            {
                _increaseAllowed = false;
                _evalCount = 0;
                // F-07：_bestTarget/_capacity 的读写统一在 _sync 内。
                int bestTarget, capacity;
                lock (_sync) { bestTarget = _bestTarget; capacity = _capacity; }
                SetTarget(Math.Min(bestTarget, capacity));
                return;
            }

            if (now < _cooldownUntilUtc) return;

            var previousBest = _bestRate;
            if (throughput > _bestRate)
            {
                _bestRate = throughput;
                lock (_sync)
                {
                    _bestTarget = Math.Min(Volatile.Read(ref _target), _capacity);
                }
            }

            var cv = ComputeCv();
            var deadbandPct = Math.Clamp(0.02 + cv * 1.5, 0.02, 0.05);
            var requiredGain = previousBest > 0 ? previousBest * deadbandPct : 0;
            var gain = throughput - previousBest;

            if (gain >= requiredGain && (previousBest <= 0 || throughput >= previousBest * 0.98))
            {
                _noGainCount = 0;
                _declineCount = 0;
                _pulseIntervalMs = Math.Max(100, _pulseIntervalMs * 0.75);
                _increaseAllowed = true;
            }
            else if (throughput >= previousBest * 0.98)
            {
                _noGainCount++;
                _pulseIntervalMs = Math.Min(2000, _pulseIntervalMs * 1.5);
                if (_noGainCount >= 2)
                {
                    _increaseAllowed = false;
                    int bestTarget, capacity;
                    lock (_sync) { bestTarget = _bestTarget; capacity = _capacity; }
                    SetTarget(Math.Min(bestTarget, capacity));
                    _nextReevaluateUtc = now.AddSeconds(30);
                    _noGainCount = 0;
                }
            }
            else if (throughput < previousBest * 0.95)
            {
                _declineCount++;
                if (_declineCount >= 3)
                {
                    DowngradeCapacity(now);
                }
            }
            else
            {
                _noGainCount = 0;
            }

            if (!_increaseAllowed && now >= _nextReevaluateUtc && now >= _cooldownUntilUtc)
            {
                _increaseAllowed = true;
                _noGainCount = 0;
                _pulseIntervalMs = 200;
                _nextReevaluateUtc = now.AddSeconds(30);
            }

            EvaluateCapacity(now, gain, previousBest, requiredGain);
        }

        private void EvaluateCapacity(DateTime now, double gain, double previousBest, double requiredGain)
        {
            // F-07：容量相关字段快照，避免与 ExpandCapacity/DowngradeCapacity 竞争。
            int capacityIndex, capacity, bestTarget, target;
            lock (_sync)
            {
                capacityIndex = _capacityIndex;
                capacity = _capacity;
                bestTarget = _bestTarget;
                target = Volatile.Read(ref _target);
            }

            if (capacityIndex >= _capacities.Count - 1) return;
            if (!_increaseAllowed) return;
            if (now < _nextCapacityEvalUtc) return;
            if (target < capacity * 0.9 && _current < capacity * 0.9) return;

            var latestRate = LatestRecentRate();
            var newEff = latestRate / Math.Max(1, target);
            var oldEff = _bestRate / Math.Max(1, bestTarget);
            var efficiencyOk = oldEff <= 0 || newEff >= oldEff * 0.7;

            if (gain >= requiredGain && previousBest > 0 && efficiencyOk)
            {
                _evalCount++;
                if (_evalCount >= 2)
                {
                    ExpandCapacity();
                }
            }
            else
            {
                _evalCount = 0;
                _increaseAllowed = false;
                SetTarget(bestTarget);
                _nextCapacityEvalUtc = now.AddSeconds(7);
                _nextReevaluateUtc = now.AddSeconds(7);
            }
        }

        private void ExpandCapacity()
        {
            // F-07：容量阶梯推进在 _sync 内完成，防止并发扩展导致容量翻倍。
            var newWorkers = 0;
            lock (_sync)
            {
                if (_capacityIndex < _capacities.Count - 1)
                {
                    var oldCapacity = _capacity;
                    _capacityIndex++;
                    _capacity = _capacities[_capacityIndex];
                    _evalCount = 0;
                    _pulseIntervalMs = 200;
                    _increaseAllowed = true;
                    newWorkers = _capacity - oldCapacity;
                }
            }

            // 在锁外启动 worker，避免在持锁期间调度任务。
            if (newWorkers <= 0) return;
            StartWorkerBatch(newWorkers);
            Logger.Log($"Adaptive capacity expanded to {Volatile.Read(ref _capacity)} (+{newWorkers} workers)");
        }

        private void DowngradeCapacity(DateTime now)
        {
            int bestTarget, capacity;
            lock (_sync)
            {
                if (_capacityIndex <= 0) return;
                _capacityIndex--;
                _capacity = _capacities[_capacityIndex];
                _evalCount = 0;
                _declineCount = 0;
                _bestTarget = Math.Min(_bestTarget, _capacity);
                bestTarget = _bestTarget;
                capacity = _capacity;
            }

            _increaseAllowed = false;
            SetTarget(Math.Min(bestTarget, capacity));
            _cooldownUntilUtc = now.AddSeconds(20);
            _nextReevaluateUtc = now.AddSeconds(30);
            _nextCapacityEvalUtc = now.AddSeconds(20);
        }

        private double ComputeCv()
        {
            // F-07：在锁内取快照，避免与 Observe 的入队/出队并发。
            double[] values;
            lock (_sync) { values = _recentRates.ToArray(); }

            if (values.Length < 3) return 0;
            var mean = values.Average();
            if (mean <= 0) return 0;
            var variance = values.Average(x => (x - mean) * (x - mean));
            return Math.Sqrt(variance) / mean;
        }

        /// <summary>
        /// F-07：读取最近一次速率样本，必须加锁。
        /// </summary>
        private double LatestRecentRate()
        {
            lock (_sync) { return _recentRates.Count > 0 ? _recentRates.Last() : 0; }
        }

        private void ReportActiveLocked()
        {
            var now = DateTime.UtcNow;
            if (_lastReported == _current || (now - _lastReportUtc).TotalMilliseconds < 200) return;
            _lastReported = _current;
            _lastReportUtc = now;
            _onActive?.Invoke(_current);
        }
    }

    private Task StartNicMonitor(Stopwatch overall, CancellationToken c, List<NetworkAdapterInfo> ad, NicState st,
        Action<double, double, long>? dl, Action<double, double, long>? ul, Action<string, double, double>? ar,
        Action<double>? adl, Action<double>? aul, Action<double>? atl, Action<double>? as_, LongRef tbd,
        Action<long>? tb = null, int tc = 128, AdaptiveController? adaptive = null, int initialDelayMs = 0, int throughputMode = 0)
    {
        var nicTask = Task.Run(async () =>
        {
            try
            {
                var lb = new Dictionary<string, (long R, long S)>(); double lt = 0; bool fp = true;
                var dh = new List<(double, double)>(); var uh = new List<(double, double)>();
                var ws = _options.RateWindowSec; bool as2 = false; long asb = 0; double ast = 0;
                var totalBytes = tbd;
                // 双向模式启动预热：下载、上传都产生过流量后才开始喂自适应控制器。
                var fullWarmupDownloadSeen = false;
                var fullWarmupUploadSeen = false;
                while (!c.IsCancellationRequested)
                {
                    try { await Task.Delay(initialDelayMs > 0 ? initialDelayMs : _options.NicPollIntervalMs, c); } catch { break; }
                    var e = overall.Elapsed.TotalSeconds; long dd = 0, du = 0;
                    foreach (var a in ad)
                    { var n = _networkInfo.GetCurrentBytes(a.Id); if (!n.HasValue) continue; if (lb.TryGetValue(a.Id, out var p)) { var x = n.Value.Received - p.R; var y = n.Value.Sent - p.S; if (x < 0 || y < 0) { lb[a.Id] = (n.Value.Received, n.Value.Sent); continue; } dd += x; du += y; var dt = e - lt; ar?.Invoke(a.Name, dt > 0 ? (x * 8.0) / (dt * 1_000_000.0) : 0, dt > 0 ? (y * 8.0) / (dt * 1_000_000.0) : 0); } lb[a.Id] = (n.Value.Received, n.Value.Sent); }
                    if (fp) { fp = false; st.FR = lb.Values.Sum(x => x.R); st.FS = lb.Values.Sum(x => x.S); }
                    else { st.AR = lb.Values.Sum(x => x.R); st.AS = lb.Values.Sum(x => x.S); }
                    if (!st.R && e >= _options.AverageDelaySec) { st.R = true; st.BR = lb.Values.Sum(x => x.R); st.BS = lb.Values.Sum(x => x.S); }
                    tb?.Invoke(Math.Max(0, st.AR + st.AS - st.FR - st.FS));
                    if (lt > 0) { var dt = e - lt; var dr = dt > 0 ? (dd * 8.0) / (dt * 1_000_000.0) : 0; var ur = dt > 0 ? (du * 8.0) / (dt * 1_000_000.0) : 0; dh.Add((e, dr)); dh.RemoveAll(x => e - x.Item1 > ws); uh.Add((e, ur)); uh.RemoveAll(x => e - x.Item1 > ws); dl?.Invoke(e, dh.Count > 0 ? dh.Average(x => x.Item2) : dr, Interlocked.Read(ref totalBytes.Value)); ul?.Invoke(e, uh.Count > 0 ? uh.Average(x => x.Item2) : ur, du); if (st.R) { var ae = e - _options.AverageDelaySec; adl?.Invoke(ae > 0 ? (st.AR - st.BR) * 8.0 / (ae * 1_000_000.0) : 0); aul?.Invoke(ae > 0 ? (st.AS - st.BS) * 8.0 / (ae * 1_000_000.0) : 0); atl?.Invoke(ae > 0 ? (st.AR - st.BR + st.AS - st.BS) * 8.0 / (ae * 1_000_000.0) : 0); } }
                    var sr = dh.Count > 0 ? dh.Average(x => x.Item2) : 0;
                    var ur_ = uh.Count > 0 ? uh.Average(x => x.Item2) : 0;
                    var combined = Math.Max(sr, ur_);
                    if (combined > st.PeakRate) st.PeakRate = combined;
                    if (_options.CompensationEnabled && st.R)
                    {
                        if (!st.IsCompensating && st.PeakRate > 0 && combined < st.PeakRate * _options.CompensationThreshold)
                        {
                            st.BelowThresholdSec += _options.NicPollIntervalMs / 1000.0;
                            if (st.BelowThresholdSec >= _options.CompensationConfirmSec)
                            {
                            st.IsCompensating = true;
                            st.BelowThresholdSec = 0;
                            st.DropStartTime = e;
                                st.DropStartBytes = st.AR + st.AS;
                            }
                        }
                        else if (st.IsCompensating && combined > st.PeakRate * 0.5)
                        {
                            st.IsCompensating = false;
                            st.PeakRate = combined;
                            st.TotalDropDuration += e - st.DropStartTime;
                            st.TotalDropBytes += Math.Max(0, st.AR + st.AS - st.DropStartBytes);
                        }
                        else if (!st.IsCompensating) { st.BelowThresholdSec = 0; }
                    }
                    if (adaptive != null)
                    {
                        if (throughputMode == 0 && !(fullWarmupDownloadSeen && fullWarmupUploadSeen))
                        {
                            if (sr > 0) fullWarmupDownloadSeen = true;
                            if (ur_ > 0) fullWarmupUploadSeen = true;
                        }

                        if (ShouldObserveAdaptiveValue(throughputMode, fullWarmupDownloadSeen, fullWarmupUploadSeen))
                        {
                            adaptive.Observe(
                                SelectAdaptiveObserveValue(throughputMode, sr, ur_),
                                st.IsCompensating,
                                overall.Elapsed.TotalSeconds);
                        }
                    }
                    lt = e;
                    if (!as2 && e >= _options.AverageDelaySec) { as2 = true; asb = Interlocked.Read(ref totalBytes.Value); ast = e; }
                    if (as2 && as_ != null) { var b = Interlocked.Read(ref totalBytes.Value) - asb; var t_ = e - ast; as_(t_ > 0 ? (b * 8.0) / (t_ * 1_000_000.0) : 0); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Log($"NIC error: {ex.Message}"); }
        });
        return nicTask;
    }

    /// <summary>
    /// 双向模式的自适应反馈取下载/上传中较小的一侧（瓶颈方向），
    /// 避免下载吞吐远高于上传时，上传方向的加压增益被合计值稀释而提前停止。
    /// </summary>
    internal static double SelectAdaptiveObserveValue(int throughputMode, double downloadMbps, double uploadMbps)
        => throughputMode == 1 ? downloadMbps
         : throughputMode == 2 ? uploadMbps
         : Math.Min(downloadMbps, uploadMbps);

    /// <summary>
    /// 双向模式启动预热门控：下载、上传都产生过流量后才开始观察。
    /// 否则先起量的单方向速率会先写入 _bestRate，另一方向稍后起量时会被误判为掉速，
    /// 导致 target 被错误回缩、上传并发无法爬升。
    /// </summary>
    internal static bool ShouldObserveAdaptiveValue(int throughputMode, bool downloadSeen, bool uploadSeen)
        => throughputMode != 0 || (downloadSeen && uploadSeen);

    private (Task? gatewayTask, Task wanTask, Task jitterTask, Task lossTask) StartGatewayAndWanLatency(string? gateway, CancellationToken ctLinked, Action<double>? onLatency, Action<double>? onWanLatency, Action<double>? onJitter, Action<PacketLossSample>? onPacketLoss, IPAddress? sourceIp = null, HttpClient? probeClient = null)
    {
        Task? gt = null;
        if (!string.IsNullOrEmpty(gateway))
        {
            Logger.Log($"网关延迟测试启动: gateway={gateway}");
            gt = Task.Run(async () => { try { while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(gateway, ctLinked, sourceIp: sourceIp, probeClient: probeClient); if (v > 0) onLatency?.Invoke(v); } catch { break; } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } } catch { } });
        }
        var wt = Task.Run(async () => { try { var wHost = "8.8.8.8"; while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(wHost, ctLinked, sourceIp: sourceIp, probeClient: probeClient); if (v > 0) onWanLatency?.Invoke(v); } catch { } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } } catch { } });
        var jt = Task.Run(async () => { try { try { await Task.Delay(TimeSpan.FromSeconds(_options.AverageDelaySec), ctLinked); } catch { return; } var jHost = string.IsNullOrEmpty(_options.JitterTargetHost) ? "8.8.8.8" : _options.JitterTargetHost; var jInterval = Math.Max(500, _options.JitterPollIntervalMs); while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(jHost, ctLinked, sourceIp: sourceIp, probeClient: probeClient); if (v > 0) onJitter?.Invoke(v); } catch { } try { await Task.Delay(jInterval, ctLinked); } catch { break; } } } catch { } });
        var lossTask = onPacketLoss == null ? Task.CompletedTask : StartPacketLossMonitor(_options.PacketLossTargetHost, ctLinked, onPacketLoss);
        return (gt, wt, jt, lossTask);
    }

    // ====== 丢包率监测 ======

    private Task StartPacketLossMonitor(string host, CancellationToken ct, Action<PacketLossSample>? onPacketLoss)
    {
        return Task.Run(async () =>
        {
            try
            {
                var target = string.IsNullOrWhiteSpace(host) ? "8.8.8.8" : host;
                var ip = ResolveHost(target);
                if (ip == null) return;
                var interval = Math.Clamp(_options.PacketLossPollIntervalMs, 500, 5000);
                var useIcmp = true;

                while (!ct.IsCancellationRequested)
                {
                    var sample = useIcmp
                        ? await RunPacketLossBatchAsync(ip, target, "ICMP", ct)
                        : await RunPacketLossBatchAsync(ip, target, "UDP", ct);

                    // 首轮 ICMP 全部失败时，用 UDP 复核一次，避免防火墙屏蔽 ICMP 导致误报 100% 丢包
                    if (useIcmp && sample.Received == 0)
                    {
                        sample = await RunPacketLossBatchAsync(ip, target, "UDP", ct);
                        useIcmp = false;
                    }

                    onPacketLoss?.Invoke(sample);
                    try { await Task.Delay(interval, ct); } catch { break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Log($"Packet loss monitor error: {ex.Message}"); }
        });
    }

    private async Task<PacketLossSample> RunPacketLossBatchAsync(IPAddress ip, string target, string method, CancellationToken ct)
    {
        const int batchSize = 5;
        const int timeoutMs = 1000;

        if (method == "UDP")
        {
            var received = 0;
            for (var i = 0; i < batchSize; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var udp = new UdpClient();
                    udp.Connect(ip, 33434);
                    udp.Client.SendTimeout = timeoutMs;
                    udp.Client.ReceiveTimeout = timeoutMs;
                    var probe = new byte[] { 0x00 };
                    var outcome = await SendUdpProbeAsync(udp, probe, timeoutMs, ct);
                    // PortUnreachable 表示收到 ICMP Port Unreachable，主机可达，不能算丢包。
                    if (outcome != UdpProbeOutcome.Timeout) received++;
                }
            catch (OperationCanceledException) { throw; }
                catch { }
                if (i < batchSize - 1)
                {
                    try { await Task.Delay(50, ct); } catch { break; }
                }
            }
            return new PacketLossSample { Sent = batchSize, Received = received, Target = target, Method = "UDP" };
        }

        var pingTasks = new List<Task<int>>(batchSize);
        for (var i = 0; i < batchSize; i++)
        {
            ct.ThrowIfCancellationRequested();
            pingTasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ip, timeoutMs);
                    return reply.Status == IPStatus.Success ? 1 : 0;
                }
                catch { return 0; }
            }, ct));
        }
        var results = await Task.WhenAll(pingTasks);
        return new PacketLossSample { Sent = batchSize, Received = results.Sum(), Target = target, Method = "ICMP" };
    }

    // ====== 上传测速 ======

    /// <summary>
    /// 执行一次上传请求并校验响应状态。
    /// 非 2xx 响应会由 <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> 抛出，
    /// 避免把服务器拒收的上传计入成功吞吐。
    /// </summary>
    internal static async Task SendUploadAsync(HttpClient http, string url, byte[] payload, CancellationToken ct)
    {
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<SpeedTestResult> RunUploadTestAsync(
        List<string> urls, int threadCount, List<NetworkAdapterInfo> adapters, string profileName,
        string? gateway = null,
        Action<double, double, long>? onDownloadProgress = null, Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null, Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null, Action<double>? onWanLatency = null, Action<double>? onJitter = null,
        Action<double>? onAverageDownload = null, Action<double>? onAverageUpload = null, Action<double>? onAverageTotal = null,
Action<long>? onTotalBytes = null, Action<PacketLossSample>? onPacketLoss = null,
        int adaptiveThreadCap = 0,
        CancellationToken ct = default, HttpClient? client = null)
    {
        if (urls.Count == 0) throw new ArgumentException("URL 列表不能为空");
        if (adapters == null || adapters.Count == 0) throw new ArgumentException("至少需要一个活跃网卡");
        threadCount = Math.Clamp(threadCount, 2, 1024);
        var overall = Stopwatch.StartNew(); int activeThreads = 0; var dummy = new LongRef();
        var nicState = new NicState();
        var http = CreateEgressClient(adapters, client, out var isAdapterBound, out var ownsHttpClient);
        using var ownedClient = ownsHttpClient ? http : null;

        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;
        var adaptiveMaxBase = _options.AdaptiveThreadsEnabled
            ? (adaptiveThreadCap > 0 ? Math.Max(8, adaptiveThreadCap) : Math.Max(8, GetAutomaticAdaptiveMax()))
            : 0;
        var useAdaptive = adaptiveMaxBase > 0;
        var workerCount = useAdaptive ? adaptiveMaxBase : threadCount;
        var startThreads = useAdaptive ? Math.Clamp(_options.AdaptiveStartThreads, 1, adaptiveMaxBase) : 0;
        AdaptiveController? adaptive = useAdaptive
            ? new AdaptiveController(adaptiveMaxBase, startThreads, _options.TestTimeoutSec, onActiveThreadCount)
            : null;
        using var semaphore = new SemaphoreSlim(workerCount, workerCount);

        var nicMonitorUpload = StartNicMonitor(overall, ctLinked, adapters, nicState, onDownloadProgress, onUploadProgress, onAdapterRates, onAverageDownload, onAverageUpload, onAverageTotal, null, dummy, onTotalBytes, tc: threadCount, adaptive: adaptive, throughputMode: 2);
        var sourceIp = isAdapterBound ? GetAdapterSourceIp(adapters) : null;
        var gwUploadTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss, sourceIp, http);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var urlBalancer = new UrlBalancer(urls, useFastestAfterProbe: false);


        async Task RunOneUploadAsync(int workerId, CancellationToken requestCt, CancellationToken globalCt)
        {
            var url = urlBalancer.GetUrlForWorker(workerId);
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);
                // 非 2xx（4xx/5xx）不代表上传成功，必须显式校验，否则会把失败算作成功吞吐。
                await SendUploadAsync(http, url, buf, linked.Token);
                urlBalancer.ReportSuccess(url, 0, 0);
            }
            catch (OperationCanceledException)
            {
                if (requestCt.IsCancellationRequested && !globalCt.IsCancellationRequested)
                {
                    urlBalancer.ReportTimeout(url);
                    await Task.Delay(200, globalCt);
                    return;
                }
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[D-URL] nic={adapters[0].Name} url={url} FAIL {ex.Message}");
                urlBalancer.ReportFailure(url);
                await Task.Delay(500, globalCt);
            }
        }

        var tasks = new List<Task>(); var rampBatch = Math.Max(1, threadCount / 256);
        if (adaptive != null)
        {
            adaptive.StartWorkers(ctLinked, async (workerId, requestCt) =>
            {
                await RunOneUploadAsync(workerId, requestCt, ctLinked);
            });
        }
        else
        {
            for (int i = 0; i < threadCount; i++)
            {
                var idx = i;
                if (ct.IsCancellationRequested) break;

                tasks.Add(Task.Run(async () =>
                {
                    try { await semaphore.WaitAsync(ctLinked); } catch { return; }
                    var c = Interlocked.Increment(ref activeThreads);
                    try
                    {
                        onActiveThreadCount?.Invoke(c);
                        while (!ctLinked.IsCancellationRequested)
                        {
                            try { await RunOneUploadAsync(idx, ctLinked, ctLinked); }
                            catch (OperationCanceledException) { break; }
                        }
                    }
                    finally
                    {
                        try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                        onActiveThreadCount?.Invoke(Interlocked.Decrement(ref activeThreads));
                    }
                }));

                if (_options.ThreadRampUpMs > 0 && (i + 1) % rampBatch == 0 && i + 1 < threadCount)
                {
                    try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); } catch { break; }
                }
            }
        }
        if (adaptive != null) { await adaptive.WaitAsync(); }
        await Task.WhenAll(tasks); overall.Stop(); internalCts.Cancel();
        await Task.WhenAll(gwUploadTasks.gatewayTask ?? Task.CompletedTask, gwUploadTasks.wanTask, gwUploadTasks.jitterTask, gwUploadTasks.lossTask, nicMonitorUpload);
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        var ts = Math.Max(overall.Elapsed.TotalSeconds, 0.1); double dl, ul;
        if (nicState.R) { var e = Math.Max(ts - _options.AverageDelaySec, 0.1); var drop = _options.CompensationEnabled ? nicState.TotalDropDuration : 0; var adj = Math.Max(e - drop, 0.1); dl = Math.Max(0, (nicState.AR - nicState.BR) * 8.0 / (adj * 1_000_000.0)); ul = Math.Max(0, (nicState.AS - nicState.BS) * 8.0 / (adj * 1_000_000.0)); }
        else { dl = Math.Max(0, (nicState.AR - nicState.FR) * 8.0 / (ts * 1_000_000.0)); ul = Math.Max(0, (nicState.AS - nicState.FS) * 8.0 / (ts * 1_000_000.0)); }
        var ulBytes = Math.Max(0, nicState.R ? nicState.AS - nicState.BS : nicState.AS - nicState.FS);
        return new SpeedTestResult { Timestamp = DateTime.Now, DownloadMbps = dl, UploadMbps = ul, PeakMbps = nicState.PeakRate, LatencyMs = 0, JitterMs = 0, PacketLoss = 0, NodeName = profileName, NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")), BytesDownloaded = 0, BytesUploaded = ulBytes, DurationSeconds = ts, ThreadCount = adaptive != null ? Math.Max(1, adaptive.Peak) : threadCount, UrlDetails = urlBalancer.BuildDetails() };
    }

    // ====== 双向测速（下载+上传同时跑） ======

    public async Task<SpeedTestResult> RunFullTestAsync(
        List<string> dlUrls, List<string> ulUrls, int threadCount, List<NetworkAdapterInfo> adapters, string profileName,
        string? gateway = null,
        Action<double, double, long>? onDownloadProgress = null, Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null, Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null, Action<double>? onWanLatency = null, Action<double>? onJitter = null,
        Action<double>? onAverageDownload = null, Action<double>? onAverageUpload = null, Action<double>? onAverageTotal = null,
Action<long>? onTotalBytes = null, Action<PacketLossSample>? onPacketLoss = null,
        int adaptiveThreadCap = 0,
        CancellationToken ct = default, HttpClient? client = null)
    {
        bool hasDl = dlUrls.Count > 0, hasUl = ulUrls.Count > 0;
        if (!hasDl && !hasUl) throw new ArgumentException("无可用测速地址");
        if (!hasDl) return await RunUploadTestAsync(ulUrls, threadCount, adapters, profileName, gateway, onDownloadProgress, onUploadProgress, onAdapterRates, onActiveThreadCount, onLatency, onWanLatency, onJitter, onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, adaptiveThreadCap, ct, client);
        if (!hasUl) return await RunMultiUrlTestAsync(dlUrls, threadCount, adapters, profileName, gateway, null, onDownloadProgress, onUploadProgress, onAdapterRates, onActiveThreadCount, onLatency, onWanLatency, onJitter, onPacketLoss: onPacketLoss, adaptiveThreadCap: adaptiveThreadCap, onAverageDownload: onAverageDownload, onAverageUpload: onAverageUpload, onAverageTotal: onAverageTotal, onTotalBytes: onTotalBytes, ct: ct, client: client);

        threadCount = Math.Clamp(threadCount, 2, 1024);
        var overall = Stopwatch.StartNew(); int activeThreads = 0;
        var nicState = new NicState(); var bytesDl = new LongRef();
        var http = CreateEgressClient(adapters, client, out var isAdapterBound, out var ownsHttpClient);
        using var ownedClient = ownsHttpClient ? http : null;

        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;
        var adaptiveMaxBase = _options.AdaptiveThreadsEnabled
            ? (adaptiveThreadCap > 0 ? Math.Max(8, adaptiveThreadCap) : Math.Max(8, GetAutomaticAdaptiveMax()))
            : 0;
        var useAdaptive = adaptiveMaxBase > 0;
        var workerCount = useAdaptive ? adaptiveMaxBase : threadCount;
        var startThreads = useAdaptive ? Math.Clamp(_options.AdaptiveStartThreads, 2, adaptiveMaxBase) : 0;
        AdaptiveController? adaptive = useAdaptive
            ? new AdaptiveController(adaptiveMaxBase, startThreads, _options.TestTimeoutSec, onActiveThreadCount)
            : null;
        using var semaphore = new SemaphoreSlim(workerCount, workerCount);

        var nicMonitorFull = StartNicMonitor(overall, ctLinked, adapters, nicState, onDownloadProgress, onUploadProgress, onAdapterRates, onAverageDownload, onAverageUpload, onAverageTotal, null, bytesDl, onTotalBytes, tc: threadCount, adaptive: adaptive, throughputMode: 0);
        var sourceIp = isAdapterBound ? GetAdapterSourceIp(adapters) : null;
        var gwFullTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss, sourceIp, http);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var dlBalancer = new UrlBalancer(dlUrls, useFastestAfterProbe: true);
        var ulBalancer = new UrlBalancer(ulUrls, useFastestAfterProbe: false);
        async Task RunOneFullAsync(bool isDl, string url, CancellationToken requestCt)
        {
            try
            {
                if (isDl)
                {
                    using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                    headerCts.CancelAfter(TimeSpan.FromSeconds(10));
                    // F-05：手动跟随重定向，逐跳校验目标为公网地址。
                    using var resp = await SendWithValidatedRedirectsAsync(
                        http, url, TimeSpan.FromSeconds(10), headerCts.Token);
                    resp.EnsureSuccessStatusCode();
                    await using var s = await resp.Content.ReadAsStreamAsync(requestCt);
                    var b = ArrayPool<byte>.Shared.Rent(64 * 1024);
                    try
                    {
                        while (!requestCt.IsCancellationRequested)
                        {
                            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                            readCts.CancelAfter(TimeSpan.FromSeconds(15));
                            var r = await s.ReadAsync(b.AsMemory(0, 64 * 1024), readCts.Token);
                            if (r == 0) break;
                            Interlocked.Add(ref bytesDl.Value, r);
                        }
                        dlBalancer.ReportSuccess(url, 0, 0);
                    }
                    finally { ArrayPool<byte>.Shared.Return(b); }
                }
                else
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);
                    // 非 2xx（4xx/5xx）不代表上传成功，必须显式校验，否则会把失败算作成功吞吐。
                    await SendUploadAsync(http, url, buf, linked.Token);
                    ulBalancer.ReportSuccess(url, 0, 0);
                }
            }
            catch (OperationCanceledException)
            {
                if (!requestCt.IsCancellationRequested)
                {
                    if (isDl) dlBalancer.ReportTimeout(url); else ulBalancer.ReportTimeout(url);
                }
                throw;
            }
            catch
            {
                if (isDl) dlBalancer.ReportFailure(url); else ulBalancer.ReportFailure(url);
                await Task.Delay(500, requestCt);
            }
        }

        var tasks = new List<Task>(); var rampBatch = Math.Max(1, threadCount / 256);
        if (adaptive != null)
        {
            adaptive.StartWorkers(ctLinked, async (workerId, requestCt) =>
            {
                var isDl = (workerId & 1) == 0;
                var url = isDl ? dlBalancer.GetUrlForWorker(workerId) : ulBalancer.GetUrlForWorker(workerId);
                await RunOneFullAsync(isDl, url, requestCt);
            });
        }
        else
        {
            for (int i = 0; i < threadCount; i += 2)
            {
                if (ct.IsCancellationRequested) break;

                for (int j = 0; j < 2; j++)
                {
                    int idx = i + j;
                    if (idx >= threadCount) break;
                    bool isDl = j == 0;

                    tasks.Add(Task.Run(async () =>
                    {
                        try { await semaphore.WaitAsync(ctLinked); } catch { return; }
                        var c = Interlocked.Increment(ref activeThreads);
                        try
                        {
                            onActiveThreadCount?.Invoke(c);
                            while (!ctLinked.IsCancellationRequested)
                            {
                                var currentUrl = isDl ? dlBalancer.GetUrlForWorker(idx) : ulBalancer.GetUrlForWorker(idx);
                                try { await RunOneFullAsync(isDl, currentUrl, ctLinked); }
                                catch (OperationCanceledException) { break; }
                            }
                        }
                        finally
                        {
                            try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                            onActiveThreadCount?.Invoke(Interlocked.Decrement(ref activeThreads));
                        }
                    }));
                }

                if (_options.ThreadRampUpMs > 0 && (i + 2) % rampBatch == 0 && i + 2 < threadCount)
                {
                    try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); } catch { break; }
                }
            }
        }

        if (adaptive != null) { await adaptive.WaitAsync(); }
        await Task.WhenAll(tasks); overall.Stop(); internalCts.Cancel();
        await Task.WhenAll(gwFullTasks.gatewayTask ?? Task.CompletedTask, gwFullTasks.wanTask, gwFullTasks.jitterTask, gwFullTasks.lossTask, nicMonitorFull);
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        var ts_ = Math.Max(overall.Elapsed.TotalSeconds, 0.1); double dl_, ul_;
        if (nicState.R) { var e = Math.Max(ts_ - _options.AverageDelaySec, 0.1); var drop = _options.CompensationEnabled ? nicState.TotalDropDuration : 0; var adj = Math.Max(e - drop, 0.1); dl_ = Math.Max(0, (nicState.AR - nicState.BR) * 8.0 / (adj * 1_000_000.0)); ul_ = Math.Max(0, (nicState.AS - nicState.BS) * 8.0 / (adj * 1_000_000.0)); }
        else { dl_ = Math.Max(0, (nicState.AR - nicState.FR) * 8.0 / (ts_ * 1_000_000.0)); ul_ = Math.Max(0, (nicState.AS - nicState.FS) * 8.0 / (ts_ * 1_000_000.0)); }
        long dlBytes_ = bytesDl.Value, ulBytes_ = Math.Max(0, nicState.R ? nicState.AS - nicState.BS : nicState.AS - nicState.FS);
        return new SpeedTestResult { Timestamp = DateTime.Now, DownloadMbps = dl_, UploadMbps = ul_, PeakMbps = nicState.PeakRate, LatencyMs = 0, JitterMs = 0, PacketLoss = 0, NodeName = profileName, NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")), BytesDownloaded = dlBytes_, BytesUploaded = ulBytes_, DurationSeconds = ts_, ThreadCount = adaptive != null ? Math.Max(1, adaptive.Peak) : threadCount, UrlDetails = new() };
    }


    /// <summary>
    /// 创建本次测速使用的 HttpClient。
    /// 单张显式勾选网卡且系统存在其他活动网卡时，绑定该网卡源 IP，避免流量按默认路由从
    /// 其他网卡出去；系统只有一张活动网卡时保持默认路由，兼容单网卡环境。
    /// </summary>
    private HttpClient CreateEgressClient(List<NetworkAdapterInfo> adapters, HttpClient? requestedClient,
        out bool isAdapterBound, out bool ownsClient)
    {
        var hasMultipleActiveAdapters = adapters.Count == 1
            && requestedClient == null
            && HasMultipleActiveAdapters();

        var resolved = ResolveEgressClientCore(
            adapters,
            requestedClient,
            hasMultipleActiveAdapters,
            CreateNicBoundClient,
            _httpClient);

        isAdapterBound = resolved.IsAdapterBound;
        ownsClient = resolved.OwnsClient;

        if (resolved.OwnsClient)
        {
            Logger.Log($"[NIC] 测速出口已绑定: {adapters[0].Name} ip={adapters[0].IPAddress ?? "null"}");
        }
        else if (resolved.BindFailed)
        {
            var message = $"网卡 {adapters[0].Name} 未获取可用 IPv4 地址，无法进行绑定测速";
            Logger.Log($"[NIC] {message}");
            throw new InvalidOperationException(message);
        }

        return resolved.Client;
    }

    /// <summary>
    /// F-05：允许的最大重定向跳数。
    /// </summary>
    internal const int MaxSpeedTestRedirects = 3;

    /// <summary>
    /// F-05：解析重定向目标。相对 Location 按当前请求 URI 解析，非 http/https 一律拒绝。
    /// </summary>
    internal static bool TryResolveRedirect(Uri current, string? location, out Uri next)
    {
        next = null!;
        if (string.IsNullOrWhiteSpace(location)) return false;
        if (!Uri.TryCreate(current, location, out var resolved)) return false;
        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return false;
        next = resolved;
        return true;
    }

    /// <summary>
    /// F-05：手动逐跳跟随重定向，每一跳都用公网校验（HTTP 状态码 + 地址 + 端口白名单）。
    /// 自动重定向已关闭，否则一次 302 就能跳到 127.0.0.1 / 169.254.169.254 绕过校验。
    /// </summary>
    internal static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
        HttpClient client, string url, TimeSpan perRequestTimeout, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            ct.ThrowIfCancellationRequested();

            using var hopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hopCts.CancelAfter(perRequestTimeout);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, hopCts.Token);

            if (!IsRedirectStatusCode(response.StatusCode)) return response;

            var location = response.Headers.Location?.ToString();
            response.Dispose();

            if (hop >= MaxSpeedTestRedirects)
                throw new HttpRequestException($"Too many redirects (limit {MaxSpeedTestRedirects})");

            if (!TryResolveRedirect(new Uri(url), location, out var next))
                throw new HttpRequestException("Redirect target is missing or not a valid http/https URL");

            var allowed = await TargetValidator(next.ToString(), ct);
            if (!allowed)
                throw new HttpRequestException($"Redirect target rejected: {next}");

            url = next.ToString();
        }
    }

    /// <summary>
    /// 重定向目标的公网校验器，默认使用 WebServerService 的 SSRF 校验，可在测试中替换。
    /// </summary>
    internal static Func<string, CancellationToken, Task<bool>> TargetValidator { get; set; } =
        async (url, ct) => (await WebServerService.ResolvePublicHttpTargetAsync(url, ct)).IsAllowed;

    internal static bool IsRedirectStatusCode(HttpStatusCode status)
        => status == HttpStatusCode.MovedPermanently
           || status == HttpStatusCode.Found
           || status == HttpStatusCode.SeeOther
           || status == HttpStatusCode.TemporaryRedirect
           || status == HttpStatusCode.PermanentRedirect
           || (int)status == 300
           || (int)status == 305;

    /// <summary>
    /// 出口客户端解析核心逻辑（无网络依赖，供单测覆盖）。
    /// </summary>
    internal static (HttpClient Client, bool IsAdapterBound, bool OwnsClient, bool BindFailed)
        ResolveEgressClientCore(
            IReadOnlyList<NetworkAdapterInfo> adapters,
            HttpClient? requestedClient,
            bool hasMultipleActiveAdapters,
            Func<NetworkAdapterInfo, HttpClient?> boundClientFactory,
            HttpClient fallbackClient)
    {
        if (requestedClient != null)
            return (requestedClient, true, false, false);

        if (adapters.Count == 1 && hasMultipleActiveAdapters)
        {
            var boundClient = boundClientFactory(adapters[0]);
            if (boundClient != null)
                return (boundClient, true, true, false);

            return (fallbackClient, false, false, true);
        }

        return (fallbackClient, false, false, false);
    }

    private bool HasMultipleActiveAdapters()
    {
        try
        {
            return _networkInfo.GetAdapters(includeVirtual: true).Count > 1;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NIC] 活动网卡枚举失败，按多网卡环境处理: {ex.Message}");
            return true;
        }
    }
    /// <summary>
    /// 为指定网卡创建绑定源 IP 的 HttpClient（ConnectCallback 内 Socket.Bind 绑定源 IP）
    /// 创建失败返回 null（该网卡将被跳过）
    /// </summary>
    private HttpClient? CreateNicBoundClient(NetworkAdapterInfo adapter)
    {
        try
        {
            if (string.IsNullOrEmpty(adapter.IPAddress)) return null;
            if (!IPAddress.TryParse(adapter.IPAddress, out var localIp)) return null;
            if (localIp.AddressFamily != AddressFamily.InterNetwork) return null;
            var addressBytes = localIp.GetAddressBytes();
            if (localIp.Equals(IPAddress.Any) || IPAddress.IsLoopback(localIp)) return null;
            if (addressBytes.Length == 4 && addressBytes[0] == 169 && addressBytes[1] == 254) return null;
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                // F-05：禁止自动跟随重定向，逐跳改用经校验的跟随逻辑。
                AllowAutoRedirect = false,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                },
                ConnectCallback = async (ctx, ct) =>
                {
                    var port = ctx.DnsEndPoint.Port;
                    // 绑定的是 IPv4 socket，只取 IPv4 候选；同一 Host 做简单缓存，避免重复 DNS 解析
                    var host = ctx.DnsEndPoint.Host;
                    if (!_dnsCache.TryGetValue(host, out var addrs))
                    {
                        addrs = await Dns.GetHostAddressesAsync(host, ct);
                        _dnsCache[host] = addrs;
                    }
                    var candidates = addrs.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                    if (candidates.Count == 0) throw new SocketException((int)SocketError.HostNotFound);

                    Exception? last = null;
                    foreach (var ip in candidates)
                    {
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            socket.Bind(new IPEndPoint(localIp, 0));
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            cts.CancelAfter(TimeSpan.FromSeconds(5));
                            await socket.ConnectAsync(new IPEndPoint(ip, port), cts.Token);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                            socket.Dispose();
                        }
                    }
                    throw last ?? new SocketException((int)SocketError.HostNotFound);
                }
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(900) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(NetSpeedTest.Helpers.AppVersion.UserAgent);
            return client;
        }
        catch (Exception ex)
        {
            Logger.Log($"NIC 绑定失败 ({adapter.Name}): {ex.Message}");
            return null;
        }
    }


    /// <summary>
    /// 多网卡同时测速：每张网卡独立绑定 HttpClient 并行测速，返回每张网卡各自结果。
    /// </summary>
    public async Task<List<SpeedTestResult>> RunMultiNicTestsAsync(
        List<string> dlUrls, List<string> ulUrls, int threadCount,
        List<NetworkAdapterInfo> adapters, string profileName, string? gateway = null,
        Action<NetworkAdapterInfo, double, double, long>? onNicDownloadProgress = null,
        Action<NetworkAdapterInfo, double, double, long>? onNicUploadProgress = null,
        Action<NetworkAdapterInfo, double, double>? onNicAdapterRates = null,
        Action<double, double, long>? onDownloadProgress = null,
        Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null,
        Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null, Action<double>? onWanLatency = null, Action<double>? onJitter = null,
        Action<double>? onAverageSpeed = null, Action<double>? onAverageDownload = null,
        Action<double>? onAverageUpload = null, Action<double>? onAverageTotal = null,
        Action<long>? onTotalBytes = null,
        Action<PacketLossSample>? onPacketLoss = null,
        CancellationToken ct = default,
        List<NetworkAdapterInfo>? monitorAdapters = null,
        MultiNicMetricCallbacks? metricCallbacks = null)
    {
        bool hasDl = dlUrls.Count > 0, hasUl = ulUrls.Count > 0;
        if (!hasDl && !hasUl) throw new ArgumentException("无可用测速地址");
        if (adapters == null || adapters.Count == 0) throw new ArgumentException("至少需要一个活跃网卡");

        _dnsCache.Clear();
        // 单网卡分支：Run* 内部会在系统存在其他活动网卡时自动绑定选中网卡源 IP；
        // 仅当系统只有一张活动网卡时才走默认路由，以兼容单网卡环境。
        if (adapters.Count == 1)
        {
            var single = adapters[0];
            var nicOnly = new List<NetworkAdapterInfo> { single };
            var monitor = monitorAdapters ?? nicOnly;
            var nicGw = !string.IsNullOrEmpty(single.Gateway) ? single.Gateway : gateway;

            Action<double, double, long> singleDl = (e, r, b) =>
            {
                onNicDownloadProgress?.Invoke(single, e, r, b);
                onDownloadProgress?.Invoke(e, r, b);
            };
            Action<double, double, long> singleUl = (e, r, b) =>
            {
                onNicUploadProgress?.Invoke(single, e, r, b);
                onUploadProgress?.Invoke(e, r, b);
            };
            Action<string, double, double> singleAr = (name, dl, ul) =>
            {
                onNicAdapterRates?.Invoke(single, dl, ul);
                onAdapterRates?.Invoke(name, dl, ul);
            };

            Action<double>? singleLatency = onLatency;
        Action<double>? singleWanLatency = onWanLatency;
        Action<double>? singleJitter = onJitter;
        if (metricCallbacks?.OnLatency != null) singleLatency = v => metricCallbacks!.OnLatency!(single, v);
        if (metricCallbacks?.OnWanLatency != null) singleWanLatency = v => metricCallbacks!.OnWanLatency!(single, v);
        if (metricCallbacks?.OnJitterRtt != null) singleJitter = v => metricCallbacks!.OnJitterRtt!(single, v);

        SpeedTestResult singleResult;
            if (hasDl && hasUl)
                singleResult = await RunFullTestAsync(dlUrls, ulUrls, threadCount, monitor, profileName, nicGw,
                    singleDl, singleUl, singleAr, onActiveThreadCount,
                    singleLatency, singleWanLatency, singleJitter,
                    onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, 0, ct);
            else if (hasDl)
                singleResult = await RunMultiUrlTestAsync(dlUrls, threadCount, monitor, profileName, nicGw,
                    null, singleDl, singleUl, singleAr, onActiveThreadCount,
                    singleLatency, singleWanLatency, singleJitter,
                    onAverageSpeed, onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, 0, ct);
            else
                singleResult = await RunUploadTestAsync(ulUrls, threadCount, monitor, profileName, nicGw,
                    singleDl, singleUl, singleAr, onActiveThreadCount,
                    singleLatency, singleWanLatency, singleJitter,
                    onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, 0, ct);

            return new List<SpeedTestResult> { singleResult };
        }


        var nicClients = new List<(NetworkAdapterInfo adapter, HttpClient? client)>();
        var failedAdapters = new List<NetworkAdapterInfo>();
        foreach (var a in adapters)
        {
            if (string.IsNullOrEmpty(a.IPAddress))
            {
                failedAdapters.Add(a);
                continue;
            }
            var c = CreateNicBoundClient(a);
            if (c != null) nicClients.Add((a, c));
            else failedAdapters.Add(a);
        }

        using var lossCts = new CancellationTokenSource();
        var lossMonitor = onPacketLoss == null
            ? Task.CompletedTask
            : StartPacketLossMonitor(_options.PacketLossTargetHost, lossCts.Token, onPacketLoss);

        try
        {
            if (nicClients.Count == 0)
            {
                return failedAdapters.Select(a => new SpeedTestResult
                {
                    Timestamp = DateTime.Now,
                    NodeName = profileName,
                    NetworkAdapterName = a.Name,
                    ErrorMessage = "无法创建绑定连接",
                    TestType = hasDl && hasUl ? "双向" : hasDl ? "下载" : "上传"
                }).ToList();
            }

            int n = nicClients.Count;
            int perNicThreads = Math.Max(1, threadCount / n);
            var perNicAdaptiveCap = _options.AdaptiveThreadsEnabled ? Math.Max(1, GetAutomaticAdaptiveMax() / n) : 0;
            using var gate = new SemaphoreSlim(Math.Min(n, threadCount), Math.Min(n, threadCount));
            var aggLock = new object();
            var dlRate = new double[n]; var ulRate = new double[n]; var bytesVals = new long[n];
            var avgDl = new double[n]; var avgUl = new double[n]; var avgTot = new double[n]; var avgSpd = new double[n];
            var actCount = new int[n];

            Action<double, double, long> WrapDl(int i, NetworkAdapterInfo adapter) => (e, r, b) =>
            {
                lock (aggLock) { dlRate[i] = r; }
                onNicDownloadProgress?.Invoke(adapter, e, r, b);
                onDownloadProgress?.Invoke(e, dlRate.Sum(), bytesVals.Sum());
            };
            Action<double, double, long> WrapUl(int i, NetworkAdapterInfo adapter) => (e, r, b) =>
            {
                lock (aggLock) { ulRate[i] = r; }
                onNicUploadProgress?.Invoke(adapter, e, r, b);
                onUploadProgress?.Invoke(e, ulRate.Sum(), bytesVals.Sum());
            };
            Action<string, double, double> WrapAdapterRates(NetworkAdapterInfo adapter) => (name, dl, ul) =>
            {
                onNicAdapterRates?.Invoke(adapter, dl, ul);
                onAdapterRates?.Invoke(name, dl, ul);
            };
            Action<long> WrapBytes(int i) => b => { lock (aggLock) { bytesVals[i] = b; } onTotalBytes?.Invoke(bytesVals.Sum()); };
            Action<int> WrapAct(int i) => c => { lock (aggLock) { actCount[i] = c; } onActiveThreadCount?.Invoke(actCount.Sum()); };
            Action<double> WrapAvgDl(int i) => v => { lock (aggLock) { avgDl[i] = v; } onAverageDownload?.Invoke(avgDl.Sum()); };
            Action<double> WrapAvgUl(int i) => v => { lock (aggLock) { avgUl[i] = v; } onAverageUpload?.Invoke(avgUl.Sum()); };
            Action<double> WrapAvgTot(int i) => v => { lock (aggLock) { avgTot[i] = v; } onAverageTotal?.Invoke(avgTot.Sum()); };
            Action<double> WrapAvgSpd(int i) => v => { lock (aggLock) { avgSpd[i] = v; } onAverageSpeed?.Invoke(avgSpd.Sum()); };

            var tasks = new List<Task<SpeedTestResult>>();
            for (int i = 0; i < n; i++)
            {
                var idx = i;
                var adapter = nicClients[i].adapter;
                var client = nicClients[i].client;
                var nicOnly = new List<NetworkAdapterInfo> { adapter };
                var nicGw = !string.IsNullOrEmpty(adapter.Gateway) ? adapter.Gateway : gateway;
                Action<double>? nicLatency = metricCallbacks?.OnLatency == null ? onLatency : v => metricCallbacks!.OnLatency!(adapter, v);
                Action<double>? nicWanLatency = metricCallbacks?.OnWanLatency == null ? onWanLatency : v => metricCallbacks!.OnWanLatency!(adapter, v);
                Action<double>? nicJitter = metricCallbacks?.OnJitterRtt == null ? onJitter : v => metricCallbacks!.OnJitterRtt!(adapter, v);

                Task<SpeedTestResult> t;
                if (hasDl && hasUl)
                    t = RunFullTestAsync(dlUrls, ulUrls, perNicThreads, nicOnly, profileName, nicGw,
                        WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        nicLatency, nicWanLatency, nicJitter, WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
                else if (hasDl)
                    t = RunMultiUrlTestAsync(dlUrls, perNicThreads, nicOnly, profileName, nicGw,
                        null, WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        nicLatency, nicWanLatency, nicJitter, WrapAvgSpd(idx), WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
                else
                    t = RunUploadTestAsync(ulUrls, perNicThreads, nicOnly, profileName, nicGw,
                        WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        nicLatency, nicWanLatency, nicJitter, WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
                var task = t;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct);
                    if (_options.AdaptiveThreadsEnabled && idx > 0) { try { await Task.Delay(300 * idx, ct); } catch (OperationCanceledException) { throw; } }
                    try { return await task; }
            catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Logger.Log($"网卡 {adapter.Name} 测速失败: {ex.Message}");
                        return new SpeedTestResult
                        {
                            Timestamp = DateTime.Now,
                            NodeName = profileName,
                            NetworkAdapterName = adapter.Name,
                            ErrorMessage = ex.Message,
                            TestType = hasDl && hasUl ? "双向" : hasDl ? "下载" : "上传"
                        };
                    }
                    finally { gate.Release(); }
                }));
            }

            var results = (await Task.WhenAll(tasks)).ToList();
            for (var i = 0; i < results.Count && i < nicClients.Count; i++)
                results[i].NetworkAdapterId = nicClients[i].adapter.Id;
            foreach (var a in failedAdapters)
            {
                results.Add(new SpeedTestResult
                {
                    Timestamp = DateTime.Now,
                    NodeName = profileName,
                    NetworkAdapterName = a.Name,
                    ErrorMessage = "无法创建绑定连接",
                    TestType = hasDl && hasUl ? "双向" : hasDl ? "下载" : "上传"
                });
            }
            return results;
        }
        finally
        {
            lossCts.Cancel();
            try { await lossMonitor; } catch { }

            foreach (var x in nicClients)
                if (x.client != null && !ReferenceEquals(x.client, _httpClient))
                    x.client.Dispose();
        }
    }

}
