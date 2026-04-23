# CPU Guard Windows Service

Windows Service in C#/.NET 8 (x64) that:

- Auto-discovers `WindroseServer-Win64-Shipping.exe` processes by executable path.
- Derives each log path from executable path (`...\R5\Binaries\Win64\...exe` -> `...\R5\Saved\Logs\R5.log`).
- Tails each discovered `R5.log` independently in real time.
- Tracks player count independently per discovered instance.
- Attaches `WindroseServer-Win64-Shipping.exe` to a Windows Job Object.
- Applies a hard CPU cap after configured zero-player delay.
- Removes cap immediately when players return or `ProcessAddPlayer` is detected.
- Re-attaches automatically when the server restarts.

## Requirements

- Windows x64 machine (Windows 10/11 or Windows Server) with Services support.
- `.NET 8` runtime (x64) installed on the target machine (release build is framework-dependent).

## Latest release

- Latest version: [`v2026.04.23`](https://github.com/Ageous27/Windrose-Server-CPU-Cap/releases/tag/v2026.04.23)
- Always-current link: [Latest release](https://github.com/Ageous27/Windrose-Server-CPU-Cap/releases/latest)

## Install as Windows Service (Administrator)

1. Download `CpuGuard.Service-net8.0-windows-win-x64-publish.zip` from the latest release.
2. Extract the zip to `C:\CpuGuard` (overwrite files if updating).
3. Set your `C:\CpuGuard\appsettings.json` values.
4. Run the installer script:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\CpuGuard\install_CpuGuard_service.ps1"
```

If the service is already installed and you are updating to a newer release:

```powershell
sc.exe stop CpuGuardService
# Extract latest release zip to C:\CpuGuard and overwrite existing files
sc.exe start CpuGuardService
```

## Uninstall (Administrator)

```powershell
$serviceName = "CpuGuardService"
sc.exe stop $serviceName
sc.exe delete $serviceName
```

## Key configuration (`CpuGuard.Service\appsettings.json`)

- `ServerProcessName`: process name without `.exe`.
- `AutoDiscover`: must remain `true` for auto-discovered instances.
- `MaxManagedInstances`: max number of discovered executable paths to manage (auto-discovery scope).
- `ZeroPlayersDelaySeconds`: idle delay before capping.
- `ProcessAddPlayerGraceSeconds`: grace timer started by `ProcessAddPlayer` when player count is zero.
- `TransitionCooldownSeconds`: anti-flap cooldown.
- `CpuCapPercent`: hard cap percentage (supports decimals, range `0.01` to `100`).
- `ProcessPollingSeconds`: watcher interval for restart re-attach.
- `LogPollingMilliseconds`: tail poll interval.
- `ServiceLogPath`: JSON log output path.
- `EventLogSource`: Windows Event Log source name.
