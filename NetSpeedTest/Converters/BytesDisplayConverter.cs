using System.Globalization;
using System.Windows.Data;
using NetSpeedTest.Helpers;

namespace NetSpeedTest.Converters;

public class BytesDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long l) return FormatHelper.FormatBytes(l);
        if (value is int i) return FormatHelper.FormatBytes(i);
        return "--";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
