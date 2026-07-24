using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Application.Current.Shutdown();
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
