namespace CpuGuard.Service.Monitoring;

public sealed record DiscoveredServerProcess(
    string ExecutablePath,
    int Pid,
    DateTimeOffset? StartTimeUtc,
    int DuplicateCount);
