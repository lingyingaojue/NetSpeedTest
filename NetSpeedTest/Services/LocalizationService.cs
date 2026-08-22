using System.IO;
using System.Text.Json;
using System.Windows;

namespace NetSpeedTest.Services;

public enum LanguageMode
{
    ZhCN,
    EnUS
}

/// <summary>
/// 多语言服务：切换界面语言并持久化用户选择。
/// </summary>
public static class LocalizationService
{
    private const string LanguageFileName = "language.json";
    private static LanguageMode _current = LanguageMode.ZhCN;

    public static LanguageMode Current => _current;

    public static event Action? LanguageChanged;

    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s) return s;
        return key;
    }

    public static void ApplySavedLanguage()
    {
        Apply(LoadSavedLanguage());
    }

    public static void Apply(LanguageMode mode)
    {
        _current = mode;
        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;

        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Languages/Strings.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var languageUri = mode == LanguageMode.ZhCN
            ? new Uri("pack://application:,,,/Languages/Strings.zh-CN.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Languages/Strings.en-US.xaml", UriKind.Absolute);

        dictionaries.Insert(0, new ResourceDictionary { Source = languageUri });
        LanguageChanged?.Invoke();
    }

    public static void Save(LanguageMode mode)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, LanguageFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(new { Language = mode.ToString() }));
        }
        catch (Exception ex)
        {
            Logger.Log($"Language save failed: {ex.Message}");
        }
    }

    private static LanguageMode LoadSavedLanguage()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            var path = Path.Combine(dir, LanguageFileName);
            if (!File.Exists(path)) return LanguageMode.ZhCN;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var lang = doc.RootElement.TryGetProperty("Language", out var prop)
                ? prop.GetString()
                : null;

            return lang == nameof(LanguageMode.EnUS) ? LanguageMode.EnUS : LanguageMode.ZhCN;
        }
        catch (Exception ex)
        {
            Logger.Log($"Language load failed: {ex.Message}");
            return LanguageMode.ZhCN;
        }
    }
}
