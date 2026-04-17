using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;
using SDL3;

using var app = new PickingApp();
app.Run();

internal sealed class PickingApp : Application
{
    public override string Name => "Picking Test";
    private static readonly ILogger _logger = LogManager.Create<PickingApp>();
    private HelixToolkit.Nex.Graphics.IContext? _ctx;
    private PickingDemo? _demo;
    private int _mouseX, _mouseY;
    private bool _isRotating, _isPanning;

    public PickingApp()
        : base(new ApplicationConfig { WindowResizable = true, WindowWidth = 1400, WindowHeight = 900 })
    { }

    protected override void Initialize()
    {
        base.Initialize();
        _ctx = VulkanBuilder.Create(
            new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = CreateSurface,
            },
            MainWindow.Instance,
            0
        );
        var windowSize = MainWindow.Size;
        _ctx.RecreateSwapchain(windowSize.Width, windowSize.Height);

        _demo = new PickingDemo(_ctx);
        _demo.Initialize(windowSize.Width, windowSize.Height);
    }

    protected override void HandleResize(int width, int height)
    {
        _ctx?.RecreateSwapchain(width, height);
        base.HandleResize(width, height);
    }

    protected override void OnTick()
    {
        _demo?.Render(MainWindow.Size.Width, MainWindow.Size.Height);
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        _mouseX = x;
        _mouseY = y;
        _demo?.OnMouseMove(x, y, _isRotating, _isPanning);
    }

    protected override void OnMouseButtonDown(SDL_Button button)
    {
        switch (button)
        {
            case SDL_Button.Right:
                _isRotating = true;
                _demo?.OnMouseDown(1, _mouseX, _mouseY);
                break;
            case SDL_Button.Middle:
                _isPanning = true;
                _demo?.OnMouseDown(2, _mouseX, _mouseY);
                break;
        }
    }

    protected override void OnMouseButtonUp(SDL_Button button)
    {
        switch (button)
        {
            case SDL_Button.Left:
                _demo?.Pick(_mouseX, _mouseY);
                break;
            case SDL_Button.Right:
                _isRotating = false;
                break;
            case SDL_Button.Middle:
                _isPanning = false;
                break;
        }
    }

    protected override void OnMouseWheel(int deltaX, int deltaY)
    {
        _demo?.OnMouseWheel(deltaY);
    }

    protected override void OnDisposing()
    {
        _demo?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
