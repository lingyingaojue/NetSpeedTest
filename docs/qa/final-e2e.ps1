# 最终修复端到端补充验证（批4 CLAMP 交叉400 / 批6 防火墙规则 / D10 取消落库带字节）
# 仅启动 Release exe、发请求、取证、停进程；不改数据库、不改配置（清理与还原在脚本外单独、可审查地执行）。
# 必须在管理员会话中运行（D8① 后 exe 清单为 requireAdministrator）。
$ErrorActionPreference = 'Stop'
$repo   = 'D:\Program Files\DSH\NetSpeedTest'
$exe    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$cfgDir = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
$base   = 'http://127.0.0.1:8080'

function Req($Method, $Path, $Body = $null) {
    $p = @{ Uri = "$base$Path"; Method = $Method; TimeoutSec = 15; UseBasicParsing = $true }
    if ($null -ne $Body) { $p.Body = $Body; $p.ContentType = 'application/json' }
    Invoke-WebRequest @p
}
function Status-Of($Method, $Path, $Body) {
    try { $r = Req $Method $Path $Body; return @{ code = [int]$r.StatusCode; body = $r.Content } }
    catch {
        $code = 0
        try { $code = [int]$_.Exception.Response.StatusCode } catch {}
        $msg = $_.ErrorDetails.Message
        if (-not $msg) { $msg = $_.Exception.Message }
        return @{ code = $code; body = $msg }
    }
}

$app = $null
$pass = 0; $fail = 0
function A($cond, $name) {
    if ($cond) { Write-Host "PASS  $name"; $script:pass++ }
    else { Write-Host "FAIL  $name"; $script:fail++ }
}

try {
    Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Set-Content -Path (Join-Path $cfgDir 'web.json') -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

    $app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        try { $null = Req GET '/api/status'; $ready = $true; break } catch {}
    }
    if (-not $ready) { throw 'server not ready within 30s' }
    Start-Sleep -Seconds 3
    Write-Host "server ready pid=$($app.Id)"

    Write-Host "`n=== BATCH4 cross-field validation ==="
    $before = (Req GET '/api/settings').Content | ConvertFrom-Json
    Write-Host "before: testTimeoutSec=$($before.testTimeoutSec) averageDelaySec=$($before.averageDelaySec)"
    $r1 = Status-Of POST '/api/settings' '{"testTimeoutSec":10,"averageDelaySec":30}'
    Write-Host "cross-invalid -> code=$($r1.code) body=$($r1.body)"
    $after = (Req GET '/api/settings').Content | ConvertFrom-Json
    Write-Host "after-invalid: testTimeoutSec=$($after.testTimeoutSec) averageDelaySec=$($after.averageDelaySec)"
    $r2 = Status-Of POST '/api/settings' '{"testTimeoutSec":60,"averageDelaySec":10}'
    Write-Host "valid -> code=$($r2.code)"
    $valid = (Req GET '/api/settings').Content | ConvertFrom-Json
    Write-Host "after-valid: testTimeoutSec=$($valid.testTimeoutSec) averageDelaySec=$($valid.averageDelaySec)"

    Write-Host "`n=== D10 cancelled download persists byte counters ==="
    $s0 = Status-Of POST '/api/test/start' '{"mode":"download"}'
    Write-Host "start -> $($s0.code)"
    Start-Sleep -Seconds 5
    $st = Status-Of POST '/api/test/stop' '{}'
    Write-Host "stop -> $($st.code)"
    Start-Sleep -Seconds 3
    $h = (Req GET '/api/history?page=1&pageSize=1').Content | ConvertFrom-Json
    $latest = $h.records[0]
    Write-Host ("latest: Id={0} TestType={1} Duration={2:N2}s BytesDownloaded={3} TotalBytes={4} PeakMbps={5:N2}" -f `
        $latest.Id, $latest.TestType, $latest.DurationSeconds, $latest.BytesDownloaded, $latest.TotalBytes, $latest.PeakMbps)

    Write-Host "`n=== BATCH6 firewall rule ==="
    $fw = & netsh.exe advfirewall firewall show rule name='NetSpeedTest Web Server' 2>&1 | Out-String
    Write-Host $fw

    Write-Host "`n=== ASSERTIONS ==="
    A ($r1.code -eq 400) 'cross-invalid testTimeoutSec<averageDelaySec returns 400'
    A ($r1.body -match 'testTimeoutSec') '400 body explains the rejected cross-field pair'
    A ($after.testTimeoutSec -eq $before.testTimeoutSec) 'timeout rolled back (cross-invalid not applied)'
    A ($after.averageDelaySec -eq $before.averageDelaySec) 'averageDelay rolled back (cross-invalid not applied)'
    A ($r2.code -eq 200) 'valid settings still return 200'
    A ($valid.testTimeoutSec -eq 60) 'valid timeout persisted'
    A ([long]$latest.BytesDownloaded -gt 0) 'cancelled download record has BytesDownloaded>0 (D10)'
    A ([long]$latest.TotalBytes -gt 0) 'cancelled record has TotalBytes>0'
    A ($fw -match [regex]::Escape('NetSpeedTest Web Server')) 'firewall rule exists by name'
    A ($fw -match '(?<!\d)8080(?!\d)') 'firewall rule localport is 8080'
    A ($fw -match 'Allow|允许') 'firewall rule action is allow'

    Write-Host "`nE2E Total Pass=$pass Fail=$fail"
    if ($fail -ne 0) { exit 1 }
}
finally {
    if ($app) { try { $app.Kill() } catch {} }
    Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
