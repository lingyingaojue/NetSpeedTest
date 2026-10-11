<#
.SYNOPSIS
    中英双语 README 的离线自检：编码卫生、本地链接与图片存在、目录锚点与标题一致、
    两份文档互为语言切换入口，并把每个文件里的本地锚点做去重校验。

.DESCRIPTION
    与 NetSpeedTest.Tests 里的 ReadmeDocumentationTests / ReadmeBilingualTests 互补：
    那两组测试把 README 钉在版本号、出厂默认值、滑块区间、Web API 清单与工具箱数量上；
    本脚本负责 xUnit 不便覆盖的「静态网站」层面——相对链接、图片、跨文件锚点与结构镜像。
    默认完全离线，仅做本地检查。

.PARAMETER Online
    额外对外部 http(s) 链接做 HEAD 探测（屏蔽徽章等会因网络抖动失败，故默认关闭）。

.PARAMETER Silent
    只输出汇总与失败明细，适合在流水线里调用。

.EXAMPLE
    pwsh -File tools\verify-readme.ps1
.EXAMPLE
    pwsh -File tools\verify-readme.ps1 -Online -Silent
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

Write-Host "README 双语自检 · $repo" -ForegroundColor Cyan

$docs = @(
    [pscustomobject]@{ Language = '中文'; Path = Join-Path $repo 'README.md'; Counterpart = 'README.en.md'; Switch = '[English](README.en.md)'; FileName = 'README.md' },
    [pscustomobject]@{ Language = 'English'; Path = Join-Path $repo 'README.en.md'; Counterpart = 'README.md'; Switch = '[中文](README.md)'; FileName = 'README.en.md' }
)

# ========== 1. 编码卫生 ==========
foreach ($doc in $docs) {
    $local = $doc
    Check ("$($local.FileName) 编码") {
        Assert (Test-Path -LiteralPath $local.Path) "$($local.FileName) 不存在"
        $bytes = [System.IO.File]::ReadAllBytes($local.Path)
        Assert (($bytes.Length -lt 3) -or ($bytes[0] -ne 0xEF) -or ($bytes[1] -ne 0xBB) -or ($bytes[2] -ne 0xBF)) "$($local.FileName) 带 UTF-8 BOM"
        $crlf = 0; $bareLf = 0
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -ne 0x0A) { continue }
            if ($i -gt 0 -and $bytes[$i - 1] -eq 0x0D) { $crlf++ } else { $bareLf++ }
        }
        Assert ($bytes.Length -ge 1 -and $bytes[$bytes.Length - 1] -eq 0x0A) "$($local.FileName) 缺少结尾换行"
        Assert ($bareLf -eq 0) "$($local.FileName) 含 $bareLf 处裸 LF（本仓库源码统一用 CRLF）"
        "UTF-8 无 BOM · CRLF $crlf 行 · 单结尾换行"
    }
}

# ========== 2. 目录锚点 ==========
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

# ========== 3. 锚点去重 ==========
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

# ========== 4. 本地链接与图片 ==========
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

# ========== 5. 语言切换互链 ==========
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

# ========== 6. 结构镜像 ==========
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

# ========== 7. 外部链接（可选） ==========
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
    Write-Host "README 自检通过：$passed/$checks 项" -ForegroundColor Green
    exit 0
}

Write-Host "README 自检失败：$passed 通过 / $failed 失败（共 $checks 项）" -ForegroundColor Red
foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
exit 1
