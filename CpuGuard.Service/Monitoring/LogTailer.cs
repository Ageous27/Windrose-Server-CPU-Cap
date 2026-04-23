using CpuGuard.Service.Logging;
using CpuGuard.Service.Windows;
using System.Text;

namespace CpuGuard.Service.Monitoring;

public sealed class LogTailer
{
    private readonly string _logPath;
    private readonly TimeSpan _pollInterval;
    private readonly bool _bootstrapFromExistingLog;
    private readonly int _bootstrapMaxBytes;
    private readonly Action<string> _onLine;
    private readonly Action? _onAttached;
    private readonly Action<Exception>? _onError;
    private readonly StructuredLogger _logger;

    public LogTailer(
        string logPath,
        TimeSpan pollInterval,
        bool bootstrapFromExistingLog,
        int bootstrapMaxMegabytes,
        Action<string> onLine,
        StructuredLogger logger,
        Action? onAttached = null,
        Action<Exception>? onError = null)
    {
        _logPath = logPath;
        _pollInterval = pollInterval;
        _bootstrapFromExistingLog = bootstrapFromExistingLog;
        _bootstrapMaxBytes = bootstrapMaxMegabytes * 1024 * 1024;
        _onLine = onLine;
        _onAttached = onAttached;
        _onError = onError;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var firstOpen = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            FileStream? stream = null;

            try
            {
                if (!File.Exists(_logPath))
                {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!FileIdentity.TryFromHandle(stream.SafeFileHandle, out var openFileIdentity))
                {
                    openFileIdentity = default;
                }

                var startPosition = ResolveStartPosition(firstOpen, stream.Length);
                stream.Seek(startPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

                if (startPosition > 0)
                {
                    _ = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                firstOpen = false;
                _onAttached?.Invoke();
                _logger.Info("Log tailer attached.", new { path = _logPath, startPosition });

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is not null)
                    {
                        _onLine(line);
                        continue;
                    }

                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);

                    if (stream.Length < stream.Position)
                    {
                        _logger.Info("Log file truncated; reopening.", new { path = _logPath }, writeEventLog: true);
                        break;
                    }

                    if (openFileIdentity != default
                        && FileIdentity.TryFromPath(_logPath, out var currentPathIdentity)
                        && currentPathIdentity != openFileIdentity)
                    {
                        _logger.Info("Log rotation detected; reopening.", new { path = _logPath }, writeEventLog: true);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);
                _logger.Error("Log tailer encountered an error.", ex, new { path = _logPath });
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                stream?.Dispose();
            }
        }
    }

    private long ResolveStartPosition(bool firstOpen, long fileLength)
    {
        if (!firstOpen)
        {
            return 0;
        }

        if (!_bootstrapFromExistingLog)
        {
            return fileLength;
        }

        if (fileLength <= _bootstrapMaxBytes)
        {
            return 0;
        }

        return fileLength - _bootstrapMaxBytes;
    }
}
