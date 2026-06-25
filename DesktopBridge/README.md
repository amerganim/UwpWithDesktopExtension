# DesktopBridge solution

UWP app + full-trust WPF extension + a packaged LocalSystem Windows service.

## Projects

- **DesktopBridge** — UWP app. Hosts the `SampleInteropService` app service and launches the
  full-trust process via `FullTrustProcessLauncher`.
- **WPF** — .NET 8 WPF full-trust process. Creates the tray icon and opens the bidirectional
  `AppServiceConnection` back to the UWP app. Runs with package identity.
- **TrayLauncherService** — .NET 8 Windows service. Installed by the package, runs as
  **LocalSystem**, **auto-start**. On service start and on each user logon / unlock / console
  connect it activates the packaged app in the interactive user session.
- **WAPP** — MSIX packaging project (`.wapproj`). Bundles the three projects and declares the
  Windows service via the `windows.service` manifest extension.

## How the service brings up the tray + IPC

A LocalSystem service runs in **session 0** and cannot show UI, and any process it launches
directly has **no MSIX package identity** — but `AppServiceConnection` requires package identity.
To get identity **and** show only the tray (not the UWP UI), WPF is also declared as a hidden
full-trust app entry (`Application Id="TrayApp"`, `AppListEntry="none"`) with an
**AppExecutionAlias** (`DesktopBridgeTray.exe`). The service launches that alias:

1. `TrayLauncherService` gets the active console session token
   (`WTSGetActiveConsoleSessionId` → `WTSQueryUserToken` → `DuplicateTokenEx`).
2. It resolves the per-user alias path
   `%LOCALAPPDATA%\Microsoft\WindowsApps\DesktopBridgeTray.exe` (`SHGetKnownFolderPath` with the
   user token) and starts it with `CreateProcessAsUser`. Launching the alias gives the process
   **full package identity**, and only the **tray icon** appears (no UWP UI).
3. WPF opens the `AppServiceConnection` to `SampleInteropService` → the UWP background host
   activates headlessly → **UWP ↔ WPF IPC works**.
4. When the user later launches the **UWP app** from Start, its `MainPage` calls
   `FullTrustProcessLauncher`, which starts a second WPF instance; the single-instance guard sees
   the tray instance already running and sends it `WM_SHOWME`, so the **WPF window is shown**.

`TRAYLAUNCHER_ALIAS` (full path or bare exe name) can override the alias path for manual testing.

> The service launches **WPF** (same package) directly via its alias — it never activates the UWP
> UI, so installation/logon brings up only the tray icon.

## Packaged service manifest wiring

In [`WAPP/Package.appxmanifest`](WAPP/Package.appxmanifest):

- `xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"` namespace.
- A `windows.service` extension under the **`<Application>`** `<Extensions>` (the packaging
  validator requires it there, not at package level):
  ```xml
  <desktop6:Extension Category="windows.service"
      Executable="TrayLauncherService\TrayLauncherService.exe"
      EntryPoint="Windows.FullTrustApplication">
    <desktop6:Service Name="TrayLauncherService" StartupType="auto" StartAccount="localSystem" />
  </desktop6:Extension>
  ```
- `<rescap:Capability Name="packagedServices" />` and `<rescap:Capability Name="localSystemServices" />`
  (required to install a packaged service and run it as LocalSystem).
- `<rescap:Capability Name="runFullTrust" />` (existing, for the WPF full-trust process).
- `Windows.Desktop` min version raised to **10.0.19041** (packaged services need Windows 10 2004+).
- A second hidden full-trust `Application Id="TrayApp"` (`Executable="WPF\WPF.exe"`,
  `AppListEntry="none"`) with a `uap5:AppExecutionAlias` (`DesktopBridgeTray.exe`) — this is what the
  service launches so WPF starts with identity, tray-only.

The service is installed when the package is installed and removed on uninstall.

## Building

### What builds with the .NET 8 SDK (no Visual Studio)

```sh
dotnet build WPF/WPF.csproj -c Release
dotnet build TrayLauncherService/TrayLauncherService.csproj -c Release
```

Both compile cleanly with the .NET 8 SDK on this machine.

### What requires Visual Studio

`DesktopBridge` (UWP) and `WAPP` (MSIX packaging) need **Visual Studio 2022/2026** with:

- **Universal Windows Platform development** workload (UWP build targets).
- **.NET / MSIX Packaging Tools** (the `.wapproj` DesktopBridge packaging targets).
- Windows SDK **10.0.26100.0** (already installed here; projects were retargeted from the missing
  `10.0.22621.0` to `10.0.26100.0`).

Open `DesktopBridge.sln` in Visual Studio, set **WAPP** as the startup project, choose an `x64`
configuration, and Build / Deploy to produce and install the MSIX (with the service).

> Note: these two project types cannot be built by `dotnet build` / the standalone MSBuild Build
> Tools — the UWP `WindowsXaml` and the `DesktopBridge` packaging MSBuild targets ship only with the
> Visual Studio workloads above.

## Manually testing the service (without packaging)

From an **elevated** prompt (LocalSystem cross-session launch requires admin):

```powershell
# Build
dotnet publish TrayLauncherService\TrayLauncherService.csproj -c Release -r win-x64 --self-contained false -o C:\Temp\TrayLauncherService

# Tell it which app to activate (full PFN!App, or just the package family name)
setx TRAYLAUNCHER_AUMID "<PackageFamilyName>!App" /M

# Run the one-shot launch logic in the foreground
C:\Temp\TrayLauncherService\TrayLauncherService.exe --console
```

Get the package family name with:

```powershell
Get-AppxPackage *WAPP* | Select-Object Name, PackageFamilyName
```

Logs are written to `C:\ProgramData\DesktopBridge\TrayLauncherService.log`.
