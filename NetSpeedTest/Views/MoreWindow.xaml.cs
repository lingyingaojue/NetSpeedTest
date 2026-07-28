using System.Windows;

namespace NetSpeedTest.Views;

public partial class MoreWindow : Window
{
    public MoreWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Helpers.WindowHelper.ClampToScreen(this);
    }
}
