using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public SettingsViewModel(SpeedTestOptions options)
    {
        _options = options;
        ThreadCount = ThreadOptions.Contains(options.ThreadCount) ? options.ThreadCount : 128;
        TestTimeoutSec = options.TestTimeoutSec;
        AverageDelaySec = options.AverageDelaySec;
        RateWindowSec = options.RateWindowSec;
        NicPollIntervalMs = options.NicPollIntervalMs;
        ThreadRampUpMs = options.ThreadRampUpMs;
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

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "appsettings.json");
            Logger.Log($"设置保存路径: {path}");

            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            var root = JsonNode.Parse(json) ?? new JsonObject();
            root["SpeedTest"] = new JsonObject
            {
                ["ThreadCount"] = ThreadCount,
                ["TestTimeoutSec"] = TestTimeoutSec,
                ["AverageDelaySec"] = AverageDelaySec,
                ["RateWindowSec"] = RateWindowSec,
                ["NicPollIntervalMs"] = NicPollIntervalMs,
                ["ThreadRampUpMs"] = ThreadRampUpMs
            };
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _options.ThreadCount = ThreadCount;
            _options.TestTimeoutSec = TestTimeoutSec;
            _options.AverageDelaySec = AverageDelaySec;
            _options.RateWindowSec = RateWindowSec;
            _options.NicPollIntervalMs = NicPollIntervalMs;
            _options.ThreadRampUpMs = ThreadRampUpMs;

            MessageBox.Show("设置已保存", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Information);
            CloseWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow();
    }

    private void CloseWindow()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is Views.SettingsWindow)
            {
                w.Close();
                break;
            }
        }
    }
}
