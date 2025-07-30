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
                else if (evt.type == SDL_EventType.MouseMotion)
                {
                    OnMouseMove(SDL_abs((int)evt.motion.x), SDL_abs((int)evt.motion.y), SDL_abs((int)evt.motion.xrel), SDL_abs((int)evt.motion.yrel));
                }
                else if (evt.type == SDL_EventType.MouseButtonDown)
                {
                    OnMouseButtonDown(evt.button.Button);
                }
                else if (evt.type == SDL_EventType.MouseButtonUp)
                {
                    OnMouseButtonUp(evt.button.Button);
                }
                else if (evt.type == SDL_EventType.MouseWheel)
                {
                    OnMouseWheel((int)evt.wheel.x, (int)evt.wheel.y);
                }
                else
                {
                    logger.LogInformation($"Window Event: {evt.type}");
                }
            }

            if (!running)
                break;

            OnTick();
        }
    }

    private void HandleWindowEvent(in SDL_Event evt)
    {
        logger.LogInformation($"Window Event: {evt.window.type}");
        switch (evt.window.type)
        {
            case SDL_EventType.WindowResized:
                HandleResize(evt.window.data1, evt.window.data2);
                break;
            case SDL_EventType.WindowMouseEnter:
                OnMouseEnter();
                break;
            case SDL_EventType.WindowMouseLeave:
                OnMouseLeave();
                break;
            case SDL_EventType.WindowDisplayScaleChanged:
                OnDisplayScaleChanged(evt.window.data1 / 100.0f, evt.window.data2 / 100.0f);
                break;
        }
    }

    protected virtual void OnDisplayScaleChanged(float scaleX, float scaleY)
    {
    }

    protected virtual void HandleResize(int width, int height) { }

    protected virtual void OnMouseMove(int x, int y, int xrel, int yrel)
    {
    }

    protected virtual void OnMouseEnter()
    {

    }

    protected virtual void OnMouseLeave()
    {
    }

    protected virtual void OnMouseButtonDown(SDL_Button button)
    {

    }

    protected virtual void OnMouseButtonUp(SDL_Button button)
    {
    }

    protected virtual void OnMouseWheel(int deltaX, int deltaY) { }

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