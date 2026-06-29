#Requires -Version 5
<#
    Installs the DesktopBridge package and immediately starts the system-tray helper, so the tray
    icon appears right away - without waiting for the next sign-in and without opening the main app.

    Run this instead of Add-AppDevPackage.ps1:
        Right-click -> Run with PowerShell   (it elevates as needed)

    Afterwards the package's startup task starts the tray automatically on every later sign-in.
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Identity Name from Package.appxmanifest.
$identityName = '16d8fb9e-dfee-4bd6-9bc2-f6b775863920'

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

# 3) Launch the tray helper now (via its AppExecutionAlias) so the icon shows immediately.
if ($pkg) {
    try {
        Start-Process 'DesktopBridgeTray.exe'
        Write-Host 'DesktopBridge installed; tray helper started.'
    } catch {
        Write-Warning "Installed, but could not start the tray helper now: $($_.Exception.Message)"
        Write-Warning 'It will start automatically at your next sign-in.'
    }
} else {
    Write-Warning 'Package not detected after install; the tray will appear at your next sign-in.'
}
