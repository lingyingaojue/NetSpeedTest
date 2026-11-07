# NetSpeedTest real end-to-end speed test driver (ASCII console output).
# Runs a FULL download / upload / bidirectional test against real nodes via HTTP,
# polls the rate curve, waits for completion, then reads persisted history rows.
$ErrorActionPreference = 'Stop'
trap {
  Write-Host "`n*** TRAP: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Magenta
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  exit 99
}
$repo   = 'D:\Program Files\DSH\NetSpeedTest'
$exe    = Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'
$cfgDir = Join-Path $env:LOCALAPPDATA 'NetSpeedTest'
$base   = 'http://127.0.0.1:8080'
$outDir = Join-Path $repo 'qa-results\netspeedtest-coreqa'
$app    = $null

function Get-Json([string]$path) {
  return Invoke-RestMethod -Method GET -Uri ($base + $path) -TimeoutSec 15
}
function Post-Code([string]$path, [string]$json) {
  try {
    $r = Invoke-RestMethod -Method POST -Uri ($base + $path) -ContentType 'application/json' -Body $json -TimeoutSec 60
    return 200
  } catch [System.Net.WebException] {
    if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
    return -1
  } catch { return -1 }
}

try {
  Write-Host "`n=== Setup ===" -ForegroundColor Cyan
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Set-Content -Path (Join-Path $cfgDir 'web.json') `
    -Value '{"Enabled":true,"AllowLanAccess":true,"PortMode":"Auto","CustomPort":8080,"LastActualPort":8080}' -Encoding UTF8

  Write-Host "`n=== Launch ===" -ForegroundColor Cyan
  $app = Start-Process -FilePath $exe -ArgumentList '--debug' -PassThru -WorkingDirectory (Split-Path $exe)
  Write-Host "launched pid=$($app.Id)"
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    if ($app.HasExited) { throw "app exited early code=$($app.ExitCode)" }
    try { $null = Get-Json '/api/status'; $ready = $true; break } catch {}
  }
  if (-not $ready) { throw 'server not ready within 30s' }
  Start-Sleep -Seconds 3
  Write-Host 'server ready'

  # Shorten per-phase duration while keeping it above the 10s average window (UI-valid range 10..600).
  $sc = Post-Code '/api/settings' '{"testTimeoutSec":25}'
  Write-Host "settings testTimeoutSec=25 -> $sc"
  Start-Sleep -Seconds 1

  $initialTotal = (Get-Json '/api/history?page=1&pageSize=1').total
  Write-Host "history total before = $initialTotal"

  $summary = @()
  foreach ($mode in @('download','upload','full')) {
    Write-Host "`n=== TEST $mode ===" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $code = Post-Code '/api/test/start' ("{`"mode`":`"$mode`"}")
    $sw.Stop()
    Write-Host "start -> $code after $($sw.ElapsedMilliseconds)ms"
    if ($code -ne 200) { $summary += [pscustomobject]@{mode=$mode;startCode=$code}; continue }

    $samples = @()
    $deadline = (Get-Date).AddSeconds(180)
    do {
      Start-Sleep -Seconds 2
      $s = Get-Json '/api/status'
      $samples += $s
      if ($samples.Count % 3 -eq 1) {
        Write-Host ("  t={0,3}s run={1} dl={2} ul={3} avgDl={4} avgUl={5} threads={6} bytes={7} :: {8}" -f `
          [int]$s.elapsedSeconds, $s.running, $s.downloadMbps, $s.uploadMbps, $s.averageDownloadMbps, $s.averageUploadMbps, $s.activeThreads, $s.totalBytes, $s.status)
      }
    } while ($s.running -eq $true -and (Get-Date) -lt $deadline)
    Start-Sleep -Seconds 2
    $final = Get-Json '/api/status'

    $peakDl = ($samples | Measure-Object downloadMbps -Maximum).Maximum
    $peakUl = ($samples | Measure-Object uploadMbps -Maximum).Maximum
    $peakThreads = ($samples | Measure-Object activeThreads -Maximum).Maximum
    $maxBytes = ($samples | Measure-Object totalBytes -Maximum).Maximum
    $samples | ConvertTo-Json -Depth 6 | Out-File -FilePath (Join-Path $outDir ("realcurve-$mode.json")) -Encoding UTF8

    Write-Host ("PEAK dl={0} ul={1} avgDl={2} avgUl={3} avgTotal={4} peakThreads={5} maxBytes={6}" -f `
      $peakDl, $peakUl, $final.averageDownloadMbps, $final.averageUploadMbps, $final.averageTotalMbps, $peakThreads, $maxBytes) -ForegroundColor Yellow
    $summary += [pscustomobject]@{
      mode=$mode; startCode=$code; peakDl=[math]::Round($peakDl,3); peakUl=[math]::Round($peakUl,3);
      avgDl=$final.averageDownloadMbps; avgUl=$final.averageUploadMbps; avgTotal=$final.averageTotalMbps;
      peakThreads=$peakThreads; maxBytes=$maxBytes; recentDl=$final.recentResult.downloadMbps; recentUl=$final.recentResult.uploadMbps
    }
    Start-Sleep -Seconds 3
  }

  Write-Host "`n=== HISTORY (new rows) ===" -ForegroundColor Cyan
  Start-Sleep -Seconds 3
  $h = Get-Json '/api/history?page=1&pageSize=10'
  Write-Host "history total after = $($h.total)"
  $h | ConvertTo-Json -Depth 6 | Out-File -FilePath (Join-Path $outDir 'realhistory.json') -Encoding UTF8
  $h.records | Select-Object -First 6 | ForEach-Object {
    Write-Host ("  {0} type={1} dl={2} ul={3} peak={4} avgTotal={5} bytesDl={6} bytesUl={7} dur={8}s err='{9}'" -f `
      $_.timestamp, $_.testType, $_.downloadMbps, $_.uploadMbps, $_.peakMbps, $_.averageTotalMbps, $_.bytesDownloaded, $_.bytesUploaded, $_.durationSeconds, $_.errorMessage)
  }

  $summary | ConvertTo-Json -Depth 6 | Out-File -FilePath (Join-Path $outDir 'realsummary.json') -Encoding UTF8
  Write-Host "`n=== Summary ===" -ForegroundColor Cyan
  $summary | Format-Table -AutoSize
}
finally {
  Write-Host "`n=== Teardown ===" -ForegroundColor Cyan
  if ($app -and -not $app.HasExited) {
    try { $null = Invoke-RestMethod -Method POST -Uri "$base/api/test/stop" -TimeoutSec 5 } catch {}
    Start-Sleep -Milliseconds 500
    $null = $app.CloseMainWindow(); Start-Sleep -Seconds 2
    if (-not $app.HasExited) { $app.Kill() }
  }
  Get-Process NetSpeedTest -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Write-Host 'done'
}
