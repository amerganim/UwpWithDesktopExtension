# DesktopBridge solution

UWP app + full-trust WPF extension (IPC) + a native C++ system-tray helper + a packaged
LocalSystem Windows service that launches the tray.

## Projects

- **DesktopBridge** — UWP app. Hosts the `SampleInteropService` app service and launches the
  full-trust WPF process via `FullTrustProcessLauncher`.
- **WPF** — .NET 8 full-trust process. Hosts the bidirectional `AppServiceConnection` (IPC) and the
  demo UI (registry read / calc / notification). Runs with package identity. **No tray icon.**
- **TrayHelper** — native **C++ Win32** app (`Shell_NotifyIcon`). Owns the **system tray icon**.
  Menu: **Open** (activate the UWP app) and **Exit** (close the UWP app + its WPF process and remove
  the icon). ~2–5 MB footprint.
- **TrayLauncherService** — .NET 8 **Windows service**, **LocalSystem**, **auto-start**. Installed
  with the package. Starts right after install and at every boot, and launches `TrayHelper` in the
  interactive user session (on start and on each user logon).
- **WAPP** — MSIX packaging project (`.wapproj`). Bundles the four projects.

## Behavior

- **Autostart via the service:** the package installs a `windows.service` (LocalSystem, auto-start).
  It starts **right after install** and at **every boot**. Because it runs in session 0, it
  launches `TrayHelper` into the active user session with `CreateProcessAsUser`, running the
  helper's `AppExecutionAlias` (`DesktopBridgeTray.exe`) so it starts **with package identity**. So
  the **tray icon appears after install and on every sign-in — without launching the main app**, and
  without the `windows.startupTask` "must be launched once" gate.
- **Open (tray):** `TrayHelper` activates the UWP app via `shell:AppsFolder\<PFN>!App`. The UWP app
  calls `FullTrustProcessLauncher`, which starts **WPF**; WPF opens the `AppServiceConnection`, so
  **UWP ↔ WPF IPC works**, and its window is shown.
- **Exit (tray):** `TrayHelper` enumerates and terminates the package's other processes (the UWP app
  and WPF) by package family name, then removes the icon and quits.

> **Microsoft Store note:** the `packagedServices` / `localSystemServices` capabilities are
> **restricted** — publishing to the Store requires Microsoft approval, and an auto-running service
> also affects the Store's "active devices" metrics (it runs independently of the user opening the
> app). This service design is best suited to **sideload / enterprise** distribution.

## Manifest wiring

In [`WAPP/Package.appxmanifest`](WAPP/Package.appxmanifest):

- The UWP `Application Id="App"` keeps the `windows.appService` (`SampleInteropService`) and
  `windows.fullTrustProcess` (`WPF\WPF.exe`) extensions, plus the service:
  ```xml
  <desktop6:Extension Category="windows.service"
      Executable="TrayLauncherService\TrayLauncherService.exe"
      EntryPoint="Windows.FullTrustApplication">
    <desktop6:Service Name="TrayLauncherService" StartupType="auto" StartAccount="localSystem" />
  </desktop6:Extension>
  ```
- A second hidden full-trust `Application Id="TrayHelper"` (`AppListEntry="none"`) with a
  `uap5:AppExecutionAlias` (`DesktopBridgeTray.exe`) — the service launches this alias.
- Capabilities: `internetClient`, `runFullTrust`, and (restricted) `packagedServices` +
  `localSystemServices`.

## Building

### Builds with the .NET 8 SDK (no Visual Studio)

```sh
dotnet build WPF/WPF.csproj -c Release
dotnet build TrayLauncherService/TrayLauncherService.csproj -c Release
```

The native **TrayHelper** (C++) builds with the installed VC++ Build Tools:

```sh
msbuild TrayHelper/TrayHelper.vcxproj -p:Configuration=Release -p:Platform=x64
```

### Requires Visual Studio

`DesktopBridge` (UWP) and `WAPP` (MSIX packaging) need **Visual Studio 2022/2026** with the
**Universal Windows Platform development** and **.NET / MSIX Packaging Tools** workloads
(plus the **Desktop development with C++** workload for `TrayHelper`), and Windows SDK
**10.0.26100.0**. Open `DesktopBridge.sln`, set **WAPP** as startup, choose `x64`, and Build /
Deploy to produce and install the MSIX. CI ([`.github/workflows/build-and-release.yml`](../.github/workflows/build-and-release.yml))
does this on a GitHub `windows-latest` runner.

## Manually testing the tray helper (without packaging)

`TrayHelper.exe` can be run directly, but **outside the package it has no identity**, so "Open"
(which resolves `GetCurrentPackageFamilyName`) won't find the UWP app. To test the tray UI itself,
run `x64\Release\TrayHelper.exe`; for the full Open/IPC flow, install the MSIX.
