using System.Windows;

namespace NetSpeedTest.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Helpers.WindowHelper.ClampToScreen(this);
    }
}
