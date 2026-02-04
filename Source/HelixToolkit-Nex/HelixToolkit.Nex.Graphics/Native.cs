namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Provides platform-specific native API calls.
/// </summary>
public static class Native
{
    /// <summary>
    /// Gets the module handle for the specified module name (Windows only).
    /// </summary>
    /// <param name="lpModuleName">The name of the module, or null for the executable file of the current process.</param>
    /// <returns>A handle to the specified module, or IntPtr.Zero if the module is not found.</returns>
    /// <remarks>
    /// This is a wrapper for the Windows kernel32.dll GetModuleHandleA function.
    /// Only supported on Windows platforms.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleA", CharSet = CharSet.Ansi)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
