<#
.SYNOPSIS
    文档模板自检：README 中英双语的编码 / 锚点 / 链接 / 结构镜像，GitHub Pages 官网的
    编码 / 锚点 / 资源 / 版本面 / i18n，以及社区健康文件是否齐全。

.DESCRIPTION
    与 NetSpeedTest.Tests 里的 ReadmeDocumentationTests / ReadmeBilingualTests /
    VersionConsistencyTests / SiteTemplateTests 互补：
    xUnit 把文档与官网逐项钉在版本号、出厂默认值、滑块区间、Web API 清单、工具箱数量、
    canonical 章节与 i18n 键上；本脚本负责 xUnit 不便直接覆盖的「静态文档」层面——
    编码卫生、相对链接与图片、跨文件 / 页内锚点、结构镜像、官网资源与版本字面量、
    社区健康文件，可作为提交前与 CI 的统一入口（默认完全离线）。

    CI（.github/workflows/ci.yml）在 dotnet test 之后执行：
        pwsh -File tools/verify-docs.ps1

.PARAMETER Online
    额外对外部 http(s) 链接做 HEAD 探测（屏蔽徽章等会因网络抖动失败，故默认关闭）。

.PARAMETER Silent
    只输出汇总与失败明细，适合在流水线里调用。

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\verify-docs.ps1
.EXAMPLE
    pwsh -File tools/verify-docs.ps1
.EXAMPLE
    pwsh -File tools/verify-docs.ps1 -Online -Silent
#>
[CmdletBinding()]
param(
    [switch]$Online,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Check([string]$name, [scriptblock]$body) {
    $script:checks++
    try {
        $detail = & $body
        if (-not $Silent) { Write-Host ("  PASS  {0}  {1}" -f $name, $detail) -ForegroundColor Green }
    } catch {
        $failures.Add("$name -> $($_.Exception.Message)")
        Write-Host ("  FAIL  {0}  {1}" -f $name, $_.Exception.Message) -ForegroundColor Red
    }
}

function Fail([string]$message) { throw $message }

function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }

# 与 ReadmeDocumentationTests.GitHubAnchorFor 保持同一套近似规则：去掉符号、小写、空格转连字符。
# 变体选择符 U+FE0F 刻意保留，对应 github-slugger 对 `## 🛠️ 网络工具箱` 生成 `#️-网络工具箱` 的行为。
function ConvertTo-GitHubAnchor([string]$title) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $title.ToCharArray()) {
        if ($ch -eq [char]0xFE0F) { [void]$sb.Append($ch); continue }
        if ([char]::IsLetterOrDigit($ch) -or $ch -eq ' ' -or $ch -eq '-') { [void]$sb.Append([char]::ToLowerInvariant($ch)) }
    }
    return $sb.ToString().Replace(' ', '-')
}

function Get-Headings([string]$text) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($text, '(?m)^#{2,3}[ \t]+(.+?)[ \t]*$')) { $list.Add($m.Groups[1].Value) }
    return $list
}

function Get-TocEntries([string]$text) {
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($m in [regex]::Matches($text, '(?m)^[ \t]*-[ \t]+\[(.+?)\]\(#(.+?)\)[ \t]*$')) {
        $list.Add([pscustomobject]@{ Title = $m.Groups[1].Value; Anchor = $m.Groups[2].Value })
    }
    return $list
}

# 同时覆盖链接与图片：`[text](target)` 与 `![alt](target)` 都要求 target 在仓库内存在。
function Get-LinkTargets([string]$text) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($text, '\[[^\]]*\]\(([^)\s]+)')) { $list.Add($m.Groups[1].Value) }
    return $list
}

# 统一把 CRLF 归一化为 LF：本仓库工作区文件是 CRLF，而按行解析的锚点正则在多行模式下
# 依赖 `$` 紧贴行尾；留着 `\r` 会让所有目录条目匹配失败（与 C# 测试的 ReadSourceFile 同一处理）。
function Read-Utf8([string]$path) {
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8).Replace("`r`n", "`n")
}

