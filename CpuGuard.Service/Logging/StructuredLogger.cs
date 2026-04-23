using System.Text;
using System.Text.Json;

namespace CpuGuard.Service.Logging;

public enum GuardLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed class StructuredLogger : IDisposable
{
    private readonly bool _interactiveConsole;
    private readonly string _eventSource;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private readonly WindowsEventLogger _eventLogger;
    private bool _disposed;

    public StructuredLogger(string logPath, string eventSource, bool interactiveConsole)
    {
        _interactiveConsole = interactiveConsole;
        _eventSource = eventSource;

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(
            new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        _eventLogger = new WindowsEventLogger(_eventSource);
    }

    public void Debug(string message, object? data = null) => Write(GuardLogLevel.Debug, message, data, null, writeEventLog: false);

    public void Info(string message, object? data = null, bool writeEventLog = false) => Write(GuardLogLevel.Information, message, data, null, writeEventLog);

    public void Warn(string message, object? data = null, bool writeEventLog = false) => Write(GuardLogLevel.Warning, message, data, null, writeEventLog);

    public void Error(string message, Exception exception, object? data = null, bool writeEventLog = true) => Write(GuardLogLevel.Error, message, data, exception, writeEventLog);

    private void Write(GuardLogLevel level, string message, object? data, Exception? exception, bool writeEventLog)
    {
        if (_disposed)
        {
            return;
        }

        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level = level.ToString(),
            message,
            data,
            exception = exception?.ToString()
        };

        var line = JsonSerializer.Serialize(entry, _jsonOptions);

        lock (_gate)
        {
            _writer.WriteLine(line);
        }

        if (_interactiveConsole)
        {
            Console.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }

        if (writeEventLog)
        {
            TryWriteEventLog(level, message, data, exception);
        }
    }

    private void TryWriteEventLog(GuardLogLevel level, string message, object? data, Exception? exception)
    {
        try
        {
            var payload = data is null ? string.Empty : $" Data={JsonSerializer.Serialize(data, _jsonOptions)}";
            var error = exception is null ? string.Empty : $" Exception={exception.Message}";
            var finalMessage = (message + payload + error).Trim();

            if (finalMessage.Length > 30000)
            {
                finalMessage = finalMessage[..30000];
            }

            _eventLogger.TryWrite(
                level switch
                {
                    GuardLogLevel.Error => WindowsEventType.Error,
                    GuardLogLevel.Warning => WindowsEventType.Warning,
                    _ => WindowsEventType.Information
                },
                finalMessage);
        }
        catch
        {
            // Avoid recursive logging if Event Log is unavailable.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _writer.Dispose();
        }

        _eventLogger.Dispose();
    }
}
