namespace HelixToolkit.Nex.Graphics;

public static class Native
{
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleA", CharSet = CharSet.Ansi)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
