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

## Build

```powershell
dotnet build .\CpuGuard.sln
```

## Run in console mode (debug)

```powershell
dotnet run --project .\CpuGuard.Service\CpuGuard.Service.csproj -- --console
```

## Publish

```powershell
dotnet publish .\CpuGuard.Service\CpuGuard.Service.csproj -c Release -r win-x64 --self-contained false
```

## Install as Windows Service (Administrator)

```powershell
$serviceName = "CpuGuardService"
$exePath = "C:\Deploy\CpuGuard\CpuGuard.Service.exe"

sc.exe create $serviceName binPath= "\"$exePath\"" start= auto obj= LocalSystem
sc.exe description $serviceName "Monitors Windrose server activity and applies/removes Job Object CPU cap."
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start $serviceName
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

## Test scenarios

1. Join event increments player count.
2. Disconnect event decrements count and never goes below zero.
3. `ProcessAddPlayer` with zero players removes cap immediately, then re-applies after grace delay if still zero.
4. No players for configured delay applies cap.
5. Player joins while capped removes cap immediately.
6. Two discovered servers keep independent player/cap state with no cross-impact.
7. Process restart re-attaches to new PID within polling window.
8. Log rotation/truncation is detected and tailing resumes from new file.
9. Service restart removes stale cap state and resumes monitoring.
