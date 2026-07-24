using System.Buffers;
using System.Diagnostics;
using System.IO;
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
        Action<double>? onAverageSpeed = null,
        Action<double>? onAverageDownload = null,
        Action<double>? onAverageUpload = null,
        Action<double>? onAverageTotal = null,
        Action<long>? onTotalBytes = null,
        CancellationToken ct = default)
    {
        if (urls == null || urls.Count == 0)
            throw new ArgumentException("URL 列表不能为空");
        if (adapters == null || adapters.Count == 0)
            throw new ArgumentException("至少需要一个活跃网卡");

        threadCount = Math.Clamp(threadCount, 1, Math.Max(1, _options.ThreadCount));

        var overall = Stopwatch.StartNew();
        var urlDetails = new List<UrlTestDetail>();
        var allRateSamples = new List<double>();
        var globalLock = new object();
        int activeThreads = 0;
        long totalBytesDownloaded = 0;
        var nicState = new NicState();

        // 内部取消令牌：方法返回时取消所有后台任务
        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;

        // 信号量控制并发度
        using var semaphore = new SemaphoreSlim(threadCount + _options.CompensationExtraThreads, threadCount + _options.CompensationExtraThreads);

        StartNicMonitor(overall, ctLinked, adapters, nicState,
            onDownloadProgress, onUploadProgress, onAdapterRates,
            onAverageDownload, onAverageUpload, onAverageTotal, onAverageSpeed,
            new LongRef { Value = totalBytesDownloaded }, onTotalBytes, tc: threadCount);

        StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency);

        // 创建线程池：每 1 秒 +2 线程，每个 URL 可被多线程同时连接
        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (nicState.AdaptiveCap > 0 && i >= nicState.AdaptiveCap) break;

            var url = urls[i % urls.Count];
            tasks.Add(Task.Run(async () =>
            {
                try { await semaphore.WaitAsync(ctLinked); }
                catch { return; }
                var current = Interlocked.Increment(ref activeThreads);

                try
                {
                    onActiveThreadCount?.Invoke(current);
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
                                if (delta > 0) Interlocked.Add(ref totalBytesDownloaded, delta);
                                detail.BytesDownloaded = bytes;
                                detail.AvgMbps = rate;
                                detail.DurationSeconds = elapsed;
                                onUrlProgress?.Invoke(url, detail.Host, elapsed, rate, bytes);
                            },
                            ctLinked);
                        detail.AvgMbps = result.avgMbps;
                        detail.PeakMbps = result.peakMbps;
                        detail.BytesDownloaded = result.totalBytes;
                        detail.DurationSeconds = result.duration;
                        detail.RateHistory = result.history;

                        lock (globalLock) { allRateSamples.AddRange(result.history.Select(p => p.RateMbps)); }
                    }
                    catch (OperationCanceledException)
                    {
                        detail.IsFailed = true;
                        detail.ErrorMessage = "超时取消";
                    }
                    catch (Exception ex)
                    {
                        detail.IsFailed = true;
                        detail.ErrorMessage = ex.Message;
                    }
                }
                finally
                {
                    try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                    current = Interlocked.Decrement(ref activeThreads);
                    onActiveThreadCount?.Invoke(current);
                }
            }));

            // 线程启动间隔
            if (_options.ThreadRampUpMs > 0 && i + 1 < threadCount)
            {
                try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); }
                catch (OperationCanceledException) { break; }
            }
        }

        // 等待所有 URL 测速完成
        await Task.WhenAll(tasks);

        overall.Stop();

        // 汇总结果
        var successful = urlDetails.Where(d => !d.IsFailed).ToList();
        var totalBytes = successful.Sum(d => d.BytesDownloaded);
        var peakAggMbps = allRateSamples.Count > 0 ? allRateSamples.Max() : 0;

        // 去重：合并同一 URL 的多线程明细
        var dedupedDetails = urlDetails
            .GroupBy(d => d.Url)
            .Select(g =>
            {
                var first = g.First();
                var succeeded = g.Where(d => !d.IsFailed).ToList();
                var merged = new UrlTestDetail
                {
                    Url = first.Url,
                    Host = first.Host,
                    AvgMbps = succeeded.Count > 0 ? succeeded.Average(d => d.AvgMbps) : 0,
                    PeakMbps = succeeded.Count > 0 ? succeeded.Max(d => d.PeakMbps) : 0,
                    BytesDownloaded = g.Sum(d => d.BytesDownloaded),
                    DurationSeconds = succeeded.Count > 0 ? succeeded.Average(d => d.DurationSeconds) : 0,
                    IsFailed = g.All(d => d.IsFailed),
                    ErrorMessage = g.FirstOrDefault(d => d.IsFailed)?.ErrorMessage
                };
                return merged;
            })
            .ToList();

        // 最终内网延迟
        double finalLatency = 0;
        if (!string.IsNullOrEmpty(gateway))
        {
            try { finalLatency = await TestGatewayLatencyAsync(gateway, ct); } catch { }
        }

        // 停止所有后台报告任务
        internalCts.Cancel();

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
            LatencyMs = finalLatency,
            JitterMs = 0,
            PacketLoss = 0,
            NodeName = profileName,
            NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")),
            BytesDownloaded = totalBytes,
            BytesUploaded = 0,
            DurationSeconds = overall.Elapsed.TotalSeconds,
            ThreadCount = threadCount,
            UrlDetails = dedupedDetails
        };
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

    /// <summary>
    /// 延迟测试：ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 四层回退
    /// </summary>
    private async Task<double> TestGatewayLatencyAsync(string host, CancellationToken ct)
    {
        const int count = 10;
        var latencies = new List<double>();

        // 第一层：ICMP Ping
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

        // 第二层：TCP 连接 443
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

        // 第三层：HTTPS HEAD
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

        // 第四层：HTTP HEAD（网关/路由器端口 80）
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
            CancellationToken ct = default)
    {
        const int bufferSize = 64 * 1024;

        var stopwatch = Stopwatch.StartNew();
        long totalBytes = 0;
        var rateSamples = new List<double>();
        var history = new List<RateDataPoint>();

        using var response = await _httpClient.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, ct);
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

                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), ct);
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

    // ====== 共享监控方法 ======

    private sealed class NicState {
        public long FR, FS, AR, AS, BR, BS;
        public bool R;
        public volatile bool IsCompensating;
        public double PeakRate, BelowThresholdSec, DropStartTime;
        public long DropStartBytes;
        public double TotalDropDuration;
        public long TotalDropBytes;
        public volatile int SaturationHits, AdaptiveCap;
        public double PeakEfficiency;
    }
    private sealed class LongRef { public long Value; }

    private void StartNicMonitor(Stopwatch overall, CancellationToken c, List<NetworkAdapterInfo> ad, NicState st,
        Action<double, double, long>? dl, Action<double, double, long>? ul, Action<string, double, double>? ar,
        Action<double>? adl, Action<double>? aul, Action<double>? atl, Action<double>? as_, LongRef tbd,
        Action<long>? tb = null, int tc = 128)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var lb = new Dictionary<string, (long R, long S)>(); double lt = 0; bool fp = true;
                var dh = new List<(double, double)>(); var uh = new List<(double, double)>();
                var ws = _options.RateWindowSec; bool as2 = false; long asb = 0; double ast = 0;
                var totalBytes = tbd;
                while (!c.IsCancellationRequested)
                {
                    try { await Task.Delay(_options.NicPollIntervalMs, c); } catch { break; }
                    var e = overall.Elapsed.TotalSeconds; long dd = 0, du = 0;
                    foreach (var a in ad)
                    { var n = _networkInfo.GetCurrentBytes(a.Id); if (!n.HasValue) continue; if (lb.TryGetValue(a.Id, out var p)) { var x = n.Value.Received - p.R; var y = n.Value.Sent - p.S; if (x < 0 || y < 0) { lb[a.Id] = (n.Value.Received, n.Value.Sent); continue; } dd += x; du += y; var dt = e - lt; ar?.Invoke(a.Name, dt > 0 ? (x * 8.0) / (dt * 1_000_000.0) : 0, dt > 0 ? (y * 8.0) / (dt * 1_000_000.0) : 0); } lb[a.Id] = (n.Value.Received, n.Value.Sent); }
                    if (fp) { fp = false; st.FR = lb.Values.Sum(x => x.R); st.FS = lb.Values.Sum(x => x.S); }
                    else { st.AR = lb.Values.Sum(x => x.R); st.AS = lb.Values.Sum(x => x.S); }
                    if (!st.R && e >= _options.AverageDelaySec) { st.R = true; st.BR = lb.Values.Sum(x => x.R); st.BS = lb.Values.Sum(x => x.S); }
                    tb?.Invoke(Math.Max(0, st.AR + st.AS - st.FR - st.FS));
                    if (lt > 0) { var dt = e - lt; var dr = dt > 0 ? (dd * 8.0) / (dt * 1_000_000.0) : 0; var ur = dt > 0 ? (du * 8.0) / (dt * 1_000_000.0) : 0; dh.Add((e, dr)); dh.RemoveAll(x => e - x.Item1 > ws); uh.Add((e, ur)); uh.RemoveAll(x => e - x.Item1 > ws); dl?.Invoke(e, dh.Count > 0 ? dh.Average(x => x.Item2) : dr, Interlocked.Read(ref totalBytes.Value)); ul?.Invoke(e, uh.Count > 0 ? uh.Average(x => x.Item2) : ur, 0); if (st.R) { var ae = e - _options.AverageDelaySec; adl?.Invoke(ae > 0 ? (st.AR - st.BR) * 8.0 / (ae * 1_000_000.0) : 0); aul?.Invoke(ae > 0 ? (st.AS - st.BS) * 8.0 / (ae * 1_000_000.0) : 0); atl?.Invoke(ae > 0 ? (st.AR - st.BR + st.AS - st.BS) * 8.0 / (ae * 1_000_000.0) : 0); } }
                    if (_options.CompensationEnabled && st.R)
                    {
                        var sr = dh.Count > 0 ? dh.Average(x => x.Item2) : 0;
                        var ur_ = uh.Count > 0 ? uh.Average(x => x.Item2) : 0;
                        var combined = Math.Max(sr, ur_);
                        if (combined > st.PeakRate) st.PeakRate = combined;
                        if (!st.IsCompensating && st.PeakRate > 0 && combined < st.PeakRate * _options.CompensationThreshold)
                        {
                            st.BelowThresholdSec += _options.NicPollIntervalMs / 1000.0;
                            if (st.BelowThresholdSec >= _options.CompensationConfirmSec)
                            {
                                st.IsCompensating = true;
                                st.DropStartTime = e;
                                st.DropStartBytes = st.AR + st.AS;
                            }
                        }
                        else if (st.IsCompensating && combined > st.PeakRate * 0.8)
                        {
                            st.IsCompensating = false;
                            st.TotalDropDuration += e - st.DropStartTime;
                            st.TotalDropBytes += Math.Max(0, st.AR + st.AS - st.DropStartBytes);
                        }
                        else if (!st.IsCompensating) { st.BelowThresholdSec = 0; }
                    }
                    if (_options.AdaptiveThreadsEnabled && st.R && dh.Count > 0 && tc >= 8)
                    {
                        var tr = (dh.Count > 0 ? dh.Average(x => x.Item2) : 0)
                               + (uh.Count > 0 ? uh.Average(x => x.Item2) : 0);
                        var eff = tr / tc;
                        if (eff > st.PeakEfficiency) st.PeakEfficiency = eff;
                        if (eff < st.PeakEfficiency * 0.3)
                        {
                            st.SaturationHits++;
                            if (st.SaturationHits >= 2) st.AdaptiveCap = tc;
                        }
                        else if (eff > st.PeakEfficiency * 0.7)
                        {
                            st.SaturationHits = 0;
                            if (st.AdaptiveCap > 0) st.AdaptiveCap = 0;
                        }
                        else { st.SaturationHits = 0; }
                    }
                    lt = e;
                    if (!as2 && e >= _options.AverageDelaySec) { as2 = true; asb = Interlocked.Read(ref totalBytes.Value); ast = e; }
                    if (as2 && as_ != null) { var b = Interlocked.Read(ref totalBytes.Value) - asb; var t_ = e - ast; as_(t_ > 0 ? (b * 8.0) / (t_ * 1_000_000.0) : 0); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Log($"NIC error: {ex.Message}"); }
        });
    }

    private void StartGatewayAndWanLatency(string? gateway, CancellationToken ctLinked, Action<double>? onLatency, Action<double>? onWanLatency)
    {
        if (!string.IsNullOrEmpty(gateway)) { Logger.Log($"网关延迟测试启动: gateway={gateway}"); _ = Task.Run(async () => { try { while (!ctLinked.IsCancellationRequested) { try { var v = await TestGatewayLatencyAsync(gateway, ctLinked); if (v > 0) onLatency?.Invoke(v); } catch { break; } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } } catch { } }); }
        _ = Task.Run(async () => { var t = new[] { "www.baidu.com", "8.8.8.8", "114.114.114.114", "www.aliyun.com", "www.qq.com", "www.jd.com", "www.163.com", "www.bilibili.com", "1.1.1.1", "223.5.5.5", "119.29.29.29", "www.taobao.com" }; while (!ctLinked.IsCancellationRequested) { try { using var c3 = new CancellationTokenSource(TimeSpan.FromSeconds(3)); using var tk = CancellationTokenSource.CreateLinkedTokenSource(ctLinked, c3.Token); double b = double.MaxValue; object l = new(); var p = t.Select(async x => { var v = await TestGatewayLatencyAsync(x, tk.Token); if (v > 0) lock (l) { if (v < b) b = v; } }); await Task.WhenAny(Task.WhenAll(p), Task.Delay(3000, ctLinked)); if (b < double.MaxValue) onWanLatency?.Invoke(b); } catch { } try { await Task.Delay(_options.LatencyPollIntervalMs, ctLinked); } catch { break; } } });
    }

    // ====== 上传测速 ======

    public async Task<SpeedTestResult> RunUploadTestAsync(
        List<string> urls, int threadCount, List<NetworkAdapterInfo> adapters, string profileName,
        string? gateway = null,
        Action<double, double, long>? onDownloadProgress = null, Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null, Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null, Action<double>? onWanLatency = null,
        Action<double>? onAverageDownload = null, Action<double>? onAverageUpload = null, Action<double>? onAverageTotal = null,
        Action<long>? onTotalBytes = null, CancellationToken ct = default)
    {
        if (urls.Count == 0) throw new ArgumentException("URL 列表不能为空");
        if (adapters == null || adapters.Count == 0) throw new ArgumentException("至少需要一个活跃网卡");
        threadCount = Math.Clamp(threadCount, 1, Math.Max(1, _options.ThreadCount));
        var overall = Stopwatch.StartNew(); int activeThreads = 0; var dummy = new LongRef();
        var nicState = new NicState();

        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;
        using var semaphore = new SemaphoreSlim(threadCount + _options.CompensationExtraThreads, threadCount + _options.CompensationExtraThreads);

        StartNicMonitor(overall, ctLinked, adapters, nicState, onDownloadProgress, onUploadProgress, onAdapterRates, onAverageDownload, onAverageUpload, onAverageTotal, null, dummy, onTotalBytes, tc: threadCount);
        StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (nicState.AdaptiveCap > 0 && i >= nicState.AdaptiveCap) break; var url = urls[i % urls.Count];
            tasks.Add(Task.Run(async () =>
            {
                try { await semaphore.WaitAsync(ctLinked); } catch { return; }
                var c = Interlocked.Increment(ref activeThreads);
                try { onActiveThreadCount?.Invoke(c); while (!ctLinked.IsCancellationRequested) { try { using var co = new ByteArrayContent(buf); co.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream"); using var rq = new HttpRequestMessage(HttpMethod.Post, url) { Content = co }; using var _ = await _httpClient.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead, ctLinked); } catch (OperationCanceledException) { break; } catch { try { await Task.Delay(500, ctLinked); } catch { break; } } } }
                finally { try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } onActiveThreadCount?.Invoke(Interlocked.Decrement(ref activeThreads)); }
            }));
            if (_options.ThreadRampUpMs > 0 && i + 1 < threadCount) { try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); } catch { break; } }
        }
        await Task.WhenAll(tasks); overall.Stop(); internalCts.Cancel();
        var ts = Math.Max(overall.Elapsed.TotalSeconds, 0.1); double dl, ul;
        if (nicState.R) { var e = Math.Max(ts - _options.AverageDelaySec, 0.1); var drop = _options.CompensationEnabled ? nicState.TotalDropDuration : 0; var adj = Math.Max(e - drop, 0.1); dl = Math.Max(0, (nicState.AR - nicState.BR) * 8.0 / (adj * 1_000_000.0)); ul = Math.Max(0, (nicState.AS - nicState.BS) * 8.0 / (adj * 1_000_000.0)); }
        else { dl = Math.Max(0, (nicState.AR - nicState.FR) * 8.0 / (ts * 1_000_000.0)); ul = Math.Max(0, (nicState.AS - nicState.FS) * 8.0 / (ts * 1_000_000.0)); }
        double fl = 0; if (!string.IsNullOrEmpty(gateway)) try { fl = await TestGatewayLatencyAsync(gateway, ct); } catch { }
        var ulBytes = Math.Max(0, nicState.R ? nicState.AS - nicState.BS : nicState.AS - nicState.FS);
        return new SpeedTestResult { Timestamp = DateTime.Now, DownloadMbps = dl, UploadMbps = ul, PeakMbps = 0, LatencyMs = fl, JitterMs = 0, PacketLoss = 0, NodeName = profileName, NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")), BytesDownloaded = 0, BytesUploaded = ulBytes, DurationSeconds = ts, ThreadCount = threadCount, UrlDetails = new() };
    }

    // ====== 双向测速（下载+上传同时跑） ======

    public async Task<SpeedTestResult> RunFullTestAsync(
        List<string> dlUrls, List<string> ulUrls, int threadCount, List<NetworkAdapterInfo> adapters, string profileName,
        string? gateway = null,
        Action<double, double, long>? onDownloadProgress = null, Action<double, double, long>? onUploadProgress = null,
        Action<string, double, double>? onAdapterRates = null, Action<int>? onActiveThreadCount = null,
        Action<double>? onLatency = null, Action<double>? onWanLatency = null,
        Action<double>? onAverageDownload = null, Action<double>? onAverageUpload = null, Action<double>? onAverageTotal = null,
        Action<long>? onTotalBytes = null, CancellationToken ct = default)
    {
        bool hasDl = dlUrls.Count > 0, hasUl = ulUrls.Count > 0;
        if (!hasDl && !hasUl) throw new ArgumentException("无可用测速地址");
        if (!hasDl) return await RunUploadTestAsync(ulUrls, threadCount, adapters, profileName, gateway, onDownloadProgress, onUploadProgress, onAdapterRates, onActiveThreadCount, onLatency, onWanLatency, onAverageDownload, onAverageUpload, onAverageTotal, onTotalBytes, ct);
        if (!hasUl) return await RunMultiUrlTestAsync(dlUrls, threadCount, adapters, profileName, gateway, null, onDownloadProgress, onUploadProgress, onAdapterRates, onActiveThreadCount, onLatency, onWanLatency, onAverageDownload: onAverageDownload, onAverageUpload: onAverageUpload, onAverageTotal: onAverageTotal, onTotalBytes: onTotalBytes, ct: ct);

        threadCount = Math.Clamp(threadCount, 1, Math.Max(1, _options.ThreadCount));
        var overall = Stopwatch.StartNew(); int activeThreads = 0;
        var nicState = new NicState(); var bytesDl = new LongRef();

        using var internalCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TestTimeoutSec)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token, timeoutCts.Token);
        var ctLinked = linkedCts.Token;
        using var semaphore = new SemaphoreSlim(threadCount + _options.CompensationExtraThreads, threadCount + _options.CompensationExtraThreads);

        StartNicMonitor(overall, ctLinked, adapters, nicState, onDownloadProgress, onUploadProgress, onAdapterRates, onAverageDownload, onAverageUpload, onAverageTotal, null, bytesDl, onTotalBytes, tc: threadCount);
        StartGatewayAndWanLatency(gateway, ctLinked, onLatency, onWanLatency);

        var rng = new Random(Guid.NewGuid().GetHashCode()); var buf = new byte[64 * 1024]; rng.NextBytes(buf);
        var tasks = new List<Task>();
        for (int i = 0; i < threadCount; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (nicState.AdaptiveCap > 0 && i >= nicState.AdaptiveCap) break;
            bool isDl = i % 2 == 0;
            var url = isDl ? dlUrls[(i / 2) % dlUrls.Count] : ulUrls[(i / 2) % ulUrls.Count];
            tasks.Add(Task.Run(async () =>
            {
                try { await semaphore.WaitAsync(ctLinked); } catch { return; }
                var c = Interlocked.Increment(ref activeThreads);
                try
                {
                    onActiveThreadCount?.Invoke(c);
                    while (!ctLinked.IsCancellationRequested)
                    {
                        try
                        {
                            if (isDl) { using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ctLinked); await using var s = await resp.Content.ReadAsStreamAsync(ctLinked); var b = ArrayPool<byte>.Shared.Rent(64 * 1024); try { while (!ctLinked.IsCancellationRequested) { var r = await s.ReadAsync(b.AsMemory(0, 64 * 1024), ctLinked); if (r == 0) break; Interlocked.Add(ref bytesDl.Value, r); } } finally { ArrayPool<byte>.Shared.Return(b); } }
                            else { using var co = new ByteArrayContent(buf); co.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream"); using var rq = new HttpRequestMessage(HttpMethod.Post, url) { Content = co }; using var _ = await _httpClient.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead, ctLinked); }
                        }
                        catch (OperationCanceledException) { break; } catch { try { await Task.Delay(500, ctLinked); } catch { break; } }
                    }
                }
                finally { try { semaphore.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } onActiveThreadCount?.Invoke(Interlocked.Decrement(ref activeThreads)); }
            }));
            if (_options.ThreadRampUpMs > 0 && i + 1 < threadCount) { try { await Task.Delay(_options.ThreadRampUpMs, ctLinked); } catch { break; } }
        }

        await Task.WhenAll(tasks); overall.Stop(); internalCts.Cancel();
        var ts_ = Math.Max(overall.Elapsed.TotalSeconds, 0.1); double dl_, ul_;
        if (nicState.R) { var e = Math.Max(ts_ - _options.AverageDelaySec, 0.1); var drop = _options.CompensationEnabled ? nicState.TotalDropDuration : 0; var adj = Math.Max(e - drop, 0.1); dl_ = Math.Max(0, (nicState.AR - nicState.BR) * 8.0 / (adj * 1_000_000.0)); ul_ = Math.Max(0, (nicState.AS - nicState.BS) * 8.0 / (adj * 1_000_000.0)); }
        else { dl_ = Math.Max(0, (nicState.AR - nicState.FR) * 8.0 / (ts_ * 1_000_000.0)); ul_ = Math.Max(0, (nicState.AS - nicState.FS) * 8.0 / (ts_ * 1_000_000.0)); }
        double fl_ = 0; if (!string.IsNullOrEmpty(gateway)) try { fl_ = await TestGatewayLatencyAsync(gateway, ct); } catch { }
        long dlBytes_ = bytesDl.Value, ulBytes_ = Math.Max(0, nicState.R ? nicState.AS - nicState.BS : nicState.AS - nicState.FS);
        return new SpeedTestResult { Timestamp = DateTime.Now, DownloadMbps = dl_, UploadMbps = ul_, PeakMbps = 0, LatencyMs = fl_, JitterMs = 0, PacketLoss = 0, NodeName = profileName, NetworkAdapterName = string.Join(", ", adapters.Select(a => a.Name ?? "")), BytesDownloaded = dlBytes_, BytesUploaded = ulBytes_, DurationSeconds = ts_, ThreadCount = threadCount, UrlDetails = new() };
    }
}
