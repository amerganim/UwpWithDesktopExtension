#Requires -Version 5
<#
    Removes the per-user logon autostart and uninstalls the DesktopBridge package.
    Run with PowerShell.
#>

$ErrorActionPreference = 'SilentlyContinue'
$identityName = '16d8fb9e-dfee-4bd6-9bc2-f6b775863920'
$runName      = 'DesktopBridgeTrayHelper'

# Stop the running tray helper, if any.
Get-Process -Name 'TrayHelper' -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove the logon autostart entry.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $runName -ErrorAction SilentlyContinue

# Remove the Task Scheduler logon task the service registers (must match LogonTaskName in
# SmartThings.Service/TrayService.cs). Otherwise it lingers and tries to start a removed service.
schtasks.exe /Delete /TN 'DesktopBridge Tray Logon' /F 2>$null

# Uninstall the package.
Get-AppxPackage -Name $identityName | Remove-AppxPackage

Write-Host 'DesktopBridge uninstalled and autostart removed.'
