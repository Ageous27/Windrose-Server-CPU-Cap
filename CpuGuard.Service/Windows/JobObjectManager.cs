using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CpuGuard.Service.Windows;

public sealed class JobObjectManager : IDisposable
{
    private readonly IntPtr _jobHandle;
    private int? _attachedPid;
    private bool _capApplied;
    private bool _disposed;

    public JobObjectManager()
    {
        _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Job Object.");
        }
    }

    public bool IsAttachedTo(int pid) => _attachedPid == pid;

    public bool IsCapApplied => _capApplied;

    public void AttachToProcess(int pid)
    {
        ThrowIfDisposed();

        if (_attachedPid == pid)
        {
            return;
        }

        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_TERMINATE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            inheritHandle: false,
            processId: pid);

        if (processHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open process {pid}.");
        }

        try
        {
            if (!NativeMethods.AssignProcessToJobObject(_jobHandle, processHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to assign process {pid} to Job Object.");
            }

            _attachedPid = pid;
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }

    public void ApplyHardCap(double percent)
    {
        ThrowIfDisposed();
        var clampedPercent = Math.Clamp(percent, 0.01, 100.0);
        var cpuRate = (uint)Math.Clamp(
            (int)Math.Round(clampedPercent * 100.0, MidpointRounding.AwayFromZero),
            1,
            10000);

        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
            CpuRate = cpuRate
        };

        SetCpuInfo(info);
        _capApplied = true;
    }

    public void RemoveCap()
    {
        ThrowIfDisposed();

        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = 0,
            CpuRate = 0
        };

        SetCpuInfo(info);
        _capApplied = false;
    }

    private void SetCpuInfo(NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info)
    {
        var size = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);

            if (!NativeMethods.SetInformationJobObject(
                _jobHandle,
                NativeMethods.JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation,
                ptr,
                (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set Job Object CPU rate.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(JobObjectManager));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_jobHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_jobHandle);
        }
    }
}
