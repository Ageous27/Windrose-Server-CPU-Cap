using System.Runtime.InteropServices;

namespace CpuGuard.Service.Logging;

public enum WindowsEventType : ushort
{
    Error = 0x0001,
    Warning = 0x0002,
    Information = 0x0004
}

public sealed class WindowsEventLogger : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public WindowsEventLogger(string sourceName)
    {
        _handle = RegisterEventSource(null, sourceName);
    }

    public bool TryWrite(WindowsEventType type, string message, uint eventId = 1000)
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return false;
        }

        var strings = new[] { message };
        return ReportEvent(
            _handle,
            type,
            0,
            eventId,
            IntPtr.Zero,
            (ushort)strings.Length,
            0,
            strings,
            IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            DeregisterEventSource(_handle);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterEventSource(string? lpUNCServerName, string lpSourceName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReportEvent(
        IntPtr hEventLog,
        WindowsEventType wType,
        ushort wCategory,
        uint dwEventID,
        IntPtr lpUserSid,
        ushort wNumStrings,
        uint dwDataSize,
        string[] lpStrings,
        IntPtr lpRawData);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterEventSource(IntPtr hEventLog);
}
