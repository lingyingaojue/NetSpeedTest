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
/// 官网（GitHub Pages 落地页）与 README、社区文件的模板一致性回归测试。
///
/// 契约来源是 <c>docs/DESIGN.md</c>：§4 canonical 章节、§5 六张特性卡、§6 统计数字、
/// §7 事实表（参数 / 工具箱）、§8 官网 DOM 契约、§9 README 契约。
/// 这组测试只做「读仓库文件 → 断言」，不依赖网络与端口，因此在工作区可直接运行；
/// 与 <c>tools/verify-docs.ps1</c> 互补：xUnit 逐项守住模板契约，脚本负责提交前 / CI 的快速自检。
///
/// 复用 <see cref="ReadmeAssertions"/>（同程序集 internal）读取仓库源文件、appsettings 与滑块区间，
/// 参数事实的唯一来源仍是 <c>appsettings.json</c> 与 <c>Views/SettingsWindow.xaml</c>。
/// </summary>
public class SiteTemplateTests
{
    // 官网 canonical 章节：data-section → 中文 H2 / 英文 H2（docs/DESIGN.md §4，官网按此顺序渲染）。
    private static readonly (string Key, string Chinese, string English)[] CanonicalSections =
    {
        ("features", "核心特性", "Core Features"),
        ("screenshot", "界面预览", "Screenshots"),
        ("toolbox", "网络工具箱", "Network Toolbox"),
        ("reference", "快捷键与参数", "Shortcuts & Parameters"),
        ("api", "Web 控制台与 REST API", "Web Console & REST API"),
        ("architecture", "技术架构", "Tech Stack"),
        ("changelog", "更新日志", "Changelog"),
        ("download", "下载", "Download"),
        ("contact", "反馈与联系", "Feedback & Contact"),
    };

    // 六张特性卡：i18n 键的中间段 → 中文 / 英文标题（docs/DESIGN.md §5）。
    private static readonly (string Key, string Chinese, string English)[] FeatureCards =
    {
        ("engine", "智能测速引擎", "Smart Speed Engine"),
        ("multinic", "多网卡并行测速", "Multi-NIC Parallel Testing"),
        ("latency", "全链路延迟探测", "Full-Chain Latency Probing"),
        ("metering", "NIC 级精准计量", "NIC-Level Precision Metering"),
        ("ux", "专业交互体验", "Professional Interaction"),
        ("history", "历史与配置管理", "History & Profile Management"),
    };

    /// <summary>
    /// JS 专用字典键：只由 <c>assets/site.js</c> 的 <c>translate()</c> 按字面量读取，
    /// 不会出现在任何 <c>data-i18n*</c> 属性里，因此允许「未被 HTML 使用」。
    /// </summary>
    private static readonly string[] JsOnlyI18nKeys =
    {
        "meta.title", "meta.description", "toast.copied", "toast.copyFailed",
    };

    /// <summary>官网导航必须覆盖的 8 个章节锚点（docs/DESIGN.md §4 末段）。</summary>
    private static readonly string[] NavigationAnchors =
    {
        "features", "screenshot", "toolbox", "reference", "api", "architecture", "changelog", "download",
    };

    // ========== 1. 版本面 ==========

    /// <summary>
    /// 版本号在官网上有三处：顶栏 <c>#siteVersion</c> 胶囊、hero 徽标文案、更新日志前三个
    /// <c>version-tag</c>。三者都必须以 <see cref="AppVersion"/> 为准，且更新日志只保留
    /// 当前版本 + 前两个版本（与 CHANGELOG.md 的前三条版本标题一致）。
    /// </summary>
    [Fact]
    public void LandingPage_version_surfaces_match_app_version()
    {
        var landing = LandingPage();
        var changelogVersions = ChangelogVersions();
        Assert.True(changelogVersions.Count >= 3,
            $"CHANGELOG.md 至少要有 3 个版本标题，实际只有 {changelogVersions.Count} 个");
        Assert.Equal(AppVersion.Short, changelogVersions[0]);

        // 顶栏版本胶囊：<span class="version-chip" id="siteVersion">v1.4.4</span>
        var chip = Assert.Single(Regex.Matches(landing, @"<span class=""version-chip"" id=""siteVersion"">([^<]+)</span>"));
        Assert.Equal(AppVersion.Display, chip.Groups[1].Value.Trim());

        // hero 徽标：中文文案直接写在 HTML 里（无 JS 时也可见），必须含当前版本。
        var heroBadge = Regex.Match(landing, @"<span[^>]*data-i18n=""hero\.badge""[^>]*>([^<]+)</span>");
        Assert.True(heroBadge.Success, "index.html 的 hero 徽标缺少 data-i18n=\"hero.badge\" 文案节点");
        Assert.Contains(AppVersion.Display, heroBadge.Groups[1].Value, StringComparison.Ordinal);

        // 更新日志：3 个 version-tag，最新在前，且顺序与 CHANGELOG.md 前三个版本逐项一致。
        var tags = Regex.Matches(landing, @"<span class=""version-tag"">([^<]+)</span>")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();
        Assert.Equal(3, tags.Count);
        Assert.Equal(string.Join(" > ", changelogVersions.Take(3).Select(v => "v" + v)),
            string.Join(" > ", tags));
    }

