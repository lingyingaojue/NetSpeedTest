using System.Diagnostics;
using System.Windows;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class UpdateWindow : Window
{
    private readonly string _downloadUrl;

    public UpdateWindow(string version, string body, string downloadUrl)
    {
        InitializeComponent();
        TitleText.Text = $"发现新版本 {version}";
        BodyText.Text = body;
        _downloadUrl = downloadUrl;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Log($"Open download failed: {ex.Message}"); }
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
