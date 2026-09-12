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
/// </summary>
public class ReadmeDocumentationTests
{
    /// <summary>
    /// README「可调参数」表按 <c>appsettings.json</c> 的 SpeedTest 节书写出厂默认值。
    /// 内置 appsettings.json 会覆盖 <see cref="NetSpeedTest.Models.SpeedTestOptions"/> 的
    /// C# 初始值，因此它才是用户实际看到的默认值。
    /// </summary>
    private static readonly (string Label, string Key)[] ShippedDefaultRows =
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
    private static readonly (string Label, string Binding)[] SliderRows =
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
    public void Readme_release_badge_matches_the_assembly_version()
    {
        var readme = Readme();

        Assert.Contains($"release-v{AppVersion.Short}", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_changelog_section_announces_the_current_version()
    {
        var readme = Readme();

        Assert.Contains($"当前版本 **v{AppVersion.Short}**", readme, StringComparison.Ordinal);
    }

    // ========== 参数表：默认值与区间 ==========

    [Fact]
    public void Readme_default_column_matches_shipped_appsettings()
    {
        var readme = Readme();
        var speed = ShippedSpeedTestSection();

        var mismatches = new List<string>();
        foreach (var (label, key) in ShippedDefaultRows)
        {
            var cells = FindTableRow(readme, label);
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
            var documentedNumber = FirstNumber(documented);
            if (documentedNumber == null || Math.Abs(documentedNumber.Value - expectedNumber) > 1e-9)
                mismatches.Add($"{label}: 文档写「{documented}」，appsettings.json 是 {expectedNumber.ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.True(mismatches.Count == 0,
            "README 参数表与内置 appsettings.json 不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Readme_range_column_matches_the_settings_sliders()
    {
        var readme = Readme();
        var sliders = SettingsSliders();

        var mismatches = new List<string>();
        foreach (var (label, binding) in SliderRows)
        {
            if (!sliders.TryGetValue(binding, out var slider))
            {
                mismatches.Add($"{binding}: SettingsWindow.xaml 中找不到对应滑块");
                continue;
            }

            var cells = FindTableRow(readme, label);
            if (cells == null) { mismatches.Add($"{label}: README 参数表中没有该行"); continue; }

            var numbers = NumbersIn(cells[1]);
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
    public void Readme_web_api_table_covers_every_served_route_and_invents_none()
    {
        var readme = Readme();
        var served = ServedApiRoutes();
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
    public void Readme_toolbox_count_matches_the_localized_tool_entries()
    {
        var readme = Readme();
        var strings = ReadSourceFile(Path.Combine("Languages", "Strings.zh-CN.xaml"));
        var toolCount = Regex.Matches(strings, @"x:Key=""Tool_\w+""").Count;

        Assert.True(toolCount > 0, "Strings.zh-CN.xaml 中没有 Tool_ 条目");
        Assert.Contains($"内置 **{toolCount} 种专业网络诊断工具**", readme, StringComparison.Ordinal);
        Assert.Contains($"<b>{toolCount} 合 1 工具箱</b>", readme, StringComparison.Ordinal);
    }

    // ========== 目录锚点 ==========

    [Fact]
    public void Readme_toc_links_resolve_to_existing_headings()
    {
        var readme = Readme();
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

            var expected = GitHubAnchorFor(title);
            if (!string.Equals(anchor, expected, StringComparison.Ordinal))
                mismatches.Add($"目录项「{title}」锚点是「{anchor}」，GitHub 会生成「{expected}」");
        }

        Assert.True(mismatches.Count == 0,
            "README 目录锚点失效：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== 辅助方法 ==========

    /// <summary>
    /// 近似复现 GitHub 的标题锚点规则：去掉符号，小写，空格转连字符。
    ///
    /// 两个刻意的细节：
    /// 1. emoji 字符会被去掉，且 emoji 代理对的两个 char 都不是字母/数字，因此自然被丢弃；
    /// 2. 变体选择符 U+FE0F 会被保留（github-slugger 的已知行为），
    ///    所以 `## 🛠️ 网络工具箱` 的锚点是 `#️-网络工具箱` 而不是 `#-网络工具箱`。
    /// </summary>
    private static string GitHubAnchorFor(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title)
        {
            if (ch == '\uFE0F') { sb.Append(ch); continue; }
            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-') sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Replace(' ', '-');
    }

    private static string Readme() => ReadSourceFile("README.md");

    /// <summary>
    /// 返回 README 中第一行首格以 <paramref name="label"/> 开头的表格行（按列切分）。
    /// </summary>
    private static string[]? FindTableRow(string markdown, string label)
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

    private static JsonElement ShippedSpeedTestSection()
    {
        using var doc = JsonDocument.Parse(ReadSourceFile("appsettings.json"));
        return doc.RootElement.GetProperty("SpeedTest").Clone();
    }

    private sealed record Slider(double Min, double Max);

    private static Dictionary<string, Slider> SettingsSliders()
    {
        var xaml = ReadSourceFile(Path.Combine("Views", "SettingsWindow.xaml"));
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
    private static HashSet<string> ServedApiRoutes()
    {
        var source = ReadSourceFile(Path.Combine("Services", "WebServerService.cs"));
        return Regex.Matches(source, @"case ""(/api/[a-z/]+)"":")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<double> NumbersIn(string text)
        => Regex.Matches(text, @"\d+(?:\.\d+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToList();

    private static double? FirstNumber(string text)
    {
        var numbers = NumbersIn(text);
        return numbers.Count > 0 ? numbers[0] : null;
    }

    /// <summary>
    /// 从测试输出目录向上查找仓库中的源文件，兼容 "README.md" 这类仓库根文件
    /// 与 "Views\X.xaml" 这类位于 NetSpeedTest 子目录下的文件。
    ///
    /// 统一把 CRLF 归一化为 LF：仓库在工作区使用 CRLF，而按行解析的锚点正则
    /// 依赖 `$` 紧跟在行尾，留着 `\r` 会让匹配整体失效。
    /// </summary>
    private static string ReadSourceFile(string relative)
    {
        var candidates = new[] { relative, Path.Combine("NetSpeedTest", relative) };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(dir.FullName, candidate);
                if (File.Exists(path)) return File.ReadAllText(path).Replace("\r\n", "\n");
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"{relative} not found above {AppContext.BaseDirectory}");
    }
}
