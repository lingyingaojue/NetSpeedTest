using System.IO;
using System.Text.Json;
using System.Windows;

namespace NetSpeedTest.Services;

public enum ThemeMode
{
    Dark,
    Light
}

/// <summary>
/// 主题服务：负责加载/切换深色与浅色主题，并持久化用户选择。
/// </summary>
public static class ThemeService
{
    private const string ThemeFileName = "theme.json";
    private static ThemeMode _current = ThemeMode.Dark;

    public static ThemeMode Current => _current;

    public static event Action? ThemeChanged;

    public static void ApplySavedTheme()
    {
        var mode = LoadSavedTheme();
        Apply(mode);
    }

    public static void Apply(ThemeMode mode)
    {
        _current = mode;
        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;

        // 移除旧的主题字典
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var themeUri = mode == ThemeMode.Dark
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);

        dictionaries.Insert(0, new ResourceDictionary { Source = themeUri });
        ThemeChanged?.Invoke();
    }

    public static void Save(ThemeMode mode)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ThemeFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(new { Theme = mode.ToString() }));
        }
        catch (Exception ex)
        {
            Logger.Log($"Theme save failed: {ex.Message}");
        }
    }

    private static ThemeMode LoadSavedTheme()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            var path = Path.Combine(dir, ThemeFileName);
            if (!File.Exists(path)) return ThemeMode.Dark;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var theme = doc.RootElement.TryGetProperty("Theme", out var prop)
                ? prop.GetString()
                : null;

            return theme == nameof(ThemeMode.Light) ? ThemeMode.Light : ThemeMode.Dark;
        }
        catch (Exception ex)
        {
            Logger.Log($"Theme load failed: {ex.Message}");
            return ThemeMode.Dark;
        }
    }
}
