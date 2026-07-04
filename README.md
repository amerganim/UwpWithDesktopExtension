# UwpWithDesktopExtension

A UWP app with a full-trust desktop (WPF) extension for IPC, a **native C++ system-tray helper**,
and a packaged **LocalSystem Windows service** that starts after install and at every boot and
launches the tray helper. The tray icon lets the user open or exit the main UWP app; opening it
establishes the UWP&nbsp;↔&nbsp;WPF IPC connection.

The Visual Studio solution lives in [`DesktopBridge/`](DesktopBridge/). See
[`DesktopBridge/README.md`](DesktopBridge/README.md) for architecture and build instructions.

## Projects

| Project | Type | Role |
| --- | --- | --- |
| `DesktopBridge` | UWP (UAP) | Hosts the `SampleInteropService` app service; launches the full-trust WPF process. |
| `WPF` | .NET 8 WPF (full trust) | Bidirectional app-service (IPC) client + demo UI. No tray icon. |
| `TrayHelper` | Native C++ Win32 | Owns the system tray icon (Open / Exit). |
| `TrayLauncherService` | .NET 8 Windows service | LocalSystem, auto-start. Launches TrayHelper in the user session after install / at boot / on logon. |
| `WAPP` | MSIX packaging (`.wapproj`) | Bundles everything; declares the service. |

## Build at a glance

- `WPF` and `TrayLauncherService` build with the **.NET 8 SDK** (`dotnet build`); `TrayHelper`
  builds with the **VC++ Build Tools** (`msbuild TrayHelper/TrayHelper.vcxproj`).
- `DesktopBridge` (UWP) and `WAPP` (MSIX packaging) require **Visual Studio** with the
  *Universal Windows Platform development*, *MSIX Packaging Tools*, and *Desktop development with
  C++* workloads — they cannot be built with the .NET SDK alone.

## CI / Releases

[`.github/workflows/build-and-release.yml`](.github/workflows/build-and-release.yml) builds the
whole solution on the GitHub-hosted `windows-latest` runner (which has the UWP + MSIX workloads),
signs the package with a throwaway self-signed certificate, and uploads it.

- **Every push / PR / manual run** uploads the package as a workflow **artifact**
  (`DesktopBridge-MSIX`) you can download from the Actions run.
- **Pushing a tag `v*`** (e.g. `git tag v1.0.0 && git push origin v1.0.0`) additionally publishes a
  **GitHub Release** with the installable package zip attached.

### Install

1. Download **DesktopBridge-MSIX.zip** (from the Release or the Actions artifact) and extract it.
2. Right-click **Add-AppDevPackage.ps1** → **Run with PowerShell** (installs the bundled certificate
   and the app). The LocalSystem service is installed with it, starts immediately, and launches the
   tray icon — no need to open the main app. It also starts at every boot / sign-in.
3. Prerequisites: Windows 10 2004 (19041)+ and the **.NET 8 Desktop Runtime (x64)**.
