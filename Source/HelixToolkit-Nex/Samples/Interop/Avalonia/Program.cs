using Avalonia;

namespace AvaloniaInterop;

/// <summary>
/// Entry point for the cross-platform Avalonia interop sample. Boots the Avalonia application using
/// the classic desktop lifetime so the sample runs as a normal windowed desktop app on both Windows
/// and Linux.
/// </summary>
internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet and stuff will break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
