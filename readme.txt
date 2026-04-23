Windrose Server CPU Cap (CpuGuard)
==================================

Overview
--------
CpuGuard is a Windows service written in C#/.NET that auto-discovers running
Windrose dedicated server processes and dynamically applies/removes CPU caps
per server instance using Windows Job Objects.

Key Features
------------
- Auto-discovers up to 2 server instances by executable path.
- Derives each server log path from executable path:
  ...\R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe
  -> ...\R5\Saved\Logs\R5.log
- Independent player count and cap state per discovered server.
- Parses:
  - Join succeeded:
  - PlayerDisconnected. State Connected
  - ProcessAddPlayer
- Applies CPU hard cap when player count is zero for configured delay.
- Removes cap when players join (or ProcessAddPlayer signal appears).
- Handles process restarts, PID remapping, and log rotation/truncation.

Main Config
-----------
File: CpuGuard.Service\appsettings.json

Important settings:
- ServerProcessName
- AutoDiscover
- MaxManagedInstances
- CpuCapPercent (supports decimal values, e.g. 1.5)
- ZeroPlayersDelaySeconds
- ProcessAddPlayerGraceSeconds
- TransitionCooldownSeconds
- ProcessPollingSeconds
- LogPollingMilliseconds

Build
-----
dotnet build .\CpuGuard.sln

Publish
-------
dotnet publish .\CpuGuard.Service\CpuGuard.Service.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

Install Service
---------------
Use:
install_CpuGuard_service.ps1

Deploy Update (Typical)
-----------------------
1) Stop service
2) Replace files in deploy folder
3) Start service
4) Verify service status and logs