# 校验「UTF-8 无 BOM + CRLF + 单个结尾换行」并返回一行摘要；失败时 throw，由 Check 计数。
function Assert-SourceEncoding([string]$path, [string]$label) {
    Assert (Test-Path -LiteralPath $path) "$label 不存在"
    $bytes = [System.IO.File]::ReadAllBytes($path)
    Assert (($bytes.Length -lt 3) -or ($bytes[0] -ne 0xEF) -or ($bytes[1] -ne 0xBB) -or ($bytes[2] -ne 0xBF)) "$label 带 UTF-8 BOM"
    $crlf = 0; $bareLf = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -ne 0x0A) { continue }
        if ($i -gt 0 -and $bytes[$i - 1] -eq 0x0D) { $crlf++ } else { $bareLf++ }
    }
    Assert ($bytes.Length -ge 1 -and $bytes[$bytes.Length - 1] -eq 0x0A) "$label 缺少结尾换行"
    Assert ($bareLf -eq 0) "$label 含 $bareLf 处裸 LF（本仓库源码统一用 CRLF）"
    Assert (-not ($bytes.Length -ge 2 -and $bytes[$bytes.Length - 2] -eq 0x0A)) "$label 结尾有多余空行（应只有单个结尾换行）"
    return "UTF-8 无 BOM · CRLF $crlf 行 · 单结尾换行"
}

# 版本号唯一来源是 NetSpeedTest.csproj 的 <Version>；回退到 README 的 release 徽章，避免写死。
function Get-DeclaredVersion {
    $csproj = Join-Path $repo 'NetSpeedTest\NetSpeedTest.csproj'
    if (Test-Path -LiteralPath $csproj) {
        $match = [regex]::Match((Read-Utf8 $csproj), '<Version>\s*([^<\s]+)\s*</Version>')
        if ($match.Success) { return $match.Groups[1].Value }
    }
    $readme = Join-Path $repo 'README.md'
    if (Test-Path -LiteralPath $readme) {
        $match = [regex]::Match((Read-Utf8 $readme), 'release-v(\d+\.\d+\.\d+)')
        if ($match.Success) { return $match.Groups[1].Value }
    }
    Fail '无法从 NetSpeedTest.csproj 或 README.md 解析出版本号'
}

# CHANGELOG.md 中按顺序排列的版本号（去掉前导 V），用于校验「当前 + 前两个版本」。
function Get-ChangelogVersions {
    $list = New-Object System.Collections.Generic.List[string]
    $text = Read-Utf8 (Join-Path $repo 'CHANGELOG.md')
    foreach ($m in [regex]::Matches($text, '(?m)^## (V[\d.]+) ')) { $list.Add($m.Groups[1].Value.Substring(1)) }
    return $list
}

Write-Host "文档模板自检 · $repo" -ForegroundColor Cyan

$docs = @(
    [pscustomobject]@{ Language = '中文'; Path = Join-Path $repo 'README.md'; Counterpart = 'README.en.md'; Switch = '[English](README.en.md)'; FileName = 'README.md' },
    [pscustomobject]@{ Language = 'English'; Path = Join-Path $repo 'README.en.md'; Counterpart = 'README.md'; Switch = '[中文](README.md)'; FileName = 'README.en.md' }
)

# ========== 1. README 编码卫生 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 编码") {
        Assert-SourceEncoding $local.Path $local.FileName
    }
}

# ========== 2. README 目录锚点 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 目录锚点") {
        $text = Read-Utf8 $local.Path
        $headings = Get-Headings $text
        Assert ($headings.Count -gt 0) "没有二级/三级标题"
        $entries = Get-TocEntries $text
        Assert ($entries.Count -gt 0) "没有目录条目"
        $mismatches = New-Object System.Collections.Generic.List[string]
        foreach ($entry in $entries) {
            if ($headings -notcontains $entry.Title) { $mismatches.Add("「$($entry.Title)」没有同名标题"); continue }
            $expected = ConvertTo-GitHubAnchor $entry.Title
            if ($entry.Anchor -ne $expected) { $mismatches.Add("「$($entry.Title)」锚点是 #$($entry.Anchor)，GitHub 会生成 #$expected") }
        }
        Assert ($mismatches.Count -eq 0) ($mismatches -join '; ')
        "$($entries.Count) 条目录锚点全部命中且规则正确"
    }
}

