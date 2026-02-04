using System.Runtime.InteropServices;

namespace HelixToolkit.Nex;

/// <summary>
/// Provides system and runtime information about the current platform and environment.
/// </summary>
public static class SystemInfo
{
    /// <summary>
    /// Gets a value indicating whether the application is running in debug mode.
    /// </summary>
    /// <value>
    /// True if a debugger is attached; otherwise, false.
    /// </value>
    public static bool IsDebugBuild => Debugger.IsAttached; // Check if the debugger is attached to determine if it's a debug build

    /// <summary>
    /// Gets a value indicating whether the application is running in release mode.
    /// </summary>
    /// <value>
    /// True if no debugger is attached; otherwise, false.
    /// </value>
    public static bool IsReleaseBuild => !IsDebugBuild; // If not a debug build, it's a release build

    /// <summary>
    /// Gets a description of the current operating system platform.
    /// </summary>
    /// <returns>A string describing the OS platform (e.g., "Microsoft Windows 10.0.19045").</returns>
    public static string GetCurrentPlatform()
    {
        return RuntimeInformation.OSDescription; // Get the current platform description
    }

    /// <summary>
    /// Gets a description of the current .NET framework.
    /// </summary>
    /// <returns>A string describing the framework (e.g., ".NET 8.0.1").</returns>
    public static string GetCurrentFramework()
    {
        return RuntimeInformation.FrameworkDescription; // Get the current framework description
    }

    /// <summary>
    /// Gets the processor architecture of the current process.
    /// </summary>
    /// <returns>The <see cref="Architecture"/> of the process (e.g., X64, X86, Arm, Arm64).</returns>
    public static Architecture GetCurrentArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture; // Get the current process architecture
    }

    /// <summary>
    /// Determines whether the current operating system is Windows.
    /// </summary>
    /// <returns>True if running on Windows; otherwise, false.</returns>
    public static bool IsWindowsPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows); // Check if the current OS is Windows
    }

    /// <summary>
    /// Determines whether the current operating system is Linux.
    /// </summary>
    /// <returns>True if running on Linux; otherwise, false.</returns>
    public static bool IsLinuxPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux); // Check if the current OS is Linux
    }

    /// <summary>
    /// Determines whether the current operating system is macOS.
    /// </summary>
    /// <returns>True if running on macOS; otherwise, false.</returns>
    public static bool IsMacOSPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX); // Check if the current OS is macOS
    }

    /// <summary>
    /// Determines whether the current processor architecture is ARM-based.
    /// </summary>
    /// <returns>True if the architecture is ARM or ARM64; otherwise, false.</returns>
    public static bool IsArmArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm || RuntimeInformation.ProcessArchitecture == Architecture.Arm64; // Check if the current architecture is ARM or ARM64
    }
}
