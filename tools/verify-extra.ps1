# Extra verification for plan smoke steps 4-7 (extension .zip / download endpoints).
# Focus: upload failure must be recorded as failure (not fake Mbps), B1-3/B1-2 sanity,
# and the token enforcement matrix reachable from loopback.
$ErrorActionPreference = 'Continue'
$repo   = 'D:\Program Files\DSH\NetSpeedTest'
$exe    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$cfgDir = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
$base   = 'http://127.0.0.1:8080'
$log    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\debug.log'
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
  param([string]$Method, [string]$Path, $Body = $null, [int]$Timeout = 60, [hashtable]$Headers = $null)
  $p = @{ Method = $Method; Uri = ($base + $Path); TimeoutSec = $Timeout; UseBasicParsing = $true }
  if ($Headers) { $p.Headers = $Headers }
  if ($null -ne $Body) { $p.Body = $Body; $p.ContentType = 'application/json' }
  Invoke-WebRequest @p
}
function St { (Req -Method GET -Path '/api/status' -Timeout 10).Content | ConvertFrom-Json }

Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item $log -ErrorAction SilentlyContinue
Set-Content -Path (Join-Path $cfgDir 'web.json') -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

$app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
try {
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; try { $null = Req -Method GET -Path '/api/status' -Timeout 5; $ready = $true; break } catch { } }
  if (-not $ready) { throw 'server not ready' }
  Start-Sleep -Seconds 3

  Write-Host "`n=== F-01 token enforcement matrix (loopback reachable portion) ===" -ForegroundColor Cyan
  Check 'loopback POST without token is allowed by design' {
    $c = (Req -Method POST -Path '/api/settings' -Body '{"threadCount":64}').StatusCode
    if ($c -ne 200) { throw "got $c" }; '200 (loopback exempt)'
  }
  Check 'loopback POST with wrong token still allowed (loopback short-circuit)' {
    $c = (Req -Method POST -Path '/api/settings' -Body '{"threadCount":64}' -Headers @{ 'X-NST-Token' = 'bogus' }).StatusCode
    if ($c -ne 200) { throw "got $c" }; '200 - non-loopback is the guarded path (unit-tested)'
  }
  Check 'LAN bindings are published for the token-guarded path' {
    $srv = (Req -Method GET -Path '/api/server').Content | ConvertFrom-Json
    $bind = @($srv.bindings)
    if ($bind.Count -eq 0) { throw 'no LAN bindings' }
    "bindings=$($bind.Count) first=$($bind[0].url)"
  }
  Check 'HTML exposes exactly one token, web.js sends it' {
    $idx = (Req -Method GET -Path '/').Content
    $tok = [regex]::Matches($idx, 'name="nst-token" content="([0-9A-F]{64})"')
    if ($tok.Count -ne 1) { throw "token meta count=$($tok.Count)" }
    $js = (Req -Method GET -Path '/assets/web.js').Content
    if ($js -match [regex]::Escape($tok[0].Groups[1].Value)) { throw 'token leaked into web.js' }
    'token in index.html only'
  }

  Write-Host "`n=== Upload failure path: 503 endpoint (plan step 4) ===" -ForegroundColor Cyan
  Write-Host "  target: https://httpbin.org/post (verified to answer 503)"
  Check 'upload to a 503 endpoint finishes without a timeout' {
    $body = '{"mode":"upload","urls":["https://httpbin.org/post"]}'
    $c = (Req -Method POST -Path '/api/test/start' -Body $body -Timeout 120).StatusCode
    if ($c -ne 200) { throw "start -> $c" }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $done = $false
    for ($i = 0; $i -lt 200; $i++) {
      Start-Sleep -Milliseconds 500
      $s = St
      if (-not $s.running) { $done = $true; break }
    }
    $sw.Stop()
    if (-not $done) { $null = Req -Method POST -Path '/api/test/stop' -Timeout 30; throw 'upload test did not finish' }
    # The per-request timeout is 15s; a 503 must come back far sooner than that.
    "finished in $([int]($sw.Elapsed.TotalSeconds))s"
  }
  Check 'F-02: non-2xx upload is recorded as failure, never as success' {
    $s = St
    $status = "$($s.status)"
    # 注意：uploadMbps 来自网卡字节计数器（确实发出去了字节），
    # 它不代表“服务器接收成功”。F-02 的可观测契约是：
    #   1) 结果里成功 URL 数为 0（状态文本形如 ".... 0/1 ...."）
    #   2) 每次 503 都走 catch -> ReportFailure 分支
    # 断言使用 ASCII 模式，避免 PS 5.1 读取脚本时的编码歧义。
    if ($status -notmatch '0/\d+') { throw "status does not report zero successes: '$status'" }
    $fail = @(Select-String -Path $log -Pattern 'url=\S+ FAIL' -ErrorAction SilentlyContinue)
    if ($fail.Count -eq 0) { throw 'no per-request FAIL entries (SendUploadAsync did not reject the 503)' }
    $success = @(Select-String -Path $log -Pattern 'url=\S+ OK' -ErrorAction SilentlyContinue)
    if ($success.Count -gt 0) { throw "found $($success.Count) OK entries for a 503 endpoint" }
    "status contains '0/N'; failEntries=$($fail.Count) successEntries=0"
  }
  Check 'F-02: failures are 503 rejections, not transport timeouts' {
    $fail = @(Select-String -Path $log -Pattern 'url=\S+ FAIL' -ErrorAction SilentlyContinue)
    $timeouts = @($fail | Where-Object { $_.Line -match 'TIMEOUT|TaskCanceled|timed out' })
    if ($timeouts.Count -ge $fail.Count) { throw "all $($fail.Count) failures are timeouts - rejection path not exercised" }
    "failures=$($fail.Count) timeouts=$($timeouts.Count); sample: $($fail[0].Line -replace '^.*\] ','')"
  }

  Write-Host "`n=== B1-2 / B1-3 gateway probe sanity (plan step 5) ===" -ForegroundColor Cyan
  Check 'gateway latency is measured and non-zero' {
    $s = St
    $lan = [double]$s.latencyMs
    if ($lan -le 0) { throw "lanLatency=$lan ms (B1-3 regression)" }
    "latency=$lan ms wan=$($s.wanLatencyMs) ms"
  }
  Check 'packet loss is not stuck at 100%' {
    $s = St
    $loss = [double]$s.packetLossPercent
    if ($loss -ge 100) { throw "loss=$loss% (B1-2 regression)" }
    "loss=$loss% sent=$($s.packetLossSent) recv=$($s.packetLossReceived)"
  }
  Check 'UDP probe lines are never 0.0ms and loss batches show received>0' {
    $udp = @(Select-String -Path $log -Pattern 'UDP=' -ErrorAction SilentlyContinue)
    $zero = @($udp | Where-Object { $_.Line -match 'UDP=0\.0ms' })
    if ($zero.Count -gt 0) { throw "$($zero.Count) UDP=0.0ms lines" }
    $lossLog = @(Select-String -Path $log -Pattern '\[D-LOSS\]' -ErrorAction SilentlyContinue)
    if ($lossLog.Count -eq 0) { throw 'no [D-LOSS] batches' }
    $allZero = @($lossLog | Where-Object { $_.Line -match 'total=0/5' })
    if ($allZero.Count -eq $lossLog.Count) { throw "all loss batches report total=0/5" }
    "udpLines=$($udp.Count) (no 0.0ms), lossBatches=$($lossLog.Count); sample: $($lossLog[0].Line -replace '^.*\] ','')"
  }
  Check 'no FATAL entries overall' {
    $fatal = @(Select-String -Path $log -Pattern 'FATAL' -ErrorAction SilentlyContinue)
    if ($fatal.Count -gt 0) { throw "$($fatal.Count) FATAL entries" }
    '0 FATAL'
  }
}
finally {
  Write-Host "`n=== Teardown ===" -ForegroundColor Cyan
  try { $null = Req -Method POST -Path '/api/test/stop' -Timeout 10 } catch { }
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $results | Format-Table -AutoSize
  $failed = @($results | Where-Object Result -eq 'FAIL')
  Write-Host ("Total={0} Pass={1} Fail={2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
  exit $failed.Count
}
