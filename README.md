# System Pulse

A minimal Windows 11 system dashboard with a cyberpunk visual style. The app has three pages: Dashboard, Cleanup, and Hardware. All user-facing text is in English. It runs locally without telemetry or administrator elevation. The borderless window includes its own minimise, maximise/restore, and hide-to-tray controls; drag the header to move it.

The dark dashboard uses a circuit texture and a bundled display font. Select **TOP PROCESSES +** on CPU Usage or Memory Usage to expand a live top-eight list. CPU ranking uses each process's share of system-wide processor time; memory ranking uses its current working set. Each row has a relative-size background bar and a 20-sample sparkline. The lists refresh every three seconds while the window is visible. A temperature reading of zero is treated as unavailable.

When the main window is hidden or minimised, a small circular floating shortcut appears near the lower-right corner. Click it to open the app. The tray menu can turn the shortcut off for the current session.

## Requirements

- Windows 11 x64
- .NET 10 SDK to build (the distributed installer is self-contained)

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

- [LibreHardwareMonitorLib 0.9.6](https://www.nuget.org/packages/LibreHardwareMonitorLib/) — MPL 2.0; see its [third-party notices](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/THIRD-PARTY-NOTICES.txt).
- [System.Management 10.0.12](https://www.nuget.org/packages/System.Management/) — MIT.
- [Chakra Petch](https://github.com/google/fonts/tree/main/ofl/chakrapetch) — SIL Open Font License; the licence text is included in `src/XinweiManager/Assets/Fonts/OFL.txt`.
