# SmartThings solution

UWP app + full-trust player (WPF) extension (IPC) + a native C++ system-tray helper + a packaged
LocalSystem Windows service that launches the tray.

> Project files/folders and assembly names are `SmartThings.*`; the internal C# code namespaces
> (`DesktopBridge`, `WPF`, `TrayLauncherService`) are unchanged, so `x:Class`, the UWP entry point,
> and the app-service wiring keep working.

## Projects

- **SmartThings.UI** — UWP app (code namespace `DesktopBridge`). Hosts the `SampleInteropService`
  app service and launches the full-trust player via `FullTrustProcessLauncher`.
- **SmartThings.AVplayer** — .NET 8 full-trust WPF process (code namespace `WPF`). Hosts the
  bidirectional `AppServiceConnection` (IPC) and the demo UI. Runs with package identity. **No tray
  icon.**
- **SmartThings.Tray** — native **C++ Win32** app (`Shell_NotifyIcon`). Owns the **system tray
  icon**. Swaps between a **light/dark icon** with the system theme and shows a **localized**
  Open/Exit menu. **Open** activates the UWP app; **Exit** closes the UWP app + player and removes
  the icon. ~2–5 MB footprint.
- **SmartThings.Service** — .NET 8 **Windows service** (`SmartThings.Service`), **LocalSystem**,
  **auto-start**. Installed with the package. Starts right after install and at every boot, launches
  **SmartThings.Tray and SmartThings.AVplayer** in the interactive user session, then (after a short
  delay) **kills the player** and **stops itself**. If no user is signed in yet at boot, it waits for
  logon, does the same, and stops.
- **SmartThings.WAPP** — MSIX packaging project (`.wapproj`). Bundles the four projects.

## Behavior

- **Autostart via the service:** the package installs a `windows.service` (LocalSystem, auto-start).
  It starts **right after install** and at **every boot**. Running in session 0, it uses
  `CreateProcessAsUser` to launch, in the active user session, the app-execution aliases
  `DesktopBridgeTray.exe` (TrayHelper) and `DesktopBridgeWpf.exe` (WPF) — so both start **with
  package identity**. The **tray icon appears after install and on every sign-in without launching
  the main app**, and without the `windows.startupTask` "must be launched once" gate.
- **WPF is launched then killed:** the service launches WPF alongside the tray, waits ~5s, then
  terminates the `WPF.exe` process. (WPF is otherwise launched on demand by the UWP app when you
  click **Open**.) Adjust `WpfLifetime` in `TrayService.cs` to change the delay.
- **The service stops itself** once it has launched the tray (and killed WPF). It's auto-start, so it
  runs again at the next boot; at boot before anyone is signed in it stays running only until a user
  logs on.
- **Theme icon:** TrayHelper reads `HKCU\...\Themes\Personalize\SystemUsesLightTheme` and shows
  `app-light.ico` (light theme) or `app-dark.ico` (dark theme). It re-evaluates on the
  `WM_SETTINGCHANGE`/`ImmersiveColorSet` broadcast, so switching theme swaps the icon live. The
  mapping (light→light, dark→dark) is one line in `LoadThemeIcon()` — swap the IDs to invert it.
- **Localized menu:** the Open/Exit/tooltip strings come from a table in `main.cpp` keyed by the
  two-letter Windows UI language (English fallback). **To add a language, add one row** to
  `kLanguages`. The menu is rebuilt on each right-click, so it always reflects the current language;
  `WM_SETTINGCHANGE`/`intl` refreshes the tooltip.
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

In [`SmartThings.WAPP/Package.appxmanifest`](SmartThings.WAPP/Package.appxmanifest):

- The UWP `Application Id="App"` keeps the `windows.appService` (`SampleInteropService`) and
  `windows.fullTrustProcess` (`SmartThings.AVplayer\SmartThings.AVplayer.exe`) extensions, plus the
  service:
  ```xml
  <desktop6:Extension Category="windows.service"
      Executable="SmartThings.Service\SmartThings.Service.exe"
      EntryPoint="Windows.FullTrustApplication">
    <desktop6:Service Name="SmartThings.Service" StartupType="auto" StartAccount="localSystem" />
  </desktop6:Extension>
  ```
- Two hidden full-trust entries (`AppListEntry="none"`), each with a `uap5:AppExecutionAlias` the
  service launches: `Application Id="TrayHelper"` (`SmartThings.Tray\SmartThings.Tray.exe`) →
  `DesktopBridgeTray.exe`, and `Application Id="WpfHost"`
  (`SmartThings.AVplayer\SmartThings.AVplayer.exe`) → `DesktopBridgeWpf.exe`.
- Capabilities: `internetClient`, `runFullTrust`, and (restricted) `packagedServices` +
  `localSystemServices`.

## Building

### Builds with the .NET 8 SDK (no Visual Studio)

```sh
dotnet build SmartThings.AVplayer/SmartThings.AVplayer.csproj -c Release
dotnet build SmartThings.Service/SmartThings.Service.csproj -c Release
```

The native **SmartThings.Tray** (C++) builds with the installed VC++ Build Tools:

```sh
msbuild SmartThings.Tray/SmartThings.Tray.vcxproj -p:Configuration=Release -p:Platform=x64
```

### Requires Visual Studio

`SmartThings.UI` (UWP) and `SmartThings.WAPP` (MSIX packaging) need **Visual Studio 2022/2026** with
the **Universal Windows Platform development** and **.NET / MSIX Packaging Tools** workloads
(plus the **Desktop development with C++** workload for `SmartThings.Tray`), and Windows SDK
**10.0.26100.0**. Open `DesktopBridge.sln`, set **WAPP** as startup, choose `x64`, and Build /
Deploy to produce and install the MSIX. CI ([`.github/workflows/build-and-release.yml`](../.github/workflows/build-and-release.yml))
does this on a GitHub `windows-latest` runner.

## Manually testing the tray helper (without packaging)

`TrayHelper.exe` can be run directly, but **outside the package it has no identity**, so "Open"
(which resolves `GetCurrentPackageFamilyName`) won't find the UWP app. To test the tray UI itself,
run `x64\Release\TrayHelper.exe`; for the full Open/IPC flow, install the MSIX.
