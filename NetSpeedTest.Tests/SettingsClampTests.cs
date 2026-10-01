using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// BUG-CLAMP-003 回归：设置参数边界必须以后端/VM/XAML 共用的 SpeedOptionLimits 单一事实源为准
/// （统一到 UI 口径 testTimeoutSec 10-600、threadRampUpMs 0-2000）；单字段越界静默夹取，
/// 交叉非法 testTimeoutSec&lt;averageDelaySec 由 ValidateSpeedOptions 判为应拒绝（HTTP 400、不落盘）。
/// </summary>
public class SettingsClampTests
{
    [Theory]
    [InlineData(45, 45)]   // 界内
    [InlineData(10, 10)]   // 下界
    [InlineData(600, 600)] // 上界
    [InlineData(9, 10)]    // 旧后端下限 5 口径下会被放过，统一后必须夹到 10
    [InlineData(5, 10)]
    [InlineData(601, 600)] // 上界 +1
    [InlineData(-10, 10)]  // 负数
    public void TestTimeoutSec_clamps_to_unified_bounds(int input, int expected)
    {
        var actual = Math.Clamp(input, SpeedOptionLimits.TestTimeoutSecMin, SpeedOptionLimits.TestTimeoutSecMax);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 2000)] // 旧后端上限 5000 口径下会被放过，统一后必须夹到 2000
    [InlineData(5000, 2000)]
    [InlineData(-1, 0)]
    public void ThreadRampUpMs_clamps_to_unified_bounds(int input, int expected)
    {
        var actual = Math.Clamp(input, SpeedOptionLimits.ThreadRampUpMsMin, SpeedOptionLimits.ThreadRampUpMsMax);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Unified_bounds_match_UI_caliber()
    {
        Assert.Equal(10, SpeedOptionLimits.TestTimeoutSecMin);
        Assert.Equal(600, SpeedOptionLimits.TestTimeoutSecMax);
        Assert.Equal(0, SpeedOptionLimits.ThreadRampUpMsMin);
        Assert.Equal(2000, SpeedOptionLimits.ThreadRampUpMsMax);
    }

    [Fact]
    public void Timeout_below_average_window_is_rejected()
    {
        var options = new SpeedTestOptions { TestTimeoutSec = 10, AverageDelaySec = 30 };
        var error = WebServerService.ValidateSpeedOptions(options);
        Assert.NotNull(error);
        Assert.Contains("averageDelaySec", error);
    }

    [Fact]
    public void Timeout_equal_to_average_window_is_valid()
    {
        var options = new SpeedTestOptions { TestTimeoutSec = 10, AverageDelaySec = 10 };
        Assert.Null(WebServerService.ValidateSpeedOptions(options));
    }

    [Fact]
    public void Timeout_above_average_window_is_valid()
    {
        var options = new SpeedTestOptions { TestTimeoutSec = 60, AverageDelaySec = 10 };
        Assert.Null(WebServerService.ValidateSpeedOptions(options));
    }

    [Fact]
    public void XAML_slider_bounds_match_SpeedOptionLimits()
    {
        var xaml = ReadSettingsXaml();

        var timeout = ReadSliderBounds(xaml, nameof(SpeedTestOptions.TestTimeoutSec));
        Assert.Equal(SpeedOptionLimits.TestTimeoutSecMin, (int)timeout.min);
        Assert.Equal(SpeedOptionLimits.TestTimeoutSecMax, (int)timeout.max);

        var ramp = ReadSliderBounds(xaml, nameof(SpeedTestOptions.ThreadRampUpMs));
        Assert.Equal(SpeedOptionLimits.ThreadRampUpMsMin, (int)ramp.min);
        Assert.Equal(SpeedOptionLimits.ThreadRampUpMsMax, (int)ramp.max);
    }

    // ===== 辅助 =====

    private static string ReadSettingsXaml()
    {
        var relative = Path.Combine("Views", "SettingsWindow.xaml");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidate in new[] { relative, Path.Combine("NetSpeedTest", relative) })
            {
                var path = Path.Combine(dir.FullName, candidate);
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"SettingsWindow.xaml not found above {AppContext.BaseDirectory}");
    }

    private static (double min, double max) ReadSliderBounds(string xaml, string boundProperty)
    {
        var match = Regex.Match(
            xaml,
            $@"<Slider[^>]*?Minimum=""(-?[0-9]+(?:\.[0-9]+)?)""\s+Maximum=""(-?[0-9]+(?:\.[0-9]+)?)""\s+Value=""{{Binding {boundProperty}}}""");
        Assert.True(match.Success, $"未找到绑定 {boundProperty} 的 Slider");
        return (
            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }
}
