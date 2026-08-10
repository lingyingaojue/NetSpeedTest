using System.Windows;
using NetSpeedTest.Services;

namespace NetSpeedTest.Views;

public partial class EulaWindow : Window
{
    public bool Agreed { get; private set; }
    public bool Revoked { get; private set; }
    private readonly bool _isFirstLaunch;

    public string EulaText { get; } = NetSpeedTest.Models.EulaText.Text;

    public EulaWindow(bool isFirstLaunch = true)
    {
        InitializeComponent();
        DataContext = this;
        _isFirstLaunch = isFirstLaunch;
        Loaded += (_, _) => Helpers.WindowHelper.ClampToScreen(this);

        if (isFirstLaunch)
        {
            HeaderText.Text = "请仔细阅读以下协议，同意后方可使用本软件";
            AgreeBtn.Visibility = Visibility.Visible;
            DisagreeBtn.Content = "不同意";
            DisagreeBtn.Width = 80;
            CloseBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeaderText.Text = "用户协议与声明（已同意）";
            AgreeBtn.Visibility = Visibility.Collapsed;
            DisagreeBtn.Content = "撤销同意并退出";
            DisagreeBtn.Width = 130;
            CloseBtn.Visibility = Visibility.Visible;
        }
    }

    private void Agree_Click(object sender, RoutedEventArgs e)
    {
        Agreed = true;
        Close();
    }

    private void Disagree_Click(object sender, RoutedEventArgs e)
    {
        if (_isFirstLaunch)
        {
            Agreed = false;
        }
        else
        {
            var result = System.Windows.MessageBox.Show(
                "撤销同意后本软件将退出，下次打开需重新同意协议。确定要撤销吗？",
                "NetSpeedTest", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try { using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\NetSpeedTest", true); rk?.DeleteValue("EulaAccepted", false); } catch (Exception ex) { Logger.Log($"EULA revoke failed: {ex.Message}"); }
            Revoked = true;
        }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
