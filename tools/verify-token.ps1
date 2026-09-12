# Plan step 6: token enforcement over real HTTP from a non-loopback source.
# Requests are sent to the machine's LAN IP, so RemoteEndPoint is 192.168.x.x
# and IsLoopbackRequest() is false -> the X-NST-Token check must apply.
$ErrorActionPreference = 'Continue'
$repo   = 'D:\Program Files\DSH\NetSpeedTest'
$exe    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$cfgDir = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
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

# Returns @{ Code = <int>; Body = <string>; Headers = <webheadercollection> }
function Call {
  param([string]$Url, [string]$Method = 'GET', $Body = $null, [string]$Token = $null, [int]$Timeout = 20)
  $req = [System.Net.HttpWebRequest]::Create($Url)
  $req.Method = $Method
  $req.Timeout = $Timeout * 1000
  $req.AllowAutoRedirect = $false
  if ($Token) { $req.Headers.Add('X-NST-Token', $Token) }
  # 写请求一律带最小 JSON body：HttpWebRequest 无法发送 Content-Length: 0，
  # 空 body 会被 HTTP.sys 以 411 挡下，测不到应用层的令牌校验。
  $effectiveBody = $Body
  if (($Method -eq 'POST' -or $Method -eq 'PUT' -or $Method -eq 'PATCH') -and [string]::IsNullOrEmpty($effectiveBody)) {
    $effectiveBody = '{}'
  }
  if ($null -ne $effectiveBody) {
    $req.ContentType = 'application/json'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($effectiveBody)
    $req.ContentLength = $bytes.Length
    $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
  }
  try {
    $resp = $req.GetResponse()
    $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd()
    $code = [int]$resp.StatusCode
    $headers = $resp.Headers
    $resp.Close()
    return @{ Code = $code; Body = $text; Headers = $headers }
  } catch [System.Net.WebException] {
    if ($_.Exception.Response) {
      $r = $_.Exception.Response
      $text = ''
      try { $text = (New-Object System.IO.StreamReader($r.GetResponseStream())).ReadToEnd() } catch { }
      $code = [int]$r.StatusCode
      $headers = $r.Headers
      $r.Close()
      return @{ Code = $code; Body = $text; Headers = $headers }
    }
    return @{ Code = -1; Body = $_.Exception.Message; Headers = $null }
  }
}

Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Set-Content -Path (Join-Path $cfgDir 'web.json') -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

