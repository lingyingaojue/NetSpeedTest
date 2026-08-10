using System.Windows;
using System.Windows.Controls;
using NetSpeedTest.Models;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class EulaPage : UserControl
{
    public string EulaText { get; } = NetSpeedTest.Models.EulaText.Text;

    public EulaPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Revoke_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "撤销同意后本软件将退出，下次打开需重新同意协议。确定要撤销吗？",
            "NetSpeedTest", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try { using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\NetSpeedTest", true); rk?.DeleteValue("EulaAccepted", false); } catch (Exception ex) { Logger.Log($"EULA revoke failed: {ex.Message}"); }

        if (Application.Current.MainWindow is MainWindow mw)
            mw.ForceClose();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.ClosePage();
    }
}
