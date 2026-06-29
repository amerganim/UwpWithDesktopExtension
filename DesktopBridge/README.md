# DesktopBridge solution

UWP app + full-trust WPF extension (IPC) + a native C++ system-tray helper.

## Projects

- **DesktopBridge** — UWP app. Hosts the `SampleInteropService` app service and launches the
  full-trust WPF process via `FullTrustProcessLauncher`.
- **WPF** — .NET 8 full-trust process. Hosts the bidirectional `AppServiceConnection` (IPC) and the
  demo UI (registry read / calc / notification). Runs with package identity. **No tray icon.**
- **TrayHelper** — native **C++ Win32** app (`Shell_NotifyIcon`). Owns the **system tray icon**.
  Launched at logon by a per-user **startup task**. Menu: **Open** (activate the UWP app) and
  **Exit** (close the UWP app + its WPF process and remove the icon). ~2–5 MB footprint.
- **WAPP** — MSIX packaging project (`.wapproj`). Bundles the three projects.

## Behavior

- **At logon:** the package's `windows.startupTask` launches `TrayHelper.exe` (with package
  identity). Only the **tray icon** appears — no window, no UWP UI.
- **Open (tray):** `TrayHelper` activates the UWP app via `shell:AppsFolder\<PFN>!App`. The UWP app
  calls `FullTrustProcessLauncher`, which starts **WPF**; WPF opens the `AppServiceConnection`, so
  **UWP ↔ WPF IPC works**, and its window is shown.
- **Exit (tray):** `TrayHelper` enumerates and terminates the package's other processes (the UWP app
  and WPF) by package family name, then removes the icon and quits.

> The native helper is the only owner of the tray icon; WPF no longer creates one. There is **no
> Windows service** in this design — the tray comes from a per-user startup task (lighter, no
> LocalSystem / restricted capabilities, and it doesn't run before the user is present).

## Manifest wiring

In [`WAPP/Package.appxmanifest`](WAPP/Package.appxmanifest):

- The UWP `Application Id="App"` keeps the `windows.appService` (`SampleInteropService`) and
  `windows.fullTrustProcess` (`WPF\WPF.exe`) extensions.
- A second hidden full-trust entry runs the native helper at logon:
  ```xml
  <Application Id="TrayHelper" Executable="TrayHelper\TrayHelper.exe"
               EntryPoint="Windows.FullTrustApplication">
    <uap:VisualElements ... AppListEntry="none">...</uap:VisualElements>
    <Extensions>
      <uap5:Extension Category="windows.startupTask">
        <uap5:StartupTask TaskId="DesktopBridgeTrayHelper" Enabled="true" DisplayName="DesktopBridge Tray" />
      </uap5:Extension>
    </Extensions>
  </Application>
  ```
- Capabilities: only `internetClient` and `<rescap:Capability Name="runFullTrust" />`.

## Building

### Builds with the .NET 8 SDK (no Visual Studio)

```sh
dotnet build WPF/WPF.csproj -c Release
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
