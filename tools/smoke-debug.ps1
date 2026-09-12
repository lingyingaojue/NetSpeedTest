# Smoke-test the Debug build: Debug enables Debug.Assert and stricter runtime
# checks, so a clean compile is not sufficient evidence that it behaves.
$ErrorActionPreference = 'Stop'
$repo    = 'D:\Program Files\DSH\NetSpeedTest'
$exe     = Join-Path $repo 'NetSpeedTest\bin\Debug\net8.0-windows\NetSpeedTest.exe'
$cfgDir  = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
$base    = 'http://127.0.0.1:8080'
$log     = Join-Path $repo 'NetSpeedTest\bin\Debug\net8.0-windows\debug.log'
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
function Req {
  param([string]$Method, [string]$Path, $Body = $null, [int]$Timeout = 30, [hashtable]$Headers = $null)
  $p = @{ Method = $Method; Uri = ($base + $Path); TimeoutSec = $Timeout; UseBasicParsing = $true }
  if ($Headers) { $p.Headers = $Headers }
  if ($null -ne $Body) { $p.Body = $Body; $p.ContentType = 'application/json' }
  Invoke-WebRequest @p
}
function StatusOf {
  param([string]$Method, [string]$Path, $Body = $null)
  try { return (Req -Method $Method -Path $Path -Body $Body).StatusCode }
  catch [System.Net.WebException] { return [int]$_.Exception.Response.StatusCode }
  catch { return -1 }
}
function St { (Req -Method GET -Path '/api/status' -Timeout 10).Content | ConvertFrom-Json }

Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
Remove-Item $log -ErrorAction SilentlyContinue
Set-Content -Path (Join-Path $cfgDir 'web.json') -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

$app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
Write-Host "Debug exe pid=$($app.Id)"
try {
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    if ($app.HasExited) { throw "Debug build exited early, code $($app.ExitCode)" }
    try { $null = Req -Method GET -Path '/api/status' -Timeout 5; $ready = $true; break } catch { }
  }
  if (-not $ready) { throw 'Debug server not ready in 30s' }
  Write-Host "Debug server ready"
  Start-Sleep -Seconds 3

  Check 'Debug: index.html served with token + version' {
    $c = (Req -Method GET -Path '/').Content
    if ($c -match '%%NST') { throw 'placeholder left unreplaced' }
    if ($c -notmatch 'id="appVersion">v([\d.]+)<') { throw 'appVersion not rendered' }
    "version=v$($Matches[1])"
  }
  Check 'Debug: security headers present' {
    $r = Req -Method GET -Path '/api/status'
    if ($r.Headers['X-Content-Type-Options'] -ne 'nosniff') { throw 'nosniff missing' }
    if ($r.Headers['X-Frame-Options'] -ne 'DENY') { throw 'DENY missing' }
    'nosniff + DENY'
  }
  Check 'Debug: test lifecycle works' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download"}'
    if ($c -ne 200) { throw "start -> $c" }
    $s = St
    if ($s.running -ne $true) { throw "start returned 200 but running=$($s.running)" }
    $c2 = StatusOf -Method POST -Path '/api/test/stop'
    if ($c2 -ne 200) { throw "stop -> $c2" }
    $ok = $false
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 250; if (-not (St).running) { $ok = $true; break } }
    if (-not $ok) { throw 'still running after stop' }
    'start 200 -> running -> stop'
  }
  Check 'Debug: SSRF guard still rejects private targets' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download","urls":["http://169.254.169.254/"]}'
    if ($c -ne 400) { throw "got $c" }
    '400'
  }
  Check 'Debug: malformed JSON -> 400' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{bad'
    if ($c -ne 400) { throw "got $c" }
    '400'
  }
  Check 'Debug: 10x rapid start/stop stays responsive' {
    $worst = 0
    for ($n = 1; $n -le 10; $n++) {
      $null = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download"}'
      $null = StatusOf -Method POST -Path '/api/test/stop'
      $sw = [System.Diagnostics.Stopwatch]::StartNew()
      $null = Req -Method GET -Path '/api/status'
      $sw.Stop()
      if ($sw.ElapsedMilliseconds -gt $worst) { $worst = $sw.ElapsedMilliseconds }
      if ($sw.ElapsedMilliseconds -gt 2000) { throw "cycle $n status took $($sw.ElapsedMilliseconds)ms" }
    }
    "10 cycles OK, worst status latency ${worst}ms"
  }
  Check 'Debug: process still alive (no assert crash)' {
    $app.Refresh()
    if ($app.HasExited) { throw "Debug build exited during checks, code $($app.ExitCode)" }
    'alive'
  }
  Check 'Debug: no FATAL in debug.log' {
    if (-not (Test-Path $log)) { throw 'debug.log missing' }
    $f = @(Select-String -Path $log -Pattern 'FATAL' -ErrorAction SilentlyContinue)
    if ($f.Count -gt 0) { throw "$($f.Count) FATAL entries" }
    "0 FATAL in $((Get-Content $log | Measure-Object -Line).Lines) lines"
  }
  Check 'Debug: no assertion failures logged' {
    $a = @(Select-String -Path $log -Pattern 'Assert|Assertion' -ErrorAction SilentlyContinue)
    if ($a.Count -gt 0) { throw "$($a.Count) assertion-related lines: $($a[0].Line)" }
    'none'
  }
}
finally {
  Write-Host "`n=== Teardown ===" -ForegroundColor Cyan
  if ($app -and -not $app.HasExited) {
    try { $null = Req -Method POST -Path '/api/test/stop' -Timeout 5 } catch { }
    Start-Sleep -Milliseconds 500
    $null = $app.CloseMainWindow()
    Start-Sleep -Seconds 2
    if (-not $app.HasExited) { $app.Kill() }
  }
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $results | Format-Table -AutoSize
  $failed = @($results | Where-Object Result -eq 'FAIL')
  Write-Host ("Total={0} Pass={1} Fail={2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
  exit $failed.Count
}
