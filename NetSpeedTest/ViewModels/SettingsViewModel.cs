using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;

namespace NetSpeedTest.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SpeedTestOptions _options;
    private readonly WebServerService _webServer;
    private bool _suppressThemeIndex;
    private bool _suppressCategoryIndex;
    private bool _suppressLanguageIndex;

    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _testTimeoutSec;
    [ObservableProperty] private int _averageDelaySec;
    [ObservableProperty] private double _rateWindowSec;
    [ObservableProperty] private int _nicPollIntervalMs;
    [ObservableProperty] private int _threadRampUpMs;
    [ObservableProperty] private int _latencyPollIntervalMs;
    [ObservableProperty] private string _jitterTargetHost;
    [ObservableProperty] private int _jitterPollIntervalMs;
    [ObservableProperty] private string _packetLossTargetHost;
    [ObservableProperty] private int _packetLossPollIntervalMs;
    [ObservableProperty] private bool _compensationEnabled;
    [ObservableProperty] private double _compensationThreshold;
    [ObservableProperty] private int _compensationConfirmSec;
    [ObservableProperty] private bool _adaptiveThreadsEnabled;

    [ObservableProperty] private int _selectedCategoryIndex;

    [ObservableProperty] private ObservableCollection<string> _categories = new();

    [ObservableProperty] private ObservableCollection<string> _themeOptions = new();

    [ObservableProperty] private ObservableCollection<string> _languageOptions = new();

    [ObservableProperty] private int _languageIndex;

    [ObservableProperty] private int _themeIndex;

    [ObservableProperty] private string _adStatusText = "";
    [ObservableProperty] private string _adSponsorName = "";

    [ObservableProperty] private bool _webServerEnabled;
    [ObservableProperty] private string _webServerStatusText = "";






    partial void OnSelectedCategoryIndexChanged(int value)
    {
        if (_suppressCategoryIndex) return;
    }
    partial void OnLanguageIndexChanged(int value)
    {
        if (_suppressLanguageIndex) return;
        if (value < 0 || value > 1) return;

        var target = value == 0 ? LanguageMode.ZhCN : LanguageMode.EnUS;
        if (LocalizationService.Current == target) return;

        Logger.Log($"LanguageIndex changed to {value}");
        LocalizationService.Apply(target);
    }
    partial void OnThemeIndexChanged(int value)
    {
        if (_suppressThemeIndex) return;
        ThemeService.Apply(value == 0 ? ThemeMode.Dark : ThemeMode.Light);
    }
    private void RefreshCategories()
    {
        var current = SelectedCategoryIndex;
        _suppressCategoryIndex = true;
        var items = new[]
        {
            LocalizationService.Get("Set_Cat_Params"),
            LocalizationService.Get("Set_Cat_Network"),
            LocalizationService.Get("Set_Cat_Comp"),
            LocalizationService.Get("Set_Cat_Ads"),
            LocalizationService.Get("Set_Cat_Appearance"),
        };
        if (Categories.Count == items.Length)
        {
            for (int i = 0; i < items.Length; i++) Categories[i] = items[i];
        }
        else
        {
            Categories = new ObservableCollection<string>(items);
        }
        SelectedCategoryIndex = current;
        _suppressCategoryIndex = false;
    }

    private void RefreshOptions()
    {
        var currentTheme = ThemeIndex;
        var currentLanguageIndex = LanguageIndex is 0 or 1
            ? LanguageIndex
            : (LocalizationService.Current == LanguageMode.ZhCN ? 0 : 1);

        _suppressThemeIndex = true;
        _suppressLanguageIndex = true;

        var themes = new[]
        {
            LocalizationService.Get("Theme_Dark"),
            LocalizationService.Get("Theme_Light")
        };

        if (ThemeOptions.Count == themes.Length)
        {
            ThemeOptions[0] = themes[0];
            ThemeOptions[1] = themes[1];
        }
        else
        {
            ThemeOptions = new ObservableCollection<string>(themes);
        }

        ThemeIndex = currentTheme;

        // 语言名称固定，不需要在每次语言切换时重建集合。
        if (LanguageOptions.Count != 2)
        {
            LanguageOptions = new ObservableCollection<string>
            {
                "简体中文",
                "English"
            };
        }

        LanguageIndex = currentLanguageIndex;

        _suppressThemeIndex = false;
        _suppressLanguageIndex = false;
    }
    public SettingsViewModel(SpeedTestOptions options, Microsoft.Extensions.Configuration.IConfiguration config, WebServerService webServer)
    {
        _options = options;
        _webServer = webServer;
        RefreshCategories();
        RefreshOptions();
        LocalizationService.LanguageChanged += RefreshCategories;
        LocalizationService.LanguageChanged += RefreshOptions;
        AdSponsorName = config.GetSection("Advertising")["SponsorName"] ?? "暂无";
        ThreadCount = Math.Clamp(options.ThreadCount, 2, 1024);
        TestTimeoutSec = options.TestTimeoutSec;
        AverageDelaySec = options.AverageDelaySec;
        RateWindowSec = options.RateWindowSec;
        NicPollIntervalMs = options.NicPollIntervalMs;
        ThreadRampUpMs = options.ThreadRampUpMs;
        LatencyPollIntervalMs = options.LatencyPollIntervalMs;
        JitterTargetHost = options.JitterTargetHost;
        JitterPollIntervalMs = options.JitterPollIntervalMs;
        PacketLossTargetHost = options.PacketLossTargetHost;
        PacketLossPollIntervalMs = options.PacketLossPollIntervalMs;
        CompensationEnabled = options.CompensationEnabled;
        CompensationThreshold = options.CompensationThreshold;
        CompensationConfirmSec = options.CompensationConfirmSec;
        AdaptiveThreadsEnabled = options.AdaptiveThreadsEnabled;
        ThemeIndex = ThemeService.Current == ThemeMode.Dark ? 0 : 1;
        LanguageIndex = LocalizationService.Current == LanguageMode.ZhCN ? 0 : 1;
        RefreshAdStatus();
        WebServerEnabled = _webServer.Enabled;
        RefreshWebStatus();
    }


    private void RefreshWebStatus()
    {
        WebServerStatusText = _webServer.Enabled ? $"运行中 · http://127.0.0.1:{_webServer.CurrentPort}" : "未启动";
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
        ThreadCount = Math.Clamp(ThreadCount, 2, 1024);
        TestTimeoutSec = Math.Clamp(TestTimeoutSec, 5, 600);
        AverageDelaySec = Math.Clamp(AverageDelaySec, 1, 30);
        RateWindowSec = Math.Clamp(RateWindowSec, 0.5, 10.0);
        NicPollIntervalMs = Math.Clamp(NicPollIntervalMs, 200, 5000);
        ThreadRampUpMs = Math.Clamp(ThreadRampUpMs, 0, 5000);
        LatencyPollIntervalMs = Math.Clamp(LatencyPollIntervalMs, 500, 10000);
        JitterPollIntervalMs = Math.Clamp(JitterPollIntervalMs, 500, 5000);
        PacketLossPollIntervalMs = Math.Clamp(PacketLossPollIntervalMs, 500, 5000);
        if (string.IsNullOrWhiteSpace(PacketLossTargetHost)) PacketLossTargetHost = "8.8.8.8";
        CompensationThreshold = Math.Clamp(CompensationThreshold, 0.3, 0.8);
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
        _options.PacketLossTargetHost = PacketLossTargetHost;
        _options.PacketLossPollIntervalMs = PacketLossPollIntervalMs;
        _options.CompensationEnabled = CompensationEnabled;
        _options.CompensationThreshold = CompensationThreshold;
        _options.CompensationConfirmSec = CompensationConfirmSec;
        _options.AdaptiveThreadsEnabled = AdaptiveThreadsEnabled;
        ThemeService.Save(ThemeIndex == 0 ? ThemeMode.Dark : ThemeMode.Light);
        var languageMode = LanguageIndex switch
        {
            0 => LanguageMode.ZhCN,
            1 => LanguageMode.EnUS,
            _ => LocalizationService.Current
        };
        LocalizationService.Save(languageMode);
        _webServer.SetEnabled(WebServerEnabled);
        _webServer.SaveEnabled(WebServerEnabled);

        PersistSpeedOptions();
        MessageBox.Show("设置已保存", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Information);
        CloseWindow();
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow();
    }


    private void PersistSpeedOptions()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "appsettings.json");

            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var speed = root["SpeedTest"] as JsonObject ?? new JsonObject();
            speed["ThreadCount"] = ThreadCount;
            speed["TestTimeoutSec"] = TestTimeoutSec;
            speed["AverageDelaySec"] = AverageDelaySec;
            speed["RateWindowSec"] = RateWindowSec;
            speed["NicPollIntervalMs"] = NicPollIntervalMs;
            speed["ThreadRampUpMs"] = ThreadRampUpMs;
            speed["LatencyPollIntervalMs"] = LatencyPollIntervalMs;
            speed["JitterTargetHost"] = JitterTargetHost;
            speed["JitterPollIntervalMs"] = JitterPollIntervalMs;
            speed["PacketLossTargetHost"] = PacketLossTargetHost;
            speed["PacketLossPollIntervalMs"] = PacketLossPollIntervalMs;
            speed["CompensationEnabled"] = CompensationEnabled;
            speed["CompensationThreshold"] = CompensationThreshold;
            speed["CompensationConfirmSec"] = CompensationConfirmSec;
            speed["AdaptiveThreadsEnabled"] = AdaptiveThreadsEnabled;
            root["SpeedTest"] = speed;

            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Log($"PersistSpeedOptions failed: {ex.Message}");
        }
    }
    public event Action? CloseRequested;

    private void CloseWindow()
    {
        LocalizationService.LanguageChanged -= RefreshCategories;
        LocalizationService.LanguageChanged -= RefreshOptions;
        CloseRequested?.Invoke();
    }
}
