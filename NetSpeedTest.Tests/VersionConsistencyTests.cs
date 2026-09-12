using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NetSpeedTest.Helpers;
using NetSpeedTest.Services;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// 版本号一致性回归测试。
/// 版本号唯一来源是 NetSpeedTest.csproj 的 &lt;Version&gt;；
/// UA、关于页、Web 控制台都从程序集读取，避免发布时漏改其中一处。
/// </summary>
public class VersionConsistencyTests
{
    private static string ProjectFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NetSpeedTest", "NetSpeedTest.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("NetSpeedTest.csproj not found above " + AppContext.BaseDirectory);
    }

    private static string DeclaredVersion()
    {
        var text = File.ReadAllText(ProjectFilePath());
        var match = Regex.Match(text, @"<Version>\s*([^<\s]+)\s*</Version>");
        if (!match.Success) throw new InvalidOperationException("<Version> not found in csproj");
        return match.Groups[1].Value;
    }

    [Fact]
    public void AppVersion_short_matches_csproj_version()
    {
        Assert.Equal(DeclaredVersion(), AppVersion.Short);
    }

    [Fact]
    public void AppVersion_short_is_semver_like()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Short);
    }

    [Fact]
    public void AppVersion_display_has_v_prefix()
    {
        Assert.Equal("v" + AppVersion.Short, AppVersion.Display);
    }

    [Fact]
    public void AppVersion_user_agent_embeds_the_version()
    {
        Assert.Equal($"NetSpeedTest/{AppVersion.Short}", AppVersion.UserAgent);
    }

    [Fact]
    public void AppVersion_never_contains_build_metadata()
    {
        // InformationalVersion 可能是 "1.4.2+<commit>"，展示与 UA 都不能带 "+"。
        Assert.DoesNotContain("+", AppVersion.Short, StringComparison.Ordinal);
    }

    [Fact]
    public void WebConsole_injects_version_placeholder()
    {
        var html = System.Text.Encoding.UTF8.GetBytes(
            "<html><head><meta name=\"nst-version\" content=\"" + WebServerService.VersionPlaceholder + "\" />" +
            "<meta name=\"nst-token\" content=\"" + WebServerService.TokenPlaceholder + "\" /></head></html>");

        var result = System.Text.Encoding.UTF8.GetString(WebServerService.InjectSessionToken(html));

        Assert.DoesNotContain(WebServerService.VersionPlaceholder, result, StringComparison.Ordinal);
        Assert.DoesNotContain(WebServerService.TokenPlaceholder, result, StringComparison.Ordinal);
        Assert.Contains($"content=\"{AppVersion.Short}\"", result, StringComparison.Ordinal);
        Assert.Contains(WebServerService.SessionToken, result, StringComparison.Ordinal);
    }

    [Fact]
    public void WebConsole_placeholder_is_not_a_valid_version_literal()
    {
        // 占位符必须明显不可能被误认为真实版本号。
        Assert.Contains("%%", WebServerService.VersionPlaceholder, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_wwwroot_indexhtml_has_no_hardcoded_version_literal()
    {
        // 静态资源里不应残留形如 v1.4.x 的硬编码版本号（占位符除外）。
        var path = FindWwwRootIndex();
        var html = File.ReadAllText(path);

        Assert.Contains(WebServerService.VersionPlaceholder, html, StringComparison.Ordinal);
        var literals = Regex.Matches(html, @"v\d+\.\d+\.\d+");
        Assert.True(literals.Count == 0,
            $"{path} still contains hardcoded version literal(s): {string.Join(", ", literals.Select(m => m.Value))}");
    }

    [Fact]
    public void Shipped_aboutpage_has_no_hardcoded_version_badge()
    {
        var path = FindSourceFile(Path.Combine("Views", "AboutWindow.xaml"));
        var xaml = File.ReadAllText(path);

        // 徽标改为绑定 VersionBadge，不应再有 V1.x.y 字面量。
        var literals = Regex.Matches(xaml, @"""V\d+\.\d+\.\d+""");
        Assert.True(literals.Count == 0,
            $"{path} still hardcodes a version badge: {string.Join(", ", literals.Select(m => m.Value))}");
    }

    [Fact]
    public void Shipped_aboutpage_changelog_tops_out_at_the_current_version()
    {
        var path = FindSourceFile(Path.Combine("Views", "AboutWindow.xaml.cs"));
        var source = File.ReadAllText(path);

        var first = Regex.Match(source, @"new ChangelogEntry\(""(V[\d.]+)""");
        Assert.True(first.Success, "no ChangelogEntry found");
        Assert.Equal("V" + AppVersion.Short, first.Groups[1].Value);
    }

    [Fact]
    public void Shipped_aboutpage_dropped_the_1_3_6_entry()
    {
        var path = FindSourceFile(Path.Combine("Views", "AboutWindow.xaml.cs"));
        Assert.DoesNotContain("V1.3.6", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_documents_the_current_version_and_drops_1_3_6()
    {
        var path = FindSourceFile("CHANGELOG.md");
        var text = File.ReadAllText(path);

        Assert.Contains($"## V{AppVersion.Short} ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("## V1.3.6", text, StringComparison.Ordinal);
    }

    private static string FindWwwRootIndex()
        => FindSourceFile(Path.Combine("NetSpeedTest", "wwwroot", "index.html"));

    /// <summary>
    /// 从测试输出目录向上查找仓库中的源文件，兼容 "CHANGELOG.md" 这类仓库根文件
    /// 与 "Views\X.xaml" 这类位于 NetSpeedTest 子目录下的文件。
    /// </summary>
    private static string FindSourceFile(string relative)
    {
        var candidates = new[] { relative, Path.Combine("NetSpeedTest", relative) };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(dir.FullName, candidate);
                if (File.Exists(path)) return path;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"{relative} not found above {AppContext.BaseDirectory}");
    }
}