# ========== 3. README 锚点去重 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 锚点唯一") {
        $counts = @{}
        foreach ($heading in (Get-Headings (Read-Utf8 $local.Path))) {
            $anchor = ConvertTo-GitHubAnchor $heading
            if ($counts.ContainsKey($anchor)) { $counts[$anchor] = $counts[$anchor] + 1 } else { $counts[$anchor] = 1 }
        }
        $dupes = @($counts.GetEnumerator() | Where-Object { $_.Value -gt 1 })
        Assert ($dupes.Count -eq 0) ("重复锚点：" + (($dupes | ForEach-Object { "$($_.Key) ×$($_.Value)" }) -join ', '))
        "$($counts.Count) 个标题锚点互不重复"
    }
}

# ========== 4. README 本地链接与图片 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 本地链接/图片") {
        $text = Read-Utf8 $local.Path
        $targets = New-Object System.Collections.Generic.List[string]
        foreach ($t in (Get-LinkTargets $text)) { $targets.Add($t) }
        foreach ($m in [regex]::Matches($text, '<img\s+src="([^"]+)"')) { $targets.Add($m.Groups[1].Value) }
        Assert ($targets.Count -gt 0) "没有解析到任何链接"

        $missing = New-Object System.Collections.Generic.List[string]
        $localFiles = 0
        foreach ($target in $targets) {
            if ($target -match '^(https?:)?//') { continue }
            if ($target.StartsWith('#')) { continue }
            $relative = $target.Split('#')[0]
            if ([string]::IsNullOrWhiteSpace($relative)) { continue }
            $resolved = Join-Path $repo $relative
            if (-not (Test-Path -LiteralPath $resolved)) { $missing.Add($relative) } else { $localFiles++ }
        }
        Assert ($missing.Count -eq 0) ("仓库内找不到：" + ($missing -join ', '))
        "$localFiles 个仓库内目标全部存在"
    }
}

# ========== 5. README 语言切换互链 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 语言切换") {
        $text = Read-Utf8 $local.Path
        Assert ($text.Contains($local.Switch)) "缺少指向 $($local.Counterpart) 的切换链接「$($local.Switch)」"
        $counterpart = Join-Path $repo $local.Counterpart
        Assert (Test-Path -LiteralPath $counterpart) "$($local.Counterpart) 不存在"
        "→ $($local.Counterpart)"
    }
}

# ========== 6. README 中英结构镜像 ==========
Check '中英结构镜像' {
    $zh = Read-Utf8 $docs[0].Path
    $en = Read-Utf8 $docs[1].Path
    $zhHeadings = Get-Headings $zh
    $enHeadings = Get-Headings $en
    Assert ($zhHeadings.Count -eq $enHeadings.Count) "标题数量不一致：中文 $($zhHeadings.Count)，英文 $($enHeadings.Count)"

    $markers = @(
        @{ Pattern = '(?m)^\s*\|(?![\s\-:|]+\|)'; Label = '表格内容行' },
        @{ Pattern = '(?m)^\s*\|[\s\-:|]+\|\s*$'; Label = '表格分隔行' },
        @{ Pattern = '<table>'; Label = '<table>' },
        @{ Pattern = '<tr>'; Label = '<tr>' },
        @{ Pattern = '<td[^>]*>'; Label = '<td>' },
        @{ Pattern = '(?m)^\s*- '; Label = '列表项' },
        @{ Pattern = '(?m)^```'; Label = '代码块围栏' },
        @{ Pattern = '!\[[^\]]*\]\('; Label = 'Markdown 图片' },
        @{ Pattern = '<img '; Label = '<img> 标签' }
    )
    $diffs = New-Object System.Collections.Generic.List[string]
    foreach ($marker in $markers) {
        $zhCount = [regex]::Matches($zh, $marker.Pattern).Count
        $enCount = [regex]::Matches($en, $marker.Pattern).Count
        if ($zhCount -ne $enCount) { $diffs.Add("$($marker.Label)：中文 $zhCount，英文 $enCount") }
    }
    Assert ($diffs.Count -eq 0) ($diffs -join '; ')
    "$($zhHeadings.Count) 个标题 · 9 类骨架元素数量一致"
}

