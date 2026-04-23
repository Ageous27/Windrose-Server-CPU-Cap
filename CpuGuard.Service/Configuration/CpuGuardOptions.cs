using System.Text.Json;

namespace CpuGuard.Service.Configuration;

public sealed class CpuGuardOptions
{
    public string ServiceName { get; set; } = "CpuGuardService";
    public string ServerProcessName { get; set; } = "WindroseServer-Win64-Shipping";
    public bool AutoDiscover { get; set; } = true;
    public int MaxManagedInstances { get; set; } = 2;
    public int ZeroPlayersDelaySeconds { get; set; } = 60;
    public int TransitionCooldownSeconds { get; set; } = 15;
    public int ProcessAddPlayerGraceSeconds { get; set; } = 60;
    public double CpuCapPercent { get; set; } = 5;
    public int ProcessPollingSeconds { get; set; } = 2;
    public int LogPollingMilliseconds { get; set; } = 500;
    public bool BootstrapFromExistingLog { get; set; } = true;
    public int BootstrapMaxMegabytes { get; set; } = 20;
    public string ServiceLogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CpuGuard",
        "logs",
        "service.log");
    public string EventLogSource { get; set; } = "CpuGuardService";

    public static CpuGuardOptions Load(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return new CpuGuardOptions().Normalize();
            }

            using var stream = File.OpenRead(configPath);
            var root = JsonSerializer.Deserialize<ConfigRoot>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (root?.CpuGuard ?? new CpuGuardOptions()).Normalize();
        }
        catch
        {
            return new CpuGuardOptions().Normalize();
        }
    }

    public CpuGuardOptions Normalize()
    {
        ServiceName = string.IsNullOrWhiteSpace(ServiceName) ? "CpuGuardService" : ServiceName.Trim();
        ServerProcessName = NormalizeProcessName(ServerProcessName);
        ServiceLogPath = ExpandOrDefault(
            ServiceLogPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CpuGuard", "logs", "service.log"));
        EventLogSource = string.IsNullOrWhiteSpace(EventLogSource) ? ServiceName : EventLogSource.Trim();

        AutoDiscover = true;
        MaxManagedInstances = Math.Clamp(MaxManagedInstances, 1, 16);
        ZeroPlayersDelaySeconds = Math.Max(1, ZeroPlayersDelaySeconds);
        TransitionCooldownSeconds = Math.Max(0, TransitionCooldownSeconds);
        ProcessAddPlayerGraceSeconds = Math.Max(1, ProcessAddPlayerGraceSeconds);
        CpuCapPercent = Math.Clamp(CpuCapPercent, 0.01, 100);
        CpuCapPercent = Math.Round(CpuCapPercent, 2, MidpointRounding.AwayFromZero);
        ProcessPollingSeconds = Math.Clamp(ProcessPollingSeconds, 1, 10);
        LogPollingMilliseconds = Math.Clamp(LogPollingMilliseconds, 200, 5000);
        BootstrapMaxMegabytes = Math.Clamp(BootstrapMaxMegabytes, 1, 512);

        return this;
    }

    private static string NormalizeProcessName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "WindroseServer-Win64-Shipping";
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^4];
        }

        return trimmed;
    }

    private static string ExpandOrDefault(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Environment.ExpandEnvironmentVariables(value.Trim());
    }

    private sealed class ConfigRoot
    {
        public CpuGuardOptions? CpuGuard { get; set; }
    }
}
