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

        var nicMonitorTask = StartNicMonitor(overall, ctLinked, adapters, nicState,
            onDownloadProgress, onUploadProgress, onAdapterRates,
            onAverageDownload, onAverageUpload, onAverageTotal, onAverageSpeed,
            totalBytesDownloaded, onTotalBytes, tc: threadCount, adaptive: adaptive, throughputMode: 1);

        var gwTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss);

        var urlBalancer = new UrlBalancer(urls);

        // 单次下载迭代：按 worker 均衡分配 URL，超时/失败自动切换最优 URL
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
                    requestCt, client);
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
            { var idx = i; var url = urls[idx % urls.Count];
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

    private static IPAddress? ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try { return Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
        catch (Exception ex) { Logger.Log($"DNS resolve failed for {host}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// 延迟测试：UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 五层回退
    /// </summary>
    private async Task<double> TestGatewayLatencyAsync(string host, CancellationToken ct, IPAddress? ip = null)
    {
        var latencies = new List<double>();

        // 第一层：UDP 探测（端口 33434，主机回 ICMP Port Unreachable）
        try
        {
            var ipv4 = ip ?? (await Dns.GetHostAddressesAsync(host, ct)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                using var udp = new UdpClient();
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
                        await udp.SendAsync(probe, probe.Length);
                        try
                        {
                            var receiveTask = udp.ReceiveAsync();
                            using var probeCts = new CancellationTokenSource(1000);
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, probeCts.Token);
                            var winner = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
                            if (winner == receiveTask) latencies.Add(sw.Elapsed.TotalMilliseconds);
                        }
                        catch (SocketException) { }
                    }
                    catch { }
                    if (ct.IsCancellationRequested) break;
                    if (i < 4) try { await Task.Delay(50, ct); } catch { break; }
                }
            }
        }
        catch { }
        if (latencies.Count > 0) { Logger.Log($"延迟({host}): UDP={latencies.Average():F1}ms"); return latencies.Average(); }

        // 第二层：ICMP Ping
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

        // 第三层：TCP 连接 443
        for (int i = 0; i < 5; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var tcp = new TcpClient();
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
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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
        using var response = await (client ?? _httpClient).GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
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
    /// URL 负载均衡器：worker 均分 URL；不健康 URL 的线程自动切到最优 URL。
    /// </summary>
    private sealed class UrlBalancer
    {
        private sealed class UrlHealth
        {
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

        public UrlBalancer(IEnumerable<string> urls)
        {
            _urls = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var u in _urls) _health[u] = new UrlHealth();
        }

        public string GetUrlForWorker(int workerId)
        {
            lock (_sync)
            {
                if (_urls.Count == 0) return string.Empty;
                var preferred = _urls[Math.Abs(workerId) % _urls.Count];
                if (IsHealthy(preferred)) return preferred;

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
    private sealed class AdaptiveController
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
        private int _workerSeq;

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
        private bool _increaseAllowed = true;
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

                if (!_increaseAllowed) continue;
                SetTarget(Math.Min(_target + 2, _capacity));
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
            target = Math.Clamp(target, 1, _capacity);
            List<int>? trimSlots = null;
            var wakeCount = 0;
            lock (_sync)
            {
                _target = target;
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
            _recentRates.Enqueue(throughput);
            while (_recentRates.Count > 10) _recentRates.Dequeue();

            if (compensating)
            {
                _increaseAllowed = false;
                _evalCount = 0;
                SetTarget(Math.Min(_bestTarget, _capacity));
                return;
            }

            if (now < _cooldownUntilUtc) return;

            var previousBest = _bestRate;
            if (throughput > _bestRate)
            {
                _bestRate = throughput;
                _bestTarget = Math.Min(_target, _capacity);
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
                    SetTarget(_bestTarget);
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
            if (_capacityIndex >= _capacities.Count - 1) return;
            if (!_increaseAllowed) return;
            if (now < _nextCapacityEvalUtc) return;
            if (_target < _capacity * 0.9 && _current < _capacity * 0.9) return;

            var latestRate = _recentRates.Count > 0 ? _recentRates.Last() : 0;
            var newEff = latestRate / Math.Max(1, _target);
            var oldEff = _bestRate / Math.Max(1, _bestTarget);
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
                SetTarget(_bestTarget);
                _nextCapacityEvalUtc = now.AddSeconds(7);
                _nextReevaluateUtc = now.AddSeconds(7);
            }
        }

        private void ExpandCapacity()
        {
            if (_capacityIndex >= _capacities.Count - 1) return;
            var oldCapacity = _capacity;
            _capacityIndex++;
            _capacity = _capacities[_capacityIndex];
            _evalCount = 0;
            _pulseIntervalMs = 200;
            _increaseAllowed = true;
            StartWorkerBatch(_capacity - oldCapacity);
        }

        private void DowngradeCapacity(DateTime now)
        {
            if (_capacityIndex <= 0) return;
            _capacityIndex--;
            _capacity = _capacities[_capacityIndex];
            _increaseAllowed = false;
            _evalCount = 0;
            _declineCount = 0;
            SetTarget(Math.Min(_bestTarget, _capacity));
            _cooldownUntilUtc = now.AddSeconds(20);
            _nextReevaluateUtc = now.AddSeconds(30);
            _nextCapacityEvalUtc = now.AddSeconds(20);
        }

        private double ComputeCv()
        {
            if (_recentRates.Count < 3) return 0;
            var values = _recentRates.ToList();
            var mean = values.Average();
            if (mean <= 0) return 0;
            var variance = values.Average(x => (x - mean) * (x - mean));
            return Math.Sqrt(variance) / mean;
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
                        var totalThroughput = sr + ur_;
                        var adaptiveValue = throughputMode == 1 ? sr : throughputMode == 2 ? ur_ : sr + ur_;
                        adaptive.Observe(adaptiveValue, st.IsCompensating, overall.Elapsed.TotalSeconds);
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

    private (Task? gatewayTask, Task wanTask, Task jitterTask, Task lossTask) StartGatewayAndWanLatency(string? gateway, CancellationToken ctLinked, Action<double>? onLatency, Action<double>? onWanLatency, Action<double>? onJitter, Action<PacketLossSample>? onPacketLoss)
    {
        Task? gt = null;
        if (!string.IsNullOrEmpty(gateway))
        {
            Logger.Log($"网关延迟测试启动: gateway={gateway}");
            gt = Task.Run(async () => { try { while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(gateway, ctLinked); if (v > 0) onLatency?.Invoke(v); } catch { break; } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } } catch { } });
        }
        var wt = Task.Run(async () => { try { var wHost = "8.8.8.8"; while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(wHost, ctLinked); if (v > 0) onWanLatency?.Invoke(v); } catch { } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } } catch { } });
        var jt = Task.Run(async () => { try { try { await Task.Delay(TimeSpan.FromSeconds(_options.AverageDelaySec), ctLinked); } catch { return; } var jHost = string.IsNullOrEmpty(_options.JitterTargetHost) ? "8.8.8.8" : _options.JitterTargetHost; var jInterval = Math.Max(500, _options.JitterPollIntervalMs); while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(jHost, ctLinked); if (v > 0) onJitter?.Invoke(v); } catch { } try { await Task.Delay(jInterval, ctLinked); } catch { break; } } } catch { } });
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
                    await udp.SendAsync(probe, probe.Length);
                    try
                    {
                        var receiveTask = udp.ReceiveAsync();
                        using var probeCts = new CancellationTokenSource(timeoutMs);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, probeCts.Token);
                        var winner = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
                        if (winner == receiveTask) received++;
                    }
                    catch (OperationCanceledException) { }
                    catch (SocketException) { }
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
        var http = client ?? _httpClient;

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
        var gwUploadTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var urlBalancer = new UrlBalancer(urls);

        async Task RunOneUploadAsync(int workerId, CancellationToken requestCt, CancellationToken globalCt)
        {
            var url = urlBalancer.GetUrlForWorker(workerId);
            try
            {
                using var co = new ByteArrayContent(buf);
                co.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var rq = new HttpRequestMessage(HttpMethod.Post, url) { Content = co };
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);
                using var _ = await http.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead, linked.Token);
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
            { var idx = i; var url = urls[idx % urls.Count];
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
        return new SpeedTestResult { Timestamp = DateTime.Now, DownloadMbps = dl, UploadMbps = ul, PeakMbps = nicState.PeakRate, LatencyMs = 0, JitterMs = 0, PacketLoss = 0, NodeName = profileName, NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")), BytesDownloaded = 0, BytesUploaded = ulBytes, DurationSeconds = ts, ThreadCount = adaptive != null ? Math.Max(1, adaptive.Peak) : threadCount, UrlDetails = new() };
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
        var http = client ?? _httpClient;

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

        var nicMonitorFull = StartNicMonitor(overall, ctLinked, adapters, nicState, onDownloadProgress, onUploadProgress, onAdapterRates, onAverageDownload, onAverageUpload, onAverageTotal, null, bytesDl, onTotalBytes, tc: threadCount, adaptive: adaptive, throughputMode: 0);
        var gwFullTasks = StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency, onJitter, onPacketLoss);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var dlBalancer = new UrlBalancer(dlUrls);
        var ulBalancer = new UrlBalancer(ulUrls);
        async Task RunOneFullAsync(bool isDl, string url, CancellationToken requestCt)
        {
            try
            {
                if (isDl)
                {
                    using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                    headerCts.CancelAfter(TimeSpan.FromSeconds(10));
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
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
                    using var co = new ByteArrayContent(buf);
                    co.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    using var rq = new HttpRequestMessage(HttpMethod.Post, url) { Content = co };
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);
                    using var _ = await http.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead, linked.Token);
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
                    var url = isDl ? dlUrls[(idx / 2) % dlUrls.Count] : ulUrls[(idx / 2) % ulUrls.Count];
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
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetSpeedTest/1.4.1");
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
        List<NetworkAdapterInfo>? monitorAdapters = null)
    {
        bool hasDl = dlUrls.Count > 0, hasUl = ulUrls.Count > 0;
        if (!hasDl && !hasUl) throw new ArgumentException("无可用测速地址");
        if (adapters == null || adapters.Count == 0) throw new ArgumentException("至少需要一个活跃网卡");

        _dnsCache.Clear();
        // 单网卡时不强制绑定源 IP，走系统默认路由，最大程度兼容不同电脑的网络环境
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

            SpeedTestResult singleResult;
            if (hasDl && hasUl)
                singleResult = await RunFullTestAsync(dlUrls, ulUrls, threadCount, monitor, profileName, nicGw,
                    singleDl, singleUl, singleAr, onActiveThreadCount,
                    onLatency, onWanLatency, onJitter,
                    onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, 0, ct);
            else if (hasDl)
                singleResult = await RunMultiUrlTestAsync(dlUrls, threadCount, monitor, profileName, nicGw,
                    null, singleDl, singleUl, singleAr, onActiveThreadCount,
                    onLatency, onWanLatency, onJitter,
                    onAverageSpeed, onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, onPacketLoss, 0, ct);
            else
                singleResult = await RunUploadTestAsync(ulUrls, threadCount, monitor, profileName, nicGw,
                    singleDl, singleUl, singleAr, onActiveThreadCount,
                    onLatency, onWanLatency, onJitter,
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

                Task<SpeedTestResult> t;
                if (hasDl && hasUl)
                    t = RunFullTestAsync(dlUrls, ulUrls, perNicThreads, nicOnly, profileName, nicGw,
                        WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        onLatency, onWanLatency, onJitter, WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
                else if (hasDl)
                    t = RunMultiUrlTestAsync(dlUrls, perNicThreads, nicOnly, profileName, nicGw,
                        null, WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        onLatency, onWanLatency, onJitter, WrapAvgSpd(idx), WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
                else
                    t = RunUploadTestAsync(ulUrls, perNicThreads, nicOnly, profileName, nicGw,
                        WrapDl(idx, adapter), WrapUl(idx, adapter), WrapAdapterRates(adapter), WrapAct(idx),
                        onLatency, onWanLatency, onJitter, WrapAvgDl(idx), WrapAvgUl(idx), WrapAvgTot(idx), WrapBytes(idx), null, perNicAdaptiveCap, ct, client);
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