$app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
try {
  $loop = 'http://127.0.0.1:8080'
  for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; try { $null = Call "$loop/api/status"; break } catch { } }
  Start-Sleep -Seconds 3

  $srv = (Call "$loop/api/server").Body | ConvertFrom-Json
  $lan = @($srv.bindings)[0].ip
  if (-not $lan) { throw 'no LAN binding published' }
  $base = "http://${lan}:8080"
  $token = [regex]::Match((Call "$loop/").Body, 'name="nst-token" content="([0-9A-F]{64})"').Groups[1].Value
  Write-Host "LAN base   : $base"
  Write-Host "loopback   : $loop"
  Write-Host "token      : $($token.Substring(0,8))... ($($token.Length) chars)"

  Write-Host "`n=== Plan step 6a: non-loopback write without token ===" -ForegroundColor Cyan
  Check 'LAN GET is allowed without token (read-only endpoint)' {
    $r = Call "$base/api/status"
    if ($r.Code -ne 200) { throw "got $($r.Code)" }
    '200'
  }
  Check 'LAN POST without token -> 403' {
    $r = Call "$base/api/test/start" 'POST' '{"mode":"download"}'
    if ($r.Code -ne 403) { throw "got $($r.Code): $($r.Body)" }
    if ($r.Body -notmatch 'Forbidden') { throw "unexpected body: $($r.Body)" }
    '403 Forbidden'
  }
  Check 'LAN POST with wrong token -> 403' {
    $r = Call "$base/api/test/start" 'POST' '{"mode":"download"}' 'deadbeef'
    if ($r.Code -ne 403) { throw "got $($r.Code)" }
    '403'
  }
  Check 'LAN POST with lowercased token -> 403 (case sensitive)' {
    $r = Call "$base/api/test/start" 'POST' '{"mode":"download"}' $token.ToLowerInvariant()
    if ($r.Code -ne 403) { throw "got $($r.Code)" }
    '403'
  }
  Check 'LAN POST with valid token passes the token gate' {
    $r = Call "$base/api/test/stop" 'POST' $null $token
    if ($r.Code -eq 403) { throw "token was rejected: $($r.Body)" }
    if ($r.Code -ne 200) { throw "expected 200, got $($r.Code): $($r.Body)" }
    '200 (token accepted)'
  }
  Check 'LAN POST valid token can actually start a test' {
    $r = Call "$base/api/test/start" 'POST' '{"mode":"download"}' $token 60
    if ($r.Code -ne 200) { throw "got $($r.Code): $($r.Body)" }
    $null = Call "$base/api/test/stop" 'POST' $null $token
    '200 + started'
  }
  Check 'LAN DELETE with valid token passes the token gate' {
    $r = Call "$base/api/history?id=999999" 'DELETE' $null $token
    if ($r.Code -eq 403) { throw "token was rejected: $($r.Body)" }
    "not 403 (got $($r.Code) - reached the handler)"
  }
  Check 'LAN DELETE without token -> 403' {
    $r = Call "$base/api/history?id=1" 'DELETE'
    if ($r.Code -ne 403) { throw "got $($r.Code)" }
    '403'
  }
  Check 'LAN settings POST without token -> 403 (no config tampering)' {
    $r = Call "$base/api/settings" 'POST' '{"threadCount":1024}'
    if ($r.Code -ne 403) { throw "got $($r.Code)" }
    '403'
  }
  Check '403 body carries security headers and no internals' {
    $r = Call "$base/api/test/start" 'POST' '{"mode":"download"}'
    if ($r.Headers['X-Content-Type-Options'] -ne 'nosniff') { throw 'nosniff missing' }
    if ($r.Body -match 'netsh|C:\\\\|Exception|at NetSpeedTest') { throw "leaks internals: $($r.Body)" }
    "headers ok, body=$($r.Body)"
  }

  Write-Host "`n=== Plan step 6b: loopback stays exempt ===" -ForegroundColor Cyan
  Check 'loopback POST without token still allowed (web UI unaffected)' {
    $r = Call "$loop/api/settings" 'POST' '{"threadCount":64}'
    if ($r.Code -ne 200) { throw "got $($r.Code)" }
    '200'
  }

  Write-Host "`n=== Plan step 6c: cross-origin behaviour ===" -ForegroundColor Cyan
  Check 'no CORS headers are emitted (cross-origin reads blocked by browser)' {
    $req = [System.Net.HttpWebRequest]::Create("$base/api/status")
    $req.Headers.Add('Origin', 'http://evil.example')
    $resp = $req.GetResponse()
    $acao = $resp.Headers['Access-Control-Allow-Origin']
    $resp.Close()
    if ($acao) { throw "Access-Control-Allow-Origin is set to '$acao'" }
    'no Access-Control-Allow-Origin -> cross-origin fetch is blocked'
  }
  Check 'preflight OPTIONS is not answered with CORS permission' {
    $r = Call "$base/api/settings" 'OPTIONS'
    if ($r.Code -eq 200 -and $r.Headers['Access-Control-Allow-Origin']) { throw 'CORS preflight allowed' }
    "OPTIONS -> $($r.Code), no CORS grant"
  }
}
finally {
  Write-Host "`n=== Teardown ===" -ForegroundColor Cyan
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $results | Format-Table -AutoSize
  $failed = @($results | Where-Object Result -eq 'FAIL')
  Write-Host ("Total={0} Pass={1} Fail={2}" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
  exit $failed.Count
}
