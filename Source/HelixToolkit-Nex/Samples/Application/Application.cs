using Microsoft.Extensions.Logging;
using SDL3;
using Vortice.Vulkan;
using static SDL3.SDL3;

namespace HelixToolkit.Nex.Sample.Application;

public record ApplicationConfig
{
    public bool WindowResizable { get; set; } = false;
    public bool FullScreen { get; set; } = false;

    public int WindowWidth { get; set; } = 1311;
    public int WindowHeight { get; set; } = 1001;
}

public abstract class Application : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<Application>();

    private readonly bool _closeRequested = false;
    private bool _disposedValue;

    protected Application(ApplicationConfig? config = null)
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
        config ??= new ApplicationConfig();
        var flags = WindowFlags.None;
        if (config.WindowResizable)
        {
            flags |= WindowFlags.Resizable;
        }
        if (config.FullScreen)
        {
            flags = WindowFlags.Fullscreen;
        }

        // Create main window.
        MainWindow = new Window(Name, config.WindowWidth, config.WindowHeight, flags);
    }

    public abstract string Name { get; }

    public Window MainWindow { get; }

    protected virtual void Initialize() { }

    protected virtual void OnTick() { }

    public unsafe void Run()
    {
        Initialize();
        MainWindow.Show();

        bool running = true;
        bool paused = false;
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

                if (
                    evt.type == SDL_EventType.WindowCloseRequested
                    && evt.window.windowID == MainWindow.Id
                )
                {
                    running = false;
                    break;
                }
                else if (
                    evt.type == SDL_EventType.WindowHidden
                    || evt.type == SDL_EventType.WindowMinimized
                )
                {
                    _logger.LogInformation($"Window Event: {evt.window.type} - Pausing updates");
                    // Optionally, you could set a flag here to pause updates/rendering when the window is hidden or minimized.
                    paused = true;
                }
                else if (evt.type == SDL_EventType.WindowRestored)
                {
                    _logger.LogInformation($"Window Event: {evt.window.type} - Resuming updates");
                    paused = false;
                }
                else if (
                    evt.type >= SDL_EventType.WindowFirst
                    && evt.type <= SDL_EventType.WindowLast
                )
                {
                    HandleWindowEvent(evt);
                }
                else if (evt.type == SDL_EventType.MouseMotion)
                {
                    OnMouseMove(
                        SDL_abs((int)evt.motion.x),
                        SDL_abs((int)evt.motion.y),
                        SDL_abs((int)evt.motion.xrel),
                        SDL_abs((int)evt.motion.yrel)
                    );
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
                else if (evt.type == SDL_EventType.KeyDown)
                {
                    OnKeyDown(evt.key.scancode, evt.key.repeat);
                }
                else if (evt.type == SDL_EventType.KeyUp)
                {
                    OnKeyUp(evt.key.scancode);
                }
                else
                {
                    _logger.LogInformation($"Window Event: {evt.type}");
                }
            }

            if (!running)
                break;
            if (paused)
            {
                continue;
            }
            OnTick();
        }
    }

    private void HandleWindowEvent(in SDL_Event evt)
    {
        _logger.LogInformation($"Window Event: {evt.window.type}");
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
                var scale = SDL_GetWindowDisplayScale(MainWindow.Instance);
                OnDisplayScaleChanged(scale);
                break;
        }
    }

    protected virtual void OnDisplayScaleChanged(float scale) { }

    protected virtual void HandleResize(int width, int height) { }

    protected virtual void OnMouseMove(int x, int y, int xrel, int yrel) { }

    protected virtual void OnMouseEnter() { }

    protected virtual void OnMouseLeave() { }

    protected virtual void OnMouseButtonDown(SDL_Button button) { }

    protected virtual void OnMouseButtonUp(SDL_Button button) { }

    protected virtual void OnMouseWheel(int deltaX, int deltaY) { }

    protected virtual void OnKeyDown(SDL_Scancode scancode, bool repeat) { }

    protected virtual void OnKeyUp(SDL_Scancode scancode) { }

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
    private static void Log_SDL(
        SDL_LogCategory category,
        SDL_LogPriority priority,
        string? description
    )
    {
        if (priority >= SDL_LogPriority.Error)
        {
            _logger.LogError($"[{priority}] SDL: {description}");
            throw new Exception(description);
        }
        else
        {
            _logger.LogInformation($"[{priority}] SDL: {description}");
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                OnDisposing();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    protected virtual void OnDisposing()
    {
        // This method can be overridden to perform actions before disposal
        // For example, you might want to clean up resources or log messages
        _logger.LogInformation("Disposing application resources.");
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
