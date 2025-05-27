using System.Runtime.InteropServices;

namespace HelixToolkit.Nex;

public static class SystemInfo
{
    public static bool IsDebugBuild => Debugger.IsAttached; // Check if the debugger is attached to determine if it's a debug build
    public static bool IsReleaseBuild => !IsDebugBuild; // If not a debug build, it's a release build
    public static string GetCurrentPlatform()
    {
        return RuntimeInformation.OSDescription; // Get the current platform description
    }

    public static string GetCurrentFramework()
    {
        return RuntimeInformation.FrameworkDescription; // Get the current framework description
    }

    public static Architecture GetCurrentArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture; // Get the current process architecture
    }

    public static bool IsWindowsPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows); // Check if the current OS is Windows
    }

    public static bool IsLinuxPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux); // Check if the current OS is Linux
    }

    public static bool IsMacOSPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX); // Check if the current OS is macOS
    }

    public static bool IsArmArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm || RuntimeInformation.ProcessArchitecture == Architecture.Arm64; // Check if the current architecture is ARM or ARM64
    }
}
