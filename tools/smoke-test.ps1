# NetSpeedTest B1-B3 smoke test (ASCII-only for Windows PowerShell 5.1)
# Drives the real Release build over HTTP on 127.0.0.1:8080.
$ErrorActionPreference = 'Stop'
trap {
  Write-Host "`n*** TRAP: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Magenta
  Write-Host $_.ScriptStackTrace -ForegroundColor Magenta
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  exit 99
}
$repo    = 'D:\Program Files\DSH\NetSpeedTest'
$exe     = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$cfgDir  = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
$base    = 'http://127.0.0.1:8080'
$log     = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\debug.log'
$results = New-Object System.Collections.Generic.List[object]
$app     = $null

function Check([string]$name, [scriptblock]$body) {
  try {
    $detail = & $body
    $results.Add([pscustomobject]@{ Test = $name; Result = 'PASS'; Detail = "$detail" })
    Write-Host ("  PASS  {0}  {1}" -f $name, $detail) -ForegroundColor Green
  } catch {
    $results.Add([pscustomobject]@{ Test = $name; Result = 'FAIL'; Detail = $_.Exception.Message })
    Write-Host ("  FAIL  {0}  {1}" -f $name, $_.Exception.Message) -ForegroundColor Red
  }
}

function Req {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$Path,
    $Body = $null,
    [int]$Timeout = 30
  )
  $p = @{ Method = $Method; Uri = ($base + $Path); TimeoutSec = $Timeout; UseBasicParsing = $true }
  if ($null -ne $Body) {
    $p.Body = if ($Body -is [string]) { $Body } else { ($Body | ConvertTo-Json -Compress) }
    $p.ContentType = 'application/json'
  }
  Invoke-WebRequest @p
}

function StatusOf {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$Path,
    $Body = $null
  )
  try { return (Req -Method $Method -Path $Path -Body $Body).StatusCode }
  catch [System.Net.WebException] { return [int]$_.Exception.Response.StatusCode }
  catch { return -1 }
}

# --- concurrent POST helper (runspaces: no HttpClient in PS 5.1) ---
$pool = [runspacefactory]::CreateRunspacePool(1, 16); $pool.Open()
function Post-Concurrent([string]$url, [string[]]$bodies) {
  $hs = @(); $ps = @()
  foreach ($b in $bodies) {
    $p = [powershell]::Create(); $p.RunspacePool = $pool
    $null = $p.AddScript({
      param($u, $payload)
      try {
        $r = [System.Net.WebRequest]::Create($u)
        $r.Method = 'POST'; $r.ContentType = 'application/json'
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        $r.ContentLength = $bytes.Length
        $s = $r.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
        $resp = $r.GetResponse()
        $code = [int]$resp.StatusCode
        $resp.Close()
        return $code
      } catch [System.Net.WebException] {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return -1
      } catch { return -1 }
    }).AddArgument($url).AddArgument($b)
    $hs += $p; $ps += $p.BeginInvoke()
  }
  $codes = @()
  for ($i = 0; $i -lt $ps.Count; $i++) {
    $codes += @($hs[$i].EndInvoke($ps[$i]))[0]
    $hs[$i].Dispose()
  }
  return $codes
}

