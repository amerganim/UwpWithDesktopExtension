#Requires -Version 5
<#
    Enables the DesktopBridge system-tray helper. Run this AFTER installing the package with
    Add-AppDevPackage.ps1. No administrator rights are needed.

    It registers a per-user logon autostart (HKCU Run) for the tray helper and starts it now, so the
    tray icon appears immediately and on every later sign-in - WITHOUT launching the main app. A
    packaged windows.startupTask can't do this because Windows blocks it until the app is launched
    once; a classic Run entry is not subject to that gate.

    Right-click -> Run with PowerShell  (or:  powershell -ExecutionPolicy Bypass -File .\EnableTray.ps1)
#>

$ErrorActionPreference = 'Stop'
try {
    $identityName = '16d8fb9e-dfee-4bd6-9bc2-f6b775863920'

    $pkg = Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue
    if (-not $pkg) {
        throw "DesktopBridge is not installed for this user. Run Add-AppDevPackage.ps1 first, then re-run this script."
    }
    Write-Host "Found package: $($pkg.PackageFullName)"

    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\DesktopBridgeTray.exe'
    $aumid = "$($pkg.PackageFamilyName)!TrayHelper"

    if (Test-Path $alias) {
        $runCommand = "`"$alias`""
        Write-Host "Using AppExecutionAlias: $alias"
    } else {
        # Fall back to activating the app entry by AUMID (works even if the alias wasn't created).
        $runCommand = "explorer.exe shell:AppsFolder\$aumid"
        Write-Warning "Alias not found; falling back to AUMID activation ($aumid)."
    }

    New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'DesktopBridgeTrayHelper' -Value $runCommand -PropertyType String -Force | Out-Null
    Write-Host "Registered logon autostart -> $runCommand"

    if (Test-Path $alias) {
        Start-Process $alias
    } else {
        Start-Process 'explorer.exe' "shell:AppsFolder\$aumid"
    }
    Write-Host ''
    Write-Host 'Done. The tray icon should appear now and on every sign-in (no app launch needed).' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Read-Host 'Press Enter to close'
}