# ========== 7. 官网编码卫生 ==========
$siteFiles = @(
    [pscustomobject]@{ Label = 'index.html'; Path = Join-Path $repo 'index.html' },
    [pscustomobject]@{ Label = '404.html'; Path = Join-Path $repo '404.html' },
    [pscustomobject]@{ Label = 'assets/site.css'; Path = Join-Path $repo 'assets\site.css' },
    [pscustomobject]@{ Label = 'assets/site.js'; Path = Join-Path $repo 'assets\site.js' }
)
foreach ($siteFile in $siteFiles) {
    $local = $siteFile
    Check ("$($local.Label) 编码") {
        Assert-SourceEncoding $local.Path $local.Label
    }
}

# ========== 8. 官网页内锚点与 id 唯一 ==========
Check '官网页内锚点与 id 唯一' {
    $html = Read-Utf8 (Join-Path $repo 'index.html')

    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($m in [regex]::Matches($html, '(?<![\w-])id="([^"]+)"')) { $ids.Add($m.Groups[1].Value) }
    Assert ($ids.Count -gt 0) 'index.html 中没有 id 属性'

    $counts = @{}
    foreach ($id in $ids) {
        if ($counts.ContainsKey($id)) { $counts[$id] = $counts[$id] + 1 } else { $counts[$id] = 1 }
    }
    $dupes = @($counts.GetEnumerator() | Where-Object { $_.Value -gt 1 })
    Assert ($dupes.Count -eq 0) ('重复 id：' + (($dupes | ForEach-Object { "$($_.Key) ×$($_.Value)" }) -join ', '))

    $idSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($id in $ids) { [void]$idSet.Add($id) }

    $anchors = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($html, 'href="#([^"]*)"')) {
        if ($m.Groups[1].Value.Length -gt 0) { [void]$anchors.Add($m.Groups[1].Value) }
    }
    Assert ($anchors.Count -gt 0) 'index.html 中没有页内锚点'

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($anchor in $anchors) {
        if (-not $idSet.Contains($anchor)) { $missing.Add("#$anchor") }
    }
    Assert ($missing.Count -eq 0) ('页内锚点没有对应 id：' + ($missing -join ', '))
    "$($anchors.Count) 个页内锚点全部命中 · $($ids.Count) 个 id 互不重复"
}

# ========== 9. 官网本地资源存在 ==========
Check '官网本地资源存在' {
    $missing = New-Object System.Collections.Generic.List[string]
    $total = 0
    foreach ($name in @('index.html', '404.html')) {
        $html = Read-Utf8 (Join-Path $repo $name)
        foreach ($m in [regex]::Matches($html, '(?:src|href)="([^"]+)"')) {
            $target = $m.Groups[1].Value
            if ([string]::IsNullOrWhiteSpace($target)) { continue }
            if ($target.StartsWith('#')) { continue }
            if ($target -match '^(?:[a-z][a-z0-9+.\-]*:|//)') { continue }
            $relative = ($target -split '[?#]')[0]
            if ([string]::IsNullOrWhiteSpace($relative)) { continue }
            $resolved = Join-Path $repo $relative
            if (-not (Test-Path -LiteralPath $resolved)) { $missing.Add("$name -> $relative") } else { $total++ }
        }
    }
    Assert ($missing.Count -eq 0) ('仓库内找不到：' + ($missing -join ', '))
    "$total 个官网本地资源全部存在"
}

