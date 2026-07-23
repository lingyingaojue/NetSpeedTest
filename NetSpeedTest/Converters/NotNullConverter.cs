using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetSpeedTest.Converters;

/// <summary>
/// 非空转换器（支持 bool 和 Visibility 两种目标类型）
/// </summary>
public class NotNullConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNotNull = value != null && !string.IsNullOrEmpty(value?.ToString());

        if (targetType == typeof(Visibility))
            return isNotNull ? Visibility.Visible : Visibility.Collapsed;

        return isNotNull;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
