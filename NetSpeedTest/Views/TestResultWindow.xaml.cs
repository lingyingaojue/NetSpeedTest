using System.Windows;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Views;

public partial class TestResultWindow : Window
{
    public TestResultWindow(string testMode, double elapsed, double dlRate,
        double? ulRate, long totalBytes,
        double totalAvg, double lanLat, double wanLat, double jitter)
    {
        InitializeComponent();

        if (testMode == "上传")
            DownloadRow.Visibility = Visibility.Collapsed;
        else if (testMode == "下载")
            UploadRow.Visibility = Visibility.Collapsed;

        ElapsedValue.Text = FormatHelper.FormatDuration(elapsed);
        DlValue.Text = FormatHelper.FormatRate(dlRate);
        UlValue.Text = FormatHelper.FormatRate(ulRate);
        TotalAvgValue.Text = FormatHelper.FormatRate(totalAvg);
        TotalBytesValue.Text = FormatHelper.FormatBytes(totalBytes);
        LanValue.Text = FormatHelper.FormatLatency(lanLat);
        JitterValue.Text = FormatHelper.FormatLatency(jitter);
        WanValue.Text = FormatHelper.FormatLatency(wanLat);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(
                $"NetSpeedTest 测速结果\n"
                + $"下载平均速度: {DlValue.Text}\n"
                + $"上传平均速度: {UlValue.Text}\n"
                + $"总均速: {TotalAvgValue.Text}\n"
                + $"总流量: {TotalBytesValue.Text}\n"
                + $"内网平均延迟: {LanValue.Text}\n"
                + $"抖动延迟: {JitterValue.Text}\n"
                + $"外网平均延迟: {WanValue.Text}\n"
                + $"测速时长: {ElapsedValue.Text}");
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
