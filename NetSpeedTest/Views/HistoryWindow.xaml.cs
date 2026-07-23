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
                $"测速详情\n\n时间: {r.Timestamp:yyyy-MM-dd HH:mm:ss}\n类型: {r.TestType}\n下载平均: {FormatHelper.FormatRate(r.DownloadMbps)}\n上传平均: {FormatHelper.FormatRate(r.UploadMbps)}\n"
                + $"内网延迟: {FormatHelper.FormatLatency(r.LatencyMs)}\n外网延迟: {FormatHelper.FormatLatency(r.WanLatencyMs)}\n"
                + $"总均速: {FormatHelper.FormatRate(r.AverageTotalMbps)}\n总流量: {FormatHelper.FormatBytes(r.TotalBytes)}\n"
                + $"耗时: {FormatHelper.FormatDuration(r.DurationSeconds)}\n网卡: {r.NetworkAdapterName}",
                "测速详情", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
