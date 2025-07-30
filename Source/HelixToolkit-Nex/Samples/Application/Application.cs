using Microsoft.Extensions.Logging;
using SDL3;
using Vortice.Vulkan;
using static SDL3.SDL3;

namespace HelixToolkit.Nex.Sample.Application;

public abstract class Application : IDisposable
{
    static readonly ILogger logger = LogManager.Create<Application>();

    private bool _closeRequested = false;
    private bool disposedValue;

    protected unsafe Application()
    {
        if (!SDL_Init(SDL_InitFlags.Video))
        {
            throw new PlatformNotSupportedException("SDL is not supported");
        }

        SDL_SetLogOutputFunction(Log_SDL);

        if (!SDL_Vulkan_LoadLibrary())
        {
            throw new PlatformNotSupportedException("SDL: Failed to init vulkan");
        }

        // Create main window.
        MainWindow = new Window(Name, 1280, 720);
    }

    public abstract string Name { get; }

    public Window MainWindow { get; }

    protected virtual void Initialize()
    {

    }

    protected virtual void OnTick()
    {
    }

    public unsafe void Run()
    {
        Initialize();
        MainWindow.Show();

        bool running = true;

        while (running && !_closeRequested)
        {
            SDL_Event evt;
            while (SDL_PollEvent(&evt))
            {
                if (evt.type == SDL_EventType.Quit)
                {
                    running = false;
                    break;
                }

                if (evt.type == SDL_EventType.WindowCloseRequested && evt.window.windowID == MainWindow.Id)
                {
                    running = false;
                    break;
                }
                else if (evt.type >= SDL_EventType.WindowFirst && evt.type <= SDL_EventType.WindowLast)
                {
                    HandleWindowEvent(evt);
                }
            }

            if (!running)
                break;

            OnTick();
        }
    }

    private void HandleWindowEvent(in SDL_Event evt)
    {
        switch (evt.window.type)
        {
            case SDL_EventType.WindowResized:
                //_minimized = false;
                HandleResize(evt);
                break;
        }
    }

    protected virtual void HandleResize(in SDL_Event evt) { }

    protected VkSurfaceKHR CreateSurface(VkInstance instance)
    {
        unsafe
        {
            VkSurfaceKHR surface;
            if (!SDL_Vulkan_CreateSurface(MainWindow.Instance, instance, 0, (ulong**)&surface))
            {
                throw new PlatformNotSupportedException("SDL: Failed to create vulkan surface");
            }
            return surface;
        }
    }

    //[UnmanagedCallersOnly]
    private static void Log_SDL(SDL_LogCategory category, SDL_LogPriority priority, string? description)
    {
        if (priority >= SDL_LogPriority.Error)
        {
            logger.LogError($"[{priority}] SDL: {description}");
            throw new Exception(description);
        }
        else
        {
            logger.LogInformation($"[{priority}] SDL: {description}");
        }
    }

    void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                OnDisposing();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    protected virtual void OnDisposing()
    {
        // This method can be overridden to perform actions before disposal
        // For example, you might want to clean up resources or log messages
        logger.LogInformation("Disposing application resources.");
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}