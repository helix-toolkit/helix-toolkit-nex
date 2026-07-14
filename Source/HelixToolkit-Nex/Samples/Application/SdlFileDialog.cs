using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;
using static SDL3.SDL3;

namespace Demo.Utils;

/// <summary>
/// Native open-file dialog backed by SDL3's asynchronous dialog API.
/// </summary>
/// <remarks>
/// A GTK-based dialog (e.g. NativeFileDialogSharp) runs its own nested event loop and, on a
/// Wayland session, opens as an XWayland top-level that is detached from SDL's native Wayland
/// window — so it never surfaces and the open request appears to do nothing. SDL's own dialog
/// integrates with the same windowing backend SDL already owns and uses the desktop portal on
/// Linux, so it works consistently across Windows, Linux (X11/Wayland) and macOS.
/// </remarks>
public static unsafe class SdlFileDialog
{
    // The UI only ever shows one dialog at a time, so a single static slot is enough to route
    // the asynchronous result back to managed code from the unmanaged callback.
    private static Action<string?>? s_pending;
    private static SDL_DialogFileFilter* s_filters;
    private static int s_filterCount;

    /// <summary>
    /// Shows an asynchronous open-file dialog. <paramref name="onResult"/> is invoked later on the
    /// main thread (while SDL pumps events) with the chosen path, or <c>null</c> if the user
    /// cancelled or an error occurred. Re-entrant calls while a dialog is open are ignored.
    /// </summary>
    public static void OpenFile(
        SDL_Window window,
        Action<string?> onResult,
        params (string Name, string Pattern)[] filters)
    {
        if (s_pending is not null)
        {
            return;
        }
        s_pending = onResult;

        s_filterCount = filters.Length;
        if (s_filterCount > 0)
        {
            // The filter strings must stay alive until the async callback fires, so allocate them
            // on the native heap and release everything in the callback.
            s_filters = (SDL_DialogFileFilter*)NativeMemory.Alloc(
                (nuint)s_filterCount, (nuint)sizeof(SDL_DialogFileFilter));
            for (int i = 0; i < s_filterCount; i++)
            {
                s_filters[i].name = (byte*)Marshal.StringToCoTaskMemUTF8(filters[i].Name);
                s_filters[i].pattern = (byte*)Marshal.StringToCoTaskMemUTF8(filters[i].Pattern);
            }
        }

        SDL_ShowOpenFileDialog(
            &Callback,
            IntPtr.Zero,
            window,
            s_filters,
            s_filterCount,
            (byte*)null,
            false);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Callback(IntPtr userdata, byte** filelist, int filter)
    {
        Action<string?>? cb = s_pending;
        s_pending = null;
        FreeFilters();

        if (cb is null)
        {
            return;
        }

        // filelist == null -> error; filelist[0] == null -> the user cancelled.
        string? path = null;
        if (filelist is not null && filelist[0] is not null)
        {
            path = Marshal.PtrToStringUTF8((IntPtr)filelist[0]);
        }

        cb(path);
    }

    private static void FreeFilters()
    {
        if (s_filters is null)
        {
            return;
        }
        for (int i = 0; i < s_filterCount; i++)
        {
            Marshal.FreeCoTaskMem((IntPtr)s_filters[i].name);
            Marshal.FreeCoTaskMem((IntPtr)s_filters[i].pattern);
        }
        NativeMemory.Free(s_filters);
        s_filters = null;
        s_filterCount = 0;
    }
}
