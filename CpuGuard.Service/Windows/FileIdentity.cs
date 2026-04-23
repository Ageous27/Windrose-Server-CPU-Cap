using Microsoft.Win32.SafeHandles;

namespace CpuGuard.Service.Windows;

internal readonly record struct FileIdentity(uint VolumeSerialNumber, uint FileIndexHigh, uint FileIndexLow)
{
    public static bool TryFromHandle(SafeFileHandle handle, out FileIdentity identity)
    {
        if (NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            identity = new FileIdentity(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
            return true;
        }

        identity = default;
        return false;
    }

    public static bool TryFromPath(string path, out FileIdentity identity)
    {
        identity = default;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return TryFromHandle(stream.SafeFileHandle, out identity);
        }
        catch
        {
            return false;
        }
    }
}
