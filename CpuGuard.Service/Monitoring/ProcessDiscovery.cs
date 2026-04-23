using CpuGuard.Service.Windows;
using System.Diagnostics;

namespace CpuGuard.Service.Monitoring;

public sealed class ProcessDiscovery
{
    private readonly string _processName;

    public ProcessDiscovery(string processName)
    {
        _processName = processName;
    }

    public IReadOnlyList<DiscoveredServerProcess> Discover()
    {
        var processCandidates = new List<(string ExecutablePath, int Pid, DateTimeOffset? StartTimeUtc)>();
        var processes = Process.GetProcessesByName(_processName);

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                if (!ProcessPathResolver.TryGetExecutablePath(process.Id, out var executablePath))
                {
                    continue;
                }

                processCandidates.Add((NormalizePath(executablePath), process.Id, TryGetStartTimeUtc(process)));
            }
            catch
            {
                // Ignore per-process failures; discovery is best-effort.
            }
            finally
            {
                process.Dispose();
            }
        }

        var grouped = processCandidates
            .GroupBy(candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase);

        var discovered = new List<DiscoveredServerProcess>();
        foreach (var group in grouped)
        {
            var selected = group
                .OrderByDescending(candidate => candidate.StartTimeUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(candidate => candidate.Pid)
                .First();

            discovered.Add(new DiscoveredServerProcess(
                selected.ExecutablePath,
                selected.Pid,
                selected.StartTimeUtc,
                group.Count()));
        }

        discovered.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.ExecutablePath, right.ExecutablePath));
        return discovered;
    }

    private static DateTimeOffset? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
