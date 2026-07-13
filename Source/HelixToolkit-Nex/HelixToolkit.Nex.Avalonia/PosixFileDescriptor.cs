using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Minimal POSIX file-descriptor helper for the Linux external-memory interop path.
/// <para>
/// The image/semaphore fds the <see cref="LinuxExternalMemoryBridge"/> exports are handed to
/// Avalonia's <c>ICompositionGpuInterop.ImportImage</c> / <c>ImportSemaphore</c>. Per the Avalonia
/// contract ("if import operation fails, the caller is responsible for destroying the handle"), a
/// <b>successful</b> import transfers ownership of the passed handle to the compositor's Vulkan
/// backend (which consumes it via <c>vkImport*FdKHR</c>); on <b>failure</b> the caller must close it.
/// </para>
/// <para>
/// The bridge itself continues to own and close the ORIGINAL exported fd (through its own
/// <see cref="SafeFileHandle"/>). To avoid sharing ownership of a single descriptor between the
/// bridge and the compositor, the import sites duplicate the fd with <see cref="Dup"/> and hand the
/// compositor the duplicate. The duplicate is returned as a <see cref="SafeFileHandle"/> so its
/// lifetime is managed like any other handle: on a failed import the caller disposes it; on a
/// successful import the caller relinquishes it with <see cref="SafeHandle.SetHandleAsInvalid"/>
/// because the compositor now owns it. That way neither side closes the other's descriptor: a double
/// close recycles the fd number and makes a later <c>vkImport*FdKHR</c> fail with
/// "DRM_IOCTL_SYNCOBJ_FD_TO_HANDLE failed: Invalid argument".
/// </para>
/// </summary>
internal static partial class PosixFileDescriptor
{
    /// <summary>
    /// Duplicates a file descriptor (Linux only, via <c>dup</c>) and wraps the result in an owning
    /// <see cref="SafeFileHandle"/> that closes it on disposal. Returns an invalid handle when the
    /// input is negative, the host is not Linux, or the syscall fails.
    /// </summary>
    public static SafeFileHandle Dup(int fd)
    {
        if (fd < 0 || !OperatingSystem.IsLinux())
        {
            return new SafeFileHandle(new IntPtr(-1), ownsHandle: true);
        }
        return new SafeFileHandle(new IntPtr(NativeDup(fd)), ownsHandle: true);
    }

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static partial int NativeDup(int fd);
}
