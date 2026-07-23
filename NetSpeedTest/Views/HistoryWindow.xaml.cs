using System.Windows;
using System.Windows.Input;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.HistoryViewModel vm && vm.SelectedRecord != null)
        {
            var r = vm.SelectedRecord;
            MessageBox.Show(
                $"测速详情\n\n时间: {r.Timestamp:yyyy-MM-dd HH:mm:ss}\n下载: {FormatHelper.FormatRate(r.DownloadMbps)}\n峰值: {FormatHelper.FormatRate(r.PeakMbps)}\n"
                + $"上传: {FormatHelper.FormatRate(r.UploadMbps)}\n延迟: {FormatHelper.FormatLatency(r.LatencyMs)}\n"
                + $"耗时: {FormatHelper.FormatDuration(r.DurationSeconds)}\n下载字节: {FormatHelper.FormatBytes(r.BytesDownloaded)}\n网卡: {r.NetworkAdapterName}",
                "测速详情", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
