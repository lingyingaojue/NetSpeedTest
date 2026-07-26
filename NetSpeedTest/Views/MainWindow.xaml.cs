using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NetSpeedTest.Helpers;

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
            if (DataContext is ViewModels.MainViewModel vm)
                vm.TestCompletedNotify += (t, m) => Helpers.TrayIcon.ShowBalloon(t, m);
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            if (Width > area.Width * 0.95) { Width = area.Width * 0.92; MinWidth = Width * 0.8; }
            if (Height > area.Height * 0.95) { Height = area.Height * 0.92; MinHeight = Height * 0.8; }
            Left = (area.Width - Width) / 2 + area.Left;
            Top = (area.Height - Height) / 2 + area.Top;
        };
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
            ("退出", () => { _isReallyClosing = true; Close(); })
        };

        TrayIcon.Init(this,
            onShow: () => { Show(); WindowState = WindowState.Normal; Activate(); },
            menuItems,
            isTesting: () => (DataContext as ViewModels.MainViewModel)?.IsTesting ?? false);
    }

    private void InvokeCommand(string content)
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();

            var btn = FindButton(content);
            if (btn?.Command?.CanExecute(null) == true)
                btn.Command.Execute(null);
        });
    }

    private static Button? FindButton(string content)
    {
        foreach (var window in Application.Current.Windows)
        {
            if (window is MainWindow mw)
            {
                foreach (var btn in FindVisualChildren<Button>(mw))
                    if (btn.Content?.ToString() == content) return btn;
            }
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void ForceClose() { _isReallyClosing = true; Close(); }

    private void OnHorizontalMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
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
}
