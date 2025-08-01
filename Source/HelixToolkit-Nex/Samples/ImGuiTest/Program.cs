// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using ImGuiTest;
using SDL3;
using System.Numerics;

Console.WriteLine("Hello, World!");

using var app = new App();
app.Run();

class App : Application
{
    IContext? vkContext;
    ImGuiRenderer? guiRenderer;
    Framebuffer framebuffer = new();
    RenderPass pass = new();
    Dependencies dp = new();
    TextureResource frameTexture = TextureResource.Null;
    ShaderRenderer? shaderRenderer = null;
    public override string Name => "ImGui Test Application";

    protected override void Initialize()
    {
        vkContext = VulkanBuilder.Create(new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            OnCreateSurface = CreateSurface,
            ForcePresentModeFIFO = true,
        }, MainWindow.Instance, 0);
        shaderRenderer = new ShaderRenderer(vkContext);
        shaderRenderer.Initialize();
        var windowSize = MainWindow.Size;
        vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
        guiRenderer = new ImGuiRenderer(vkContext, new ImGuiConfig());
        guiRenderer.Initialize();
        pass.Colors[0].ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
        pass.Colors[0].LoadOp = LoadOp.Clear;

        frameTexture = vkContext.CreateTexture(new TextureDesc()
        {
            Format = Format.RGBA_UN8,
            Dimensions = new((uint)windowSize.Width, (uint)windowSize.Height, 1),
            Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment
        },
            "Frame Texture");

        dp.Textures[0] = frameTexture;
    }

    protected override void OnDisplayScaleChanged(float scaleX, float scaleY)
    {
        if (guiRenderer != null && scaleX != 0)
        {
            // Update the ImGui guiRenderer with the new display scale.
            guiRenderer.DisplayScale = scaleX;
        }
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        if (guiRenderer == null)
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
        if (vkContext == null || guiRenderer == null)
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
        shaderRenderer?.Render(cmdBuf, MainWindow.Size, frameTexture);

        cmdBuf.BeginRendering(pass, framebuffer, dp);
        guiRenderer.BeginFrame(framebuffer);
        ImGui.ShowDemoWindow();
        ImGui.Begin("Hello, ImGui!");
        ImGui.Text("This is a simple ImGui test application.");
        ImGui.Text($"Current time: {DateTime.Now:HH:mm:ss}");
        ImGui.Text($"Window size: {MainWindow.Size.Width}x{MainWindow.Size.Height}");
        ImGui.Text($"Display scale: {guiRenderer.DisplayScale}");
        ImGui.Image((nint)frameTexture.Index, new Vector2(MainWindow.Size.Width / 2, MainWindow.Size.Height / 2), new Vector2(0, 0), new Vector2(1, 1));
        ImGui.End();
        guiRenderer.EndFrame(cmdBuf);
        cmdBuf.EndRendering();
        vkContext.Submit(cmdBuf, tex);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        shaderRenderer?.Dispose();
        guiRenderer?.Dispose();
    }
}