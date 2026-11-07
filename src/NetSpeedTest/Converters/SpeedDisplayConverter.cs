using System.Globalization;
using System.Windows.Data;

namespace NetSpeedTest.Converters;

public class SpeedDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bps && bps > 0)
        {
            if (bps >= 1_000_000_000) return (bps / 1_000_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + " Gbps";
            if (bps >= 1_000_000) return (bps / 1_000_000.0).ToString("F0", CultureInfo.InvariantCulture) + " Mbps";
            if (bps >= 1_000) return (bps / 1_000.0).ToString("F0", CultureInfo.InvariantCulture) + " Kbps";
            return bps + " bps";
        }
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
