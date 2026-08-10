using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NetSpeedTest.Helpers;
using NetSpeedTest.ViewModels;

namespace NetSpeedTest.Views;

public partial class MainWindow : Window
{
    private bool _isReallyClosing;

    public MainWindow()
    {
        InitializeComponent();
        SetupTray();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.TestCompletedNotify += (t, m) => TrayIcon.ShowBalloon(t, m);
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            if (Width > area.Width * 0.95) { Width = area.Width * 0.92; }
            if (Height > area.Height * 0.95) { Height = area.Height * 0.92; }
            Height = Width * 9.0 / 16.0;
            if (Width != 1280) { MinWidth = Width * 0.8; MinHeight = Height * 0.8; }
            Left = (area.Width - Width) / 2 + area.Left;
            Top = (area.Height - Height) / 2 + area.Top;
        };

        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            var area = SystemParameters.WorkArea;
            MaxHeight = area.Height + 8;
            MaxWidth = area.Width + 8;
        }
    }

    private void SetupTray()
    {
        var menuItems = new List<(string, Action)>
        {
            ("显示主窗口", () => { Show(); WindowState = WindowState.Normal; Activate(); }),
            ("开始下载测速", () => InvokeCommand("下载测速")),
            ("开始上传测速", () => InvokeCommand("上传测速")),
            ("开始双向测速", () => InvokeCommand("双向测速")),
            ("取消测速", () => InvokeCommand("取消")),
            ("-", () => { }),
            ("退出", () => ForceClose())
        };

        TrayIcon.Init(this,
            onShow: () => { Show(); WindowState = WindowState.Normal; Activate(); },
            menuItems,
            isTesting: () => (DataContext as MainViewModel)?.IsTesting ?? false);
    }

    private void InvokeCommand(string content)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();

            if (DataContext is not MainViewModel vm) return;
            var cmd = content switch
            {
                "下载测速" => vm.StartDownloadTestCommand,
                "上传测速" => vm.StartUploadTestCommand,
                "双向测速" => vm.StartFullTestCommand,
                "取消" => vm.CancelTestCommand,
                _ => null
            };
            if (cmd?.CanExecute(null) == true) cmd.Execute(null);
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isReallyClosing && !Environment.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void ForceClose() { _isReallyClosing = true; Application.Current.Shutdown(); }

    public void ClosePage()
    {
        if (DataContext is MainViewModel vm && vm.ClosePageCommand.CanExecute(null))
            vm.ClosePageCommand.Execute(null);
    }

    private void OnHorizontalMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    public void SetChartFocus(string? mode)
    {
        Dispatcher.Invoke(() =>
        {
            var dlTarget = mode == "上传" ? new GridLength(0, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            var ulTarget = mode == "下载" ? new GridLength(0, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);

            var anim = new GridLengthAnimation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                From = DlCol.Width,
                To = dlTarget
            };
            DlCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);

            anim = new GridLengthAnimation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                From = UlCol.Width,
                To = ulTarget
            };
            UlCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        });
    }

    // ===== 标题栏按钮 =====

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
