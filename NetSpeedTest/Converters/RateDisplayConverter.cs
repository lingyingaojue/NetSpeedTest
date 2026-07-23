using System.Globalization;
using System.Windows.Data;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Converters;

public class RateDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return FormatHelper.FormatRate(d);
        return "--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
