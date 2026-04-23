using CpuGuard.Service.Configuration;
using CpuGuard.Service.Logging;
using CpuGuard.Service.Monitoring;
using CpuGuard.Service.Parsing;
using CpuGuard.Service.Policy;
using CpuGuard.Service.Windows;
using System.Text.RegularExpressions;

namespace CpuGuard.Service.Runtime;

internal sealed class ServerInstanceRuntime : IDisposable
{
    private readonly CpuGuardOptions _options;
    private readonly StructuredLogger _logger;
    private readonly PlayerActivityTracker _playerTracker;
    private readonly CpuPolicyController _policyController;
    private readonly JobObjectManager _jobObjectManager;
    private readonly LogTailer? _logTailer;
    private readonly string _expectedProcessFileName;
    private readonly object _sync = new();

    private readonly bool _isLogPathDerivable;
    private readonly string? _derivedLogPath;
    private readonly string _instanceKey;

    private CancellationTokenSource? _tailCts;
    private Task? _tailTask;
    private bool _logReadable;
    private bool _hasProcess;
    private int? _currentPid;
    private bool _disposed;
    private string? _lastMonitorabilityReason;

    public ServerInstanceRuntime(CpuGuardOptions options, StructuredLogger logger, string executablePath)
    {
        _options = options;
        _logger = logger;
        ExecutablePath = executablePath;
        _expectedProcessFileName = options.ServerProcessName + ".exe";
        _instanceKey = BuildInstanceKey(executablePath);

        _playerTracker = new PlayerActivityTracker();
        _policyController = new CpuPolicyController(
            TimeSpan.FromSeconds(options.ZeroPlayersDelaySeconds),
            TimeSpan.FromSeconds(options.TransitionCooldownSeconds),
            TimeSpan.FromSeconds(options.ProcessAddPlayerGraceSeconds));
        _jobObjectManager = new JobObjectManager();

        _playerTracker.PlayerCountChanged += OnPlayerCountChanged;
        _playerTracker.ProcessAddPlayerDetected += OnProcessAddPlayerDetected;

        if (TryDeriveLogPath(executablePath, _expectedProcessFileName, out var derivedLogPath))
        {
            _isLogPathDerivable = true;
            _derivedLogPath = derivedLogPath;

            _logTailer = new LogTailer(
                derivedLogPath,
                TimeSpan.FromMilliseconds(options.LogPollingMilliseconds),
                options.BootstrapFromExistingLog,
                options.BootstrapMaxMegabytes,
                _playerTracker.ProcessLogLine,
                logger,
                onAttached: OnLogAttached,
                onError: OnLogTailerError);
        }
        else
        {
            _isLogPathDerivable = false;
            _derivedLogPath = null;
        }
    }

    public string InstanceKey => _instanceKey;

    public string ExecutablePath { get; }

    public string? DerivedLogPath => _derivedLogPath;

    public void Start()
    {
        if (_logTailer is null)
        {
            _logger.Warn(
                "Instance log path derivation failed; monitoring disabled for this process.",
                new { ServerId = _instanceKey, ExecutablePath },
                writeEventLog: true);
            return;
        }

        _tailCts = new CancellationTokenSource();
        _tailTask = Task.Run(() => _logTailer.RunAsync(_tailCts.Token));
    }

    public void UpdateProcess(DiscoveredServerProcess? process)
    {
        var previousPid = _currentPid;

        if (process is null)
        {
            _hasProcess = false;
            _currentPid = null;
            TryRemoveCap("process-not-running");

            if (previousPid.HasValue)
            {
                _logger.Warn(
                    "Server process is no longer running.",
                    new
                    {
                        ServerId = _instanceKey,
                        ExecutablePath,
                        PreviousPid = previousPid
                    },
                    writeEventLog: true);
            }

            return;
        }

        _hasProcess = true;
        _currentPid = process.Pid;

        if (process.DuplicateCount > 1)
        {
            _logger.Warn(
                "Multiple PIDs detected for the same executable path. Using most recent process.",
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    process.Pid,
                    process.DuplicateCount
                },
                writeEventLog: true);
        }

