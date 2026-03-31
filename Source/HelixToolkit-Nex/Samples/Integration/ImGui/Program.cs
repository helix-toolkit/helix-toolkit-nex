// See https://aka.ms/new-console-template for more information
using System.Data;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Examples;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using ImGuiTest;
using Microsoft.Extensions.Logging;
using SDL3;

using var app = new App();
app.Run();

internal class App : Application
{
    public override string Name => "ImGui Editor";
    private static readonly ILogger logger = LogManager.Create<App>();
    private IContext? _ctx;
    private Editor? _example;
    private int _mouseX,
        _mouseY;

    // Keyboard state for first-person camera
    private bool _keyW,
        _keyS,
        _keyA,
        _keyD,
        _keySpace,
        _keyCtrl,
        _keyShift;

    public App()
        : base(
            new ApplicationConfig()
            {
                WindowResizable = true,
                WindowWidth = 1280,
                WindowHeight = 1080,
            }
        )
    {
        // You can set additional application settings here if needed
    }

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

        _example = new Editor(_ctx);
        _example.Initialize(windowSize.Width, windowSize.Height);
    }

    protected override void HandleResize(int width, int height)
    {
        _ctx?.RecreateSwapchain(width, height);
        base.HandleResize(width, height);
    }

    protected override void OnDisplayScaleChanged(float scale)
    {
        if (_example?.ImGui != null && scale != 0)
        {
            _example.ImGui.DisplayScale = scale;
        }
        base.OnDisplayScaleChanged(scale);
    }

    protected override void OnTick()
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(MainWindow.Size.Width, MainWindow.Size.Height);
        ImGui.SetNextWindowSizeConstraints(
            Vector2.Zero,
            new Vector2(MainWindow.Size.Width, MainWindow.Size.Height)
        );

        // Forward keyboard state to the editor for first-person camera
        _example?.OnKeyboardInput(_keyW, _keyS, _keyA, _keyD, _keySpace, _keyCtrl, _keyShift);

        _example?.Render(MainWindow.Size.Width, MainWindow.Size.Height);
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        _mouseX = x;
        _mouseY = y;
        if (_example?.ImGui != null)
        {
            var io = ImGui.GetIO();
            io.AddMousePosEvent(x / _example.ImGui.DisplayScale, y / _example.ImGui.DisplayScale);
        }
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

    protected override void OnKeyDown(SDL_Scancode scancode, bool repeat)
    {
        // Forward to ImGui
        var io = ImGui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), true);

        // Don't forward to camera if ImGui is capturing keyboard
        if (io.WantCaptureKeyboard)
            return;

        switch (scancode)
        {
            case SDL_Scancode.W:
                _keyW = true;
                break;
            case SDL_Scancode.S:
                _keyS = true;
                break;
            case SDL_Scancode.A:
                _keyA = true;
                break;
            case SDL_Scancode.D:
                _keyD = true;
                break;
            case SDL_Scancode.Space:
                _keySpace = true;
                break;
            case SDL_Scancode.LeftControl or SDL_Scancode.RightControl:
                _keyCtrl = true;
                break;
            case SDL_Scancode.LeftShift or SDL_Scancode.RightShift:
                _keyShift = true;
                break;
        }
    }

    protected override void OnKeyUp(SDL_Scancode scancode)
    {
        // Forward to ImGui
        var io = ImGui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), false);

        switch (scancode)
        {
            case SDL_Scancode.W:
                _keyW = false;
                break;
            case SDL_Scancode.S:
                _keyS = false;
                break;
            case SDL_Scancode.A:
                _keyA = false;
                break;
            case SDL_Scancode.D:
                _keyD = false;
                break;
            case SDL_Scancode.Space:
                _keySpace = false;
                break;
            case SDL_Scancode.LeftControl or SDL_Scancode.RightControl:
                _keyCtrl = false;
                break;
            case SDL_Scancode.LeftShift or SDL_Scancode.RightShift:
                _keyShift = false;
                break;
        }
    }

    private static ImGuiKey SdlScancodeToImGuiKey(SDL_Scancode scancode)
    {
        return scancode switch
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
            SDL_Scancode.Tab => ImGuiKey.Tab,
            SDL_Scancode.Return => ImGuiKey.Enter,
            SDL_Scancode.Backspace => ImGuiKey.Backspace,
            SDL_Scancode.Delete => ImGuiKey.Delete,
            SDL_Scancode.Left => ImGuiKey.LeftArrow,
            SDL_Scancode.Right => ImGuiKey.RightArrow,
            SDL_Scancode.Up => ImGuiKey.UpArrow,
            SDL_Scancode.Down => ImGuiKey.DownArrow,
            _ => ImGuiKey.None,
        };
    }

    protected override void OnDisposing()
    {
        _example?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
