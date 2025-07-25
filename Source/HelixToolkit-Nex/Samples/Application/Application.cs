using SDL3;
using static SDL3.SDL3;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Sample.Application;

public abstract class Application : IDisposable
{
    static readonly ILogger logger = LogManager.Create<Application>();

    private bool _closeRequested = false;

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

    public virtual void Dispose()
    {
    }

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
}