try {
  Write-Host "`n=== Setup ===" -ForegroundColor Cyan
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Remove-Item $log -ErrorAction SilentlyContinue
  Set-Content -Path (Join-Path $cfgDir 'web.json') `
    -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8
  Write-Host "web.json seeded"

  Write-Host "`n=== Launch ===" -ForegroundColor Cyan
  $app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
  Write-Host "launched pid=$($app.Id)"
  $ready = $false
  $lastErr = ''
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $exited = $false
    try { $exited = $app.HasExited } catch { throw "process handle invalid: $($_.Exception.Message)" }
    if ($exited) { throw "app exited early with code $($app.ExitCode)" }
    try { $null = Req -Method GET -Path '/api/status' -Timeout 5; $ready = $true; break } catch { $lastErr = $_.Exception.Message }
  }
  if (-not $ready) { throw "HTTP server not ready within 30s; last error: $lastErr" }
  Write-Host "server ready (pid=$($app.Id))"
  Start-Sleep -Seconds 3   # let MainViewModel finish adapter enumeration
  Write-Host "proceeding to checks"

  Write-Host "`n=== F-01 token / static serving ===" -ForegroundColor Cyan
  $idx = Req -Method GET -Path '/'
  Check 'index.html served' { if ($idx.StatusCode -ne 200) { throw "status $($idx.StatusCode)" }; "$($idx.RawContentLength) bytes" }
  Check 'F-01 placeholder replaced by per-run token' {
    if ($idx.Content -match '%%NST_TOKEN%%') { throw 'placeholder still present' }
    if ($idx.Content -notmatch 'name="nst-token" content="([0-9A-F]{64})"') { throw 'no 64-hex token injected' }
    "token=$($Matches[1].Substring(0,8))... (64 hex)"
  }
  Check 'F-01 static web.js carries no secret and sends header' {
    $js = Req -Method GET -Path '/assets/web.js'
    if ($js.Content -match '%%NST_TOKEN%%') { throw 'placeholder leaked into web.js' }
    if ($js.Content -notmatch 'X-NST-Token') { throw 'web.js does not send X-NST-Token' }
    'no secret embedded; X-NST-Token wired'
  }
  Check 'token differs from a literal guess' {
    $tok = [regex]::Match($idx.Content, 'name="nst-token" content="([0-9A-F]{64})"').Groups[1].Value
    if ($tok -eq ('0' * 64)) { throw 'token is all zeroes' }
    if ($tok -notmatch '^[0-9A-F]{64}$') { throw 'token charset unexpected' }
    'CSPRNG-shaped 256-bit token'
  }

  Write-Host "`n=== FN-06 security headers ===" -ForegroundColor Cyan
  Check 'FN-06 headers on JSON response' {
    $r = Req -Method GET -Path '/api/status'
    if ($r.Headers['X-Content-Type-Options'] -ne 'nosniff')    { throw "nosniff='$($r.Headers['X-Content-Type-Options'])'" }
    if ($r.Headers['X-Frame-Options'] -ne 'DENY')              { throw "frame='$($r.Headers['X-Frame-Options'])'" }
    if ($r.Headers['Referrer-Policy'] -ne 'no-referrer')       { throw "referrer='$($r.Headers['Referrer-Policy'])'" }
    'nosniff + DENY + no-referrer'
  }
  Check 'FN-06 headers on static asset' {
    $r = Req -Method GET -Path '/assets/web.css'
    if ($r.Headers['X-Content-Type-Options'] -ne 'nosniff') { throw 'nosniff missing on static asset' }
    'nosniff present'
  }
  Check 'FN-07/FN-06 error responses use fixed payload' {
    try { $null = Req -Method GET -Path '/api/does-not-exist' } catch {
      $resp = $_.Exception.Response
      if ([int]$resp.StatusCode -ne 404) { throw "status $([int]$resp.StatusCode)" }
      if ($resp.Headers['X-Content-Type-Options'] -ne 'nosniff') { throw 'security headers missing on error path' }
      return '404 + nosniff on error path'
    }
    throw 'expected 404'
  }

  Write-Host "`n=== Read APIs ===" -ForegroundColor Cyan
  Check '/api/status idle' {
    $s = (Req -Method GET -Path '/api/status').Content | ConvertFrom-Json
    if ($s.running -ne $false) { throw "running=$($s.running)" }
    if ($s.serverPort -ne 8080) { throw "port=$($s.serverPort)" }
    "running=False port=$($s.serverPort)"
  }
  Check '/api/adapters' {
    $a = @((Req -Method GET -Path '/api/adapters').Content | ConvertFrom-Json)
    if ($a.Count -lt 1) { throw "count=$($a.Count)" }
    "count=$($a.Count), selected=$(@($a | Where-Object { $_.selected }).Count)"
  }
  Check '/api/history?page=1&pageSize=5' {
    $h = (Req -Method GET -Path '/api/history?page=1&pageSize=5').Content | ConvertFrom-Json
    if ($h.page -ne 1 -or $h.pageSize -ne 5) { throw "page=$($h.page) pageSize=$($h.pageSize)" }
    "total=$($h.total) records=$(@($h.records).Count)"
  }
  Check 'F-21 /api/server lanError sanitised' {
    $s = (Req -Method GET -Path '/api/server').Content | ConvertFrom-Json
    # 中文会被 PS 5.1 按 ANSI 误读，因此只用 ASCII 模式断言。
    if ($s.lanError -ne '' -and $s.lanError -match 'netsh|\\\\|C:|Exception|user=') { throw "leaks: $($s.lanError)" }
    "lanError length=$($s.lanError.Length) port=$($s.port)"
  }
  Check 'lanAccess/bindings exposed' {
    $s = (Req -Method GET -Path '/api/server').Content | ConvertFrom-Json
    "lanAccess=$($s.lanAccess) lanReady=$($s.lanReady) bindings=$(@($s.bindings).Count)"
  }

  Write-Host "`n=== Input validation ===" -ForegroundColor Cyan
  Check 'malformed JSON -> 400' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{not json'
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'oversized body -> 400' {
    $c = StatusOf -Method POST -Path '/api/test/start' ('{"urls":["' + ('x' * 300000) + '"]}')
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'unknown route -> 404' {
    $c = StatusOf -Method GET -Path '/api/nope'
    if ($c -ne 404) { throw "got $c" }; '404'
  }
  Check 'F-05 loopback URL rejected' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download","urls":["http://127.0.0.1:9/x"]}'
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'F-05 cloud metadata URL rejected' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download","urls":["http://169.254.169.254/latest/meta-data/"]}'
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'F-05 non-http scheme rejected' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download","urls":["ftp://8.8.8.8/x"]}'
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'F-05 non-allowlisted port rejected' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download","urls":["http://8.8.8.8:8080/x"]}'
    if ($c -ne 400) { throw "got $c" }; '400'
  }
  Check 'profile save rejects private URL' {
    $c = StatusOf -Method POST -Path '/api/profiles' '{"name":"nst-smoke","downloadUrls":["http://10.0.0.1/x"]}'
    if ($c -ne 400) { throw "got $c" }; '400'
  }

  Write-Host "`n=== Test lifecycle (FN-01 / F-09 / F-12) ===" -ForegroundColor Cyan
  Check 'POST /api/test/start -> 200 only once truly running' {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download"}'
    $sw.Stop()
    if ($c -ne 200) { throw "got $c" }
    # FN-01: 200 must not be sent before the test is actually in the running state.
    $s = (Req -Method GET -Path '/api/status').Content | ConvertFrom-Json
    if ($s.running -ne $true) { throw "200 returned but running=$($s.running)" }
    "200 after $($sw.ElapsedMilliseconds)ms with running=True confirmed"
  }
  Check 'second start while running -> 409' {
    $c = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download"}'
    if ($c -ne 409) { throw "got $c" }; '409 (already testing)'
  }
  Check 'POST /api/test/stop -> 200' {
    $c = StatusOf -Method POST -Path '/api/test/stop'
    if ($c -ne 200) { throw "got $c" }; '200'
  }
  Check 'running=False after stop' {
    $ok = $false
    for ($i = 0; $i -lt 60; $i++) {
      Start-Sleep -Milliseconds 250
      $s = (Req -Method GET -Path '/api/status').Content | ConvertFrom-Json
      if ($s.running -eq $false) { $ok = $true; break }
    }
    if (-not $ok) { throw 'running stayed True after stop' }
    "stopped after ~$([int](($i+1)*0.25*1000))ms"
  }
  Check 'server stays responsive during lifecycle' {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Req -Method GET -Path '/api/status'
    $sw.Stop()
    if ($sw.ElapsedMilliseconds -gt 2000) { throw "status took $($sw.ElapsedMilliseconds)ms (> UI 2s timeout)" }
    "status answered in $($sw.ElapsedMilliseconds)ms"
  }

  Write-Host "`n=== Rapid start/stop x20 (plan step 7) ===" -ForegroundColor Cyan
  Check '20x start/stop, UI never blocks' {
    $worst = 0
    for ($n = 1; $n -le 20; $n++) {
      $sw = [System.Diagnostics.Stopwatch]::StartNew()
      $c1 = StatusOf -Method POST -Path '/api/test/start' '{"mode":"download"}'
      $sw.Stop()
      if ($c1 -ne 200 -and $c1 -ne 409) { throw "cycle $n start -> $c1" }
      if ($sw.ElapsedMilliseconds -gt $worst) { $worst = $sw.ElapsedMilliseconds }
      $c2 = StatusOf -Method POST -Path '/api/test/stop'
      if ($c2 -ne 200) { throw "cycle $n stop -> $c2" }
      $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
      $null = Req -Method GET -Path '/api/status'
      $sw2.Stop()
      if ($sw2.ElapsedMilliseconds -gt 2000) { throw "cycle $n status took $($sw2.ElapsedMilliseconds)ms" }
    }
    "20 cycles OK; worst start-response latency ${worst}ms"
  }

  Write-Host "`n=== Settings (F-10) ===" -ForegroundColor Cyan
  Check 'settings POST round-trips' {
    $c = StatusOf -Method POST -Path '/api/settings' '{"threadCount":96,"testTimeoutSec":45}'
    if ($c -ne 200) { throw "got $c" }
    $s = (Req -Method GET -Path '/api/settings').Content | ConvertFrom-Json
    if ($s.threadCount -ne 96) { throw "threadCount=$($s.threadCount)" }
    if ($s.testTimeoutSec -ne 45) { throw "testTimeoutSec=$($s.testTimeoutSec)" }
    'threadCount=96 testTimeoutSec=45'
  }
  Check 'settings persisted atomically to disk' {
    $p = Join-Path $cfgDir 'appsettings.json'
    if (-not (Test-Path $p)) { throw 'appsettings.json missing' }
    $json = Get-Content $p -Raw
    $null = $json | ConvertFrom-Json     # must be valid JSON
    if ($json -match '"ThreadCount"\s*:\s*96') { } else { throw 'threadCount 96 not persisted' }
    if (Get-ChildItem $cfgDir -Filter '*.tmp' -ErrorAction SilentlyContinue) { throw 'temp file left behind' }
    'valid JSON, no .tmp leftovers'
  }
  Check '8 concurrent settings POSTs all succeed' {
    $bodies = 1..8 | ForEach-Object { '{"threadCount":' + (32 + $_ * 8) + '}' }
    $codes = Post-Concurrent "$base/api/settings" $bodies
    $bad = @($codes | Where-Object { $_ -ne 200 })
    if ($bad.Count -gt 0) { throw "codes: $($codes -join ',')" }
    $s = (Req -Method GET -Path '/api/settings').Content | ConvertFrom-Json
    if ($s.threadCount -lt 2 -or $s.threadCount -gt 1024) { throw "implausible threadCount=$($s.threadCount)" }
    "8/8 -> 200; final threadCount=$($s.threadCount)"
  }

  Write-Host "`n=== Availability under load (F-09 / F-12) ===" -ForegroundColor Cyan
  Check 'status stays available during concurrent polling' {
    $urls = 1..6 | ForEach-Object { "$base/api/status" }
    $hs = @(); $ps = @()
    foreach ($u in $urls) {
      $p = [powershell]::Create(); $p.RunspacePool = $pool
      $null = $p.AddScript({
        param($u)
        $codes = @()
        for ($i = 0; $i -lt 15; $i++) {
          try {
            $r = [System.Net.WebRequest]::Create($u); $r.Timeout = 10000
            $resp = $r.GetResponse(); $codes += [int]$resp.StatusCode; $resp.Close()
          } catch [System.Net.WebException] {
            if ($_.Exception.Response) { $codes += [int]$_.Exception.Response.StatusCode } else { $codes += -1 }
          } catch { $codes += -1 }
        }
        return ($codes -join ',')
      }).AddArgument($u)
      $hs += $p; $ps += $p.BeginInvoke()
    }
    $all = @()
    for ($i = 0; $i -lt $ps.Count; $i++) { $all += , @($hs[$i].EndInvoke($ps[$i]))[0]; $hs[$i].Dispose() }
    $flat = ($all -join ',') -split ','
    $bad = @($flat | Where-Object { $_ -ne '200' })
    if ($bad.Count -gt 0) { throw "non-200 responses: $($bad -join ',')" }
    "90 requests, all 200"
  }

  Write-Host "`n=== Runtime errors ===" -ForegroundColor Cyan
  Check 'no FATAL entries in debug.log' {
    if (-not (Test-Path $log)) { throw 'debug.log missing (expected with --debug)' }
    $fatal = @(Select-String -Path $log -Pattern 'FATAL' -ErrorAction SilentlyContinue)
    if ($fatal.Count -gt 0) { throw "$($fatal.Count) FATAL entries: $($fatal[0].Line)" }
    "0 FATAL entries in $((Get-Content $log | Measure-Object -Line).Lines) log lines"
  }
  Check 'no unobserved-task or unhandled-exception markers' {
    $hits = @(Select-String -Path $log -Pattern 'Unhandled|Unobserved|Collection was modified' -ErrorAction SilentlyContinue)
    if ($hits.Count -gt 0) { throw "$($hits.Count) hits: $($hits[0].Line)" }
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
  try { $pool.Close(); $pool.Dispose() } catch { }

  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $results | Format-Table -AutoSize
  $failed = @($results | Where-Object Result -eq 'FAIL')
  Write-Host ("Total={0} Pass={1} Fail={2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
  if ($failed.Count -gt 0) { Write-Host "FAILED:" -ForegroundColor Red; $failed | ForEach-Object { Write-Host "  - $($_.Test): $($_.Detail)" -ForegroundColor Red } }
  exit $failed.Count
}
