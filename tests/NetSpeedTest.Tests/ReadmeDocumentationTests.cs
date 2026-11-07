using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetSpeedTest.Helpers;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// README 文档一致性回归测试。
///
/// README 是用户看到的第一手说明，也最容易在改代码后悄悄失真。这组测试把文档中
/// 可被机器验证的断言（版本号、出厂默认值、滑块区间、Web API 清单、工具箱数量、
/// 目录锚点）钉在源码上，避免出现「参数范围写着 1–512、实际是 2–1024」这类漂移。
/// 验证目标为中文主 README（<c>README.md</c>）；英文版与两文镜像由
/// <see cref="ReadmeBilingualTests"/> 覆盖。
/// </summary>
public class ReadmeDocumentationTests
{
    /// <summary>
    /// README「可调参数」表按 <c>appsettings.json</c> 的 SpeedTest 节书写出厂默认值。
    /// 内置 appsettings.json 会覆盖 <see cref="NetSpeedTest.Models.SpeedTestOptions"/> 的
    /// C# 初始值，因此它才是用户实际看到的默认值。
    /// </summary>
    public static readonly (string Label, string Key)[] ShippedDefaultRows =
    {
        ("并发线程数", "ThreadCount"),
        ("测速时长", "TestTimeoutSec"),
        ("线程启动间隔", "ThreadRampUpMs"),
        ("平均计量延迟", "AverageDelaySec"),
        ("速率平滑窗口", "RateWindowSec"),
        ("网卡轮询间隔", "NicPollIntervalMs"),
        ("延迟轮询间隔", "LatencyPollIntervalMs"),
        ("抖动采样间隔", "JitterPollIntervalMs"),
        ("抖动探测主机", "JitterTargetHost"),
        ("丢包率监测地址", "PacketLossTargetHost"),
        ("丢包率轮询间隔", "PacketLossPollIntervalMs"),
        ("补偿阈值", "CompensationThreshold"),
        ("确认时长", "CompensationConfirmSec"),
        ("自适应起始线程数", "AdaptiveStartThreads"),
    };

    /// <summary>
    /// README「范围」列必须等于设置页滑块的取值范围。
    /// </summary>
    public static readonly (string Label, string Binding)[] SliderRows =
    {
        ("并发线程数", "ThreadCount"),
        ("测速时长", "TestTimeoutSec"),
        ("线程启动间隔", "ThreadRampUpMs"),
        ("平均计量延迟", "AverageDelaySec"),
        ("速率平滑窗口", "RateWindowSec"),
        ("网卡轮询间隔", "NicPollIntervalMs"),
        ("延迟轮询间隔", "LatencyPollIntervalMs"),
        ("抖动采样间隔", "JitterPollIntervalMs"),
        ("丢包率轮询间隔", "PacketLossPollIntervalMs"),
        ("补偿阈值", "CompensationThreshold"),
        ("确认时长", "CompensationConfirmSec"),
    };

    // ========== 版本号 ==========

