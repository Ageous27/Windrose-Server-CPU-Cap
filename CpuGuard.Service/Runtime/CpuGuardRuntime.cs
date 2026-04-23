using CpuGuard.Service.Configuration;
using CpuGuard.Service.Logging;
using CpuGuard.Service.Monitoring;

namespace CpuGuard.Service.Runtime;

public sealed class CpuGuardRuntime : IDisposable
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(1);

    private readonly CpuGuardOptions _options;
    private readonly StructuredLogger _logger;
    private readonly ProcessDiscovery _processDiscovery;
    private readonly Dictionary<string, ServerInstanceRuntime> _instances = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public CpuGuardRuntime(CpuGuardOptions options, bool interactiveConsole = false)
    {
        _options = options;
        _logger = new StructuredLogger(options.ServiceLogPath, options.EventLogSource, interactiveConsole);
        _processDiscovery = new ProcessDiscovery(options.ServerProcessName);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Info(
            "CPU Guard runtime starting.",
            new
            {
                _options.ServiceName,
                _options.ServerProcessName,
                _options.AutoDiscover,
                _options.MaxManagedInstances,
                _options.CpuCapPercent,
                _options.ZeroPlayersDelaySeconds,
                _options.ProcessAddPlayerGraceSeconds
            },
            writeEventLog: true);

        var nextDiscoveryAtUtc = DateTimeOffset.MinValue;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                if (nowUtc >= nextDiscoveryAtUtc)
                {
                    RefreshDiscoveredInstances(nowUtc);
                    nextDiscoveryAtUtc = nowUtc + TimeSpan.FromSeconds(_options.ProcessPollingSeconds);
                }

                foreach (var instance in _instances.Values.ToList())
                {
                    instance.Evaluate(nowUtc);
                }

                await Task.Delay(EvaluationInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during service stop.
        }
        catch (Exception ex)
        {
            _logger.Error("Runtime stopped due to an unexpected error.", ex);
            throw;
        }
        finally
        {
            ShutdownInstances();
            _logger.Info("CPU Guard runtime stopped.", writeEventLog: true);
        }
    }

    private void RefreshDiscoveredInstances(DateTimeOffset nowUtc)
    {
        if (!_options.AutoDiscover)
        {
            _logger.Warn("Auto-discover is required and has been enabled automatically.");
        }

        var discovered = _processDiscovery.Discover();
        if (discovered.Count > _options.MaxManagedInstances)
        {
            _logger.Warn(
                "Discovered more server executables than MaxManagedInstances. Extra instances are ignored.",
                new
                {
                    DiscoveredCount = discovered.Count,
                    _options.MaxManagedInstances
                },
                writeEventLog: true);
        }

        var selected = discovered
            .Take(_options.MaxManagedInstances)
            .ToDictionary(process => process.ExecutablePath, process => process, StringComparer.OrdinalIgnoreCase);

        var staleKeys = _instances.Keys.Where(key => !selected.ContainsKey(key)).ToList();
        foreach (var staleKey in staleKeys)
        {
            var staleInstance = _instances[staleKey];
            staleInstance.UpdateProcess(null);
            staleInstance.Dispose();
            _instances.Remove(staleKey);

            _logger.Info(
                "Instance removed from active set.",
                new
                {
                    ServerId = staleInstance.InstanceKey,
                    staleInstance.ExecutablePath,
                    staleInstance.DerivedLogPath,
                    TimestampUtc = nowUtc
                },
                writeEventLog: true);
        }

        foreach (var process in selected.Values)
        {
            if (!_instances.TryGetValue(process.ExecutablePath, out var instance))
            {
                instance = new ServerInstanceRuntime(_options, _logger, process.ExecutablePath);
                instance.Start();
                _instances[process.ExecutablePath] = instance;

                _logger.Info(
                    "Discovered new instance.",
                    new
                    {
                        ServerId = instance.InstanceKey,
                        process.ExecutablePath,
                        instance.DerivedLogPath,
                        process.Pid
                    },
                    writeEventLog: true);
            }

            instance.UpdateProcess(process);
        }
    }

    private void ShutdownInstances()
    {
        foreach (var instance in _instances.Values)
        {
            try
            {
                instance.UpdateProcess(null);
                instance.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    "Failed to dispose instance during shutdown.",
                    ex,
                    new
                    {
                        ServerId = instance.InstanceKey,
                        instance.ExecutablePath,
                        instance.DerivedLogPath
                    });
            }
        }

        _instances.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShutdownInstances();
        _logger.Dispose();
    }
}
