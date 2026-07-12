using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Minimal POSIX file-descriptor helpers for the Linux external-memory interop path.
/// <para>
/// The image/semaphore fds the <see cref="LinuxExternalMemoryBridge"/> exports are handed to
/// Avalonia's <c>ICompositionGpuInterop.ImportImage</c> / <c>ImportSemaphore</c>. Per the Avalonia
/// contract ("if import operation fails, the caller is responsible for destroying the handle"), a
/// <b>successful</b> import transfers ownership of the passed handle to the compositor's Vulkan
/// backend (which consumes it via <c>vkImport*FdKHR</c>); on <b>failure</b> the caller must close it.
/// </para>
/// <para>
/// The bridge itself continues to own and close the ORIGINAL exported fd. To avoid sharing ownership
/// of a single descriptor between the bridge and the compositor, the import sites duplicate the fd
/// with <see cref="Dup"/> and hand the compositor the duplicate. That way neither side closes the
/// other's descriptor: a double close recycles the fd number and makes a later <c>vkImport*FdKHR</c>
/// fail with "DRM_IOCTL_SYNCOBJ_FD_TO_HANDLE failed: Invalid argument".
/// </para>
/// </summary>
internal static class PosixFileDescriptor
{
    /// <summary>
    /// Duplicates a file descriptor (Linux only, via <c>dup</c>). Returns the new descriptor, or
    /// <c>-1</c> when the input is negative, the host is not Linux, or the syscall fails.
    /// </summary>
    public static int Dup(int fd)
    {
        if (fd < 0 || !OperatingSystem.IsLinux())
        {
            return -1;
        }
        return NativeDup(fd);
    }

    /// <summary>Closes a file descriptor (Linux only); a no-op off Linux or for a negative fd.</summary>
    public static void Close(int fd)
    {
        if (fd < 0 || !OperatingSystem.IsLinux())
        {
            return;
        }
        _ = NativeClose(fd);
    }

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int NativeDup(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int NativeClose(int fd);
}
