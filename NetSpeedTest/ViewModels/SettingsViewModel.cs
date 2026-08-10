using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSpeedTest.Models;
using System.Windows;

namespace NetSpeedTest.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SpeedTestOptions _options;

    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _testTimeoutSec;
    [ObservableProperty] private int _averageDelaySec;
    [ObservableProperty] private double _rateWindowSec;
    [ObservableProperty] private int _nicPollIntervalMs;
    [ObservableProperty] private int _threadRampUpMs;
    [ObservableProperty] private int _latencyPollIntervalMs;
    [ObservableProperty] private string _jitterTargetHost;
    [ObservableProperty] private int _jitterPollIntervalMs;
    [ObservableProperty] private bool _compensationEnabled;
    [ObservableProperty] private double _compensationThreshold;
    [ObservableProperty] private int _compensationExtraThreads;
    [ObservableProperty] private int _compensationConfirmSec;
    [ObservableProperty] private bool _adaptiveThreadsEnabled;

    [ObservableProperty] private int _selectedCategoryIndex;

    public List<string> Categories { get; } = ["测速参数", "网络监控", "掉速补偿", "广告"];

    [ObservableProperty] private string _adStatusText = "";
    [ObservableProperty] private string _adSponsorName = "";

    public int[] ThreadOptions { get; } = { 2, 4, 8, 16, 32, 64, 128, 256, 512 };

    public int ThreadIndex
    {
        get => Math.Clamp(Array.IndexOf(ThreadOptions, ThreadCount), 0, 8);
        set => ThreadCount = ThreadOptions[Math.Clamp(value, 0, 8)];
    }

    partial void OnThreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(ThreadIndex));
    }

    public SettingsViewModel(SpeedTestOptions options, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _options = options;
        AdSponsorName = config.GetSection("Advertising")["SponsorName"] ?? "暂无";
        ThreadCount = ThreadOptions.Contains(options.ThreadCount) ? options.ThreadCount : 128;
        TestTimeoutSec = options.TestTimeoutSec;
        AverageDelaySec = options.AverageDelaySec;
        RateWindowSec = options.RateWindowSec;
        NicPollIntervalMs = options.NicPollIntervalMs;
        ThreadRampUpMs = options.ThreadRampUpMs;
        LatencyPollIntervalMs = options.LatencyPollIntervalMs;
        JitterTargetHost = options.JitterTargetHost;
        JitterPollIntervalMs = options.JitterPollIntervalMs;
        CompensationEnabled = options.CompensationEnabled;
        CompensationThreshold = options.CompensationThreshold;
        CompensationExtraThreads = options.CompensationExtraThreads;
        CompensationConfirmSec = options.CompensationConfirmSec;
        AdaptiveThreadsEnabled = options.AdaptiveThreadsEnabled;
        RefreshAdStatus();
    }

    private void RefreshAdStatus()
    {
        var days = Helpers.AdManager.RemainingDays();
        AdStatusText = days.HasValue ? $"广告已关闭，剩余 {days.Value} 天" : "广告当前展示中";
    }

    [RelayCommand]
    private void CloseAd()
    {
        Helpers.AdManager.CloseAdFor7Days();
        RefreshAdStatus();
    }

    [RelayCommand]
    private void Save()
    {
        ThreadCount = Math.Clamp(ThreadCount, 1, 512);
        TestTimeoutSec = Math.Clamp(TestTimeoutSec, 5, 600);
        AverageDelaySec = Math.Clamp(AverageDelaySec, 1, 30);
        RateWindowSec = Math.Clamp(RateWindowSec, 0.5, 10.0);
        NicPollIntervalMs = Math.Clamp(NicPollIntervalMs, 200, 5000);
        ThreadRampUpMs = Math.Clamp(ThreadRampUpMs, 0, 5000);
        LatencyPollIntervalMs = Math.Clamp(LatencyPollIntervalMs, 500, 10000);
        JitterPollIntervalMs = Math.Clamp(JitterPollIntervalMs, 500, 5000);
        CompensationThreshold = Math.Clamp(CompensationThreshold, 0.3, 0.8);
        CompensationExtraThreads = Math.Clamp(CompensationExtraThreads, 0, 64);
        CompensationConfirmSec = Math.Clamp(CompensationConfirmSec, 1, 10);

        _options.ThreadCount = ThreadCount;
        _options.TestTimeoutSec = TestTimeoutSec;
        _options.AverageDelaySec = AverageDelaySec;
        _options.RateWindowSec = RateWindowSec;
        _options.NicPollIntervalMs = NicPollIntervalMs;
        _options.ThreadRampUpMs = ThreadRampUpMs;
        _options.LatencyPollIntervalMs = LatencyPollIntervalMs;
        _options.JitterTargetHost = JitterTargetHost;
        _options.JitterPollIntervalMs = JitterPollIntervalMs;
        _options.CompensationEnabled = CompensationEnabled;
        _options.CompensationThreshold = CompensationThreshold;
        _options.CompensationExtraThreads = CompensationExtraThreads;
        _options.CompensationConfirmSec = CompensationConfirmSec;
        _options.AdaptiveThreadsEnabled = AdaptiveThreadsEnabled;

        MessageBox.Show("设置已保存", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Information);
        CloseWindow();
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow();
    }

    public event Action? CloseRequested;

    private void CloseWindow()
    {
        CloseRequested?.Invoke();
    }
}