    /// <summary>
    /// 官网不得残留陈旧版本字面量：所有 <c>vX.Y.Z</c> 只能来自当前版本与前两个版本；
    /// 非当前版本只能出现在更新日志的 <c>version-tag</c> 行上（防止正文/CTA 里藏着旧版本）。
    /// </summary>
    [Fact]
    public void LandingPage_has_no_stale_version_literals()
    {
        var landing = LandingPage();
        var changelogVersions = ChangelogVersions();
        Assert.True(changelogVersions.Count >= 3,
            $"CHANGELOG.md 至少要有 3 个版本标题，实际只有 {changelogVersions.Count} 个");

        var allowed = new HashSet<string>(
            changelogVersions.Take(3).Select(v => "v" + v),
            StringComparer.Ordinal);
        Assert.Contains(AppVersion.Display, allowed);

        var literals = new List<string>();
        foreach (var line in landing.Split('\n'))
        {
            foreach (Match match in Regex.Matches(line, @"v\d+\.\d+\.\d+"))
            {
                if (!allowed.Contains(match.Value))
                {
                    literals.Add($"{match.Value}（第 {LineNumber(landing, line)} 行附近，既非当前版本也非前两个版本）");
                }
                else if (!string.Equals(match.Value, AppVersion.Display, StringComparison.Ordinal)
                         && !line.Contains("class=\"version-tag\"", StringComparison.Ordinal))
                {
                    literals.Add($"{match.Value}（第 {LineNumber(landing, line)} 行附近，旧版本只能出现在 version-tag 中）");
                }
            }
        }

        Assert.True(literals.Count == 0,
            "index.html 存在陈旧或位置不当的版本字面量：" + Environment.NewLine + string.Join(Environment.NewLine, literals));
    }

    // ========== 2. 参数表 ==========

