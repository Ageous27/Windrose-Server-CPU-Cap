# Windrose-Server-CPU-Cap
Windows Service to monitor Windrose server process when no players on connected and cap CPU utilization for the server process

Configuration via
appsettings.json

Example settings

{
  "CpuGuard": {
    "ServiceName": "CpuGuardService",
    "ServerProcessName": "WindroseServer-Win64-Shipping",
    "AutoDiscover": true,
    "MaxManagedInstances": 5,
    "ZeroPlayersDelaySeconds": 20,
    "TransitionCooldownSeconds": 15,
    "ProcessAddPlayerGraceSeconds": 60,
    "CpuCapPercent": 1.5,
    "ProcessPollingSeconds": 2,
    "LogPollingMilliseconds": 500,
    "BootstrapFromExistingLog": true,
    "BootstrapMaxMegabytes": 20,
    "ServiceLogPath": "%ProgramData%\\CpuGuard\\logs\\service.log",
    "EventLogSource": "CpuGuardService"
  }
}

