using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
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
        if (_demo?.ImGui is null)
        { return; }
        var io = ImGui.GetIO();
        io.AddMousePosEvent(x / _demo.ImGui.DisplayScale, y / _demo.ImGui.DisplayScale);
    }

    protected override void OnMouseButtonDown(SDL_Button button)
    {
        base.OnMouseButtonDown(button);
        var io = ImGui.GetIO();
        switch (button)
        {
            case SDL_Button.Left:
                io.AddMouseButtonEvent(0, true);
                break;
            case SDL_Button.Right:
                io.AddMouseButtonEvent(1, true);
                break;
            case SDL_Button.Middle:
                io.AddMouseButtonEvent(2, true);
                break;
        }
    }

    protected override void OnMouseButtonUp(SDL_Button button)
    {
        var io = ImGui.GetIO();
        switch (button)
        {
            case SDL_Button.Left:
                io.AddMouseButtonEvent(0, false);
                break;
            case SDL_Button.Right:
                io.AddMouseButtonEvent(1, false);
                break;
            case SDL_Button.Middle:
                io.AddMouseButtonEvent(2, false);
                break;
        }
    }

    protected override void OnMouseWheel(int deltaX, int deltaY)
    {
        var io = ImGui.GetIO();
        io.AddMouseWheelEvent(deltaX, deltaY);
    }

    protected override void OnDisposing()
    {
        _demo?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