    [Fact]
    public void ChineseReadme_release_badge_matches_the_assembly_version()
    {
        var readme = ReadmeAssertions.Readme();

        Assert.Contains($"release-v{AppVersion.Short}", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ChineseReadme_changelog_section_announces_the_current_version()
    {
        var readme = ReadmeAssertions.Readme();

        Assert.Contains($"当前版本 **v{AppVersion.Short}**", readme, StringComparison.Ordinal);
    }

    // ========== 参数表：默认值与区间 ==========

    [Fact]
    public void ChineseReadme_default_column_matches_shipped_appsettings()
    {
        var readme = ReadmeAssertions.Readme();
        var speed = ReadmeAssertions.ShippedSpeedTestSection();

        var mismatches = new List<string>();
        foreach (var (label, key) in ShippedDefaultRows)
        {
            var cells = ReadmeAssertions.FindTableRow(readme, label);
            if (cells == null) { mismatches.Add($"{label}: README 参数表中没有该行"); continue; }
            if (!speed.TryGetProperty(key, out var actual)) { mismatches.Add($"{label}: appsettings.json 缺少 {key}"); continue; }

            var documented = cells[2];
            if (actual.ValueKind == JsonValueKind.String)
            {
                var expected = actual.GetString()!;
                if (!documented.Contains(expected, StringComparison.Ordinal))
                    mismatches.Add($"{label}: 文档写「{documented}」，appsettings.json 是「{expected}」");
                continue;
            }

            var expectedNumber = actual.GetDouble();
            var documentedNumber = ReadmeAssertions.FirstNumber(documented);
            if (documentedNumber == null || Math.Abs(documentedNumber.Value - expectedNumber) > 1e-9)
                mismatches.Add($"{label}: 文档写「{documented}」，appsettings.json 是 {expectedNumber.ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.True(mismatches.Count == 0,
            "README 参数表与内置 appsettings.json 不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void ChineseReadme_range_column_matches_the_settings_sliders()
    {
        var readme = ReadmeAssertions.Readme();
        var sliders = ReadmeAssertions.SettingsSliders();

        var mismatches = new List<string>();
        foreach (var (label, binding) in SliderRows)
        {
            if (!sliders.TryGetValue(binding, out var slider))
            {
                mismatches.Add($"{binding}: SettingsWindow.xaml 中找不到对应滑块");
                continue;
            }

            var cells = ReadmeAssertions.FindTableRow(readme, label);
            if (cells == null) { mismatches.Add($"{label}: README 参数表中没有该行"); continue; }

            var numbers = ReadmeAssertions.NumbersIn(cells[1]);
            if (numbers.Count != 2)
            {
                mismatches.Add($"{label}: 文档范围「{cells[1]}」应写成「min – max」两个数字");
                continue;
            }

            if (Math.Abs(numbers[0] - slider.Min) > 1e-9 || Math.Abs(numbers[1] - slider.Max) > 1e-9)
                mismatches.Add($"{label}: 文档写「{cells[1]}」，滑块是 {slider.Min} – {slider.Max}");
        }

        Assert.True(mismatches.Count == 0,
            "README 参数范围与设置页滑块不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== Web API ==========

    [Fact]
    public void ChineseReadme_web_api_table_covers_every_served_route_and_invents_none()
    {
        var readme = ReadmeAssertions.Readme();
        var served = ReadmeAssertions.ServedApiRoutes();
        Assert.NotEmpty(served);

        var documented = Regex.Matches(readme, @"`(/api/[a-z/]+)`")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = served.Where(r => !documented.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var invented = documented.Where(r => !served.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "README 未记录已实现的路由：" + string.Join(", ", missing));
        Assert.True(invented.Count == 0, "README 记录了不存在的路由：" + string.Join(", ", invented));
    }

    // ========== 工具箱 ==========

    [Fact]
    public void ChineseReadme_toolbox_count_matches_the_localized_tool_entries()
    {
        var readme = ReadmeAssertions.Readme();
        var strings = ReadmeAssertions.ReadSourceFile(Path.Combine("Languages", "Strings.zh-CN.xaml"));
        var toolCount = Regex.Matches(strings, @"x:Key=""Tool_\w+""").Count;

        Assert.True(toolCount > 0, "Strings.zh-CN.xaml 中没有 Tool_ 条目");
        Assert.Contains($"内置 **{toolCount} 种专业网络诊断工具**", readme, StringComparison.Ordinal);
        Assert.Contains($"<b>{toolCount} 合 1 工具箱</b>", readme, StringComparison.Ordinal);
    }

    // ========== 目录锚点 ==========

    [Fact]
    public void ChineseReadme_toc_links_resolve_to_existing_headings()
    {
        var readme = ReadmeAssertions.Readme();
        var headings = Regex.Matches(readme, @"^#{2,3}[ \t]+(.+?)[ \t]*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(headings);

        var entries = Regex.Matches(readme, @"^[ \t]*-[ \t]+\[(.+?)\]\(#(.+?)\)[ \t]*$", RegexOptions.Multiline)
            .Select(m => (Title: m.Groups[1].Value, Anchor: m.Groups[2].Value))
            .ToList();
        Assert.NotEmpty(entries);

        var mismatches = new List<string>();
        foreach (var (title, anchor) in entries)
        {
            if (!headings.Contains(title, StringComparer.Ordinal))
            {
                mismatches.Add($"目录项「{title}」没有同名标题");
                continue;
            }

            var expected = ReadmeAssertions.GitHubAnchorFor(title);
            if (!string.Equals(anchor, expected, StringComparison.Ordinal))
                mismatches.Add($"目录项「{title}」锚点是「{anchor}」，GitHub 会生成「{expected}」");
        }

        Assert.True(mismatches.Count == 0,
            "README 目录锚点失效：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== 署名（AI 协作） ==========

    [Fact]
    public void ChineseReadme_credits_DeepSeek_matching_the_about_page_and_landing_page()
    {
        var readme = ReadmeAssertions.Readme();

        var (developer, aiCollaborator) = ReadmeAssertions.AboutCredits();

        // 程序内「关于 → AI 协作」卡片就是署名的事实来源，README 必须与之一致。
        Assert.Equal("DeepSeek", aiCollaborator);
        Assert.False(string.IsNullOrWhiteSpace(developer), "关于页缺少开发者署名");

        Assert.Contains("## 🤖 开发方式与 AI 协作", readme, StringComparison.Ordinal);
        Assert.Contains($"**本项目使用 {aiCollaborator} AI 制作。**", readme, StringComparison.Ordinal);
        Assert.Contains($"| AI 协作 | {aiCollaborator} |", readme, StringComparison.Ordinal);
        Assert.Contains(developer, readme, StringComparison.Ordinal);

        // 落地页页脚同样署名，避免三处口径漂移。
        var landing = ReadmeAssertions.ReadSourceFile("index.html");
        var credit = Regex.Match(landing, @"class=""footer-credit"">([^<]+)<").Groups[1].Value;
        Assert.Contains(developer, credit, StringComparison.Ordinal);
        Assert.Contains(aiCollaborator, credit, StringComparison.Ordinal);
    }
}

/// <summary>
/// 英文 README（README.en.md）与中英双语一致性的回归测试。
///
/// 上面那组测试把 <c>README.md</c> 钉在中文事实源上；这组测试保证英文版不是
/// 「一次性翻译」：版本号、出厂默认值、滑块区间、Web API 清单、工具箱条目
/// 与 GitHub 锚点同样可被机器验证，且两份文档始终互为镜像。
/// </summary>
public class ReadmeBilingualTests
{
    /// <summary>
    /// 中英参数字表对应关系：中文标签 → 英文标签 → appsettings.json 的键。
    /// 第二个标签同时用于「范围」列校验（同一批键，见 <see cref="ReadmeAssertions.SliderRows"/>）。
    /// </summary>
    private static readonly (string Chinese, string English, string Key)[] BilingualParameterRows =
    {
        ("并发线程数", "Concurrent threads", "ThreadCount"),
        ("测速时长", "Test duration", "TestTimeoutSec"),
        ("线程启动间隔", "Thread ramp-up interval", "ThreadRampUpMs"),
        ("平均计量延迟", "Average measurement delay", "AverageDelaySec"),
        ("速率平滑窗口", "Rate smoothing window", "RateWindowSec"),
        ("网卡轮询间隔", "NIC poll interval", "NicPollIntervalMs"),
        ("延迟轮询间隔", "Latency poll interval", "LatencyPollIntervalMs"),
        ("抖动采样间隔", "Jitter sample interval", "JitterPollIntervalMs"),
        ("抖动探测主机", "Jitter target host", "JitterTargetHost"),
        ("丢包率监测地址", "Packet loss target host", "PacketLossTargetHost"),
        ("丢包率轮询间隔", "Packet loss poll interval", "PacketLossPollIntervalMs"),
        ("补偿阈值", "Compensation threshold", "CompensationThreshold"),
        ("确认时长", "Compensation confirm window", "CompensationConfirmSec"),
        ("自适应起始线程数", "Adaptive start threads", "AdaptiveStartThreads"),
    };

    // ========== 版本号 ==========

    [Fact]
    public void EnglishReadme_release_badge_matches_the_assembly_version()
    {
        var readme = EnglishReadme();

        Assert.Contains($"release-v{AppVersion.Short}", readme, StringComparison.Ordinal);
        Assert.Contains($"Current version: **v{AppVersion.Short}**", readme, StringComparison.Ordinal);
    }

    // ========== 参数表：默认值与区间 ==========

    [Fact]
    public void EnglishReadme_default_column_matches_shipped_appsettings()
    {
        var readme = EnglishReadme();
        var speed = ReadmeAssertions.ShippedSpeedTestSection();

        var mismatches = new List<string>();
        foreach (var (_, english, key) in BilingualParameterRows)
        {
            var cells = ReadmeAssertions.FindTableRow(readme, english);
            if (cells == null) { mismatches.Add($"{english}: 英文 README 参数表中没有该行"); continue; }
            if (!speed.TryGetProperty(key, out var actual)) { mismatches.Add($"{english}: appsettings.json 缺少 {key}"); continue; }

            var documented = cells[2];
            if (actual.ValueKind == JsonValueKind.String)
            {
                var expected = actual.GetString()!;
                if (!documented.Contains(expected, StringComparison.Ordinal))
                    mismatches.Add($"{english}: 文档写「{documented}」，appsettings.json 是「{expected}」");
                continue;
            }

            var expectedNumber = actual.GetDouble();
            var documentedNumber = ReadmeAssertions.FirstNumber(documented);
            if (documentedNumber == null || Math.Abs(documentedNumber.Value - expectedNumber) > 1e-9)
                mismatches.Add($"{english}: 文档写「{documented}」，appsettings.json 是 {expectedNumber.ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.True(mismatches.Count == 0,
            "英文 README 参数表与内置 appsettings.json 不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void EnglishReadme_range_column_matches_the_settings_sliders()
    {
        var readme = EnglishReadme();
        var sliders = ReadmeAssertions.SettingsSliders();

        var mismatches = new List<string>();
        foreach (var (_, english, key) in BilingualParameterRows)
        {
            if (!ReadmeDocumentationTests.SliderRows.Any(row => row.Binding == key)) continue;
            if (!sliders.TryGetValue(key, out var slider))
            {
                mismatches.Add($"{key}: SettingsWindow.xaml 中找不到对应滑块");
                continue;
            }

            var cells = ReadmeAssertions.FindTableRow(readme, english);
            if (cells == null) { mismatches.Add($"{english}: 英文 README 参数表中没有该行"); continue; }

            var numbers = ReadmeAssertions.NumbersIn(cells[1]);
            if (numbers.Count != 2)
            {
                mismatches.Add($"{english}: 文档范围「{cells[1]}」应写成「min – max」两个数字");
                continue;
            }

            if (Math.Abs(numbers[0] - slider.Min) > 1e-9 || Math.Abs(numbers[1] - slider.Max) > 1e-9)
                mismatches.Add($"{english}: 文档写「{cells[1]}」，滑块是 {slider.Min} – {slider.Max}");
        }

        Assert.True(mismatches.Count == 0,
            "英文 README 参数范围与设置页滑块不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== Web API ==========

    [Fact]
    public void EnglishReadme_web_api_table_covers_every_served_route()
    {
        var served = ReadmeAssertions.ServedApiRoutes();
        var documented = ReadmeAssertions.ApiRoutesIn(EnglishReadme());

        var missing = served.Where(r => !documented.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "英文 README 未记录已实现的路由：" + string.Join(", ", missing));
    }

    [Fact]
    public void Both_readmes_document_the_same_api_routes()
    {
        var chinese = ReadmeAssertions.ApiRoutesIn(ReadmeAssertions.Readme());
        var english = ReadmeAssertions.ApiRoutesIn(EnglishReadme());

        Assert.NotEmpty(english);
        Assert.True(chinese.SetEquals(english),
            "中英 README 的 API 清单不一致：仅中文有 [" + string.Join(", ", chinese.Except(english).OrderBy(r => r, StringComparer.Ordinal))
            + "]，仅英文有 [" + string.Join(", ", english.Except(chinese).OrderBy(r => r, StringComparer.Ordinal)) + "]");
    }

    // ========== 工具箱 ==========

    [Fact]
    public void EnglishReadme_toolbox_count_matches_the_localized_tool_entries()
    {
        var readme = EnglishReadme();
        var toolCount = ReadmeAssertions.ToolboxEntryCount();

        Assert.True(toolCount > 0, "Strings.zh-CN.xaml 中没有 Tool_ 条目");
        Assert.Contains($"<b>{toolCount}-in-1 Toolbox</b>", readme, StringComparison.Ordinal);

        // 英文工具名必须来自程序内的英文本地化资源，避免 README 自造术语。
        var englishStrings = ReadmeAssertions.ReadSourceFile(Path.Combine("Languages", "Strings.en-US.xaml"));
        var toolNames = Regex.Matches(englishStrings, @"x:Key=""Tool_\w+"">([^<]+)<").Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.Equal(toolCount, toolNames.Count);

        var missing = toolNames.Where(n => !readme.Contains(n, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "英文 README 工具箱表缺少：" + string.Join(", ", missing));
    }

    // ========== 目录锚点 ==========

    [Fact]
    public void EnglishReadme_toc_links_resolve_to_existing_headings()
    {
        var readme = EnglishReadme();
        var headings = ReadmeAssertions.HeadingsOf(readme);
        Assert.NotEmpty(headings);

        var entries = ReadmeAssertions.TocEntriesOf(readme);
        Assert.NotEmpty(entries);

        var mismatches = new List<string>();
        foreach (var (title, anchor) in entries)
        {
            if (!headings.Contains(title, StringComparer.Ordinal))
            {
                mismatches.Add($"目录项「{title}」没有同名标题");
                continue;
            }

            var expected = ReadmeAssertions.GitHubAnchorFor(title);
            if (!string.Equals(anchor, expected, StringComparison.Ordinal))
                mismatches.Add($"目录项「{title}」锚点是「{anchor}」，GitHub 会生成「{expected}」");
        }

        Assert.True(mismatches.Count == 0,
            "英文 README 目录锚点失效：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== 两文镜像 ==========

    [Fact]
    public void Both_readmes_are_mirrored_across_the_two_languages()
    {
        var chinese = ReadmeAssertions.Readme();
        var english = EnglishReadme();
        var mismatches = new List<string>();

        // 1) 章节结构：标题的层级与序号必须逐个对齐。
        var chineseHeadings = ReadmeAssertions.HeadingsOf(chinese);
        var englishHeadings = ReadmeAssertions.HeadingsOf(english);
        if (chineseHeadings.Count != englishHeadings.Count)
            mismatches.Add($"标题数量不一致：中文 {chineseHeadings.Count} 个，英文 {englishHeadings.Count} 个");

        // 2) 结构骨架：表格行/分隔行、HTML 容器、列表项、代码块、图片数量逐一相等。
        foreach (var (pattern, label) in StructureMarkers)
        {
            var zh = CountMatches(chinese, pattern);
            var en = CountMatches(english, pattern);
            if (zh != en) mismatches.Add($"「{label}」数量不一致：中文 {zh}，英文 {en}");
        }

        // 3) 语言切换：两份文档必须互相指路。
        Assert.Contains("[English](README.en.md)", chinese, StringComparison.Ordinal);
        Assert.Contains("[中文](README.md)", english, StringComparison.Ordinal);

        // 4) 编码卫生：无 BOM、以单个换行结尾（仓库统一 CRLF 检出）。
        foreach (var path in new[] { "README.md", "README.en.md" })
        {
            var bytes = ReadmeAssertions.SourceFileBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                mismatches.Add($"{path} 带有 UTF-8 BOM");
            if (!(bytes.Length >= 1 && bytes[^1] == (byte)'\n'))
                mismatches.Add($"{path} 缺少结尾换行");
        }

        Assert.True(mismatches.Count == 0,
            "中英 README 镜像校验失败：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== 辅助方法 ==========

    private static string EnglishReadme() => ReadmeAssertions.ReadSourceFile("README.en.md");

    private static int CountMatches(string markdown, string pattern)
        => Regex.Matches(markdown, pattern, RegexOptions.Multiline).Count;

    /// <summary>
    /// 结构骨架比较矩阵：正则 → 人类可读标签。中英两文必须逐项相等，
    /// 否则说明有一边多写或少写了表格行、容器、图片等骨架元素。
    /// </summary>
    private static readonly (string Pattern, string Label)[] StructureMarkers =
    {
        (@"^\s*\|(?![\s\-:|]+\|)", "表格内容行"),
        (@"^\s*\|[\s\-:|]+\|\s*$", "表格分隔行"),
        (@"<table>", "<table>"),
        (@"<tr>", "<tr>"),
        (@"<td[^>]*>", "<td>"),
        (@"^\s*- ", "列表项"),
        (@"^```", "代码块围栏"),
        (@"!\[[^\]]*\]\(", "Markdown 图片"),
        (@"<img ", "<img> 标签"),
    };
}

/// <summary>
/// README 文档一致性断言的可复用辅助层：读取仓库源文件、解析表格行与设置页滑块、
/// 复现 GitHub 标题锚点规则。中英文两组测试共用，避免两份实现漂移。
/// </summary>
internal static class ReadmeAssertions
{
internal static string Readme() => ReadmeAssertions.ReadSourceFile("README.md");

internal static List<string> HeadingsOf(string markdown)
    => Regex.Matches(markdown, @"^#{2,3}[ \t]+(.+?)[ \t]*$", RegexOptions.Multiline)
        .Select(m => m.Groups[1].Value)
        .ToList();

internal static List<(string Title, string Anchor)> TocEntriesOf(string markdown)
    => Regex.Matches(markdown, @"^[ \t]*-[ \t]+\[(.+?)\]\(#(.+?)\)[ \t]*$", RegexOptions.Multiline)
        .Select(m => (Title: m.Groups[1].Value, Anchor: m.Groups[2].Value))
        .ToList();

/// <summary>取出文档中以反引号包裹的全部 <c>/api/...</c> 路由。</summary>
internal static HashSet<string> ApiRoutesIn(string markdown)
    => Regex.Matches(markdown, @"`(/api/[a-z/]+)`")
        .Select(m => m.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);

/// <summary>工具箱条目数，事实来源是程序内的中文本地化资源。</summary>
internal static int ToolboxEntryCount()
    => Regex.Matches(ReadmeAssertions.ReadSourceFile(Path.Combine("Languages", "Strings.zh-CN.xaml")), @"x:Key=""Tool_\w+""").Count;

/// <summary>以字节读取仓库源文件，用于校验 BOM 与结尾换行。</summary>
internal static byte[] SourceFileBytes(string relative)
    => File.ReadAllBytes(SourceFile(relative));

/// <summary>
/// 取出「关于」页信息卡里的开发者与 AI 协作署名。程序内卡片是署名的事实来源，
/// README 与落地页都必须与之一致。
/// </summary>
internal static (string Developer, string AiCollaborator) AboutCredits()
{
    var about = ReadSourceFile(Path.Combine("Views", "AboutWindow.xaml"));
    return (AboutCardValue(about, "About_Dev"), AboutCardValue(about, "About_Collab"));
}

/// <summary>
/// 取出「关于」页信息卡里紧跟某个资源标签之后的值，例如
/// <c>About_Collab</c> → <c>DeepSeek</c>。
/// </summary>
private static string AboutCardValue(string aboutXaml, string labelKey)
{
    var pattern = $@"\{{DynamicResource {Regex.Escape(labelKey)}\}}""[^>]*/>\s*<TextBlock Text=""([^""]+)""";
    var match = Regex.Match(aboutXaml, pattern);
    Assert.True(match.Success, $"AboutWindow.xaml 中找不到 {labelKey} 卡片的值");
    return match.Groups[1].Value;
}

/// <summary>
/// 近似复现 GitHub 的标题锚点规则：去掉符号，小写，空格转连字符。
///
/// 两个刻意的细节：
/// 1. emoji 字符会被去掉，且 emoji 代理对的两个 char 都不是字母/数字，因此自然被丢弃；
/// 2. 变体选择符 U+FE0F 会被保留（github-slugger 的已知行为），
///    所以 `## 🛠️ 网络工具箱` 的锚点是 `#️-网络工具箱` 而不是 `#-网络工具箱`。
/// </summary>
internal static string GitHubAnchorFor(string title)
{
    var sb = new StringBuilder();
    foreach (var ch in title)
    {
        if (ch == '\uFE0F') { sb.Append(ch); continue; }
        if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-') sb.Append(char.ToLowerInvariant(ch));
    }
    return sb.ToString().Replace(' ', '-');
}

/// <summary>
/// 返回 README 中第一行首格以 <paramref name="label"/> 开头的表格行（按列切分）。
/// </summary>
internal static string[]? FindTableRow(string markdown, string label)
{
    foreach (var raw in markdown.Split('\n'))
    {
        var line = raw.Trim();
        if (!line.StartsWith('|')) continue;

        var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
        if (cells.Length >= 3 && cells[0].StartsWith(label, StringComparison.Ordinal)) return cells;
    }
    return null;
}

internal static JsonElement ShippedSpeedTestSection()
{
    using var doc = JsonDocument.Parse(ReadmeAssertions.ReadSourceFile("appsettings.json"));
    return doc.RootElement.GetProperty("SpeedTest").Clone();
}

internal sealed record Slider(double Min, double Max);

internal static Dictionary<string, Slider> SettingsSliders()
{
    var xaml = ReadmeAssertions.ReadSourceFile(Path.Combine("Views", "SettingsWindow.xaml"));
    var result = new Dictionary<string, Slider>(StringComparer.Ordinal);

    foreach (Match tag in Regex.Matches(xaml, @"<Slider\b[^>]*>", RegexOptions.Singleline))
    {
        var binding = Regex.Match(tag.Value, @"Value=""\{Binding\s+(\w+)");
        if (!binding.Success) continue;

        var min = Regex.Match(tag.Value, @"Minimum=""([\d.]+)""");
        var max = Regex.Match(tag.Value, @"Maximum=""([\d.]+)""");
        if (!min.Success || !max.Success) continue;

        result[binding.Groups[1].Value] = new Slider(
            double.Parse(min.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(max.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    return result;
}

/// <summary>
/// 从 <c>WebServerService.HandleApiAsync</c> 的 switch 中取出所有已实现的路由。
/// </summary>
internal static HashSet<string> ServedApiRoutes()
{
    var source = ReadmeAssertions.ReadSourceFile(Path.Combine("Services", "WebServerService.cs"));
    return Regex.Matches(source, @"case ""(/api/[a-z/]+)"":")
        .Select(m => m.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);
}

internal static List<double> NumbersIn(string text)
    => Regex.Matches(text, @"\d+(?:\.\d+)?")
        .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
        .ToList();

internal static double? FirstNumber(string text)
{
    var numbers = ReadmeAssertions.NumbersIn(text);
    return numbers.Count > 0 ? numbers[0] : null;
}

/// <summary>
/// 从测试输出目录向上查找仓库中的源文件，兼容仓库根文件（README.md）、
/// 程序源码（src/NetSpeedTest/Views/...）、官网文件（docs/index.html）与测试目录。
///
/// 统一把 CRLF 归一化为 LF：仓库在工作区使用 CRLF，而按行解析的锚点正则
/// 依赖 `$` 紧跟在行尾，留着 `\r` 会让匹配整体失效。
/// </summary>
internal static string ReadSourceFile(string relative)
    => File.ReadAllText(SourceFile(relative)).Replace("\r\n", "\n");

/// <summary>解析仓库源文件的绝对路径（不做换行归一化）。</summary>
internal static string SourceFile(string relative)
{
    var candidates = new[]
    {
        relative,
        Path.Combine("src", "NetSpeedTest", relative),
        Path.Combine("docs", relative),
        Path.Combine("tests", relative),
    };
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
