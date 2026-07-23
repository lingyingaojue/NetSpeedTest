using System.Windows;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Views;

public partial class TestResultWindow : Window
{
    public TestResultWindow(string testMode, double elapsed, double dlRate,
        double? ulRate, long totalBytes,
        double totalAvg, double lanLat, double wanLat)
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
        WanValue.Text = FormatHelper.FormatLatency(wanLat);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
