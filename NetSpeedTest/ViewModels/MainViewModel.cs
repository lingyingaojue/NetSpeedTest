using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using NetSpeedTest.Helpers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
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
    private readonly ConcurrentDictionary<string, NicQualityState> _nicQuality = new();
    private MultiNicMetricCallbacks? _multiNicMetricCallbacks;
    private readonly NetworkMonitorService _networkMonitorService;
    private readonly SemaphoreSlim _adapterRefreshGate = new(1, 1);
    private bool _pendingAdapterRefresh;
    private bool _suppressDefaultUrlSelection;
    private bool _adapterSelectionInitialized;
    /// <summary>已受理但尚未进入运行态的测速启动请求（位于准备阶段）。</summary>
    private bool _pendingTestStart;
    /// <summary>准备阶段收到的取消请求，进入运行态时立即生效。</summary>
    private bool _cancelPending;
    /// <summary>
    /// 本次测速由 Web API 发起时为 true：结束时不再弹出模态结果窗口。
    /// 模态窗口会占住 UI 线程直到用户关闭，导致 IsTesting 长期为 true、
    /// 所有 /api 请求超时（无人值守场景尤其严重）。
    /// </summary>
    private volatile bool _apiInitiatedTest;

    /// <summary>
    /// 标记本次测速由 Web API 发起。
    /// </summary>
    internal void MarkApiInitiatedTest() => _apiInitiatedTest = true;

    /// <summary>
    /// 清除 Web API 发起标记（启动失败时复位，避免影响用户手动测速）。
    /// </summary>
    internal void ClearApiInitiatedTest() => _apiInitiatedTest = false;

    /// <summary>
    /// 读取并复位“API 发起”标记，保证只影响本次测速。
    /// </summary>
    private bool ConsumeApiInitiatedTest()
    {
        var value = _apiInitiatedTest;
        _apiInitiatedTest = false;
        return value;
    }
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
    private readonly object _latencyLock = new();
    private long _packetLossSent;
    private long _packetLossReceived;
    private int _maxActiveThreadCount;
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
    private void ClosePage()
    {
        CurrentPage = null;
        RefreshProfiles();
    }

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
    /// 实时丢包率（百分比）
    /// </summary>
    [ObservableProperty]
    private double? _packetLossPercent;

    /// <summary>
    /// 丢包率收发计数文本
    /// </summary>
    [ObservableProperty]
    private string _packetLossDetail = "收 0/0";

    /// <summary>
    /// 丢包率显示文本
    /// </summary>
    public string PacketLossDisplay => PacketLossPercent.HasValue ? $"{PacketLossPercent.Value:F1}%" : "--";

    /// <summary>
    /// 丢包率颜色等级：0=无样本/0%，1=>0%，2=≥5%
    /// </summary>
    [ObservableProperty]
    private int _packetLossLevel;

    public long PacketLossSent => Interlocked.Read(ref _packetLossSent);

    public long PacketLossReceived => Interlocked.Read(ref _packetLossReceived);

    public string RecentPacketLossDisplay => _lastResult == null ? "--" : $"{_lastResult.PacketLoss:F1}%";

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
        if (_suppressDefaultUrlSelection) return;

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
                         SpeedTestOptions options, NetworkMonitorService networkMonitorService)
    {
        _profileService = profileService;
        _dataService = dataService;
        _networkInfoService = networkInfoService;
        _serviceProvider = serviceProvider;
        _options = options;
        _networkMonitorService = networkMonitorService;
        _multiNicMetricCallbacks = new MultiNicMetricCallbacks
        {
            OnLatency = OnNicLatency,
            OnWanLatency = OnNicWanLatency,
            OnJitterRtt = OnNicJitterRtt
        };
        _profileService.ProfilesChanged += OnProfilesChanged;
        _networkMonitorService.NetworkChanged += OnNetworkChanged;
        _options.AdapterFilterChanged += OnAdapterFilterChanged;

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
            await RefreshAdaptersAsync();





            RefreshProfiles();
            await RefreshHistoryAsync();

            StatusText = "就绪";
        }
        catch (Exception ex)
        {
            StatusText = $"初始化失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 重新枚举网卡并刷新界面。测速过程中只记录待刷新标记，避免破坏测速监控。
    /// </summary>
    public async Task RefreshAdaptersAsync(bool userInitiated = false)
    {
        if (IsTesting)
        {
            _pendingAdapterRefresh = true;
            return;
        }

        if (!await _adapterRefreshGate.WaitAsync(0))
            return;

        try
        {
            var adapters = await Task.Run(() =>
            {
                _networkInfoService.InvalidateCache();
                return _networkInfoService.GetAdapters(_options.IncludeVirtualAdapters);
            });

            if (IsTesting)
            {
                _pendingAdapterRefresh = true;
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
                ApplyAdapters(adapters, userInitiated);
            else
                await Application.Current.Dispatcher.InvokeAsync(() => ApplyAdapters(adapters, userInitiated));
        }
        catch (Exception ex)
        {
            Logger.Log($"[NIC] refresh failed: {ex.Message}");
            if (userInitiated) StatusText = $"刷新网卡失败: {ex.Message}";
        }
        finally
        {
            _adapterRefreshGate.Release();
        }
    }

    private void ApplyAdapters(List<NetworkAdapterInfo> adapters, bool userInitiated)
    {
        var previousSelected = AdapterSelectionItems
            .Where(x => x.IsSelected)
            .Select(x => x.Adapter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validIds = adapters.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedIds = previousSelected.Where(validIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedSelection = previousSelected.Count > 0 && selectedIds.Count == 0;
        var autoSwitched = false;

        // 首次加载、或原选中网卡都消失时，自动回退到有网关的网卡，避免测速无网卡可选。
        if (selectedIds.Count == 0 && adapters.Count > 0 && (!_adapterSelectionInitialized || removedSelection))
        {
            var fallback = adapters.FirstOrDefault(a => a.IsPhysical && !string.IsNullOrEmpty(a.Gateway))
                ?? adapters.FirstOrDefault(a => !string.IsNullOrEmpty(a.Gateway))
                ?? adapters.FirstOrDefault(a => a.IsPhysical)
                ?? adapters[0];
            autoSwitched = true;
            selectedIds.Add(fallback.Id);
        }

        Adapters = new ObservableCollection<NetworkAdapterInfo>(adapters);
        AdapterSelectionItems = new ObservableCollection<AdapterSelectionItem>(
            adapters.Select(a => new AdapterSelectionItem { Adapter = a, IsSelected = selectedIds.Contains(a.Id) }));
        _adapterSelectionInitialized = true;

        if (!IsTesting)
        {
            var previousChart = SelectedChartAdapter;
            ChartAdapterOptions.Clear();
            ChartAdapterOptions.Add("合计");
            foreach (var a in adapters) ChartAdapterOptions.Add(a.Name ?? "");
            SelectedChartAdapter = ChartAdapterOptions.Contains(previousChart) ? previousChart : "合计";

            var currentNames = adapters.Select(a => a.Name ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _downloadPointsByNic.Keys.Where(k => !currentNames.Contains(k)).ToList())
                _downloadPointsByNic.Remove(key);
            foreach (var key in _uploadPointsByNic.Keys.Where(k => !currentNames.Contains(k)).ToList())
                _uploadPointsByNic.Remove(key);
            foreach (var a in adapters)
            {
                var name = a.Name ?? "";
                if (!_downloadPointsByNic.ContainsKey(name))
                    _downloadPointsByNic[name] = new ObservableCollection<ObservablePoint>();
                if (!_uploadPointsByNic.ContainsKey(name))
                    _uploadPointsByNic[name] = new ObservableCollection<ObservablePoint>();
            }
        }

        if (adapters.Count == 0)
            StatusText = "未检测到可用网卡";
        else if (userInitiated)
            StatusText = $"网卡列表已刷新 · {adapters.Count} 张可用";
        else if (autoSwitched)
            StatusText = "检测到网卡变化，已自动切换到可用网卡";
    }

    [RelayCommand]
    private async Task RefreshAdapters() => await RefreshAdaptersAsync(userInitiated: true);

    private void OnProfilesChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.InvokeAsync(RefreshProfiles);
    }

    private void OnAdapterFilterChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.InvokeAsync(() => _ = RefreshAdaptersAsync());
    }
    private void OnNetworkChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.InvokeAsync(() => _ = RefreshAdaptersAsync());
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
        if (!await PrepareWithCancelTrackingAsync(selectedUrls)) return;
        StartTestCommon(selectedUrls.Count, "下载");
        _pendingTestStart = false;
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway(selectedAdapters);
            Logger.Log($"测速启动: gateway={gw ?? "null"}, adapters={selectedAdapters.Count}");
            var pn = SelectedProfile?.Name ?? "未知配置";

            if (selectedAdapters.Count == 1)
            {
                var result = await svc.RunMultiUrlTestAsync(
                    selectedUrls, ThreadCount, selectedAdapters, pn, gateway: gw,
                    onUrlProgress: null,
                    onDownloadProgress: SingleDownloadProgress(selectedAdapters[0]),
                    onUploadProgress: SingleUploadProgress(selectedAdapters[0]),
                    onAdapterRates: SingleAdapterRates(selectedAdapters[0]),
                    onActiveThreadCount: OnActiveThreadCount,
                    onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                    onPacketLoss: OnPacketLossSample,
                    onAverageSpeed: OnAverageSpeed, onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    ct: _cts!.Token);
                FinishTest(result);
            }
            else
            {
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
                    onPacketLoss: OnPacketLossSample,
                    onAverageSpeed: OnAverageSpeed, onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    metricCallbacks: _multiNicMetricCallbacks,
                    ct: _cts!.Token);
                FinishMultiNicTest(results);
            }
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

        if (!await PrepareWithCancelTrackingAsync(selectedUrls)) return;
        StartTestCommon(selectedUrls.Count, "上传");
        _pendingTestStart = false;
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway(selectedAdapters);
            if (selectedAdapters.Count == 1)
            {
                var result = await svc.RunUploadTestAsync(
                    selectedUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                    gateway: gw,
                    onDownloadProgress: SingleDownloadProgress(selectedAdapters[0]),
                    onUploadProgress: SingleUploadProgress(selectedAdapters[0]),
                    onAdapterRates: SingleAdapterRates(selectedAdapters[0]),
                    onActiveThreadCount: OnActiveThreadCount,
                    onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                    onPacketLoss: OnPacketLossSample,
                    onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    ct: _cts!.Token);
                FinishTest(result);
            }
            else
            {
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
                    onPacketLoss: OnPacketLossSample,
                    onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    metricCallbacks: _multiNicMetricCallbacks,
                    ct: _cts!.Token);
                FinishMultiNicTest(results);
            }
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

        var effectiveMode = dlUrls.Count > 0 && ulUrls.Count > 0 ? "双向" : dlUrls.Count > 0 ? "下载" : "上传";
        if (!await PrepareWithCancelTrackingAsync(dlUrls.Concat(ulUrls).Distinct().ToList())) return;
        StartTestCommon(dlUrls.Count + ulUrls.Count, effectiveMode);
        _pendingTestStart = false;
        try
        {
            var svc = _serviceProvider.GetRequiredService<SpeedTestService>();
            var gw = _networkInfoService.FindPingableGateway(selectedAdapters);
            (Application.Current.MainWindow as Views.MainWindow)?.SetChartFocus(null);

            if (effectiveMode == "上传")
            {
                if (selectedAdapters.Count == 1)
                {
                    var result = await svc.RunUploadTestAsync(ulUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                        gateway: gw,
                        onDownloadProgress: SingleDownloadProgress(selectedAdapters[0]), onUploadProgress: SingleUploadProgress(selectedAdapters[0]),
                        onAdapterRates: SingleAdapterRates(selectedAdapters[0]), onActiveThreadCount: OnActiveThreadCount,
                        onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                        onPacketLoss: OnPacketLossSample,
                        onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                        onTotalBytes: OnTotalBytes,
                        ct: _cts!.Token);
                    FinishTest(result);
                }
                else
                {
                    var results = await svc.RunMultiNicTestsAsync(new List<string>(), ulUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                        gateway: gw,
                        onNicDownloadProgress: OnNicDownloadProgress, onNicUploadProgress: OnNicUploadProgress, onNicAdapterRates: OnNicAdapterRates,
                        onDownloadProgress: OnDownloadProgress, onUploadProgress: OnUploadProgress,
                        onAdapterRates: OnAdapterRates, onActiveThreadCount: OnActiveThreadCount,
                        onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                        onPacketLoss: OnPacketLossSample,
                        onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                        onTotalBytes: OnTotalBytes,
                        metricCallbacks: _multiNicMetricCallbacks,
                        ct: _cts!.Token);
                    FinishMultiNicTest(results);
                }
            }
            else if (effectiveMode == "下载")
            {
                if (selectedAdapters.Count == 1)
                {
                    var result = await svc.RunMultiUrlTestAsync(dlUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                        gateway: gw,
                        onDownloadProgress: SingleDownloadProgress(selectedAdapters[0]), onUploadProgress: SingleUploadProgress(selectedAdapters[0]),
                        onAdapterRates: SingleAdapterRates(selectedAdapters[0]), onActiveThreadCount: OnActiveThreadCount,
                        onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                        onPacketLoss: OnPacketLossSample,
                        onAverageSpeed: OnAverageSpeed,
                        onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                        onTotalBytes: OnTotalBytes,
                        ct: _cts!.Token);
                    FinishTest(result);
                }
                else
                {
                    var results = await svc.RunMultiNicTestsAsync(dlUrls, new List<string>(), ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                        gateway: gw,
                        onNicDownloadProgress: OnNicDownloadProgress, onNicUploadProgress: OnNicUploadProgress, onNicAdapterRates: OnNicAdapterRates,
                        onDownloadProgress: OnDownloadProgress, onUploadProgress: OnUploadProgress,
                        onAdapterRates: OnAdapterRates, onActiveThreadCount: OnActiveThreadCount,
                        onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                        onPacketLoss: OnPacketLossSample,
                        onAverageSpeed: OnAverageSpeed, onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                        onTotalBytes: OnTotalBytes,
                    metricCallbacks: _multiNicMetricCallbacks,
                        ct: _cts!.Token);
                    FinishMultiNicTest(results);
                }
            }
            else if (selectedAdapters.Count == 1)
            {
                var result = await svc.RunFullTestAsync(dlUrls, ulUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                    gateway: gw,
                    onDownloadProgress: SingleDownloadProgress(selectedAdapters[0]), onUploadProgress: SingleUploadProgress(selectedAdapters[0]),
                    onAdapterRates: SingleAdapterRates(selectedAdapters[0]), onActiveThreadCount: OnActiveThreadCount,
                    onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                    onPacketLoss: OnPacketLossSample,
                    onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    ct: _cts!.Token);
                FinishTest(result);
            }
            else
            {
                var results = await svc.RunMultiNicTestsAsync(dlUrls, ulUrls, ThreadCount, selectedAdapters, SelectedProfile?.Name ?? "未知配置",
                    gateway: gw,
                    onNicDownloadProgress: OnNicDownloadProgress, onNicUploadProgress: OnNicUploadProgress, onNicAdapterRates: OnNicAdapterRates,
                    onDownloadProgress: OnDownloadProgress, onUploadProgress: OnUploadProgress,
                    onAdapterRates: OnAdapterRates, onActiveThreadCount: OnActiveThreadCount,
                    onLatency: OnLatency, onWanLatency: OnWanLatency, onJitter: OnJitterSample,
                    onPacketLoss: OnPacketLossSample,
                    onAverageDownload: OnAverageDownload, onAverageUpload: OnAverageUpload, onAverageTotal: OnAverageTotal,
                    onTotalBytes: OnTotalBytes,
                    metricCallbacks: _multiNicMetricCallbacks,
                    ct: _cts!.Token);
                FinishMultiNicTest(results);
            }
        }
        catch (OperationCanceledException) { StatusText = "已取消"; FinishTestCancelled(); }
        catch (Exception ex) { Logger.Log($"测速失败: {ex}"); StatusText = $"测速失败: {ex.Message}"; }
        finally { CleanupTest(); }
    }


    // ==================== 共用辅助 ====================

    /// <summary>
    /// 显示准备对话框并跟踪“已受理但尚未进入运行态”的状态，
    /// 使准备阶段收到的停止请求不会被丢弃。
    /// </summary>
    private async Task<bool> PrepareWithCancelTrackingAsync(List<string> urls)
    {
        _pendingTestStart = true;
        _cancelPending = false;
        try
        {
            var ok = await ShowPreparingDialogAsync(urls);
            if (!ok) _pendingTestStart = false;
            return ok;
        }
        catch
        {
            _pendingTestStart = false;
            throw;
        }
    }

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

        // 准备阶段收到过停止请求：立即取消，避免“停止无效、测速照跑”。
        if (_cancelPending)
        {
            _cancelPending = false;
            StatusText = "已取消";
            _cts.Cancel();
        }
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
        lock (_latencyLock) { _lanLatencies.Clear(); _wanLatencies.Clear(); _jitterSamples.Clear(); }
        Logger.Log($"[D-START] lists cleared: lan=0 wan=0 jitter=0 delaySec={_options.AverageDelaySec}");
        AverageMbps = null; AverageDownloadMbps = null; AverageUploadMbps = null; AverageTotalMbps = null;
        _maxActiveThreadCount = 0;
        _packetLossSent = 0; _packetLossReceived = 0;
        PacketLossPercent = null;
        PacketLossDetail = string.Format(LocalizationService.Get("Metric_PacketLossDetail"), 0L, 0L);
        PacketLossLevel = 0;
        OnPropertyChanged(nameof(PacketLossDisplay));
        TotalBytes = null;
        DownloadRatePoints.Clear();
        UploadRatePoints.Clear();
        UrlTestDetails.Clear();
        _downloadPointsByNic.Clear();
        _uploadPointsByNic.Clear();
        ChartAdapterOptions.Clear();
        ChartAdapterOptions.Add("合计");

        _nicQuality.Clear();
        AllAdapterRates.Clear();
        foreach (var item in AdapterSelectionItems.Where(x => x.IsSelected))
        {
            var a = item.Adapter;
            ChartAdapterOptions.Add(a.Name);
            _downloadPointsByNic[a.Name] = new ObservableCollection<ObservablePoint>();
            _uploadPointsByNic[a.Name] = new ObservableCollection<ObservablePoint>();
            AllAdapterRates.Add(new AdapterRateItem { AdapterId = a.Id, Name = a.Name, IpAddress = a.IPAddress, IsVirtual = a.IsVirtual, StatusText = "测速中..." });
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
            PacketLoss = PacketLossPercent ?? 0,
            NodeName = SelectedProfile?.Name ?? "",
            NetworkAdapterName = string.Join(", ", GetSelectedAdapters().Select(a => a.Name ?? "")),
            ThreadCount = _options.AdaptiveThreadsEnabled && _maxActiveThreadCount > 0 ? _maxActiveThreadCount : ThreadCount,
            DurationSeconds = _stopwatch?.Elapsed.TotalSeconds ?? 0,
            UrlDetails = new()
        };
        FinishTest(result, showDialog: true);
        StatusText = "已取消";
    }

    private void FinishTest(SpeedTestResult result, bool showDialog = true)
    {
        // API 发起的测速不弹模态结果窗：模态窗口会阻塞 UI 线程直到用户关闭，
        // 期间 IsTesting 保持 true、Dispatcher 无法响应，Web API 全部超时。
        if (ConsumeApiInitiatedTest()) showDialog = false;
        int lanCount, wanCount, jitterCount;
        lock (_latencyLock) { lanCount = _lanLatencies.Count; wanCount = _wanLatencies.Count; jitterCount = _jitterSamples.Count; }
        Logger.Log($"[D-FIN1] VM.LatencyMs={LatencyMs:F1} VM.WanLatencyMs={WanLatencyMs:F1} VM.JitterMs={JitterMs:F1} lists: lan={lanCount} wan={wanCount} jitter={jitterCount}");
        lock (_latencyLock) { if (_lanLatencies.Count > 0) { result.LatencyMs = _lanLatencies.Average(); } }
        lock (_latencyLock) { if (_wanLatencies.Count > 0) result.WanLatencyMs = _wanLatencies.Average(); }
        result.WanLatencyMs = (result.WanLatencyMs ?? 0) > 0 ? result.WanLatencyMs : null;
        var j = ComputeJitter();
        result.PacketLoss = PacketLossPercent ?? 0;
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
            OnPropertyChanged(nameof(RecentPacketLossDisplay));
            RecentRecords.Insert(0, result);
            while (RecentRecords.Count > 20)
                RecentRecords.RemoveAt(RecentRecords.Count - 1);
        }
        var ok = result.UrlDetails.Count(d => !d.IsFailed);
        var fail = result.UrlDetails.Count(d => d.IsFailed);
        StatusText = _currentTestMode switch
        {
            // 上传与下载共用同一套结果语义：都要能看出失败/超时的 URL 数，
            // 否则服务器全部拒收时仍显示“测速完成”，等同于假成功。
            "下载" => $"测速完成 · {ok}/{_startUrlCount} 成功{(fail > 0 ? $" · {fail} 失败/超时" : "")}",
            "上传" => $"测速完成 · {ok}/{_startUrlCount} 成功{(fail > 0 ? $" · {fail} 失败/超时" : "")}",
            "双向" => fail > 0
                ? $"测速完成 · {ok} 成功 · {fail} 失败/超时"
                : $"测速完成 · {ok} 成功",
            _ => $"测速完成 · {ok} 成功{(fail > 0 ? $" · {fail} 失败/超时" : "")}"
        };

        if (showDialog)
        {
            var dlg = new Views.TestResultWindow(
                _currentTestMode, ElapsedSeconds ?? 0,
                result.DownloadMbps ?? 0, result.UploadMbps,
                TotalBytes ?? 0,
                AverageTotalMbps ?? double.NaN, result.LatencyMs, result.WanLatencyMs ?? double.NaN,
                result.JitterMs ?? double.NaN,
                result.PacketLoss,
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
        var apiInitiated = ConsumeApiInitiatedTest();

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
            ThreadCount = _options.AdaptiveThreadsEnabled ? Math.Max(1, results.Sum(r => r.ThreadCount)) : ThreadCount,
            TestType = _currentTestMode,
            TotalBytes = TotalBytes ?? 0,
            AverageTotalMbps = _currentTestMode switch { "下载" => AverageDownloadMbps ?? 0, "上传" => AverageUploadMbps ?? 0, _ => AverageTotalMbps ?? 0 },
            BatchId = batchId
        };
        ApplyPerNicQuality(results, aggregate);



        aggregate.PacketLoss = PacketLossPercent ?? 0;
        if (_currentTestMode == "上传") aggregate.DownloadMbps = null;
        if (_currentTestMode == "下载") aggregate.UploadMbps = null;

        foreach (var r in results)
        {
            r.Timestamp = aggregate.Timestamp;
            r.BatchId = batchId;
            r.TestType = _currentTestMode;
            r.AverageTotalMbps = aggregate.AverageTotalMbps;
            r.TotalBytes = r.BytesDownloaded + r.BytesUploaded;



            r.PacketLoss = aggregate.PacketLoss;
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
        OnPropertyChanged(nameof(RecentPacketLossDisplay));
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
            aggregate.PacketLoss,
            ExportResult,
            results)
        { Owner = Application.Current.MainWindow };

        // API 发起的测速不弹模态结果窗（同上）。
        if (!apiInitiated) dlg.ShowDialog();

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
        if (_pendingAdapterRefresh)
        {
            _pendingAdapterRefresh = false;
            _ = RefreshAdaptersAsync();
        }

        Logger.Log($"[D-END] VM final: LatencyMs={LatencyMs:F1} WanLatencyMs={WanLatencyMs:F1} JitterMs={JitterMs:F1}");
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ShowDownloadMetrics = true;
        ShowUploadMetrics = true;
        ShowTotalMetrics = true;
    }

    // ==================== 回调（避免 lambda 重复分配） ====================



    /// <summary>
    /// 单卡测速时，同时更新汇总指标和该网卡曲线/速率条。
    /// </summary>
    private Action<double, double, long> SingleDownloadProgress(NetworkAdapterInfo adapter) =>
        (elapsed, rate, bytes) =>
        {
            OnDownloadProgress(elapsed, rate, bytes);
            OnNicDownloadProgress(adapter, elapsed, rate, bytes);
        };

    private Action<double, double, long> SingleUploadProgress(NetworkAdapterInfo adapter) =>
        (elapsed, rate, bytes) =>
        {
            OnUploadProgress(elapsed, rate, bytes);
            OnNicUploadProgress(adapter, elapsed, rate, bytes);
        };

    private Action<string, double, double> SingleAdapterRates(NetworkAdapterInfo adapter) =>
        (_, downloadMbps, uploadMbps) => OnNicAdapterRates(adapter, downloadMbps, uploadMbps);
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

    private NicQualityState GetNicQuality(NetworkAdapterInfo adapter) =>
        _nicQuality.GetOrAdd(adapter.Id, _ => new NicQualityState { AdapterName = adapter.Name });

    private void OnNicLatency(NetworkAdapterInfo adapter, double latency)
    {
        if (!IsTesting) return;
        var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0;
        var state = GetNicQuality(adapter);
        lock (state.Sync)
        {
            if (elapsed >= _options.AverageDelaySec) state.LanSamples.Add(latency);
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = AllAdapterRates.FirstOrDefault(r => r.AdapterId == adapter.Id);
            if (item == null) return;
            item.LatencyMs = latency;
            UpdateMultiNicAggregateQuality();
        });
    }

    private void OnNicWanLatency(NetworkAdapterInfo adapter, double latency)
    {
        if (!IsTesting) return;
        var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0;
        var state = GetNicQuality(adapter);
        lock (state.Sync)
        {
            if (elapsed >= _options.AverageDelaySec) state.WanSamples.Add(latency);
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = AllAdapterRates.FirstOrDefault(r => r.AdapterId == adapter.Id);
            if (item == null) return;
            item.WanLatencyMs = latency;
            UpdateMultiNicAggregateQuality();
        });
    }

    private void OnNicJitterRtt(NetworkAdapterInfo adapter, double rtt)
    {
        if (!IsTesting) return;
        var state = GetNicQuality(adapter);
        double jitter;
        lock (state.Sync)
        {
            state.JitterRttSamples.Add(rtt);
            if (state.JitterRttSamples.Count > 50) state.JitterRttSamples.RemoveAt(0);
            jitter = ComputeJitterFromSamples(state.JitterRttSamples);
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var item = AllAdapterRates.FirstOrDefault(r => r.AdapterId == adapter.Id);
            if (item == null) return;
            if (!double.IsNaN(jitter)) item.JitterMs = jitter;
            UpdateMultiNicAggregateQuality();
        });
    }

    private void UpdateMultiNicAggregateQuality()
    {
        var lanValues = AllAdapterRates.Where(r => r.LatencyMs.HasValue).Select(r => r.LatencyMs!.Value).ToList();
        if (lanValues.Count > 0) LatencyMs = lanValues.Average();

        var wanValues = AllAdapterRates.Where(r => r.WanLatencyMs.HasValue).Select(r => r.WanLatencyMs!.Value).ToList();
        if (wanValues.Count > 0) WanLatencyMs = wanValues.Average();

        var jitterValues = AllAdapterRates.Where(r => r.JitterMs.HasValue).Select(r => r.JitterMs!.Value).ToList();
        JitterMs = jitterValues.Count > 0 ? jitterValues.Max() : null;
        if (Application.Current.MainWindow is Views.MainWindow mw)
            mw.JitterText.Text = FormatHelper.FormatLatency(JitterMs);
    }

    private void ApplyPerNicQuality(List<SpeedTestResult> results, SpeedTestResult aggregate)
    {
        foreach (var r in results)
        {
            var state = FindNicQualityState(r);
            if (state == null) continue;
            lock (state.Sync)
            {
                if (state.LanSamples.Count > 0) r.LatencyMs = state.LanSamples.Average();
                if (state.WanSamples.Count > 0) r.WanLatencyMs = state.WanSamples.Average();
                var nicJitter = ComputeJitterFromSamples(state.JitterRttSamples);
                r.JitterMs = double.IsNaN(nicJitter) ? null : nicJitter;
            }
        }

        var valid = results.Where(r => string.IsNullOrEmpty(r.ErrorMessage)).ToList();
        var validLan = valid.Where(r => r.LatencyMs > 0).ToList();
        aggregate.LatencyMs = validLan.Count > 0 ? validLan.Average(r => r.LatencyMs) : 0;

        var validWan = valid.Where(r => r.WanLatencyMs.HasValue && r.WanLatencyMs.Value > 0).ToList();
        aggregate.WanLatencyMs = validWan.Count > 0 ? validWan.Average(r => r.WanLatencyMs!.Value) : null;

        var validJitter = valid.Where(r => r.JitterMs.HasValue).ToList();
        aggregate.JitterMs = validJitter.Count > 0 ? validJitter.Max(r => r.JitterMs!.Value) : null;

        aggregate.PacketLoss = PacketLossPercent ?? 0;
    }

    private NicQualityState? FindNicQualityState(SpeedTestResult result)
    {
        if (!string.IsNullOrEmpty(result.NetworkAdapterId) &&
            _nicQuality.TryGetValue(result.NetworkAdapterId, out var state))
            return state;

        return _nicQuality.Values.FirstOrDefault(x =>
            string.Equals(x.AdapterName, result.NetworkAdapterName, StringComparison.OrdinalIgnoreCase));
    }

    private static double ComputeJitterFromSamples(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2) return double.NaN;
        var avg = samples.Average();
        return Math.Sqrt(samples.Sum(x => (x - avg) * (x - avg)) / (samples.Count - 1));
    }

    private void OnActiveThreadCount(int count) { if (!IsTesting) return; if (count > _maxActiveThreadCount) _maxActiveThreadCount = count; Application.Current.Dispatcher.InvokeAsync(() => ActiveThreadCount = count); }
    private void OnLatency(double latency) { if (!IsTesting) return; var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0; var added = elapsed >= _options.AverageDelaySec; Application.Current.Dispatcher.InvokeAsync(() => LatencyMs = latency); int lanCount; lock (_latencyLock) { if (added) _lanLatencies.Add(latency); lanCount = _lanLatencies.Count; } Logger.Log($"[D-LAN] raw={latency:F1}ms elapsed={elapsed:F1}s added={(added?"YES":"NO")} count={lanCount}"); }

    private double ComputeJitter()
    {
        lock (_latencyLock)
        {
            if (_jitterSamples.Count < 2) return double.NaN;
            var avg = _jitterSamples.Average();
            return Math.Sqrt(_jitterSamples.Sum(x => (x - avg) * (x - avg)) / (_jitterSamples.Count - 1));
        }
    }
    private void OnJitterSample(double rtt)
    {
        if (!IsTesting) return;
        lock (_latencyLock)
        {
            _jitterSamples.Add(rtt);
            if (_jitterSamples.Count > 50) _jitterSamples.RemoveAt(0);
        }
        var j = ComputeJitter();
        JitterMs = double.IsNaN(j) ? null : j;
        int jitterCount;
        lock (_latencyLock) { jitterCount = _jitterSamples.Count; }
        Logger.Log($"[D-JIT] rawRtt={rtt:F1}ms count={jitterCount} jitter={j:F1}");
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Application.Current.MainWindow is Views.MainWindow mw)
                mw.JitterText.Text = Helpers.FormatHelper.FormatLatency(j);
        });
    }
    private void OnWanLatency(double latency) { if (!IsTesting) return; var elapsed = _stopwatch?.Elapsed.TotalSeconds ?? 0; var added = elapsed >= _options.AverageDelaySec; Application.Current.Dispatcher.InvokeAsync(() => WanLatencyMs = latency); int wanCount; lock (_latencyLock) { if (added) _wanLatencies.Add(latency); wanCount = _wanLatencies.Count; } Logger.Log($"[D-WAN] raw={latency:F1}ms elapsed={elapsed:F1}s added={(added?"YES":"NO")} count={wanCount}"); }
    private void OnPacketLossSample(PacketLossSample sample)
    {
        if (!IsTesting) return;
        Interlocked.Add(ref _packetLossSent, sample.Sent);
        Interlocked.Add(ref _packetLossReceived, sample.Received);
        var sent = Interlocked.Read(ref _packetLossSent);
        var received = Interlocked.Read(ref _packetLossReceived);
        var percent = sent <= 0 ? (double?)null : Math.Max(0, (sent - received) * 100.0 / sent);
        var level = !percent.HasValue || percent.Value <= 0 ? 0 : percent.Value < 5 ? 1 : 2;
        // detail 在 UI 线程计算：资源字典访问必须在 UI 线程
        Logger.Log($"[D-LOSS] batch={sample.Received}/{sample.Sent} ({sample.Method}) total={received}/{sent} loss={percent?.ToString("F1") ?? "--"}%");
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            PacketLossPercent = percent;
            PacketLossDetail = string.Format(LocalizationService.Get("Metric_PacketLossDetail"), received, sent);
            PacketLossLevel = level;
            OnPropertyChanged(nameof(PacketLossDisplay));
        });
    }
    private void OnTotalBytes(long bytes) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => TotalBytes = bytes); }
    private void OnAverageSpeed(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageMbps = avg); }
    private void OnAverageDownload(double avg) { if (!IsTesting) return; if (_currentTestMode == "上传") return; Application.Current.Dispatcher.InvokeAsync(() => AverageDownloadMbps = avg); }
    private void OnAverageUpload(double avg) { if (!IsTesting) return; if (_currentTestMode == "下载") return; Application.Current.Dispatcher.InvokeAsync(() => AverageUploadMbps = avg); }
    private void OnAverageTotal(double avg) { if (!IsTesting) return; Application.Current.Dispatcher.InvokeAsync(() => AverageTotalMbps = avg); }

    [RelayCommand]
    private void CancelTest()
    {
        // 前置准备阶段（URL 预探测最长 15s，此时 IsTesting 仍为 false）也受理取消，
        // 否则 Web UI 在此期间发出的停止请求会被静默丢弃。
        if (!IsTesting)
        {
            if (_pendingTestStart) RequestPendingTestCancel();
            return;
        }
        _elapsedTimer?.Stop();
        Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        if (_elapsedTimer != null && _elapsedTickHandler != null)
            _elapsedTimer.Tick -= _elapsedTickHandler;
        StatusText = "已取消";
        _cts?.Cancel();
    }

    /// <summary>
    /// 前置准备阶段请求取消：让 StartTestCommon 在真正开始前中止本次测速。
    /// </summary>
    private void RequestPendingTestCancel()
    {
        _cancelPending = true;
        StatusText = "已取消";
    }

    /// <summary>
    /// 供 Web API 使用：在准备阶段也能请求取消（内部测试钩子）。
    /// </summary>
    internal void RequestTestStopForTest()
    {
        if (IsTesting)
        {
            if (CancelTestCommand.CanExecute(null)) CancelTestCommand.Execute(null);
            return;
        }
        if (_pendingTestStart) RequestPendingTestCancel();
    }

    /// <summary>
    /// 供 Web API 使用：是否存在“已受理但尚未进入运行态”的测速启动请求。
    /// </summary>
    internal bool IsTestStartPending => _pendingTestStart;

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
    private void OpenWebServer()
    {
        var vm = _serviceProvider.GetRequiredService<WebServerViewModel>();
        vm.CloseRequested -= ClosePage;
        vm.CloseRequested += ClosePage;
        CurrentPage = new Views.WebServerPage { DataContext = vm };
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

    public void RefreshProfilesForWeb() => RefreshProfiles();

    private void RefreshProfiles()
    {
        var previousSelectedUrls = UrlSelectionItems
            .Where(x => x.IsSelected)
            .Select(x => x.Url)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousUrls = UrlSelectionItems.Select(x => x.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var profiles = _profileService.GetAllProfiles();
        var previousId = SelectedProfile?.Id;
        Profiles = new ObservableCollection<SpeedTestProfile>(profiles);

        var sameProfile = previousId != null
            ? Profiles.FirstOrDefault(p => p.Id == previousId)
            : null;
        var target = sameProfile ?? Profiles.FirstOrDefault();

        _suppressDefaultUrlSelection = true;
        try
        {
            SelectedProfile = target;
        }
        finally
        {
            _suppressDefaultUrlSelection = false;
        }

        if (target == null)
        {
            UrlSelectionItems.Clear();
            return;
        }

        UpdateUrlSelectionItems();
        var preserveSelection = sameProfile != null && previousUrls.Count > 0;
        foreach (var item in UrlSelectionItems)
            item.IsSelected = !preserveSelection || previousSelectedUrls.Contains(item.Url) || !previousUrls.Contains(item.Url);



    }

    private async Task RefreshHistoryAsync()
    {
        var records = await Task.Run(() => _dataService.GetRecords(1, 20));
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
/// 单张网卡的质量指标累计状态。
/// </summary>
internal sealed class NicQualityState
{
    public string AdapterName { get; set; } = "";

    public object Sync { get; } = new();

    public List<double> LanSamples { get; } = new();

    public List<double> WanSamples { get; } = new();

    public List<double> JitterRttSamples { get; } = new();
}


/// <summary>
/// 网卡勾选项（多网卡同时测速用）
/// </summary>
public partial class AdapterSelectionItem : ObservableObject
{
    public NetworkAdapterInfo Adapter { get; set; } = new();

    public string Name => Adapter.Name;

    public string? IPAddress => Adapter.IPAddress;

    public bool IsVirtual => Adapter.IsVirtual;

    public string KindText => IsVirtual ? "虚拟" : "物理";

    [ObservableProperty]
    private bool _isSelected = true;
}
/// <summary>
/// 网卡实时速率条目
/// </summary>
public partial class AdapterRateItem : ObservableObject
{
    public string AdapterId { get; set; } = "";

    public string Name { get; set; } = "";

    public bool IsVirtual { get; set; }

    public string KindText => IsVirtual ? "虚拟" : "物理";

    [ObservableProperty] private double? _latencyMs;

    [ObservableProperty] private double? _wanLatencyMs;

    [ObservableProperty] private double? _jitterMs;

    public string? IpAddress { get; set; }

    [ObservableProperty]
    private string _statusText = "待测";

    [ObservableProperty]
    private double _downloadMbps;

    [ObservableProperty]
    private double _uploadMbps;
}
