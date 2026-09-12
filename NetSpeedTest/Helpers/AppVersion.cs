using System.Reflection;

namespace NetSpeedTest.Helpers;

/// <summary>
/// 版本号唯一来源。
/// 版本号由 NetSpeedTest.csproj 的 &lt;Version&gt; 决定，这里只做读取，
/// 避免在 UA、关于页、Web 界面等多处硬编码导致漏改。
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// 语义化版本号，例如 "1.4.2"。
    /// </summary>
    public static string Short { get; } = ResolveShort();

    /// <summary>
    /// 带 "v" 前缀的展示用版本号，例如 "v1.4.2"。
    /// </summary>
    public static string Display => "v" + Short;

    /// <summary>
    /// 用于 HTTP User-Agent 的标识，例如 "NetSpeedTest/1.4.2"。
    /// </summary>
    public static string UserAgent => $"NetSpeedTest/{Short}";

    private static string ResolveShort()
    {
        // AssemblyInformationalVersion 可能是 "1.4.2" 或 "1.4.2+<commit>"，
        // 只取 "+" 之前的部分。
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var trimmed = informational?.Split('+')[0].Trim();
        if (!string.IsNullOrEmpty(trimmed)) return trimmed;

        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
