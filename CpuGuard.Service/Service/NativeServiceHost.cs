using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CpuGuard.Service.Service;

internal static class NativeServiceHost
{
    private const int SERVICE_CONTROL_STOP = 0x00000001;
    private const int SERVICE_CONTROL_SHUTDOWN = 0x00000005;

    private const int SERVICE_ACCEPT_STOP = 0x00000001;
    private const int SERVICE_ACCEPT_SHUTDOWN = 0x00000004;

    private const int SERVICE_STOPPED = 0x00000001;
    private const int SERVICE_START_PENDING = 0x00000002;
    private const int SERVICE_STOP_PENDING = 0x00000003;
    private const int SERVICE_RUNNING = 0x00000004;

    private static readonly object Sync = new();

    private static string _serviceName = string.Empty;
    private static Func<CancellationToken, Task>? _runner;
    private static CancellationTokenSource? _shutdown;
    private static Task? _serviceTask;
    private static IntPtr _statusHandle;
    private static SERVICE_STATUS _status;
    private static ManualResetEventSlim? _stopEvent;
    private static ServiceMainDelegate? _serviceMainDelegate;
    private static ServiceControlHandlerDelegate? _serviceControlDelegate;

    public static void Run(string serviceName, Func<CancellationToken, Task> serviceRunner)
    {
        _serviceName = serviceName;
        _runner = serviceRunner;
        _stopEvent = new ManualResetEventSlim(false);
        _serviceMainDelegate = ServiceMain;
        _serviceControlDelegate = ServiceControlHandler;

        var table = new[]
        {
            new SERVICE_TABLE_ENTRY { lpServiceName = _serviceName, lpServiceProc = _serviceMainDelegate },
            new SERVICE_TABLE_ENTRY()
        };

        if (!StartServiceCtrlDispatcher(table))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to connect to Service Control Manager.");
        }
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(_serviceName, _serviceControlDelegate!, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
        {
            return;
        }

        SetStatus(SERVICE_START_PENDING, 0, waitHint: 3000);
        _shutdown = new CancellationTokenSource();

        _serviceTask = Task.Run(async () =>
        {
            try
            {
                await _runner!(_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal stop.
            }
            finally
            {
                _stopEvent?.Set();
            }
        });

        SetStatus(SERVICE_RUNNING, SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN);
        _stopEvent!.Wait();
        SetStatus(SERVICE_STOPPED, 0);
    }

    private static int ServiceControlHandler(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case SERVICE_CONTROL_STOP:
            case SERVICE_CONTROL_SHUTDOWN:
                lock (Sync)
                {
                    SetStatus(SERVICE_STOP_PENDING, 0, waitHint: 30000);
                    _shutdown?.Cancel();
                }

                try
                {
                    _serviceTask?.Wait(TimeSpan.FromSeconds(30));
                }
                catch
                {
                    // Service is being stopped; ignore wait failures.
                }
                finally
                {
                    _stopEvent?.Set();
                }

                break;
        }

        return 0;
    }

    private static void SetStatus(int currentState, int controlsAccepted, int waitHint = 0)
    {
        _status.dwServiceType = 0x00000010; // SERVICE_WIN32_OWN_PROCESS
        _status.dwCurrentState = currentState;
        _status.dwControlsAccepted = controlsAccepted;
        _status.dwWin32ExitCode = 0;
        _status.dwServiceSpecificExitCode = 0;
        _status.dwCheckPoint = currentState is SERVICE_START_PENDING or SERVICE_STOP_PENDING ? 1u : 0u;
        _status.dwWaitHint = (uint)Math.Max(0, waitHint);

        _ = SetServiceStatus(_statusHandle, ref _status);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_TABLE_ENTRY
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpServiceName;
        public ServiceMainDelegate? lpServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(int dwNumServicesArgs, IntPtr lpServiceArgVectors);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ServiceControlHandlerDelegate(int dwControl, int dwEventType, IntPtr lpEventData, IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] SERVICE_TABLE_ENTRY[] lpServiceStartTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(
        string lpServiceName,
        ServiceControlHandlerDelegate lpHandlerProc,
        IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);
}
