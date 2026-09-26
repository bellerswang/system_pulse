# System Pulse

A minimal Windows 11 system dashboard with a cyberpunk visual style. The app has three pages: Dashboard, Cleanup, and Hardware. All user-facing text is in English. It runs locally without telemetry or administrator elevation. The borderless window includes its own minimise, maximise/restore, and hide-to-tray controls; drag the header to move it.

The dark dashboard uses a circuit texture and a bundled display font. Select **TOP PROCESSES +** on CPU Usage or Memory Usage to expand a live top-eight list. CPU ranking uses each process's share of system-wide processor time; memory ranking uses its current working set. Each row has a relative-size background bar and a 20-sample sparkline. The lists refresh every three seconds while the window is visible. A temperature reading of zero is treated as unavailable.

When the main window is hidden or minimised, a small circular floating shortcut appears near the lower-right corner. Click it to open the app. The tray menu can turn the shortcut off for the current session.

## Requirements

- Windows 11 x64 (the app targets `net10.0-windows` and is published for `win-x64`).
- No separate .NET installation is required on a computer using the installer: the release is self-contained and includes the .NET 10.0.12 and Windows Desktop 10.0.12 runtimes.
- To build from source: the .NET 10 SDK, internet access to `https://api.nuget.org/v3/index.json` for package restore, and PowerShell 7 or Windows PowerShell 5.1.
- To rebuild the installer: Inno Setup 6 in addition to the build requirements.

## Develop and verify

From the repository root in PowerShell:

```powershell
dotnet run --project .\src\XinweiManager\XinweiManager.csproj
dotnet run --project .\tests\XinweiManager.Tests\XinweiManager.Tests.csproj
```

If the SDK was installed into `.dotnet`, replace `dotnet` with `.\.dotnet\dotnet.exe`.

## Install and share

Download [SystemPulseSetup-1.1.0.exe](dist/SystemPulseSetup-1.1.0.exe) and run it on Windows 11 x64. Setup installs for the current user without administrator rights, creates a Start menu shortcut, and offers an optional desktop shortcut. It registers **System Pulse** to start in the background at every Windows sign-in; the floating shortcut and tray icon then appear. The tray menu's **Launch at sign-in** switch can disable or restore this setting. Uninstall from Windows **Installed apps** to remove the program and startup entry.

The installer does not require .NET to be installed separately. It is not code signed, so Windows may show an unknown-publisher prompt. Compare its SHA-256 hash with `dist/SHA256SUMS.txt` before sharing or running it.

To rebuild the installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php), then run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`. The result is written to `artifacts/installer`.

## Publish locally

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Local.ps1`. This creates a self-contained prototype in the project's `app` folder and adds the current-user `Run` entry for launch at sign-in. Launch `app\SystemPulse.exe` to open the window. The window's close button hides it to the tray; **Exit** in the tray menu stops the application. Use `Publish-Local.ps1 -SkipStartup` to publish without enabling launch at sign-in. Run `Remove-Local.ps1` after exiting the app to remove the prototype and its startup entry.

## Cleanup boundaries

Cleanup scans only `%LOCALAPPDATA%\Temp` and accessible `%WINDIR%\Temp` files older than 48 hours. It skips read-only and system files, links, junctions, occupied files, and files changed since the scan. Review the results and confirm before permanent deletion. The Recycle Bin, downloads, browser caches, and Windows Update files are excluded.

## Sensor availability

LibreHardwareMonitorLib reads supported CPU and GPU temperatures and fan RPM. Some hardware sensors need elevated permissions or are not exposed by the device. The app does not request elevation and shows an unavailable state when a reading cannot be obtained.

## Dependencies and licences

The application has no Python, Node.js, browser, database, or cloud-service dependency. `NuGet.Config` uses only nuget.org. These are the external NuGet libraries in the `win-x64` release; direct references are declared in `src/XinweiManager/XinweiManager.csproj`, and the remaining packages are brought in transitively by the hardware-monitoring library:

| Package | Version | Dependency | Use / licence |
|---|---:|---|---|
| [LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6) | 0.9.6 | Direct | CPU/GPU sensor and fan readings; MPL 2.0. See the project's [third-party notices](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/THIRD-PARTY-NOTICES.txt). |
| [System.Management](https://www.nuget.org/packages/System.Management/10.0.12) | 10.0.12 | Direct; also required by a transitive hardware package | Windows Management Instrumentation (WMI) hardware inventory; MIT. |
| [DiskInfoToolkit](https://www.nuget.org/packages/DiskInfoToolkit/1.1.2) | 1.1.2 | Transitive | Disk information support used by LibreHardwareMonitorLib; MPL 2.0. |
| [BlackSharp.Core](https://www.nuget.org/packages/BlackSharp.Core/1.0.7) | 1.0.7 | Transitive | Shared support library for DiskInfoToolkit and RAMSPDToolkit-NDD; MPL 2.0. |
| [HidSharp](https://www.nuget.org/packages/HidSharp/2.6.4) | 2.6.4 | Transitive | Hardware device access used by LibreHardwareMonitorLib. |
| [Mono.Posix.NETStandard](https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0) | 1.0.0 | Transitive | Cross-platform compatibility support bundled by LibreHardwareMonitorLib. |
| [RAMSPDToolkit-NDD](https://www.nuget.org/packages/RAMSPDToolkit-NDD/1.4.2) | 1.4.2 | Transitive | Memory SPD information support used by LibreHardwareMonitorLib; MPL 2.0. |
| [System.IO.Ports](https://www.nuget.org/packages/System.IO.Ports/10.0.3) | 10.0.3 | Transitive | Serial-port support used by LibreHardwareMonitorLib; MIT. |

The self-contained release also bundles the Microsoft .NET 10.0.12 `win-x64` runtime and Windows Desktop runtime (WPF/Windows Forms), plus framework libraries such as `System.CodeDom` and `System.IO.FileSystem.AccessControl`. These are supplied through the .NET SDK/runtime packs, not installed separately on the target PC. Windows provides the native APIs used for process/memory metrics (`kernel32.dll`) and folder/recycle-bin operations (`shell32.dll`); WMI is provided by Windows.

Build and installer tools (not app runtime dependencies):

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build and publish the source.
- [Inno Setup 6](https://jrsoftware.org/isdl.php) only to compile the shareable Windows installer.
- PowerShell 5.1 or later to run the included publish, installer, and removal scripts.

Bundled visual asset:

- [Chakra Petch](https://github.com/google/fonts/tree/main/ofl/chakrapetch) — SIL Open Font License; the licence text is included in `src/XinweiManager/Assets/Fonts/OFL.txt`.

Sensor readings depend on what the PC firmware and hardware expose. The app runs without an extra driver, but some sensor access may require elevated hardware access or may be unavailable on a particular device.
