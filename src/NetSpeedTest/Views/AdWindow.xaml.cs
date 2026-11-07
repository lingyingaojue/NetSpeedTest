using System.Windows;
using Microsoft.Extensions.Configuration;

namespace NetSpeedTest.Views;

public partial class AdWindow : Window
{
    public AdWindow(IConfiguration config)
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea.Height;
            var maxH = work - 260;
            AdImage.MaxHeight = Math.Clamp(maxH, 300, 600);
        };

        var section = config.GetSection("Advertising");
        SponsorNameText.Text = section["SponsorName"] ?? "";
        SponsorDetailText.Text = section["SponsorDetail"] ?? "";

        var imgPath = section["ImagePath"] ?? "Resources/ad.jpg";
        try
        {
            var uri = new Uri($"pack://application:,,,/{imgPath}", UriKind.Absolute);
            AdImage.Source = new System.Windows.Media.Imaging.BitmapImage(uri);
        }
        catch (Exception ex)
        {
            Services.Logger.Log($"Ad image load failed ({imgPath}): {ex.Message}");
            AdImage.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
