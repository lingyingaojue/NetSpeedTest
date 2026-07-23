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
    private Stopwatch? _stopwatch;
    private SpeedTestResult? _lastResult;
    private string _currentTestMode = "";

    // ==================== 可绑定属性 ====================

    [ObservableProperty]
    private ObservableCollection<NetworkAdapterInfo> _adapters = new();

    /// <summary>
    /// 全部网卡实时速率
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AdapterRateItem> _allAdapterRates = new();

    [ObservableProperty]
    private NetworkAdapterInfo? _selectedAdapter;

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
    /// 总流量（字节）
    /// </summary>
    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private double? _latencyMs;

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
            SelectedAdapter = Adapters.FirstOrDefault();

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

        var selectedUrls = UrlSelectionItems.Where(i => i.IsSelected).Select(i => i.Url).ToList();
        if (selectedUrls.Count == 0)
        {
            StatusText = "请至少选择一个下载地址";
            return;
        }

        if (Adapters.Count == 0)
        {
            StatusText = "未检测到可用网卡";
            return;
        }

        StartTestCommon(selectedUrls.Count, "下载");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            Logger.Log($"测速启动: gateway={gw ?? "null"}, adapters={Adapters.Count}");
            var pn = SelectedProfile?.Name ?? "未知配置";
            var adapters = Adapters.ToList();

            var result = await svc.RunMultiUrlTestAsync(
                selectedUrls, ThreadCount, adapters, pn, gateway: gw,
                onUrlProgress: OnUrlProgress,
                onDownloadProgress: OnDownloadProgress,
                onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates,
                onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency,
                onAverageSpeed: OnAverageSpeed, onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);

            FinishTest(result);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    [RelayCommand]
    private async Task StartUploadTestAsync()
    {
        if (IsTesting) return;

        var selectedUrls = SelectedProfile?.UploadUrls ?? new();
        if (selectedUrls.Count == 0) { StatusText = "无上传地址，请在配置管理中添加上传 URL"; return; }
        if (Adapters.Count == 0) { StatusText = "未检测到可用网卡"; return; }

        StartTestCommon(selectedUrls.Count, "上传");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            var adapters = Adapters.ToList();
            var result = await svc.RunUploadTestAsync(
                selectedUrls, ThreadCount, adapters, SelectedProfile?.Name ?? "未知配置",
                gateway: gw,
                onDownloadProgress: OnDownloadProgress,
                onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates,
                onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency,
                onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);
            FinishTest(result);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    [RelayCommand]
    private async Task StartFullTestAsync()
    {
        if (IsTesting) return;
        var dlUrls = UrlSelectionItems.Where(i => i.IsSelected).Select(i => i.Url).ToList();
        var ulUrls = SelectedProfile?.UploadUrls ?? new();
        if (dlUrls.Count == 0 && ulUrls.Count == 0) { StatusText = "无可用测速地址"; return; }
        if (Adapters.Count == 0) { StatusText = "未检测到可用网卡"; return; }

        StartTestCommon(Math.Max(dlUrls.Count, ulUrls.Count), "双向");
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway();
            var adapters = Adapters.ToList();
            (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(null);
            var result = await svc.RunFullTestAsync(
                dlUrls, ulUrls, ThreadCount, adapters, SelectedProfile?.Name ?? "未知配置",
                gateway: gw,
                onDownloadProgress: OnDownloadProgress, onUploadProgress: OnUploadProgress,
                onAdapterRates: OnAdapterRates, onActiveThreadCount: OnActiveThreadCount,
                onLatency: OnLatency, onWanLatency: OnWanLatency,
                onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,                onTotalBytes: OnTotalBytes,
                ct: _cts!.Token);
            FinishTest(result);
        }
        catch (OperationCanceledException) { StatusText = "已取消"; }
        catch (Exception ex) { StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }

    // ==================== 共用辅助 ====================

    private void StartTestCommon(int urlCount, string mode)
    {
        IsTesting = true;
        _currentTestMode = mode;
        (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(mode == "上传");
        StatusText = $"{urlCount} 个 URL · {mode}测速中...";
        _cts = new CancellationTokenSource();
        ActiveThreadCount = 0;
        ElapsedSeconds = null;
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.2) };
        var sw = Stopwatch.StartNew();
        _stopwatch = sw;
        _elapsedTickHandler = (_, _) => { if (IsTesting) ElapsedSeconds = Math.Min(sw.Elapsed.TotalSeconds, _options.TestTimeoutSec); };
        _elapsedTimer.Tick += _elapsedTickHandler;
        _elapsedTimer.Start();

        DownloadMbps = null;
        UploadMbps = null;
        OnPropertyChanged(nameof(UploadMbpsDisplay));
        LatencyMs = null; WanLatencyMs = null;
        AverageMbps = null; AverageDownloadMbps = null; AverageUploadMbps = null; AverageTotalMbps = null;
        TotalBytes = null;
        DownloadRatePoints.Clear();
        UploadRatePoints.Clear();
        UrlTestDetails.Clear();
        _urlDetailMap.Clear();

        AllAdapterRates.Clear();
        foreach (var a in Adapters)
            AllAdapterRates.Add(new AdapterRateItem { Name = a.Name });
    }

    private void FinishTest(SpeedTestResult result)
    {
        if (result.LatencyMs > 0) LatencyMs = result.LatencyMs;
        result.WanLatencyMs = (WanLatencyMs ?? 0) > 0 ? WanLatencyMs : null;
        result.AverageTotalMbps = AverageTotalMbps ?? 0;
        result.TotalBytes = TotalBytes ?? 0;
        result.TestType = _currentTestMode;
        UrlTestDetails = new ObservableCollection<UrlTestDetail>(result.UrlDetails);
        _ = Task.Run(() => _dataService.SaveResult(result));
        _lastResult = result;
        RecentRecords.Insert(0, result);
        while (RecentRecords.Count > 20)
            RecentRecords.RemoveAt(RecentRecords.Count - 1);
        Application.Current.Dispatcher.InvokeAsync(() =>
            (Application.Current.MainWindow as Views.MainWindow)?.ScrollHistoryToTop(),
            DispatcherPriority.Loaded);
        var ok = result.UrlDetails.Count(d => !d.IsFailed);
        var fail = result.UrlDetails.Count(d => d.IsFailed);
        StatusText = $"测速完成 · {ok} 成功{(fail > 0 ? $" · {fail} 失败/超时" : "")}";

        var dlg = new Views.TestResultWindow(
            _currentTestMode, ElapsedSeconds ?? 0,
            result.DownloadMbps, result.UploadMbps,
            TotalBytes ?? 0,
            AverageTotalMbps ?? 0, LatencyMs ?? 0, WanLatencyMs ?? 0)
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
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
        _cts?.Dispose();
        _cts = null;
        _urlDetailMap.Clear();
    }

    // ==================== 回调（避免 lambda 重复分配） ====================

    private readonly Dictionary<string, UrlTestDetail> _urlDetailMap = new();

    private void OnUrlProgress(string url, string host, double elapsed, double rate, long total)
    {
        if (!IsTesting) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!_urlDetailMap.TryGetValue(url, out var detail))
            {
                detail = new UrlTestDetail { Url = url, Host = host };
                UrlTestDetails.Add(detail);
                _urlDetailMap[url] = detail;
            }
            detail.AvgMbps = rate; detail.BytesDownloaded = total; detail.DurationSeconds = elapsed;
        });
    }

    private void OnDownloadProgress(double elapsed, double totalRate, long totalBytes)
    {
        if (!IsTesting) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DownloadMbps = totalRate;
            OnPropertyChanged(nameof(TotalRateMbps));
            DownloadRatePoints.Add(new ObservablePoint(elapsed, totalRate));
            var excess = DownloadRatePoints.Count - 500;
            if (excess > 0) DownloadRatePoints.RemoveAt(0);
        });
    }

    private void OnUploadProgress(double elapsed, double totalRate, long totalBytes)
    {
        if (!IsTesting) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UploadMbps = totalRate;
            OnPropertyChanged(nameof(UploadMbpsDisplay));
            OnPropertyChanged(nameof(TotalRateMbps));
            UploadRatePoints.Add(new ObservablePoint(elapsed, totalRate));
            var excess = UploadRatePoints.Count - 500;
            if (excess > 0) UploadRatePoints.RemoveAt(0);
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

    private void OnActiveThreadCount(int count) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => ActiveThreadCount = count); }
    private void OnLatency(double latency) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => LatencyMs = latency); }
    private void OnWanLatency(double latency) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => WanLatencyMs = latency); }
    private void OnTotalBytes(long bytes) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => TotalBytes = bytes); }
    private void OnAverageSpeed(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageMbps = avg); }
    private void OnAverageDownload(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageDownloadMbps = avg); }
    private void OnAverageUpload(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageUploadMbps = avg); }
    private void OnAverageTotal(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageTotalMbps = avg); }

    [RelayCommand]
    private void CancelTest()
    {
        if (!IsTesting) return;
        _elapsedTimer?.Stop();
        StatusText = "已取消";
        _cts?.Cancel();
    }

    [RelayCommand]
    private void OpenHistory()
    {
        var vm = _serviceProvider.GetRequiredService<HistoryViewModel>();
        var window = new Views.HistoryWindow
        {
            DataContext = vm,
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
        };
        window.ShowDialog();
        RefreshHistory();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        var window = new Views.SettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var window = new Views.AboutWindow
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenEula()
    {
        var window = new Views.EulaWindow(isFirstLaunch: false)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
        };
        window.ShowDialog();
        if (window.Revoked)
            System.Windows.Application.Current.Shutdown();
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
                var json = JsonSerializer.Serialize(_lastResult, new JsonSerializerOptions { WriteIndented = true });
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
        var window = new Views.ProfileConfigWindow
        {
            DataContext = vm,
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
        };
        window.ShowDialog();
        RefreshProfiles();
    }

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
/// 网卡实时速率条目
/// </summary>
public partial class AdapterRateItem : ObservableObject
{
    public string Name { get; set; } = "";

    [ObservableProperty]
    private double _downloadMbps;

    [ObservableProperty]
    private double _uploadMbps;
}
