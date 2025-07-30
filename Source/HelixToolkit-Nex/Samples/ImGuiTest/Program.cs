// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
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
        }, MainWindow.Instance, 0);
        var windowSize = MainWindow.Size;
        vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
        renderer = new ImGuiRenderer(vkContext, new ImGuiConfig());
        renderer.Initialize();
        pass.Colors[0].ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
        pass.Colors[0].LoadOp = LoadOp.Clear;
        pass.Colors[0].StoreOp = StoreOp.Store;
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
}