        if (previousPid != process.Pid)
        {
            _logger.Info(
                "Process mapping updated.",
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    DerivedLogPath = _derivedLogPath,
                    PreviousPid = previousPid,
                    process.Pid,
                    process.StartTimeUtc
                },
                writeEventLog: true);
        }

        if (_policyController.IsCapActive)
        {
            TryApplyCap("process-recovered");
        }
    }

    public void Evaluate(DateTimeOffset nowUtc)
    {
        if (!IsMonitorable(out var reason))
        {
            TryRemoveCap($"instance-unmonitorable:{reason}");
            if (!string.Equals(reason, _lastMonitorabilityReason, StringComparison.Ordinal))
            {
                _logger.Warn(
                    "Instance is unmonitorable; skipping cap application.",
                    new
                    {
                        ServerId = _instanceKey,
                        ExecutablePath,
                        DerivedLogPath = _derivedLogPath,
                        Reason = reason
                    },
                    writeEventLog: true);
                _lastMonitorabilityReason = reason;
            }

            return;
        }

        _lastMonitorabilityReason = null;
        var action = _policyController.Evaluate(nowUtc);
        ExecutePolicyAction(action, "periodic-evaluation");
    }

    private bool IsMonitorable(out string reason)
    {
        if (!_hasProcess || !_currentPid.HasValue)
        {
            reason = "process-not-running";
            return false;
        }

        if (!_isLogPathDerivable || string.IsNullOrWhiteSpace(_derivedLogPath))
        {
            reason = "derived-log-path-invalid";
            return false;
        }

        if (!File.Exists(_derivedLogPath))
        {
            reason = "derived-log-missing";
            return false;
        }

        if (!_logReadable)
        {
            reason = "derived-log-unreadable";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void OnLogAttached()
    {
        lock (_sync)
        {
            _logReadable = true;
        }

        _logger.Info(
            "Instance log tail attached.",
            new
            {
                ServerId = _instanceKey,
                ExecutablePath,
                DerivedLogPath = _derivedLogPath
            });
    }

    private void OnLogTailerError(Exception exception)
    {
        lock (_sync)
        {
            _logReadable = false;
        }

        _logger.Error(
            "Instance log tailer error.",
            exception,
            new
            {
                ServerId = _instanceKey,
                ExecutablePath,
                DerivedLogPath = _derivedLogPath
            });
    }

    private void OnPlayerCountChanged(object? sender, PlayerCountChangedEventArgs eventArgs)
    {
        _logger.Info(
            "Player count changed.",
            new
            {
                ServerId = _instanceKey,
                ExecutablePath,
                DerivedLogPath = _derivedLogPath,
                eventArgs.PreviousCount,
                eventArgs.CurrentCount,
                eventArgs.Trigger
            },
            writeEventLog: true);

        var action = _policyController.OnPlayerCountChanged(eventArgs.CurrentCount, DateTimeOffset.UtcNow);
        ExecutePolicyAction(action, $"player-{eventArgs.Trigger}");
    }

    private void OnProcessAddPlayerDetected()
    {
        _logger.Info(
            "ProcessAddPlayer signal detected.",
            new
            {
                ServerId = _instanceKey,
                ExecutablePath,
                DerivedLogPath = _derivedLogPath,
                CurrentPlayerCount = _playerTracker.CurrentPlayerCount
            },
            writeEventLog: true);

        var action = _policyController.OnProcessAddPlayerSignal(DateTimeOffset.UtcNow);
        ExecutePolicyAction(action, "process-add-player");
    }

    private void ExecutePolicyAction(PolicyAction action, string reason)
    {
        switch (action)
        {
            case PolicyAction.ApplyCap:
                TryApplyCap(reason);
                break;
            case PolicyAction.RemoveCap:
                TryRemoveCap(reason);
                break;
        }
    }

    private bool TryAttachToCurrentProcess(string reason)
    {
        if (!_currentPid.HasValue)
        {
            return false;
        }

        if (_jobObjectManager.IsAttachedTo(_currentPid.Value))
        {
            return true;
        }

        try
        {
            _jobObjectManager.AttachToProcess(_currentPid.Value);
            _logger.Info(
                "Attached process to Job Object.",
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    reason
                },
                writeEventLog: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                "Failed to attach process to Job Object.",
                ex,
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    reason
                });
            return false;
        }
    }

    private void TryApplyCap(string reason)
    {
        try
        {
            if (!TryAttachToCurrentProcess(reason))
            {
                _policyController.MarkCapApplyFailed(DateTimeOffset.UtcNow);
                _logger.Warn(
                    "Cap requested but process is unavailable.",
                    new
                    {
                        ServerId = _instanceKey,
                        ExecutablePath,
                        Pid = _currentPid,
                        reason
                    });
                return;
            }

            if (_jobObjectManager.IsCapApplied)
            {
                _policyController.MarkCapApplied(DateTimeOffset.UtcNow);
                return;
            }

            _jobObjectManager.ApplyHardCap(_options.CpuCapPercent);
            _policyController.MarkCapApplied(DateTimeOffset.UtcNow);
            _logger.Info(
                "CPU hard cap applied.",
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    CapPercent = _options.CpuCapPercent,
                    reason
                },
                writeEventLog: true);
        }
        catch (Exception ex)
        {
            _policyController.MarkCapApplyFailed(DateTimeOffset.UtcNow);
            _logger.Error(
                "Failed to apply CPU hard cap.",
                ex,
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    _options.CpuCapPercent,
                    reason
                });
        }
    }

    private void TryRemoveCap(string reason)
    {
        try
        {
            var policyThoughtCapWasActive = _policyController.IsCapActive;

            if (!_jobObjectManager.IsCapApplied)
            {
                _policyController.MarkCapRemoved(DateTimeOffset.UtcNow);
                if (policyThoughtCapWasActive)
                {
                    _logger.Info(
                        "CPU hard cap state synchronized to removed.",
                        new
                        {
                            ServerId = _instanceKey,
                            ExecutablePath,
                            Pid = _currentPid,
                            reason
                        },
                        writeEventLog: true);
                }

                return;
            }

            _jobObjectManager.RemoveCap();
            _policyController.MarkCapRemoved(DateTimeOffset.UtcNow);
            _logger.Info(
                "CPU hard cap removed.",
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    reason
                },
                writeEventLog: true);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "Failed to remove CPU hard cap.",
                ex,
                new
                {
                    ServerId = _instanceKey,
                    ExecutablePath,
                    Pid = _currentPid,
                    reason
                });
        }
    }

    private static bool TryDeriveLogPath(string executablePath, string expectedProcessFileName, out string derivedLogPath)
    {
        derivedLogPath = string.Empty;

        try
        {
            var normalizedExePath = Path.GetFullPath(executablePath);
            if (!Path.GetFileName(normalizedExePath).Equals(expectedProcessFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var win64Directory = Path.GetDirectoryName(normalizedExePath);
            if (string.IsNullOrWhiteSpace(win64Directory)
                || !new DirectoryInfo(win64Directory).Name.Equals("Win64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var binariesDirectory = Directory.GetParent(win64Directory)?.FullName;
            if (string.IsNullOrWhiteSpace(binariesDirectory)
                || !new DirectoryInfo(binariesDirectory).Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var r5Directory = Directory.GetParent(binariesDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(r5Directory)
                || !new DirectoryInfo(r5Directory).Name.Equals("R5", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            derivedLogPath = Path.Combine(r5Directory, "Saved", "Logs", "R5.log");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildInstanceKey(string executablePath)
    {
        var match = Regex.Match(executablePath, @"[\\/]+servers[\\/]+([^\\/]+)[\\/]+", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"server{match.Groups[1].Value}";
        }

        var hash = executablePath.GetHashCode(StringComparison.OrdinalIgnoreCase) & 0x7FFFFFFF;
        return $"instance-{hash:X8}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _playerTracker.PlayerCountChanged -= OnPlayerCountChanged;
        _playerTracker.ProcessAddPlayerDetected -= OnProcessAddPlayerDetected;

        _tailCts?.Cancel();
        try
        {
            _tailTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Ignore stop errors while disposing.
        }

        _tailCts?.Dispose();
        _jobObjectManager.Dispose();
    }
}
