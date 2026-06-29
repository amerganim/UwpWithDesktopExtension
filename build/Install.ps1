#Requires -Version 5
<#
    Installs the DesktopBridge package, starts the system-tray helper immediately, and registers a
    per-user logon autostart so the tray appears after install AND on every later sign-in -
    WITHOUT ever launching the main app.

    Why a Run-key instead of just the manifest's windows.startupTask: a packaged startup task is
    blocked by Windows until the app has been launched at least once (by design). A classic per-user
    Run entry is not subject to that gate, so it works on a fresh install with no app launch.

    Run this instead of Add-AppDevPackage.ps1:
        Right-click -> Run with PowerShell   (it elevates as needed)
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Identity Name from Package.appxmanifest.
$identityName = '16d8fb9e-dfee-4bd6-9bc2-f6b775863920'
# Must match the TrayHelper AppExecutionAlias in the manifest.
$aliasPath = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\DesktopBridgeTray.exe'
$runName   = 'DesktopBridgeTrayHelper'

# 1) Install certificate + dependencies + app via the package's generated installer.
$generated = Join-Path $here 'Add-AppDevPackage.ps1'
if (-not (Test-Path $generated)) {
    throw "Add-AppDevPackage.ps1 not found next to this script ($generated)."
}
& $generated

# 2) Wait until the package is registered for this user (Add-AppDevPackage may still be elevating).
$pkg = $null
for ($i = 0; $i -lt 90; $i++) {
    $pkg = Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue
    if ($pkg) { break }
    Start-Sleep -Seconds 1
}
if (-not $pkg) {
    Write-Warning 'Package not detected after install; skipping autostart registration.'
    return
}

# 3) Register a per-user logon autostart for the tray helper (not gated by the startup task rule).
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runKey -Name $runName -Value "`"$aliasPath`"" -PropertyType String -Force | Out-Null
Write-Host "Registered logon autostart: $runName -> $aliasPath"

# 4) Start the tray helper now so the icon appears immediately.
try {
    if (Test-Path $aliasPath) { Start-Process $aliasPath } else { Start-Process 'DesktopBridgeTray.exe' }
    Write-Host 'DesktopBridge installed; tray helper started.'
} catch {
    Write-Warning "Installed, but could not start the tray helper now: $($_.Exception.Message)"
    Write-Warning 'It will start at your next sign-in.'
}
