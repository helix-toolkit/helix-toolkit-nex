using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using SDL3;

using var app = new PointsApp();
app.Run();

internal sealed class PointsApp : Application
{
    public override string Name => "Point Cloud Demo";
    private static readonly ILogger _logger = LogManager.Create<PointsApp>();
    private HelixToolkit.Nex.Graphics.IContext? _ctx;
    private PointsDemo? _demo;

    // Keyboard state for camera
    private bool _keyW,
        _keyS,
        _keyA,
        _keyD,
        _keySpace,
        _keyCtrl,
        _keyShift;

    public PointsApp()
        : base(
            new ApplicationConfig
            {
                WindowResizable = true,
                WindowWidth = 1400,
                WindowHeight = 900,
            }
        )
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

        _demo = new PointsDemo(_ctx);
        _demo.Initialize(windowSize.Width, windowSize.Height);
    }

    protected override void HandleResize(int width, int height)
    {
        _ctx?.RecreateSwapchain(width, height);
    }

    protected override void OnDisplayScaleChanged(float scale)
    {
        if (_demo?.ImGui is not null && scale != 0)
            _demo.ImGui.DisplayScale = scale;
    }

    protected override void OnTick()
    {
        if (_demo is null)
            return;
        var io = ImGuiNET.ImGui.GetIO();
        io.DisplaySize = new Vector2(MainWindow.Size.Width, MainWindow.Size.Height);
        _demo.OnKeyboardInput(_keyW, _keyS, _keyA, _keyD, _keySpace, _keyCtrl, _keyShift);
        _demo.Render(MainWindow.Size.Width, MainWindow.Size.Height);
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        if (_demo?.ImGui is not null)
        {
            var io = ImGuiNET.ImGui.GetIO();
            io.AddMousePosEvent(x / _demo.ImGui.DisplayScale, y / _demo.ImGui.DisplayScale);
        }
    }

    protected override void OnMouseButtonDown(SDL_Button button)
    {
        var io = ImGuiNET.ImGui.GetIO();
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
        var io = ImGuiNET.ImGui.GetIO();
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
        var io = ImGuiNET.ImGui.GetIO();
        io.AddMouseWheelEvent(deltaX, deltaY);
    }

    protected override void OnKeyDown(SDL_Scancode scancode, bool repeat)
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.AddKeyEvent(MapKey(scancode), true);
        if (io.WantCaptureKeyboard)
            return;
        SetKey(scancode, true);
    }

    protected override void OnKeyUp(SDL_Scancode scancode)
    {
        var io = ImGuiNET.ImGui.GetIO();
        io.AddKeyEvent(MapKey(scancode), false);
        SetKey(scancode, false);
    }

    private void SetKey(SDL_Scancode sc, bool down)
    {
        switch (sc)
        {
            case SDL_Scancode.W:
                _keyW = down;
                break;
            case SDL_Scancode.S:
                _keyS = down;
                break;
            case SDL_Scancode.A:
                _keyA = down;
                break;
            case SDL_Scancode.D:
                _keyD = down;
                break;
            case SDL_Scancode.Space:
                _keySpace = down;
                break;
            case SDL_Scancode.LeftControl or SDL_Scancode.RightControl:
                _keyCtrl = down;
                break;
            case SDL_Scancode.LeftShift or SDL_Scancode.RightShift:
                _keyShift = down;
                break;
        }
    }

    private static ImGuiKey MapKey(SDL_Scancode sc) =>
        sc switch
        {
            SDL_Scancode.W => ImGuiKey.W,
            SDL_Scancode.A => ImGuiKey.A,
            SDL_Scancode.S => ImGuiKey.S,
            SDL_Scancode.D => ImGuiKey.D,
            SDL_Scancode.Space => ImGuiKey.Space,
            SDL_Scancode.LeftControl => ImGuiKey.LeftCtrl,
            SDL_Scancode.RightControl => ImGuiKey.RightCtrl,
            SDL_Scancode.LeftShift => ImGuiKey.LeftShift,
            SDL_Scancode.RightShift => ImGuiKey.RightShift,
            SDL_Scancode.Escape => ImGuiKey.Escape,
            _ => ImGuiKey.None,
        };

    protected override void OnDisposing()
    {
        _demo?.Dispose();
        _ctx?.Dispose();
    }
}
