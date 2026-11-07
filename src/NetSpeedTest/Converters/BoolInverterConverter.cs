using System.Globalization;
using System.Windows.Data;

namespace NetSpeedTest.Converters;

/// <summary>
/// 布尔值取反转换器
/// </summary>
public class BoolInverterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return Binding.DoNothing;
    }
}
