# UwpWithDesktopExtension

A UWP app with a full-trust desktop (WPF) extension, plus a **packaged Windows service**
that auto-starts as **LocalSystem** and brings up the WPF tray app in the interactive user
session so its tray icon shows and the UWP&nbsp;↔&nbsp;WPF IPC connection works.

The Visual Studio solution lives in [`DesktopBridge/`](DesktopBridge/). See
[`DesktopBridge/README.md`](DesktopBridge/README.md) for architecture, build instructions, and how
the packaged service is wired.

## Projects

| Project | Type | Role |
| --- | --- | --- |
| `DesktopBridge` | UWP (UAP) | Hosts the `SampleInteropService` app service; launches the full-trust process. |
| `WPF` | .NET 8 WPF (full trust) | Tray icon + bidirectional app-service (IPC) client. |
| `TrayLauncherService` | .NET 8 Windows service | LocalSystem, auto-start. Activates the packaged app in the user session. |
| `WAPP` | MSIX packaging (`.wapproj`) | Bundles everything; declares the `windows.service` extension. |

## Build at a glance

- `WPF` and `TrayLauncherService` build with the **.NET 8 SDK** (`dotnet build`).
- `DesktopBridge` (UWP) and `WAPP` (MSIX packaging) require **Visual Studio** with the
  *Universal Windows Platform development* and *MSIX Packaging Tools* workloads — they cannot be
  built with the .NET SDK alone.

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
   and then the app). The `TrayLauncherService` (LocalSystem, auto-start) is installed with it.
3. Prerequisites: Windows 10 2004+ and the **.NET 8 Desktop Runtime (x64)**.
