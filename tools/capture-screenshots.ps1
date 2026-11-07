<#
.SYNOPSIS
    生成 README / 官网使用的实机截图。

.DESCRIPTION
    启动 NetSpeedTest 的 Release 构建，把窗口固定到统一尺寸后逐页截图：
      assets/screenshot.png              主界面（测速页）
      assets/screenshots/settings.png    设置页（可用 -SkipSettings 跳过）
    只结束本脚本自己启动的进程，不会影响用户已打开的实例。

    截图使用 PrintWindow(PW_RENDERFULLCONTENT)，不要求窗口位于前台；
    若 DWM 返回空白帧，自动回退到屏幕拷贝（需要窗口可见）。

    需要管理员权限（程序清单为 requireAdministrator，且启动时会自动接管理防火墙规则）。

.PARAMETER Exe
    要启动的可执行文件。默认使用 Release 构建产物；不存在时回退 Debug 构建。

.PARAMETER Width
    窗口宽度（像素），默认 0 表示保持程序自身尺寸（1280×720）。与 Height 同时大于 0 时才调整。

.PARAMETER Height
    窗口高度（像素），默认 0 表示保持程序自身尺寸。与 Width 同时大于 0 时才调整。

.PARAMETER SkipSettings
    只截主界面，不切换到设置页。

.PARAMETER FactoryDefaults
    截图时临时隐藏用户的参数覆盖层（%LOCALAPPDATA%\NetSpeedTest\appsettings.json），
    让设置页展示出厂默认值；脚本结束（含异常）时自动还原。

.EXAMPLE
    pwsh -File tools\capture-screenshots.ps1

.EXAMPLE
    pwsh -File tools\capture-screenshots.ps1 -Width 1920 -Height 1200

.EXAMPLE
    pwsh -File tools\capture-screenshots.ps1 -FactoryDefaults
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [int]$Width = 0,
    [int]$Height = 0,
    [switch]$SkipSettings,
    [switch]$FactoryDefaults
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $repo 'assets'
$shotDir = Join-Path $assets 'screenshots'
New-Item -ItemType Directory -Path $shotDir -Force | Out-Null

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '需要管理员权限：请以管理员身份运行 pwsh，再执行本脚本。'
    }
}

