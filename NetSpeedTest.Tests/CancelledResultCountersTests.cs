using NetSpeedTest.Models;
using NetSpeedTest.ViewModels;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// BUG-D10 回归：用户在测速过程中停止/取消时，落库结果不得出现
/// BytesDownloaded/BytesUploaded/PeakMbps 三列为 0（而 TotalBytes 有值）的情况。
/// 进度回调采集到的分方向累计字节与峰值必须按测速模式补填进取消结果。
/// </summary>
public class CancelledResultCountersTests
{
    [Fact]
    public void Download_cancelled_fills_download_bytes_and_peak()
    {
        var result = new SpeedTestResult { TestType = "下载" };

        MainViewModel.ApplyCancelledTrafficCounters(result, downloadBytes: 3_151_793_000, uploadBytes: 0,
            peakDownloadMbps: 1777.21, peakUploadMbps: 0);

        Assert.Equal(3_151_793_000, result.BytesDownloaded);
        Assert.Equal(0, result.BytesUploaded);
        Assert.Equal(1777.21, result.PeakMbps, 6);
    }

    [Fact]
    public void Upload_cancelled_fills_upload_bytes_and_peak()
    {
        var result = new SpeedTestResult { TestType = "上传" };

        MainViewModel.ApplyCancelledTrafficCounters(result, downloadBytes: 0, uploadBytes: 398_798_757,
            peakDownloadMbps: 0, peakUploadMbps: 262.9);

        Assert.Equal(0, result.BytesDownloaded);
        Assert.Equal(398_798_757, result.BytesUploaded);
        Assert.Equal(262.9, result.PeakMbps, 6);
    }

    [Fact]
    public void Full_cancelled_fills_both_sides_and_sums_peak()
    {
        var result = new SpeedTestResult { TestType = "双向" };

        MainViewModel.ApplyCancelledTrafficCounters(result, downloadBytes: 4_091_871_218, uploadBytes: 345_585_377,
            peakDownloadMbps: 1784.83, peakUploadMbps: 272.09);

        Assert.Equal(4_091_871_218, result.BytesDownloaded);
        Assert.Equal(345_585_377, result.BytesUploaded);
        Assert.Equal(1784.83 + 272.09, result.PeakMbps, 6);
    }

    [Fact]
    public void Cancelled_before_any_progress_keeps_zero_without_fabricating()
    {
        // 极早取消、进度回调尚未给出任何字节/峰值时，保持 0（真实未传数据，不得伪造）。
        var result = new SpeedTestResult { TestType = "下载" };

        MainViewModel.ApplyCancelledTrafficCounters(result, 0, 0, 0, 0);

        Assert.Equal(0, result.BytesDownloaded);
        Assert.Equal(0, result.BytesUploaded);
        Assert.Equal(0, result.PeakMbps);
    }
}
