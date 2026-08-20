using System.Linq;
using System.Windows;
using NetSpeedTest.Helpers;
using NetSpeedTest.Models;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class TestResultWindow : Window
{
    private readonly Action? _exportAction;

    public TestResultWindow(string testMode, double elapsed, double dlRate,
        double? ulRate, long totalBytes,
        double totalAvg, double lanLat, double wanLat, double jitter,
        Action? exportAction = null,
        IEnumerable<SpeedTestResult>? nicResults = null)
    {
        _exportAction = exportAction;
        InitializeComponent();

        NicResultsList.ItemsSource = nicResults;
        NicResultsList.Visibility = nicResults != null && nicResults.Any() ? Visibility.Visible : Visibility.Collapsed;
        Logger.Log($"[D-DLG] received: lanLat={lanLat:F1} wanLat={wanLat:F1} jitter={jitter:F1}");

        if (testMode == "上传")
            DownloadRow.Visibility = Visibility.Collapsed;
        else if (testMode == "下载")
            UploadRow.Visibility = Visibility.Collapsed;

        TotalAvgRow.Visibility = testMode == "双向" ? Visibility.Visible : Visibility.Collapsed;

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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("NetSpeedTest 测速结果");
            if (DownloadRow.Visibility == Visibility.Visible)
                sb.AppendLine($"下载平均速度: {DlValue.Text}");
            if (UploadRow.Visibility == Visibility.Visible)
                sb.AppendLine($"上传平均速度: {UlValue.Text}");
            if (TotalAvgRow.Visibility == Visibility.Visible)
                sb.AppendLine($"总均速: {TotalAvgValue.Text}");
            sb.AppendLine($"总流量: {TotalBytesValue.Text}");
            sb.AppendLine($"内网平均延迟: {LanValue.Text}");
            sb.AppendLine($"平均抖动延迟: {JitterValue.Text}");
            sb.AppendLine($"外网平均延迟: {WanValue.Text}");
            sb.AppendLine($"测速时长: {ElapsedValue.Text}");
            Clipboard.SetText(sb.ToString().TrimEnd());
        }
        catch { }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try { _exportAction?.Invoke(); } catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
