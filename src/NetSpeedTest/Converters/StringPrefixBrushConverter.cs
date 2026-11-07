using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NetSpeedTest.Converters;

/// <summary>
/// 更新日志分类前缀 → 颜色（🐛修复=红 / 🔧优化=蓝 / 🚀新增=绿 / 📝修正=黄）
/// </summary>
public class StringPrefixBrushConverter : IValueConverter
{
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xD2, 0x99, 0x22));
    private static readonly Brush Default = new SolidColorBrush(Color.FromRgb(0xB3, 0xBD, 0xC6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        if (s.StartsWith("🐛")) return Red;
        if (s.StartsWith("🔧")) return Blue;
        if (s.StartsWith("🚀")) return Green;
        if (s.StartsWith("📝")) return Yellow;
        return Default;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