function Resolve-Exe {
    param([string]$Path)
    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) { throw "找不到可执行文件：$Path" }
        return (Resolve-Path -LiteralPath $Path).Path
    }

    $candidates = @(
        (Join-Path $repo 'NetSpeedTest\bin\Release\net8.0-windows\NetSpeedTest.exe'),
        (Join-Path $repo 'NetSpeedTest\bin\Debug\net8.0-windows\NetSpeedTest.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '找不到 NetSpeedTest.exe，请先执行：dotnet build NetSpeedTest\NetSpeedTest.csproj -c Release'
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NstShotWin32
{
    public const int SW_RESTORE = 9;
    public const uint PW_RENDERFULLCONTENT = 2;
    public const uint SWP_NOZORDER = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

Add-Type -AssemblyName System.Drawing

function Save-WindowCapture {
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [Parameter(Mandatory)][string]$Path
    )

    $rect = New-Object NstShotWin32+RECT
    if (-not [NstShotWin32]::GetWindowRect($Handle, [ref]$rect)) { throw 'GetWindowRect 失败' }
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { throw "窗口尺寸无效：${w}x${h}" }

    $useScreenCopy = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $bitmap = New-Object System.Drawing.Bitmap($w, $h)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $hdc = $graphics.GetHdc()
        $ok = [NstShotWin32]::PrintWindow($Handle, $hdc, [NstShotWin32]::PW_RENDERFULLCONTENT)
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()

        if ($ok) {
            # 采样 9 个点判断是否整帧空白（PrintWindow 对部分 DWM 场景会返回纯色）
            $colors = New-Object System.Collections.Generic.HashSet[int]
            foreach ($x in 0.15, 0.5, 0.85) {
                foreach ($y in 0.15, 0.5, 0.85) {
                    [void]$colors.Add($bitmap.GetPixel([int]($w * $x), [int]($h * $y)).ToArgb())
                }
            }
            if ($colors.Count -ge 2) {
                $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
                $bitmap.Dispose()
                return
            }
        }
        $bitmap.Dispose()
        Start-Sleep -Milliseconds 400
    }

    $useScreenCopy = $true
    [void][NstShotWin32]::ShowWindow($Handle, [NstShotWin32]::SW_RESTORE)
    [void][NstShotWin32]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 800

    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Find-ElementByName {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)][string]$Name
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

Assert-Admin
$exePath = Resolve-Exe -Path $Exe
Write-Host "启动：$exePath" -ForegroundColor Cyan

# 可选：临时隐藏用户参数覆盖层，让设置页展示出厂默认值（结束后还原）。
$overridePath = Join-Path $env:LOCALAPPDATA 'NetSpeedTest\appsettings.json'
$overrideBackup = "$overridePath.capture-screenshots.bak"
$hiddenOverride = $false
if ($FactoryDefaults -and (Test-Path -LiteralPath $overridePath)) {
    if (Test-Path -LiteralPath $overrideBackup) { throw "存在残留备份，请先处理：$overrideBackup" }
    Move-Item -LiteralPath $overridePath -Destination $overrideBackup
    $hiddenOverride = $true
    Write-Host '已临时隐藏用户参数覆盖层（按出厂默认值截图），结束后自动恢复。' -ForegroundColor Yellow
}

$process = Start-Process -FilePath $exePath -PassThru
$handle = [IntPtr]::Zero
try {
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "程序提前退出，退出码 $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $handle = $process.MainWindowHandle
            break
        }
    }
    if ($handle -eq [IntPtr]::Zero) { throw '30 秒内没有拿到主窗口句柄' }

    [void][NstShotWin32]::ShowWindow($handle, [NstShotWin32]::SW_RESTORE)
    if ($Width -gt 0 -and $Height -gt 0) {
        # WPF 可能在 Loaded 后回写自身尺寸，重试几次直到 GetWindowRect 生效。
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            [void][NstShotWin32]::SetWindowPos($handle, [IntPtr]::Zero, 40, 40, $Width, $Height, [NstShotWin32]::SWP_NOZORDER)
            Start-Sleep -Milliseconds 400
            $rect = New-Object NstShotWin32+RECT
            [void][NstShotWin32]::GetWindowRect($handle, [ref]$rect)
            if (($rect.Right - $rect.Left) -eq $Width -and ($rect.Bottom - $rect.Top) -eq $Height) { break }
        }
    }
    Start-Sleep -Milliseconds 500

    $mainPath = Join-Path $assets 'screenshot.png'
    Save-WindowCapture -Handle $handle -Path $mainPath
    Write-Host ("  已保存 {0}（{1:N0} 字节）" -f $mainPath, (Get-Item $mainPath).Length) -ForegroundColor Green

    if (-not $SkipSettings) {
        try {
            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            $settings = Find-ElementByName -Root $root -Name '设置'
            if ($settings -eq $null) { throw '未找到「设置」导航按钮' }
            $invoke = $settings.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $invoke.Invoke()
            Start-Sleep -Milliseconds 1500

            $settingsPath = Join-Path $shotDir 'settings.png'
            Save-WindowCapture -Handle $handle -Path $settingsPath
            Write-Host ("  已保存 {0}（{1:N0} 字节）" -f $settingsPath, (Get-Item $settingsPath).Length) -ForegroundColor Green
        }
        catch {
            Write-Warning "设置页截图失败：$($_.Exception.Message)"
        }
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if ($hiddenOverride) {
        # 运行期不应写入覆盖层；若出现则以备份为准还原。
        Remove-Item -LiteralPath $overridePath -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $overrideBackup -Destination $overridePath
        Write-Host '已恢复用户参数覆盖层。' -ForegroundColor Cyan
    }
    Write-Host '已关闭本次启动的实例。' -ForegroundColor Cyan
}
