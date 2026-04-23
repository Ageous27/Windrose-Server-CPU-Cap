using System.ComponentModel;
using System.Text;

namespace CpuGuard.Service.Windows;

internal static class ProcessPathResolver
{
    public static bool TryGetExecutablePath(int pid, out string executablePath)
    {
        executablePath = string.Empty;

        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            inheritHandle: false,
            processId: pid);

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var builder = new StringBuilder(32768);
            var size = builder.Capacity;

            if (!NativeMethods.QueryFullProcessImageName(handle, 0, builder, ref size))
            {
                return false;
            }

            executablePath = Path.GetFullPath(builder.ToString(0, size));
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