    /// <summary>
    /// 官网 <c>#paramTable</c> 每行的 <c>data-param</c> 必须对应 appsettings 的键：
    /// 「默认」列等于内置 appsettings.json 的出厂值，「范围」列等于设置页滑块的 min / max。
    /// 行集合必须恰好覆盖 README 的 14 项默认值行，其中 11 项还受滑块区间约束。
    /// </summary>
    [Fact]
    public void LandingPage_parameter_table_matches_appsettings_and_sliders()
    {
        var landing = LandingPage();
        var table = Regex.Match(landing, @"(?s)<table[^>]*id=""paramTable""[^>]*>(.*?)</table>");
        Assert.True(table.Success, "index.html 缺少 id=\"paramTable\" 的参数表");

        var rows = new Dictionary<string, (string Range, string Default)>(StringComparer.Ordinal);
        foreach (Match row in Regex.Matches(table.Groups[1].Value, @"(?s)<tr data-param=""([^""]+)"">(.*?)</tr>"))
        {
            var key = row.Groups[1].Value;
            var body = row.Groups[2].Value;
            var range = Regex.Match(body, @"<td class=""range[^""]*"">([^<]*)</td>");
            var defaultValue = Regex.Match(body, @"<td class=""default"">([^<]*)</td>");
            Assert.True(range.Success, $"data-param=\"{key}\" 行缺少 class=\"range\" 单元格");
            Assert.True(defaultValue.Success, $"data-param=\"{key}\" 行缺少 class=\"default\" 单元格");
            Assert.False(rows.ContainsKey(key), $"data-param=\"{key}\" 在参数表中重复出现");
            rows[key] = (range.Groups[1].Value.Trim(), defaultValue.Groups[1].Value.Trim());

            var i18n = Regex.Match(body, @"data-i18n=""param\.([^""]+)""");
            Assert.True(i18n.Success, $"data-param=\"{key}\" 行的标签缺少 param.{key} 的 i18n 键");
            Assert.Equal(key, i18n.Groups[1].Value);
        }

        var expectedKeys = ReadmeDocumentationTests.ShippedDefaultRows.Select(r => r.Key).ToList();
        Assert.Equal(expectedKeys.Count, rows.Count);
        var missingRows = expectedKeys.Where(key => !rows.ContainsKey(key)).ToList();
        Assert.True(missingRows.Count == 0, "官网参数表缺少行：" + string.Join(", ", missingRows));

        var speed = ReadmeAssertions.ShippedSpeedTestSection();
        var mismatches = new List<string>();
        foreach (var (label, key) in ReadmeDocumentationTests.ShippedDefaultRows)
        {
            var documented = rows[key].Default;
            if (!speed.TryGetProperty(key, out var actual))
            {
                mismatches.Add($"{key}: appsettings.json 的 SpeedTest 节缺少该键");
                continue;
            }

            if (actual.ValueKind == JsonValueKind.String)
            {
                var expected = actual.GetString() ?? "";
                if (!documented.Contains(expected, StringComparison.Ordinal))
                    mismatches.Add($"{label}: 官网写「{documented}」，appsettings.json 是「{expected}」");
                continue;
            }

            var expectedNumber = actual.GetDouble();
            var documentedNumber = ReadmeAssertions.FirstNumber(documented);
            if (documentedNumber == null || Math.Abs(documentedNumber.Value - expectedNumber) > 1e-9)
                mismatches.Add($"{label}: 官网写「{documented}」，appsettings.json 是 {expectedNumber.ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.True(mismatches.Count == 0,
            "官网参数表默认值与内置 appsettings.json 不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));

        var sliders = ReadmeAssertions.SettingsSliders();
        mismatches.Clear();
        foreach (var (label, binding) in ReadmeDocumentationTests.SliderRows)
        {
            var rangeText = rows[binding].Range;
            if (!sliders.TryGetValue(binding, out var slider))
            {
                mismatches.Add($"{binding}: SettingsWindow.xaml 中找不到对应滑块");
                continue;
            }

            var numbers = ReadmeAssertions.NumbersIn(rangeText);
            if (numbers.Count != 2)
            {
                mismatches.Add($"{label}: 官网范围「{rangeText}」应写成「min – max」两个数字");
                continue;
            }

            if (Math.Abs(numbers[0] - slider.Min) > 1e-9 || Math.Abs(numbers[1] - slider.Max) > 1e-9)
                mismatches.Add($"{label}: 官网写「{rangeText}」，滑块是 {slider.Min} – {slider.Max}");
        }

        Assert.True(mismatches.Count == 0,
            "官网参数范围与设置页滑块不一致：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // ========== 3. 工具箱 ==========

    /// <summary>
    /// <c>#toolboxGrid</c> 的工具条目数必须等于程序内本地化的 <c>Tool_</c> 条目数（18），
    /// 且 18 个中文工具名与 HTML 里的 <c>data-tool</c> 一一对应 —— 官网不能自造工具或漏项。
    /// </summary>
    [Fact]
    public void LandingPage_toolbox_matches_localized_tools()
    {
        var landing = LandingPage();
        var toolCount = ReadmeAssertions.ToolboxEntryCount();
        Assert.True(toolCount > 0, "Strings.zh-CN.xaml 中没有 Tool_ 条目");
        Assert.Equal(18, toolCount); // docs/DESIGN.md §6 / §7.2 把 18 种工具定为事实

        var declaredCount = Regex.Match(landing, @"<div class=""tool-grid""[^>]*data-count=""(\d+)""");
        Assert.True(declaredCount.Success, "index.html 缺少 data-toolbox / data-count 的工具栏容器");
        Assert.Equal(toolCount.ToString(CultureInfo.InvariantCulture), declaredCount.Groups[1].Value);

        var tools = Regex.Matches(landing, @"data-tool=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(toolCount, tools.Count);
        Assert.Equal(toolCount, tools.Distinct(StringComparer.Ordinal).Count());

        var localized = Regex.Matches(
                ReadmeAssertions.ReadSourceFile(Path.Combine("Languages", "Strings.zh-CN.xaml")),
                @"x:Key=""Tool_\w+"">([^<]+)<")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();
        Assert.Equal(toolCount, localized.Count);
        Assert.Equal(string.Join(" | ", localized.OrderBy(n => n, StringComparer.Ordinal)),
            string.Join(" | ", tools.OrderBy(n => n, StringComparer.Ordinal)));
    }

    // ========== 4. 导航与资源 ==========

    /// <summary>
    /// 官网所有 <c>href="#..."</c>（导航、跳转链接、SVG sprite 的 <c>&lt;use&gt;</c>）目标 id
    /// 都必须存在，且全页 id 唯一；导航还必须覆盖 DESIGN §4 规定的 8 个章节锚点。
    /// </summary>
    [Fact]
    public void LandingPage_navigation_anchors_resolve_and_ids_are_unique()
    {
        var landing = LandingPage();

        var ids = Regex.Matches(landing, @"(?<![\w-])id=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(ids);
        var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ×{group.Count()}")
            .ToList();
        Assert.True(duplicates.Count == 0, "index.html 存在重复 id：" + string.Join(", ", duplicates));

        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        var anchors = Regex.Matches(landing, @"href=""#([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .Where(anchor => anchor.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(anchors);
        var missing = anchors.Where(anchor => !idSet.Contains(anchor)).OrderBy(anchor => anchor, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "index.html 的页内锚点没有对应 id：" + string.Join(", ", missing));

        var nav = Regex.Match(landing, @"(?s)<nav class=""site-nav""[^>]*>(.*?)</nav>");
        Assert.True(nav.Success, "index.html 缺少 class=\"site-nav\" 的导航");
        var navAnchors = Regex.Matches(nav.Groups[1].Value, @"href=""#([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(NavigationAnchors.Length, navAnchors.Count);
        Assert.Equal(string.Join(",", NavigationAnchors), string.Join(",", navAnchors));
    }

    /// <summary>
    /// 官网与 404 页里的所有本地 <c>src</c> / <c>href</c> 引用（排除外链、<c>#</c>、
    /// <c>mailto:</c> / <c>tel:</c> / <c>data:</c>）都必须在仓库中存在。
    /// </summary>
    [Fact]
    public void LandingPage_local_assets_exist()
    {
        foreach (var (fileName, html) in new[] { ("index.html", LandingPage()), ("404.html", NotFoundPage()) })
        {
            var references = Regex.Matches(html, @"(?:src|href)=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Where(reference => !reference.StartsWith('#'))
                .Where(reference => !IsRemoteReference(reference))
                .Select(StripQueryAndFragment)
                .Where(reference => reference.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(references);

            var missing = references.Where(reference => !SourceFileExists(reference)).ToList();
            Assert.True(missing.Count == 0,
                $"{fileName} 引用了仓库中不存在的本地文件：" + string.Join(", ", missing));
        }
    }

    // ========== 5. i18n ==========

    /// <summary>
    /// 内嵌英文字典必须完整：HTML 上每个 <c>data-i18n</c> / <c>-html</c> / <c>-aria</c> /
    /// <c>-title</c> 的键都能在 <c>"en"</c> 字典里找到非空译文；反过来字典里也不允许出现
    /// 除 JS 专用键之外的无用条目（防止翻译残留 / 键名改名后失联）。
    /// </summary>
    [Fact]
    public void LandingPage_i18n_keys_are_complete()
    {
        var landing = LandingPage();
        var script = Regex.Match(landing, @"(?s)<script type=""application/json"" id=""i18n-data"">(.*?)</script>");
        Assert.True(script.Success, "index.html 缺少内嵌的 <script type=\"application/json\" id=\"i18n-data\"> 字典");
        Assert.Single(Regex.Matches(landing, @"id=""i18n-data"""));

        var dictionary = ParseEnglishDictionary(script.Groups[1].Value);
        Assert.NotEmpty(dictionary);

        var usedKeys = Regex.Matches(landing, @"data-i18n(?:-html|-aria|-title)?=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(usedKeys);

        var missing = usedKeys
            .Where(key => !dictionary.ContainsKey(key) || string.IsNullOrWhiteSpace(dictionary[key]))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, "index.html 使用了英文字典中不存在或为空的键：" + string.Join(", ", missing));

        var jsOnly = JsOnlyI18nKeys.ToHashSet(StringComparer.Ordinal);
        var unused = dictionary.Keys
            .Where(key => !usedKeys.Contains(key) && !jsOnly.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        Assert.True(unused.Count == 0, "英文字典存在未被 HTML 或 JS 使用的键：" + string.Join(", ", unused));
    }

    // ========== 6. 章节模板 ==========

    /// <summary>
    /// 官网 9 个 <c>&lt;section data-section&gt;</c> 必须按 DESIGN §4 的 canonical 顺序出现，
    /// <c>id</c> / <c>data-section</c> 同名，H2 中文文案与英文字典里的英文文案都与模板一致。
    /// </summary>
    [Fact]
    public void LandingPage_sections_follow_the_canonical_template()
    {
        var landing = LandingPage();
        var english = ParseEnglishDictionary(
            Regex.Match(landing, @"(?s)<script type=""application/json"" id=""i18n-data"">(.*?)</script>").Groups[1].Value);

        var sections = new List<(string Key, string Id, string Chinese, string I18nKey)>();
        foreach (Match section in Regex.Matches(landing, @"(?s)<section\b([^>]*)>(.*?)</section>"))
        {
            var attributes = section.Groups[1].Value;
            var dataSection = Regex.Match(attributes, @"data-section=""([^""]+)""");
            if (!dataSection.Success) continue;

            var key = dataSection.Groups[1].Value;
            var id = Regex.Match(attributes, @"(?<![\w-])id=""([^""]+)""");
            Assert.True(id.Success, $"data-section=\"{key}\" 的 section 缺少 id 属性");

            var h2 = Regex.Match(section.Groups[2].Value, @"(?s)<h2\b([^>]*)>(.*?)</h2>");
            Assert.True(h2.Success, $"data-section=\"{key}\" 的 section 缺少 H2 标题");
            var i18nKey = Regex.Match(h2.Groups[1].Value, @"data-i18n=""([^""]+)""");
            Assert.True(i18nKey.Success, $"data-section=\"{key}\" 的 H2 缺少 data-i18n 键");
            sections.Add((key, id.Groups[1].Value, h2.Groups[2].Value.Trim(), i18nKey.Groups[1].Value));
        }

        Assert.Equal(CanonicalSections.Length, sections.Count);
        Assert.Equal(string.Join(",", CanonicalSections.Select(s => s.Key)),
            string.Join(",", sections.Select(s => s.Key)));

        foreach (var canonical in CanonicalSections)
        {
            var section = sections.Single(s => string.Equals(s.Key, canonical.Key, StringComparison.Ordinal));
            Assert.Equal(canonical.Key, section.Id);
            Assert.Equal(canonical.Chinese, section.Chinese);
            Assert.Equal($"section.{canonical.Key}.title", section.I18nKey);
            Assert.True(english.TryGetValue($"section.{canonical.Key}.title", out var englishTitle),
                $"英文字典缺少 section.{canonical.Key}.title");
            Assert.Equal(canonical.English, englishTitle);
        }
    }

    // ========== 7. 六张特性卡 ==========

    /// <summary>
    /// 六张特性卡的标题在 README 中文版、README 英文版与官网 i18n 字典三处必须同名：
    /// 中文标题出现在 README.md 与官网卡片 H3，英文标题出现在 README.en.md 与
    /// <c>feature.*.title</c> 字典键。
    /// </summary>
    [Fact]
    public void Both_readmes_and_site_share_the_six_feature_titles()
    {
        var chineseReadme = ReadmeAssertions.Readme();
        var englishReadme = ReadmeAssertions.ReadSourceFile("README.en.md");
        var landing = LandingPage();
        var english = ParseEnglishDictionary(
            Regex.Match(landing, @"(?s)<script type=""application/json"" id=""i18n-data"">(.*?)</script>").Groups[1].Value);

        var cardTitles = Regex.Matches(landing, @"<h3 data-i18n=""feature\.([^""]+)\.title"">([^<]+)</h3>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);
        Assert.Equal(FeatureCards.Length, cardTitles.Count);

        foreach (var (key, chinese, englishTitle) in FeatureCards)
        {
            Assert.Contains(chinese, chineseReadme, StringComparison.Ordinal);
            Assert.True(cardTitles.TryGetValue(key, out var htmlTitle), $"官网缺少 feature.{key}.title 特性卡");
            Assert.Equal(chinese, htmlTitle);

            Assert.Contains(englishTitle, englishReadme, StringComparison.Ordinal);
            Assert.True(english.TryGetValue($"feature.{key}.title", out var dictTitle),
                $"英文字典缺少 feature.{key}.title");
            Assert.Equal(englishTitle, dictTitle);
        }

        // 英文特性卡标题键必须恰好是这 6 个，避免多出无人使用的标题键。
        var titleKeys = english.Keys
            .Where(name => name.StartsWith("feature.", StringComparison.Ordinal) && name.EndsWith(".title", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            string.Join(",", FeatureCards.Select(card => $"feature.{card.Key}.title").OrderBy(name => name, StringComparer.Ordinal)),
            string.Join(",", titleKeys));
    }

    // ========== 8. 社区文件 ==========

    /// <summary>
    /// 两份 README 都必须指向仓库的社区规范文件（CONTRIBUTING / SECURITY / CODE_OF_CONDUCT /
    /// CHANGELOG / LICENSE），且被引用文件真实存在、非空。
    /// </summary>
    [Fact]
    public void Readmes_link_to_community_files_and_files_exist()
    {
        var communityFiles = new[] { "CONTRIBUTING.md", "SECURITY.md", "CODE_OF_CONDUCT.md", "CHANGELOG.md", "LICENSE" };
        foreach (var (fileName, text) in new[] { ("README.md", ReadmeAssertions.Readme()), ("README.en.md", ReadmeAssertions.ReadSourceFile("README.en.md")) })
        {
            foreach (var target in communityFiles)
            {
                Assert.Contains(target, text, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(ReadmeAssertions.ReadSourceFile(target)),
                    $"{fileName} 引用的 {target} 不存在或为空");
            }
        }
    }

    /// <summary>
    /// GitHub 社区健康文件必须齐全且非空；CI 工作流必须真正跑 <c>dotnet test</c> 与
    /// <c>tools/verify-docs.ps1</c>（否则文档契约在流水线上失守）。
    /// </summary>
    [Fact]
    public void Community_health_files_are_present_and_non_empty()
    {
        var requiredFiles = new[]
        {
            Path.Combine(".github", "ISSUE_TEMPLATE", "bug_report.yml"),
            Path.Combine(".github", "ISSUE_TEMPLATE", "feature_request.yml"),
            Path.Combine(".github", "ISSUE_TEMPLATE", "config.yml"),
            Path.Combine(".github", "PULL_REQUEST_TEMPLATE.md"),
            Path.Combine(".github", "workflows", "ci.yml"),
        };
        foreach (var relative in requiredFiles)
        {
            Assert.False(string.IsNullOrWhiteSpace(ReadmeAssertions.ReadSourceFile(relative)),
                $"社区健康文件缺失或为空：{relative}");
        }

        var ci = ReadmeAssertions.ReadSourceFile(Path.Combine(".github", "workflows", "ci.yml"));
        Assert.Contains("dotnet test", ci, StringComparison.Ordinal);
        Assert.Contains("tools/docs/verify-docs.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("pull_request", ci, StringComparison.Ordinal);
    }

    // ========== 9. 编码卫生 ==========

    /// <summary>
    /// 官网页面 / 样式 / 脚本必须 UTF-8 无 BOM、CRLF、单个结尾换行；两份 README 同样不得有 BOM
    /// 且必须以换行结尾（README 的 CRLF 由 <see cref="ReadmeBilingualTests"/> 与脚本覆盖）。
    /// </summary>
    [Fact]
    public void Site_and_readme_files_are_utf8_without_bom_and_end_with_newline()
    {
        foreach (var path in new[] { "index.html", "404.html", Path.Combine("assets", "site.css"), Path.Combine("assets", "site.js") })
        {
            var bytes = ReadmeAssertions.SourceFileBytes(path);
            AssertUtf8WithoutBom(bytes, path);
            AssertEndsWithSingleNewline(bytes, path);

            var bareLf = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)'\n') continue;
                if (i == 0 || bytes[i - 1] != (byte)'\r') bareLf++;
            }
            Assert.True(bareLf == 0, $"{path} 含 {bareLf} 处裸 LF（官网资源统一用 CRLF）");
        }

        foreach (var path in new[] { "README.md", "README.en.md" })
        {
            var bytes = ReadmeAssertions.SourceFileBytes(path);
            AssertUtf8WithoutBom(bytes, path);
            AssertEndsWithSingleNewline(bytes, path);
        }
    }

    // ========== 10. 附加契约 ==========

    /// <summary>
    /// DESIGN §6 / §8：Hero 数据条的 <c>.counter</c> 在 HTML 里直接写最终值（JS 再从这里读取
    /// <c>data-target</c> 从 0 播放动画）；四个数字固定为 128 / 1024 / 18 / 8。
    /// </summary>
    [Fact]
    public void LandingPage_counters_render_the_canonical_stat_facts()
    {
        var landing = LandingPage();
        var counters = Regex.Matches(landing, @"<span class=""counter"" data-target=""(\d+)"">([^<]*)</span>")
            .Select(m => (Target: m.Groups[1].Value, Text: m.Groups[2].Value.Trim()))
            .ToList();

        Assert.Equal(4, counters.Count);
        Assert.Equal("128,1024,18,8", string.Join(",", counters.Select(counter => counter.Target)));
        foreach (var (target, text) in counters)
        {
            // 无 JS 时也必须显示最终值：HTML 内直接写最终值，JS 再从 0 播放动画。
            Assert.Equal(target, text);
        }
    }

    /// <summary>
    /// DESIGN §8：官网资源全部本地 —— 无 CDN、无外链脚本、无外部字体、无远程 <c>url()</c>；
    /// favicon 固定使用本地 <c>assets/site-icon.svg</c>（<c>rel="canonical"</c> 只是元数据，不算资源）。
    /// </summary>
    [Fact]
    public void LandingPage_loads_only_local_resources()
    {
        var violations = new List<string>();
        foreach (var (fileName, html) in new[] { ("index.html", LandingPage()), ("404.html", NotFoundPage()) })
        {
            foreach (Match match in Regex.Matches(html, @"<script\b[^>]*\bsrc=""([^""]+)"""))
            {
                if (IsRemoteReference(match.Groups[1].Value))
                    violations.Add($"{fileName} 外链脚本：{match.Groups[1].Value}");
            }

            foreach (Match match in Regex.Matches(html, @"<link\b[^>]*>"))
            {
                var tag = match.Value;
                var rel = Regex.Match(tag, @"rel=""([^""]+)""").Groups[1].Value;
                if (rel.IndexOf("canonical", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var href = Regex.Match(tag, @"href=""([^""]+)""").Groups[1].Value;
                if (IsRemoteReference(href)) violations.Add($"{fileName} 外链资源：{href}");
            }
        }

        var css = ReadmeAssertions.ReadSourceFile(Path.Combine("assets", "site.css"));
        if (Regex.IsMatch(css, @"@import\s+(?:url\()?['""]?(?:https?:)?//", RegexOptions.IgnoreCase))
            violations.Add("assets/site.css 通过 @import 引入外部样式");
        if (Regex.IsMatch(css, @"url\(\s*['""]?(?:https?:)?//", RegexOptions.IgnoreCase))
            violations.Add("assets/site.css 的 url() 引用了外部资源（字体 / 图片）");

        Assert.Contains(@"<link rel=""icon"" href=""assets/site-icon.svg""", LandingPage(), StringComparison.Ordinal);
        Assert.True(violations.Count == 0,
            "官网资源必须全部本地（无 CDN、无外链脚本、无外部字体）：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// DESIGN §3.1 / §8：官网顶栏使用 vendored hyalite 液态玻璃。装饰层、脚本顺序、CSS 降级与
    /// MIT 许可声明必须齐全，上游版本与 commit 被固定，避免「悄悄升级」或漏带许可。
    /// </summary>
    [Fact]
    public void LandingPage_navbar_uses_vendored_liquid_glass()
    {
        var landing = LandingPage();

        var layer = Regex.Match(landing, @"<div[^>]*\bid=""navGlass""[^>]*>");
        Assert.True(layer.Success, "index.html 缺少 #navGlass 玻璃装饰层");
        Assert.Contains("class=\"nav-glass\"", layer.Value, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", layer.Value, StringComparison.Ordinal);

        Assert.Contains("assets/vendor/hyalite.js", landing, StringComparison.Ordinal);
        Assert.True(
            landing.IndexOf("assets/vendor/hyalite.js", StringComparison.Ordinal)
                < landing.IndexOf("assets/site.js", StringComparison.Ordinal),
            "hyalite.js 必须先于 assets/site.js 加载");

        var css = ReadmeAssertions.ReadSourceFile(Path.Combine("assets", "site.css"));
        Assert.Contains("var(--hyalite", css, StringComparison.Ordinal);
        Assert.Contains("backdrop-filter: blur(", css, StringComparison.Ordinal);
        Assert.Contains("--nav-glass-bg", css, StringComparison.Ordinal);

        var vendor = ReadmeAssertions.ReadSourceFile(Path.Combine("assets", "vendor", "hyalite.js"));
        Assert.Contains("hyalite v0.5.0", vendor, StringComparison.Ordinal);
        Assert.Contains("MIT © 2026 VII-Cae", vendor, StringComparison.Ordinal);

        var license = ReadmeAssertions.ReadSourceFile(Path.Combine("assets", "vendor", "hyalite.LICENSE"));
        Assert.Contains("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 VII-Cae", license, StringComparison.Ordinal);

        var notices = ReadmeAssertions.ReadSourceFile("THIRD-PARTY-NOTICES.md");
        Assert.Contains("hyalite", notices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://github.com/VII-Cae/hyalite--liquid-glass", notices, StringComparison.Ordinal);
        Assert.Contains("b9f27192a7256bf5295bf027b582b86864f9b59d", notices, StringComparison.Ordinal);
    }

    // ========== 辅助方法 ==========

    private static string LandingPage() => ReadmeAssertions.ReadSourceFile("index.html");

    private static string NotFoundPage() => ReadmeAssertions.ReadSourceFile("404.html");

    /// <summary>CHANGELOG.md 里按顺序排列的版本号（去掉前导 V，例如 "1.4.4"）。</summary>
    private static List<string> ChangelogVersions()
        => Regex.Matches(ReadmeAssertions.ReadSourceFile("CHANGELOG.md"), @"(?m)^## (V[\d.]+) ")
            .Select(m => m.Groups[1].Value.Substring(1))
            .ToList();

    /// <summary>把内嵌 i18n JSON 的 "en" 字典解析成「键 → 非空译文」。</summary>
    private static Dictionary<string, string> ParseEnglishDictionary(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("en", out var english), "i18n 字典缺少 \"en\" 节点");
        Assert.Equal(JsonValueKind.Object, english.ValueKind);

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in english.EnumerateObject())
        {
            Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
            dictionary[property.Name] = property.Value.GetString() ?? "";
        }
        return dictionary;
    }

    /// <summary>带 scheme（http/https/mailto/...）或协议相对的引用都不是仓库内文件。</summary>
    private static bool IsRemoteReference(string value)
        => Regex.IsMatch(value, @"^(?:[a-z][a-z0-9+.\-]*:|//)", RegexOptions.IgnoreCase);

    /// <summary>去掉 <c>?query</c> 与 <c>#fragment</c>，得到仓库内的相对路径。</summary>
    private static string StripQueryAndFragment(string value)
    {
        var cut = value.IndexOfAny(new[] { '?', '#' });
        return cut >= 0 ? value.Substring(0, cut) : value;
    }

    /// <summary>沿测试输出目录向上查找仓库文件，存在即返回 true（复用 ReadmeAssertions 的定位逻辑）。</summary>
    private static bool SourceFileExists(string relative)
    {
        try
        {
            ReadmeAssertions.SourceFileBytes(relative.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static void AssertUtf8WithoutBom(byte[] bytes, string path)
    {
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{path} 带有 UTF-8 BOM");

        var valid = true;
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            valid = false;
        }
        Assert.True(valid, $"{path} 不是合法的 UTF-8 文本");
    }

    private static void AssertEndsWithSingleNewline(byte[] bytes, string path)
    {
        Assert.True(bytes.Length >= 1 && bytes[^1] == (byte)'\n', $"{path} 缺少结尾换行");
        Assert.False(bytes.Length >= 2 && bytes[^2] == (byte)'\n', $"{path} 结尾有多余空行（应只有单个结尾换行）");
    }

    /// <summary>为陈旧版本字面量报错时给出近似行号，便于定位。</summary>
    private static int LineNumber(string text, string line)
    {
        var index = text.IndexOf(line, StringComparison.Ordinal);
        if (index <= 0) return 1;
        var lineNumber = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n') lineNumber++;
        }
        return lineNumber;
    }
}
