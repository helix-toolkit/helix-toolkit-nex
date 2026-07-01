using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using SDL3;
// Alias the demo type: the class lives in the `InstancingDemo` namespace and shares its name, so the
// bare identifier would collide with the namespace from this global-namespace top-level file.
using Demo = InstancingDemo.InstancingDemo;

using var app = new App();
app.Run();

/// <summary>
/// Application host for the instancing integration demo. Owns the window, the Vulkan graphics
/// <see cref="IContext"/>, and the SDL-driven application loop. It forwards lifecycle callbacks to the
/// <see cref="Demo"/> and feeds mouse/keyboard input into ImGui so the in-app viewport and its camera
/// controller can respond to interaction.
/// </summary>
internal sealed class App : Application
{
    public override string Name => "Instancing Integration Demo";

    private static readonly ILogger _logger = LogManager.Create<App>();

    private IContext? _ctx;
    private Demo? _demo;

    public App()
        : base(
            new ApplicationConfig()
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

        _demo = new Demo(_ctx);
        _demo.Initialize(windowSize.Width, windowSize.Height);
    }

    protected override void HandleResize(int width, int height)
    {
        _ctx?.RecreateSwapchain(width, height);
        _demo?.Resize(width, height);
        base.HandleResize(width, height);
    }

    protected override void OnDisplayScaleChanged(float scale)
    {
        if (_demo is { ImGui: { } imGui } && MathF.Abs(scale) > float.Epsilon)
        {
            imGui.DisplayScale = scale;
        }
        base.OnDisplayScaleChanged(scale);
    }

    protected override void OnTick()
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(MainWindow.Size.Width, MainWindow.Size.Height);
        _demo?.Render(MainWindow.Size.Width, MainWindow.Size.Height);
    }

    // -----------------------------------------------------------------------
    // ImGui input forwarding (drives the in-app Viewport + camera controller)
    // -----------------------------------------------------------------------

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        var imGui = _demo?.ImGui;
        if (imGui != null)
        {
            var io = ImGui.GetIO();
            io.AddMousePosEvent(x / imGui.DisplayScale, y / imGui.DisplayScale);
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
        var io = ImGui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), true);
    }

    protected override void OnKeyUp(SDL_Scancode scancode)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), false);
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
        _demo?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