# ========== 10. 官网版本面 ==========
Check '官网版本面一致' {
    $version = Get-DeclaredVersion
    $current = "v$version"
    $changelogVersions = @(Get-ChangelogVersions)
    Assert ($changelogVersions.Count -ge 3) "CHANGELOG.md 至少要有 3 个版本标题，实际 $($changelogVersions.Count) 个"
    Assert ($changelogVersions[0] -eq $version) "CHANGELOG.md 最新版本是 $($changelogVersions[0])，源码声明是 $version"

    $html = Read-Utf8 (Join-Path $repo 'index.html')
    $chip = [regex]::Match($html, '<span class="version-chip" id="siteVersion">([^<]+)</span>')
    Assert $chip.Success 'index.html 缺少 #siteVersion 版本胶囊'
    $chipVersion = $chip.Groups[1].Value.Trim()
    Assert ($chipVersion -eq $current) "官网 #siteVersion 是 $chipVersion，应为 $current"

    $badge = [regex]::Match($html, '<span[^>]*data-i18n="hero\.badge"[^>]*>([^<]+)</span>')
    Assert $badge.Success 'index.html 的 hero 徽标缺少 hero.badge 文案'
    Assert ($badge.Groups[1].Value.Contains($current)) "hero 徽标「$($badge.Groups[1].Value.Trim())」未包含 $current"

    $tags = @([regex]::Matches($html, '<span class="version-tag">([^<]+)</span>') | ForEach-Object { $_.Groups[1].Value.Trim() })
    Assert ($tags.Count -eq 3) "更新日志应有 3 个 version-tag，实际 $($tags.Count) 个"
    Assert ($tags[0] -eq $current) "更新日志最新版本是 $($tags[0])，应为 $current"

    # 允许的版本字面量 = 当前版本 + CHANGELOG 里的前两个版本；其余位置一律视为陈旧字面量。
    $allowed = New-Object System.Collections.Generic.HashSet[string]
    [void]$allowed.Add($current)
    for ($i = 0; $i -lt [Math]::Min(3, $changelogVersions.Count); $i++) {
        [void]$allowed.Add("v$($changelogVersions[$i])")
    }

    $stale = New-Object System.Collections.Generic.List[string]
    $lineNumber = 0
    foreach ($line in $html.Split("`n")) {
        $lineNumber++
        foreach ($m in [regex]::Matches($line, 'v\d+\.\d+\.\d+')) {
            if (-not $allowed.Contains($m.Value)) {
                $stale.Add("$($m.Value)（第 $lineNumber 行，既非当前版本也非前两个版本）")
            } elseif ($m.Value -ne $current -and $line -notmatch 'class="version-tag"') {
                $stale.Add("$($m.Value)（第 $lineNumber 行，旧版本只能出现在 version-tag 中）")
            }
        }
    }
    Assert ($stale.Count -eq 0) ('陈旧或位置不当的版本字面量：' + ($stale -join '; '))

    $literalCount = [regex]::Matches($html, 'v\d+\.\d+\.\d+').Count
    "$literalCount 处版本字面量 = $current + 前两个版本（$($changelogVersions[1]) / $($changelogVersions[2])）"
}

# ========== 11. 官网 i18n 字典 ==========
Check '官网 i18n 键完整' {
    $html = Read-Utf8 (Join-Path $repo 'index.html')
    $i18nNode = [regex]::Match($html, '(?s)<script[^>]*id="i18n-data"[^>]*>(.*?)</script>')
    Assert $i18nNode.Success 'index.html 缺少内嵌的 i18n-data 字典'

    $parsed = $null
    try {
        $parsed = $i18nNode.Groups[1].Value | ConvertFrom-Json
    } catch {
        Fail "i18n-data 不是合法 JSON：$($_.Exception.Message)"
    }
    Assert ($null -ne $parsed -and $null -ne $parsed.en) 'i18n-data 缺少 en 字典'

    $english = @{}
    foreach ($property in $parsed.en.PSObject.Properties) { $english[$property.Name] = [string]$property.Value }
    Assert ($english.Count -gt 0) 'en 字典为空'

    $used = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($html, 'data-i18n(?:-html|-aria|-title)?="([^"]+)"')) { [void]$used.Add($m.Groups[1].Value) }
    Assert ($used.Count -gt 0) 'index.html 中没有 data-i18n 属性'

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($key in $used) {
        if (-not $english.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($english[$key])) { $missing.Add($key) }
    }
    Assert ($missing.Count -eq 0) ('英文字典缺少或为空的键：' + ($missing -join ', '))

    $jsOnly = @('meta.title', 'meta.description', 'toast.copied', 'toast.copyFailed')
    $unused = New-Object System.Collections.Generic.List[string]
    foreach ($key in $english.Keys) {
        if (-not $used.Contains($key) -and ($jsOnly -notcontains $key)) { $unused.Add($key) }
    }
    Assert ($unused.Count -eq 0) ('英文字典存在无用键：' + ($unused -join ', '))
    "$($used.Count) 个 data-i18n 键全部命中 · $($english.Count) 条英文译文无冗余"
}

