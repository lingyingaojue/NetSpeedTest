using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using NetSpeedTest.Helpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace NetSpeedTest.ViewModels;

/// <summary>
/// 主测速页 ViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ProfileService _profileService;
    private readonly DataService _dataService;
    private readonly NetworkInfoService _networkInfoService;
    private readonly IServiceProvider _serviceProvider;
    private readonly SpeedTestOptions _options;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _elapsedTimer;
    private EventHandler? _elapsedTickHandler;
    private volatile Stopwatch? _stopwatch;
    private SpeedTestResult? _lastResult;
    private List<SpeedTestResult>? _lastMultiNicResults;
    private string _currentTestMode = "";
    private int _startUrlCount;
    public event Action<string, string>? TestCompletedNotify;
    private readonly List<double> _lanLatencies = new();
    private readonly List<double> _wanLatencies = new();
    private readonly List<double> _jitterSamples = new();
    private readonly Dictionary<string, ObservableCollection<ObservablePoint>> _downloadPointsByNic = new();
    private readonly Dictionary<string, ObservableCollection<ObservablePoint>> _uploadPointsByNic = new();

    [ObservableProperty]
    private bool _showDownloadMetrics = true;
    [ObservableProperty]
    private bool _showUploadMetrics = true;
    [ObservableProperty]
    private bool _showTotalMetrics = true;

    [ObservableProperty]
    private object? _currentPage;

    [RelayCommand]
    private void ClosePage() => CurrentPage = null;

    // ==================== 可绑定属性 ====================

    [ObservableProperty]
    private ObservableCollection<NetworkAdapterInfo> _adapters = new();

    /// <summary>
    /// 网卡勾选列表（多网卡同时测速用）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AdapterSelectionItem> _adapterSelectionItems = new();

    /// <summary>
    /// 图表可切换的网卡选项（“合计” + 各网卡名）
    /// </summary>
    public ObservableCollection<string> ChartAdapterOptions { get; } = new();

    /// <summary>
    /// 当前图表显示的网卡曲线
    /// </summary>
    [ObservableProperty]
    private string _selectedChartAdapter = "合计";

    /// <summary>
    /// 全部网卡实时速率
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AdapterRateItem> _allAdapterRates = new();


    [ObservableProperty]
    private ObservableCollection<SpeedTestProfile> _profiles = new();

    [ObservableProperty]
    private SpeedTestProfile? _selectedProfile;

    /// <summary>
    /// 选中配置下的可选下载 URL 列表（供 CheckBox 绑定）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<UrlSelectionItem> _urlSelectionItems = new();

    /// <summary>
    /// 并发线程数（从设置读取）
    /// </summary>
    private int ThreadCount => _options.ThreadCount;

    /// <summary>
    /// 当前活跃线程数（实时显示）
    /// </summary>
    [ObservableProperty]
    private int _activeThreadCount;

    /// <summary>
    /// 是否正在测速
    /// </summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>
    /// 状态文字
    /// </summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private double? _downloadMbps;

    [ObservableProperty]
    private double? _uploadMbps;

    public string UploadMbpsDisplay => FormatHelper.FormatRate(UploadMbps);

    /// <summary>
    /// 总速率（下载+上传）
    /// </summary>
    public double? TotalRateMbps => DownloadMbps.HasValue || UploadMbps.HasValue ? (DownloadMbps ?? 0) + (UploadMbps ?? 0) : null;

    /// <summary>
    /// 是否存在最近一次测速结果
    /// </summary>
    public bool HasRecentResult => _lastResult != null;

    public double? RecentDownloadMbps => _lastResult?.DownloadMbps;

    public double? RecentUploadMbps => _lastResult?.UploadMbps;

    public double RecentLatencyMs => _lastResult?.LatencyMs ?? 0;

    /// <summary>
    /// 总流量（字节）
    /// </summary>
    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private double? _latencyMs;

    [ObservableProperty]
    private double? _jitterMs;

    /// <summary>
    /// 外网延迟（公网 IP Ping）
    /// </summary>
    [ObservableProperty]
    private double? _wanLatencyMs;

    /// <summary>
    /// 10 秒后平均网速
    /// </summary>
    [ObservableProperty]
    private double? _averageMbps;

    /// <summary>
    /// NIC 下载累计平均值
    /// </summary>
    [ObservableProperty]
    private double? _averageDownloadMbps;

    /// <summary>
    /// NIC 上传累计平均值
    /// </summary>
    [ObservableProperty]
    private double? _averageUploadMbps;

    [ObservableProperty]
    private double? _averageTotalMbps;

    /// <summary>
    /// 实时测速时长（秒）
    /// </summary>
    [ObservableProperty]
    private double? _elapsedSeconds;

    [ObservableProperty]
    private ObservableCollection<SpeedTestResult> _recentRecords = new();

    [ObservableProperty]
    private ObservableCollection<ObservablePoint> _downloadRatePoints = new();

    /// <summary>
    /// 上传速率数据点（图表绑定）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ObservablePoint> _uploadRatePoints = new();

    /// <summary>
    /// 每个 URL 的测速明细结果
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<UrlTestDetail> _urlTestDetails = new();

    public ObservableCollection<ISeries> DownloadChartSeries { get; } = new();

    public ObservableCollection<ISeries> UploadChartSeries { get; } = new();

    public Axis[] XAxes { get; } = new[]
    {
        new Axis
        {
            TextSize = 10,
            LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(
                new SkiaSharp.SKColor(160, 160, 160))
        }
    };

    public Axis[] YAxes { get; } = new[]
    {
        new Axis
        {
            TextSize = 10,
            LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(
                new SkiaSharp.SKColor(160, 160, 160))
        }
    };

    // ==================== 回调方法 ====================

    partial void OnSelectedProfileChanged(SpeedTestProfile? value)
    {
        UpdateUrlSelectionItems();

        // 默认全选
        foreach (var item in UrlSelectionItems)
            item.IsSelected = true;
    }

    partial void OnSelectedChartAdapterChanged(string value)
    {
        if (DownloadChartSeries.Count == 0 || UploadChartSeries.Count == 0) return;

        var dlPoints = value == "合计" ? DownloadRatePoints : (_downloadPointsByNic.TryGetValue(value, out var p) ? p : DownloadRatePoints);
        var ulPoints = value == "合计" ? UploadRatePoints : (_uploadPointsByNic.TryGetValue(value, out var up) ? up : UploadRatePoints);

        DownloadChartSeries[0].Values = dlPoints;
        UploadChartSeries[0].Values = ulPoints;
    }


    // ==================== 构造函数 ====================

    public MainViewModel(ProfileService profileService, DataService dataService,
                         NetworkInfoService networkInfoService, IServiceProvider serviceProvider,
                         SpeedTestOptions options)
    {
        _profileService = profileService;
        _dataService = dataService;
        _networkInfoService = networkInfoService;
        _serviceProvider = serviceProvider;
        _options = options;

        DownloadChartSeries.Add(new LineSeries<ObservablePoint>
        {
            Values = DownloadRatePoints,
            Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(
                new SkiaSharp.SKColor(88, 166, 255)) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = 0.3
        });

        UploadChartSeries.Add(new LineSeries<ObservablePoint>
        {
            Values = UploadRatePoints,
            Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(
                new SkiaSharp.SKColor(247, 120, 186)) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = 0.3
        });

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadInitialDataAsync(); }
            catch (Exception ex) { StatusText = $"初始化失败: {ex.Message}"; }
        });
    }

    // ==================== 初始化 ====================

    private async Task LoadInitialDataAsync()
    {
        try
        {
            var adapters = _networkInfoService.GetPhysicalAdapters();
            Adapters = new ObservableCollection<NetworkAdapterInfo>(adapters);
            var primaryAdapter = adapters.FirstOrDefault(a => !string.IsNullOrEmpty(a.Gateway)) ?? adapters.FirstOrDefault();
            AdapterSelectionItems = new ObservableCollection<AdapterSelectionItem>(
                adapters.Select(a => new AdapterSelectionItem { Adapter = a, IsSelected = a.Id == primaryAdapter?.Id }));

            RefreshProfiles();
            RefreshHistory();

            StatusText = "就绪";
        }
        catch (Exception ex)
        {
            StatusText = $"初始化失败: {ex.Message}";
        }
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private async Task StartDownloadTestAsync()
    {
        if (IsTesting) return;
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        var selectedUrls = UrlSelectionItems.Where(i => i.IsSelected).Select(i => i.Url).ToList();
        if (selectedUrls.Count == 0)
        {
            StatusText = "请至少选择一个下载地址";
            return;
        }

        var selectedAdapters = GetSelectedAdapters();
        if (selectedAdapters.Count == 0)
        {
            StatusText = "请至少选择一张网卡";
            return;
        }

        _startUrlCount = selectedUrls.Count;
        if (!await ShowPreparingDialogAsync(selectedUrls)) return;
        StartTestCommon(selectedUrls.Count, "下载");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            Logger.Log($"测速启动: gateway={gw ?? "null"}, adapters={selectedAdapters.Count}");
            var pn = SelectedProfile?.Name ?? "未知配置";

            var results = await svc.RunMultiNicTestsAsync(
                selectedUrls, new List<string>(), ThreadCount, selectedAdapters, pn, gateway: gw,
                onNicDownloadProgress: OnNicDownloadProgress,
                onNicUploadProgress: OnNicUploadProgress,
                onNicAdapterRates: OnNicAdapterRates,
                onDownloadProgress: OnDownloadProgress,
                onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates,
                onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                onAverageSpeed: OnAverageSpeed, onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);

            FinishMultiNicTest(results);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; FinishTestCancelled(); }
        catch (Exception ex) { Logger.Log($"测速失败: {ex}"); StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    [RelayCommand]
    private async Task StartUploadTestAsync()
    {
        if (IsTesting) return;
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;

        var selectedUrls = SelectedProfile?.UploadUrls ?? new();
        if (selectedUrls.Count == 0) { StatusText = "无上传地址，请在配置管理中添加上传 URL"; return; }
        var selectedAdapters = GetSelectedAdapters();
        if (selectedAdapters.Count == 0) { StatusText = "请至少选择一张网卡"; return; }

        if (!await ShowPreparingDialogAsync(selectedUrls)) return;
        StartTestCommon(selectedUrls.Count, "上传");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            var results = await svc.RunMultiNicTestsAsync(
                new List<string>(), selectedUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                gateway: gw,
                onNicDownloadProgress: OnNicDownloadProgress,
                onNicUploadProgress: OnNicUploadProgress,
                onNicAdapterRates: OnNicAdapterRates,
                onDownloadProgress: OnDownloadProgress,
                onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates,
                onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);
            FinishMultiNicTest(results);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; FinishTestCancelled(); }
        catch (Exception ex) { Logger.Log($"测速失败: {ex}"); StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    [RelayCommand]
    private async Task StartFullTestAsync()
    {
        if (IsTesting) return;
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        var dlUrls = UrlSelectionItems.Where(i => i.IsSelected).Select(i => i.Url).ToList();
        var ulUrls = SelectedProfile?.UploadUrls ?? new();
        if (dlUrls.Count == 0 && ulUrls.Count == 0) { StatusText = "无可用测速地址"; return; }
        var selectedAdapters = GetSelectedAdapters();
        if (selectedAdapters.Count == 0) { StatusText = "请至少选择一张网卡"; return; }

        if (!await ShowPreparingDialogAsync(dlUrls.Concat(ulUrls).Distinct().ToList())) return;
        StartTestCommon(dlUrls.Count + ulUrls.Count, "双向");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(null);
            var results = await svc.RunMultiNicTestsAsync(
                dlUrls, ulUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                gateway: gw,
                onNicDownloadProgress: OnNicDownloadProgress,
                onNicUploadProgress: OnNicUploadProgress,
                onNicAdapterRates: OnNicAdapterRates,
                onDownloadProgress: OnDownloadProgress, onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates, onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);
            FinishMultiNicTest(results);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; FinishTestCancelled(); }
        catch (Exception ex) { Logger.Log($"测速失败: {ex}"); StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    // ==================== 共用辅助 ====================

    private async Task<bool> ShowPreparingDialogAsync(List<string> urls)
    {
        var dlg = new Views.PreparingWindow { Owner = Application.Current.MainWindow };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var disp = Application.Current.Dispatcher;
        bool completed = false;

        var prepTask = Task.Run(async () =>
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            try
            {
                await svc.PrepareUrlsAsync(urls, cts.Token, (p, s) =>
                    { _ = disp.InvokeAsync(() => dlg.UpdateProgress(p, s)); });
                completed = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Log($"Prepare failed: {ex.Message}"); }
        });

        dlg.Show();
        while (dlg.IsVisible && !prepTask.IsCompleted)
            await Task.Delay(50);

        if (prepTask.IsCompleted)
            await Task.Delay(300);
        try { dlg.Close(); } catch { }

        bool userClosed = !prepTask.IsCompleted;
        try { cts.Cancel(); } catch { }
        await prepTask;
        return completed && !userClosed;
    }

    private void StartTestCommon(int urlCount, string mode)
    {
        try
        {
            IsTesting = true;
        _currentTestMode = mode;
        ShowDownloadMetrics = mode is "下载" or "双向";
        ShowUploadMetrics = mode is "上传" or "双向";
        ShowTotalMetrics = mode == "双向";
        (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(mode);
        StatusText = $"{urlCount} 个 URL · {mode}测速中...";
        _cts = new CancellationTokenSource();
        ActiveThreadCount = 0;
        ElapsedSeconds = null;
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.2) };
        var sw = Stopwatch.StartNew();
        _stopwatch = sw;
        _elapsedTickHandler = (_, _) =>
        {
            if (!IsTesting) return;
            var t = Math.Min(sw.Elapsed.TotalSeconds, _options.TestTimeoutSec);
            ElapsedSeconds = t;
            if (DownloadMbps.HasValue)
            {
                DownloadRatePoints.Add(new ObservablePoint(t, DownloadMbps.Value));
                var exDl = DownloadRatePoints.Count - 500;
                if (exDl > 0) DownloadRatePoints.RemoveAt(0);
            }
            if (UploadMbps.HasValue)
            {
                UploadRatePoints.Add(new ObservablePoint(t, UploadMbps.Value));
                var exUl = UploadRatePoints.Count - 500;
                if (exUl > 0) UploadRatePoints.RemoveAt(0);
            }
        };
        _elapsedTimer.Tick += _elapsedTickHandler;
        _elapsedTimer.Start();

        DownloadMbps = null;
        UploadMbps = null;
        OnPropertyChanged(nameof(UploadMbpsDisplay));
        OnPropertyChanged(nameof(TotalRateMbps));
        LatencyMs = null; WanLatencyMs = null; JitterMs = null;
        if (Application.Current.MainWindow is Views.MainWindow mw)
            mw.JitterText.Text = "--";
        _lanLatencies.Clear(); _wanLatencies.Clear(); _jitterSamples.Clear();
        Logger.Log($"[D-START] lists cleared: lan=0 wan=0 jitter=0 delaySec={_options.AverageDelaySec}");
        AverageMbps = null; AverageDownloadMbps = null; AverageUploadMbps = null; AverageTotalMbps = null;
        TotalBytes = null;
        DownloadRatePoints.Clear();
        UploadRatePoints.Clear();
        UrlTestDetails.Clear();
        _downloadPointsByNic.Clear();
        _uploadPointsByNic.Clear();
        ChartAdapterOptions.Clear();
        ChartAdapterOptions.Add("合计");

        AllAdapterRates.Clear();
        foreach (var item in AdapterSelectionItems.Where(x => x.IsSelected))
        {
            var a = item.Adapter;
            ChartAdapterOptions.Add(a.Name);
            _downloadPointsByNic[a.Name] = new ObservableCollection<ObservablePoint>();
            _uploadPointsByNic[a.Name] = new ObservableCollection<ObservablePoint>();
            AllAdapterRates.Add(new AdapterRateItem { Name = a.Name, IpAddress = a.IPAddress, StatusText = "测速中..." });
        }
        SelectedChartAdapter = "合计";
        }
        catch (Exception ex)
        {
            Logger.Log($"StartTestCommon failed: {ex.Message}");
            IsTesting = false;
            try { _cts?.Cancel(); } catch { }
            StatusText = "启动测速失败";
        }
    }

    private void FinishTestCancelled()
    {
        var result = new SpeedTestResult
        {
            TestType = _currentTestMode,
            DownloadMbps = DownloadMbps,
            UploadMbps = UploadMbps,
            TotalBytes = TotalBytes ?? 0,
            LatencyMs = LatencyMs ?? 0,
            WanLatencyMs = WanLatencyMs,
            NodeName = SelectedProfile?.Name ?? "",
            NetworkAdapterName = string.Join(", ", GetSelectedAdapters().Select(a => a.Name ?? "")),
            ThreadCount = ThreadCount,
            DurationSeconds = _stopwatch?.Elapsed.TotalSeconds ?? 0,
            UrlDetails = new()
        };
        FinishTest(result, showDialog: true);
        StatusText = "已取消";
    }

    private void FinishTest(SpeedTestResult result, bool showDialog = true)
    {
        Logger.Log($"[D-FIN1] VM.LatencyMs={LatencyMs:F1} VM.WanLatencyMs={WanLatencyMs:F1} VM.JitterMs={JitterMs:F1} lists: lan={_lanLatencies.Count} wan={_wanLatencies.Count} jitter={_jitterSamples.Count}");
        if (_lanLatencies.Count > 0) { result.LatencyMs = _lanLatencies.Average(); }
        if (_wanLatencies.Count > 0) result.WanLatencyMs = _wanLatencies.Average();
        result.WanLatencyMs = (result.WanLatencyMs ?? 0) > 0 ? result.WanLatencyMs : null;
        var j = ComputeJitter();
        result.JitterMs = double.IsNaN(j) ? null : j;
        Logger.Log($"[D-FIN2] result.LatencyMs={result.LatencyMs:F1}(AVG) VM.LatencyMs={LatencyMs:F1}(LAST) result.WanLatencyMs={result.WanLatencyMs:F1}(AVG) VM.WanLatencyMs={WanLatencyMs:F1}(LAST) result.JitterMs={result.JitterMs:F1} VM.JitterMs={JitterMs:F1}");
        result.AverageTotalMbps = _currentTestMode switch { "下载" => AverageDownloadMbps ?? 0, "上传" => AverageUploadMbps ?? 0, _ => AverageTotalMbps ?? 0 };
        result.TotalBytes = TotalBytes ?? 0;
        result.TestType = _currentTestMode;
        if (_currentTestMode == "上传") result.DownloadMbps = null;
        if (_currentTestMode == "下载") result.UploadMbps = null;
        UrlTestDetails = new ObservableCollection<UrlTestDetail>(result.UrlDetails);
        if (showDialog)
        {
            _ = Task.Run(() => { try { _dataService.SaveResult(result); } catch (Exception ex) { Logger.Log($"SaveResult failed: {ex.Message}"); } });
            _lastMultiNicResults = null;
            _lastResult = result;
            OnPropertyChanged(nameof(HasRecentResult));
            OnPropertyChanged(nameof(RecentDownloadMbps));
            OnPropertyChanged(nameof(RecentUploadMbps));
            OnPropertyChanged(nameof(RecentLatencyMs));
            RecentRecords.Insert(0, result);
            while (RecentRecords.Count > 20)
                RecentRecords.RemoveAt(RecentRecords.Count - 1);
        }
        var ok = result.UrlDetails.Count(d => !d.IsFailed);
        var fail = result.UrlDetails.Count(d => d.IsFailed);
        StatusText = _currentTestMode switch
        {
            "下载" => $"测速完成 · {ok}/{_startUrlCount} 成功{(fail > 0 ? $" · {fail} 失败/超时" : "")}",
            "上传" => "测速完成",
            "双向" => "测速完成",
            _ => $"测速完成 · {ok} 成功"
        };

        if (showDialog)
        {
            var dlg = new Views.TestResultWindow(
                _currentTestMode, ElapsedSeconds ?? 0,
                result.DownloadMbps ?? 0, result.UploadMbps,
                TotalBytes ?? 0,
                AverageTotalMbps ?? double.NaN, result.LatencyMs, result.WanLatencyMs ?? double.NaN,
                result.JitterMs ?? double.NaN,
                ExportResult)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();

            TestCompletedNotify?.Invoke(
                "NetSpeedTest",
                $"下载 {FormatHelper.FormatRate(result.DownloadMbps)} | 上传 {FormatHelper.FormatRate(result.UploadMbps)} | 总均速 {FormatHelper.FormatRate(AverageTotalMbps ?? 0)}");
        }
    }

    private void FinishMultiNicTest(List<SpeedTestResult> results)
    {
        if (results == null || results.Count == 0) { StatusText = "多网卡测速无结果"; return; }

        var batchId = Guid.NewGuid().ToString("N");
        var aggregate = new SpeedTestResult
        {
            Timestamp = DateTime.Now,
            DownloadMbps = results.Sum(r => r.DownloadMbps ?? 0),
            UploadMbps = results.Sum(r => r.UploadMbps ?? 0),
            PeakMbps = results.Sum(r => r.PeakMbps),
            LatencyMs = results.Max(r => r.LatencyMs),
            JitterMs = results.Max(r => r.JitterMs),
            WanLatencyMs = results.Max(r => r.WanLatencyMs),
            NodeName = SelectedProfile?.Name ?? "",
            NetworkAdapterName = string.Join(", ", results.Select(r => r.NetworkAdapterName)),
            BytesDownloaded = results.Sum(r => r.BytesDownloaded),
            BytesUploaded = results.Sum(r => r.BytesUploaded),
            DurationSeconds = results.Max(r => r.DurationSeconds),
            ThreadCount = ThreadCount,
            TestType = _currentTestMode,
            TotalBytes = TotalBytes ?? 0,
            AverageTotalMbps = _currentTestMode switch { "下载" => AverageDownloadMbps ?? 0, "上传" => AverageUploadMbps ?? 0, _ => AverageTotalMbps ?? 0 },
            BatchId = batchId
        };
        if (_lanLatencies.Count > 0) { aggregate.LatencyMs = _lanLatencies.Average(); }
        if (_wanLatencies.Count > 0) aggregate.WanLatencyMs = _wanLatencies.Average();
        var j = ComputeJitter();
        aggregate.JitterMs = double.IsNaN(j) ? null : j;
        if (_currentTestMode == "上传") aggregate.DownloadMbps = null;
        if (_currentTestMode == "下载") aggregate.UploadMbps = null;

        foreach (var r in results)
        {
            r.Timestamp = aggregate.Timestamp;
            r.BatchId = batchId;
            r.TestType = _currentTestMode;
            r.AverageTotalMbps = aggregate.AverageTotalMbps;
            r.TotalBytes = r.BytesDownloaded + r.BytesUploaded;
            if (_lanLatencies.Count > 0) r.LatencyMs = _lanLatencies.Average();
            if (_wanLatencies.Count > 0) r.WanLatencyMs = _wanLatencies.Average();
            r.JitterMs = aggregate.JitterMs;
            if (_currentTestMode == "上传") r.DownloadMbps = null;
            if (_currentTestMode == "下载") r.UploadMbps = null;
            _ = Task.Run(() => { try { _dataService.SaveResult(r); } catch (Exception ex) { Logger.Log($"SaveResult failed: {ex.Message}"); } });
            RecentRecords.Insert(0, r);
        }
        while (RecentRecords.Count > 20) RecentRecords.RemoveAt(RecentRecords.Count - 1);

        _lastMultiNicResults = results.ToList();
        _lastResult = aggregate;
        OnPropertyChanged(nameof(HasRecentResult));
        OnPropertyChanged(nameof(RecentDownloadMbps));
        OnPropertyChanged(nameof(RecentUploadMbps));
        OnPropertyChanged(nameof(RecentLatencyMs));
        var successCount = results.Count(r => string.IsNullOrEmpty(r.ErrorMessage));
        var failCount = results.Count - successCount;
        StatusText = failCount > 0
            ? $"多网卡测速完成 · {successCount} 成功 / {failCount} 失败"
            : $"多网卡测速完成 · {successCount} 张网卡";

        var dlg = new Views.TestResultWindow(
            _currentTestMode, ElapsedSeconds ?? 0,
            aggregate.DownloadMbps ?? 0, aggregate.UploadMbps,
            TotalBytes ?? 0,
            aggregate.AverageTotalMbps, aggregate.LatencyMs, aggregate.WanLatencyMs ?? double.NaN,
            aggregate.JitterMs ?? double.NaN,
            ExportResult,
            results)
        { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();

        TestCompletedNotify?.Invoke("NetSpeedTest",
            $"下载 {FormatHelper.FormatRate(aggregate.DownloadMbps)} | 上传 {FormatHelper.FormatRate(aggregate.UploadMbps)} | 总均速 {FormatHelper.FormatRate(aggregate.AverageTotalMbps)}");
    }
    private void CleanupTest()
    {
        (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(null);
        if (_elapsedTimer != null && _elapsedTickHandler != null)
            _elapsedTimer.Tick -= _elapsedTickHandler;
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        _elapsedTickHandler = null;
        _stopwatch?.Stop();
        _stopwatch = null;
        IsTesting = false;
        Logger.Log($"[D-END] VM final: LatencyMs={LatencyMs:F1} WanLatencyMs={WanLatencyMs:F1} JitterMs={JitterMs:F1}");
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ShowDownloadMetrics = true;
        ShowUploadMetrics = true;
        ShowTotalMetrics = true;
    }

    // ==================== 回调（避免 lambda 重复分配） ====================



    private void OnDownloadProgress(double elapsed, double totalRate, long totalBytes)
    {
        if (!IsTesting) return;
        if (_currentTestMode == "上传") return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DownloadMbps = totalRate;
            OnPropertyChanged(nameof(TotalRateMbps));
        });
    }

    private void OnUploadProgress(double elapsed, double totalRate, long totalBytes)
    {
        if (!IsTesting) return;
        if (_currentTestMode == "下载") return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UploadMbps = totalRate;
            OnPropertyChanged(nameof(UploadMbpsDisplay));
            OnPropertyChanged(nameof(TotalRateMbps));
        });
    }

    private void OnAdapterRates(string name, double dl, double ul)
    {
        if (!IsTesting) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = AllAdapterRates.FirstOrDefault(r => r.Name == name);
            if (item != null) { item.DownloadMbps = dl; item.UploadMbps = ul; }
        });
    }

    private void OnNicDownloadProgress(NetworkAdapterInfo adapter, double elapsed, double rate, long totalBytes)
    {
        if (!IsTesting || _currentTestMode == "上传") return;
        var name = adapter.Name;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_downloadPointsByNic.TryGetValue(name, out var pts))
            {
                pts.Add(new ObservablePoint(_stopwatch?.Elapsed.TotalSeconds ?? elapsed, rate));
                var excess = pts.Count - 500;
                if (excess > 0) pts.RemoveAt(0);
            }
        });
    }

    private void OnNicUploadProgress(NetworkAdapterInfo adapter, double elapsed, double rate, long totalBytes)
    {
        if (!IsTesting || _currentTestMode == "下载") return;
        var name = adapter.Name;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_uploadPointsByNic.TryGetValue(name, out var pts))
            {
                pts.Add(new ObservablePoint(_stopwatch?.Elapsed.TotalSeconds ?? elapsed, rate));
                var excess = pts.Count - 500;
                if (excess > 0) pts.RemoveAt(0);
            }
        });
    }

    private void OnNicAdapterRates(NetworkAdapterInfo adapter, double dl, double ul)
    {
        if (!IsTesting) return;
        var name = adapter.Name;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = AllAdapterRates.FirstOrDefault(r => r.Name == name);
            if (item != null) { item.DownloadMbps = dl; item.UploadMbps = ul; }
        });
    }
    private void OnActiveThreadCount(int count) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => ActiveThreadCount = count); }
    private void OnLatency(double latency) { if (!IsTesting) return; var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0; var added = elapsed >= _options.AverageDelaySec; Application.Current.Dispatcher.InvokeAsync(() => LatencyMs = latency); if (added) _lanLatencies.Add(latency); Logger.Log($"[D-LAN] raw={latency:F1}ms elapsed={elapsed:F1}s added={(added?"YES":"NO")} count={_lanLatencies.Count}"); }

    private double ComputeJitter()
    {
        if (_jitterSamples.Count < 2) return double.NaN;
        var avg = _jitterSamples.Average();
        return Math.Sqrt(_jitterSamples.Sum(x => (x - avg) * (x - avg)) / (_jitterSamples.Count - 1));
    }
    private void OnJitterSample(double rtt)
    {
        if (!IsTesting) return;
        _jitterSamples.Add(rtt);
        if (_jitterSamples.Count > 50) _jitterSamples.RemoveAt(0);
        var j = ComputeJitter();
        JitterMs = double.IsNaN(j) ? null : j;
        Logger.Log($"[D-JIT] rawRtt={rtt:F1}ms count={_jitterSamples.Count} jitter={j:F1}");
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Application.Current.MainWindow is Views.MainWindow mw)
                mw.JitterText.Text = Helpers.FormatHelper.FormatLatency(j);
        });
    }
    private void OnWanLatency(double latency)
    {
        if (!IsTesting) return;
        var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0;
        var added = elapsed >= _options.AverageDelaySec;
        Application.Current.Dispatcher.InvokeAsync(() => WanLatencyMs = latency);
        if (added) _wanLatencies.Add(latency);
        Logger.Log($"[D-WAN] raw={latency:F1}ms elapsed={elapsed:F1}s added={(added?"YES":"NO")} count={_wanLatencies.Count}");
    }
    private void OnTotalBytes(long bytes) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => TotalBytes = bytes); }
    private void OnAverageSpeed(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageMbps = avg); }
    private void OnAverageDownload(double avg) { if (!IsTesting) return; if (_currentTestMode == "上传") return; Application.Current.Dispatcher.InvokeAsync(() => AverageDownloadMbps = avg); }
    private void OnAverageUpload(double avg) { if (!IsTesting) return; if (_currentTestMode == "下载") return; Application.Current.Dispatcher.InvokeAsync(() => AverageUploadMbps = avg); }
    private void OnAverageTotal(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageTotalMbps = avg); }

    [RelayCommand]
    private void CancelTest()
    {
        if (!IsTesting) return;
        _elapsedTimer?.Stop();
        Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        if (_elapsedTimer != null && _elapsedTickHandler != null)
            _elapsedTimer.Tick -= _elapsedTickHandler;
        StatusText = "已取消";
        _cts?.Cancel();
    }

    [RelayCommand]
    private void OpenHistory()
    {
        var vm = _serviceProvider.GetRequiredService<HistoryViewModel>();
        CurrentPage = new Views.HistoryPage { DataContext = vm };
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        vm.CloseRequested += ClosePage;
        CurrentPage = new Views.SettingsPage { DataContext = vm };
    }

    [RelayCommand]
    private void OpenAbout()
    {
        CurrentPage = new Views.AboutPage();
    }

    [RelayCommand]
    private void OpenMore()
    {
        var vm = _serviceProvider.GetRequiredService<MoreViewModel>();
        CurrentPage = new Views.MorePage { DataContext = vm };
    }

    [RelayCommand]
    private void OpenEula()
    {
        CurrentPage = new Views.EulaPage();
    }

    [RelayCommand]
    private void ExportResult()
    {
        if (_lastResult == null)
        {
            StatusText = "暂无测速结果可导出";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json",
            Title = "导出测速报告",
            FileName = $"speedtest_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                object payload;
                if (_lastMultiNicResults != null && _lastMultiNicResults.Count > 0)
                {
                    payload = new
                    {
                        Aggregate = _lastResult,
                        BatchId = _lastResult?.BatchId,
                        NicResults = _lastMultiNicResults
                    };
                }
                else
                {
                    payload = _lastResult!;
                }
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                StatusText = $"已导出: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"导出失败: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void OpenProfileConfig()
    {
        var vm = _serviceProvider.GetRequiredService<ProfileViewModel>();
        CurrentPage = new Views.ProfileConfigPage { DataContext = vm };
        RefreshProfiles();
    }

    private List<NetworkAdapterInfo> GetSelectedAdapters() =>
        AdapterSelectionItems.Where(x => x.IsSelected).Select(x => x.Adapter).ToList();
    // ==================== 辅助方法 ====================

    private void UpdateUrlSelectionItems()
    {
        if (SelectedProfile != null)
            UrlSelectionItems = new ObservableCollection<UrlSelectionItem>(
                SelectedProfile.DownloadUrls.Select(u => new UrlSelectionItem { Url = u, IsSelected = false }));
        else
            UrlSelectionItems = new ObservableCollection<UrlSelectionItem>();
    }

    private void RefreshProfiles()
    {
        var profiles = _profileService.GetAllProfiles();
        var previousId = SelectedProfile?.Id;
        Profiles = new ObservableCollection<SpeedTestProfile>(profiles);

        if (previousId != null)
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == previousId);

        SelectedProfile ??= Profiles.FirstOrDefault();
    }

    private void RefreshHistory()
    {
        var records = _dataService.GetRecords(1, 20);
        RecentRecords.Clear();
        foreach (var r in records) RecentRecords.Add(r);
    }
}

/// <summary>
/// URL 选择项（用于 CheckBox 绑定）
/// </summary>
public partial class UrlSelectionItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// URL 的简短显示名
    /// </summary>
    public string DisplayHost
    {
        get
        {
            try { return new Uri(Url).Host; }
            catch { return Url; }
        }
    }
}

/// <summary>
/// 网卡勾选项（多网卡同时测速用）
/// </summary>
public partial class AdapterSelectionItem : ObservableObject
{
    public NetworkAdapterInfo Adapter { get; set; } = new();

    public string Name => Adapter.Name;

    public string? IPAddress => Adapter.IPAddress;

    [ObservableProperty]
    private bool _isSelected = true;
}
/// <summary>
/// 网卡实时速率条目
/// </summary>
public partial class AdapterRateItem : ObservableObject
{
    public string Name { get; set; } = "";

    public string? IpAddress { get; set; }

    [ObservableProperty]
    private string _statusText = "待测";

    [ObservableProperty]
    private double _downloadMbps;

    [ObservableProperty]
    private double _uploadMbps;
}
