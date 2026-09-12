using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// 双向测速上传速度低回归测试：双向模式必须让下载/上传各自独立自适应，
/// 避免下载吞吐掩盖上传增益或占用上传的并发预算。
/// </summary>
public class BidirectionalAdaptiveFeedbackTests
{
    [Fact]
    public void Download_only_mode_uses_download_throughput()
        => Assert.Equal(900.0, SpeedTestService.SelectAdaptiveObserveValue(1, 900.0, 5.0));

    [Fact]
    public void Upload_only_mode_uses_upload_throughput()
        => Assert.Equal(5.0, SpeedTestService.SelectAdaptiveObserveValue(2, 900.0, 5.0));

    [Theory]
    [InlineData(1500.0, 50.0, 50.0)]
    [InlineData(50.0, 1500.0, 50.0)]
    [InlineData(100.0, 100.0, 100.0)]
    public void Full_mode_uses_bottleneck_direction(double download, double upload, double expected)
        => Assert.Equal(expected, SpeedTestService.SelectAdaptiveObserveValue(0, download, upload));

    [Fact]
    public void Full_mode_does_not_mask_upload_gain_behind_download()
    {
        var low = SpeedTestService.SelectAdaptiveObserveValue(0, 1500.0, 10.0);
        var high = SpeedTestService.SelectAdaptiveObserveValue(0, 1500.0, 60.0);

        Assert.Equal(10.0, low);
        Assert.Equal(60.0, high);
        Assert.True(high >= low * 4, $"expected upload to drive feedback; low={low}, high={high}");
    }

    [Fact]
    public void Warmup_gate_keeps_single_direction_modes_unblocked()
    {
        Assert.True(SpeedTestService.ShouldObserveAdaptiveValue(1, false, false));
        Assert.True(SpeedTestService.ShouldObserveAdaptiveValue(2, false, false));
    }

    [Fact]
    public void Warmup_gate_waits_for_both_directions_in_full_mode()
    {
        Assert.False(SpeedTestService.ShouldObserveAdaptiveValue(0, false, false));
        Assert.False(SpeedTestService.ShouldObserveAdaptiveValue(0, true, false));
        Assert.False(SpeedTestService.ShouldObserveAdaptiveValue(0, false, true));
        Assert.True(SpeedTestService.ShouldObserveAdaptiveValue(0, true, true));
    }

    [Fact]
    public void Adaptive_start_threads_are_split_between_directions()
    {
        var even = SpeedTestService.SplitAdaptiveStartThreads(2, 1024);
        Assert.Equal(1, even.Download);
        Assert.Equal(1, even.Upload);

        var total = SpeedTestService.SplitAdaptiveStartThreads(10, 1024);
        Assert.Equal(10, total.Download + total.Upload);
        Assert.True(total.Download >= 1 && total.Upload >= 1);

        var clamped = SpeedTestService.SplitAdaptiveStartThreads(1, 64);
        Assert.Equal(2, clamped.Download + clamped.Upload);
    }
}