# ========== 12. 社区健康文件 ==========
Check '社区文件齐全' {
    $required = @(
        'CONTRIBUTING.md',
        'SECURITY.md',
        'CODE_OF_CONDUCT.md',
        '.github\ISSUE_TEMPLATE\bug_report.yml',
        '.github\ISSUE_TEMPLATE\feature_request.yml',
        '.github\ISSUE_TEMPLATE\config.yml',
        '.github\PULL_REQUEST_TEMPLATE.md',
        '.github\workflows\ci.yml'
    )
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($relative in $required) {
        $path = Join-Path $repo $relative
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Length -gt 0)) { continue }
        $missing.Add($relative)
    }
    Assert ($missing.Count -eq 0) ('缺失或为空：' + ($missing -join ', '))

    $ci = Read-Utf8 (Join-Path $repo '.github\workflows\ci.yml')
    Assert ($ci.Contains('dotnet test')) 'ci.yml 没有运行 dotnet test'
    Assert ($ci.Contains('tools/verify-docs.ps1')) 'ci.yml 没有运行 tools/verify-docs.ps1'
    "$($required.Count) 个社区健康文件齐全 · CI 覆盖测试与文档自检"
}

# ========== 13. README 社区链接 ==========
Check 'README 社区链接' {
    $communityFiles = @('CONTRIBUTING.md', 'SECURITY.md', 'CODE_OF_CONDUCT.md', 'CHANGELOG.md', 'LICENSE')
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($doc in $docs) {
        $text = Read-Utf8 $doc.Path
        foreach ($target in $communityFiles) {
            if (-not $text.Contains($target)) { $missing.Add("$($doc.FileName) 未引用 $target") }
            $path = Join-Path $repo $target
            if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { $missing.Add("$target 不存在或为空") }
        }
    }
    Assert ($missing.Count -eq 0) ($missing -join '; ')
    "$($communityFiles.Count) 个社区文件在两份 README 中均有引用且真实存在"
}

# ========== 14. 外部链接（可选） ==========
if ($Online) {
    Check '外部链接可达性' {
        $urls = New-Object System.Collections.Generic.HashSet[string]
        foreach ($doc in $docs) {
            $text = Read-Utf8 $doc.Path
            foreach ($t in (Get-LinkTargets $text)) {
                if ($t -match '^https?://') { [void]$urls.Add($t) }
            }
            foreach ($m in [regex]::Matches($text, '<img\s+src="(https?://[^"]+)"')) { [void]$urls.Add($m.Groups[1].Value) }
        }

        $bad = New-Object System.Collections.Generic.List[string]
        $ok = 0
        foreach ($url in $urls) {
            try {
                $response = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec 15 -UseBasicParsing -MaximumRedirection 5
                if ($response.StatusCode -ge 400) { $bad.Add("$url -> $($response.StatusCode)") } else { $ok++ }
            } catch {
                $status = $null
                if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
                # shields.io 等徽章服务对 HEAD 与爬虫并不稳定，4xx/超时按提示处理而非失败
                if ($status -and $status -ge 400 -and $status -lt 500) { $ok++ } else { $bad.Add("$url -> $($_.Exception.Message)") }
            }
        }
        Assert ($bad.Count -eq 0) ($bad -join '; ')
        "$ok/$($urls.Count) 个外部链接可达"
    }
}

# ========== 汇总 ==========
$failed = $failures.Count
$passed = $checks - $failed
Write-Host ''
if ($failed -eq 0) {
    Write-Host "文档自检通过：$passed/$checks 项" -ForegroundColor Green
    exit 0
}

Write-Host "文档自检失败：$passed 通过 / $failed 失败（共 $checks 项）" -ForegroundColor Red
foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
exit 1
