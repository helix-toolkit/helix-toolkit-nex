// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using ImGuiNET;
using ImGuiTest;
using SDL3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Runtime.InteropServices;

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
    ShaderToyRenderer? shaderToyRenderer = null;
    TextureResource imageSample = TextureResource.Null;
    uint toySelection = 0;

    public override string Name => "ImGui Test Application";

    protected override void Initialize()
    {
        vkContext = VulkanBuilder.Create(new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            OnCreateSurface = CreateSurface,
            ForcePresentModeFIFO = true,
        }, MainWindow.Instance, 0);
        shaderToyRenderer = new ShaderToyRenderer(vkContext);
        shaderToyRenderer.Initialize();
        var windowSize = MainWindow.Size;
        vkContext.RecreateSwapchain(windowSize.Width, windowSize.Height);
        guiRenderer = new ImGuiRenderer(vkContext, new ImGuiConfig());
        guiRenderer.Initialize();
        pass.Colors[0].ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
        pass.Colors[0].LoadOp = LoadOp.Clear;

        frameTexture = vkContext.CreateTexture(new TextureDesc()
        {
            Format = Format.RGBA_UN8,
            Dimensions = new(512, 512, 1),
            Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment
        },
            "Frame Texture");

        dp.Textures[0] = frameTexture;

        LoadImageSample();
    }

    private void LoadImageSample()
    {
        Configuration.Default.PreferContiguousImageBuffers = true;
        using var image = Image.Load<Rgba32>("Assets/image_sample.jpg");
        if (image == null || image.Width == 0 || image.Height == 0)
        {
            throw new Exception("Failed to load noise texture.");
        }
        image.Mutate(x => x.Resize(2048, 2048, KnownResamplers.Bicubic));
        if (image!.DangerousTryGetSinglePixelMemory(out var pixels))
        {
            using var data = pixels.Pin();
            unsafe
            {
                var textureDesc = new TextureDesc()
                {
                    Type = TextureType.Texture2D,
                    Dimensions = new((uint)image.Width, (uint)image.Height, 1),
                    Format = Format.RGBA_SRGB8,
                    Usage = TextureUsageBits.Sampled,
                    Data = (nint)data.Pointer,
                    DataSize = (uint)(image.Width * image.Height * Marshal.SizeOf<Rgba32>()),
                };
                imageSample = vkContext!.CreateTexture(textureDesc, "ShaderRenderer: Image Sample");
            }
        }
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
        shaderToyRenderer?.Render(cmdBuf, toySelection, new Vector2(512, 512), frameTexture);

        cmdBuf.BeginRendering(pass, framebuffer, dp);
        guiRenderer.BeginFrame(framebuffer);
        ImGui.ShowDemoWindow();
        ImGui.Begin("Hello, ImGui!");
        ImGui.Text("This is a simple ImGui test application.");
        ImGui.Text($"Current time: {DateTime.Now:HH:mm:ss}");
        ImGui.Text($"Window size: {MainWindow.Size.Width}x{MainWindow.Size.Height}");
        ImGui.Text($"Display scale: {guiRenderer.DisplayScale}");
        ImGui.End();
        ImGui.Begin("ShaderToy Renderer");
        if (ImGui.BeginCombo("ShaderToy Renderer Options", shaderToyRenderer!.ToyTypes[toySelection]))
        {
            if (ImGui.Selectable(shaderToyRenderer.ToyTypes[0], toySelection == 0))
            {
                toySelection = 0;
            }
            if (ImGui.Selectable(shaderToyRenderer.ToyTypes[1], toySelection == 1))
            {
                toySelection = 1;
            }
            if (ImGui.Selectable(shaderToyRenderer.ToyTypes[2], toySelection == 2))
            {
                toySelection = 2;
            }
            ImGui.EndCombo();
        }
        ImGui.Image((nint)frameTexture.Index, new Vector2(512, 512), new Vector2(0, 0), new Vector2(1, 1));
        ImGui.End();
        ImGui.Begin("Image Sample");
        ImGui.Text("This is an image sample loaded from Assets/image_sample.jpg.");
        ImGui.Image((nint)imageSample.Index, new Vector2(256, 256), new Vector2(0, 0), new Vector2(1, 1));
        ImGui.End();
        guiRenderer.EndFrame(cmdBuf);
        cmdBuf.EndRendering();
        vkContext.Submit(cmdBuf, tex);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        shaderToyRenderer?.Dispose();
        guiRenderer?.Dispose();
    }
}