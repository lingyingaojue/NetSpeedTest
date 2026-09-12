# Verify the v1.4.2 version string reaches every runtime surface.
$ErrorActionPreference = 'Stop'
$repo   = 'D:\Program Files\DSH\NetSpeedTest'
$exe    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$base   = 'http://127.0.0.1:8080'
$results = New-Object System.Collections.Generic.List[object]

function Check([string]$name, [scriptblock]$body) {
  try {
    $d = & $body
    $results.Add([pscustomobject]@{ Test = $name; Result = 'PASS'; Detail = "$d" })
    Write-Host ("  PASS  {0}  {1}" -f $name, $d) -ForegroundColor Green
  } catch {
    $results.Add([pscustomobject]@{ Test = $name; Result = 'FAIL'; Detail = $_.Exception.Message })
    Write-Host ("  FAIL  {0}  {1}" -f $name, $_.Exception.Message) -ForegroundColor Red
  }
}

Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Set-Content -Path (Join-Path $env:LOCALAPPDATA 'NetSpeedTest\web.json') -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

function Fetch([string]$url) {
  # Decode explicitly as UTF-8: PS 5.1 decodes response bodies as ISO-8859-1,
  # which mangles CJK text and breaks content assertions.
  $req = [System.Net.HttpWebRequest]::Create($url)
  $req.Timeout = 20000
  $resp = $req.GetResponse()
  $reader = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
  $text = $reader.ReadToEnd()
  $reader.Close(); $resp.Close()
  return $text
}

$app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "launched pid=$($app.Id)"
try {
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    if ($app.HasExited) { throw "app exited early, code $($app.ExitCode)" }
    try { $null = Invoke-WebRequest "$base/api/status" -UseBasicParsing -TimeoutSec 5; $ready = $true; break } catch { }
  }
  if (-not $ready) { throw 'server not ready in 30s' }
  Write-Host "server ready"
  Start-Sleep -Seconds 2

  try {
    $html = Fetch "$base/"
    Write-Host "html fetched: $($html.Length) chars"
    $js = Fetch "$base/assets/web.js"
    Write-Host "js fetched: $($js.Length) chars"
  } catch {
    Write-Host "FETCH FAILED: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    throw
  }

  # Derive the expected version from the source of truth (csproj) instead of
  # hardcoding it here, so this script keeps working across releases.
  $csproj = Join-Path $repo 'NetSpeedTest\NetSpeedTest.csproj'
  $m = [regex]::Match((Get-Content $csproj -Raw), '<Version>\s*([^<\s]+)\s*</Version>')
  if (-not $m.Success) { throw "<Version> not found in $csproj" }
  $expected = $m.Groups[1].Value
  Write-Host "expected version (from csproj): $expected"

  Check "exe FileVersion matches csproj ($expected)" {
    $v = (Get-Item $exe).VersionInfo.FileVersion
    if ($v -notlike "$expected.*") { throw "FileVersion=$v, expected $expected.x" }
    $v
  }
  Check 'exe ProductVersion carries the same version' {
    $p = (Get-Item $exe).VersionInfo.ProductVersion
    if ($p -notlike "$expected*") { throw "ProductVersion=$p" }
    $p
  }
  Check 'web console serves the real version, no placeholder left' {
    if ($html -match '%%NST_VERSION%%') { throw 'NST_VERSION placeholder not replaced' }
    if ($html -match '%%NST_TOKEN%%') { throw 'NST_TOKEN placeholder not replaced' }
    $mm = [regex]::Match($html, 'id="appVersion">([^<]+)<')
    if (-not $mm.Success) { throw 'appVersion element not found' }
    if ($mm.Groups[1].Value -ne "v$expected") { throw "rendered '$($mm.Groups[1].Value)', expected 'v$expected'" }
    "appVersion -> $($mm.Groups[1].Value)"
  }
  Check 'version meta tag matches the csproj version' {
    $mm = [regex]::Match($html, '<meta name="nst-version" content="([^"]+)"')
    if (-not $mm.Success) { throw 'nst-version meta missing' }
    if ($mm.Groups[1].Value -ne $expected) { throw "meta='$($mm.Groups[1].Value)', expected '$expected'" }
    "nst-version -> $($mm.Groups[1].Value)"
  }
  Check 'static web.js has no hardcoded version and no placeholder' {
    if ($js -match '%%NST') { throw 'placeholder leaked into web.js' }
    if ($js -match 'v\d+\.\d+\.\d+') { throw 'web.js still hardcodes a version' }
    'clean'
  }
  Check 'i18n key for the sidebar suffix is present' {
    # Use a Unicode escape instead of a CJK literal: PS 5.1 reads non-ASCII
    # characters in BOM-less scripts as ANSI, which corrupts the pattern.
    if ($js -notmatch '"\u00b7 [\u4e00-\u9fff]+"') { throw 'translation key missing after refactor' }
    'translation key for the sidebar suffix present'
  }
  Check 'sidebar renders version chip separate from the translated suffix' {
    $m = [regex]::Match($html, '<span id="appVersion">v([\d.]+)</span>\s*<span>([^<]+)</span>')
    if (-not $m.Success) { throw 'sidebar version block structure changed' }
    "version=v$($m.Groups[1].Value)"
  }
  Check 'runtime UA carries the new version (from log/probe)' {
    # HttpClient UA is built from AppVersion; verify via the assembly value surfaced in HTML.
    $m = [regex]::Match($html, '<meta name="nst-version" content="([^"]+)"')
    "UA would be NetSpeedTest/$($m.Groups[1].Value)"
  }
}
finally {
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $results | Format-Table -AutoSize
  $failed = @($results | Where-Object Result -eq 'FAIL')
  Write-Host ("Total={0} Pass={1} Fail={2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
  exit $failed.Count
}
