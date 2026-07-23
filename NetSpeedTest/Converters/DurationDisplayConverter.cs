using System.Globalization;
using System.Windows.Data;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Converters;

public class DurationDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return FormatHelper.FormatDuration(d);
        return "--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
