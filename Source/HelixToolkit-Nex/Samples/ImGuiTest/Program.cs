// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using SDL3;
using System.Numerics;

Console.WriteLine("Hello, World!");

using var app = new App();
app.Run();

class App : Application
{
    IContext? vkContext;
    ImGuiRenderer? renderer;
    Framebuffer framebuffer = new Framebuffer();
    RenderPass pass = new();
    Dependencies dp = new();
    public override string Name => "ImGui Test Application";

    protected override void Initialize()
    {
        vkContext = VulkanBuilder.Create(new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            OnCreateSurface = CreateSurface,
            ForcePresentModeFIFO = true,
        }, MainWindow.Instance, 0);
        var windowSize = MainWindow.Size;
        vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
        renderer = new ImGuiRenderer(vkContext, new ImGuiConfig());
        renderer.Initialize();
        pass.Colors[0].ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
        pass.Colors[0].LoadOp = LoadOp.Clear;
    }

    protected override void OnDisplayScaleChanged(float scaleX, float scaleY)
    {
        if (renderer != null && scaleX != 0)
        {
            // Update the ImGui renderer with the new display scale.
            renderer.DisplayScale = scaleX;
        }
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        if (renderer == null)
        {
            return; // Renderer not initialized, cannot set cursor position.
        }
        var io = ImGui.GetIO();
        io.AddMousePosEvent(x, y);
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
            default:
                break;
        }
    }

    protected override void OnMouseButtonUp(SDL_Button button)
    {
        base.OnMouseButtonUp(button);
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
            default:
                break;
        }
    }

    protected override void OnMouseWheel(int deltaX, int deltaY)
    {
        var io = ImGui.GetIO();
        io.AddMouseWheelEvent(deltaX, deltaY);
    }

    protected override void OnTick()
    {
        if (vkContext == null || renderer == null)
        {
            return;
        }
        var tex = vkContext.GetCurrentSwapchainTexture();
        if (tex.Empty)
        {
            return; // No swapchain texture available, nothing to render to.
        }
        framebuffer.Colors[0].Texture = tex;
        var cmdBuf = vkContext.AcquireCommandBuffer();
        cmdBuf.BeginRendering(pass, framebuffer, dp);
        renderer.BeginFrame(framebuffer);
        ImGui.ShowDemoWindow();

        renderer.EndFrame(cmdBuf);
        cmdBuf.EndRendering();
        vkContext.Submit(cmdBuf, tex);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        renderer?.Dispose();
    }
}