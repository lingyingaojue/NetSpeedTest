using System.Windows;

namespace NetSpeedTest.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Application.Current.Shutdown();
    }

    public void SetChartFocus(bool? isUpload)
    {
        Dispatcher.Invoke(() =>
        {
            if (isUpload == null)
            { DlCol.Width = new GridLength(1, GridUnitType.Star); UlCol.Width = new GridLength(1, GridUnitType.Star); }
            else if (isUpload.Value)
            { DlCol.Width = new GridLength(0.5, GridUnitType.Star); UlCol.Width = new GridLength(1.5, GridUnitType.Star); }
            else
            { DlCol.Width = new GridLength(1.5, GridUnitType.Star); UlCol.Width = new GridLength(0.5, GridUnitType.Star); }
        });
    }

    public void ScrollHistoryToTop()
    {
        if (RecentDataGrid.Items.Count > 0)
            RecentDataGrid.ScrollIntoView(RecentDataGrid.Items[0]);
    }
}
