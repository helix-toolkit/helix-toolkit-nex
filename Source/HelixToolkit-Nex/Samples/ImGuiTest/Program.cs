// See https://aka.ms/new-console-template for more information
using System.Numerics;
using System.Runtime.InteropServices;
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

Console.WriteLine("Hello, World!");

using var app = new App();
app.Run();

internal class App : Application
{
    private IContext? _ctx;
    private ImGuiRenderer? _guiRenderer;
    private readonly Framebuffer _framebuffer = new();
    private readonly RenderPass _pass = new();
    private readonly Dependencies _dp = new();
    private TextureResource _frameTexture = TextureResource.Null;
    private ShaderToyRenderer? _shaderToyRenderer = null;
    private TextureResource _imageSample = TextureResource.Null;
    private uint _toySelection = 0;

    public override string Name => "ImGui Test Application";

    protected override void Initialize()
    {
        _ctx = VulkanBuilder.Create(
            new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = CreateSurface,
                ForcePresentModeFIFO = true,
            },
            MainWindow.Instance,
            0
        );
        _shaderToyRenderer = new ShaderToyRenderer(_ctx);
        _shaderToyRenderer.Initialize();
        var windowSize = MainWindow.Size;
        _ctx.RecreateSwapchain(windowSize.Width, windowSize.Height);
        _guiRenderer = new ImGuiRenderer(_ctx, new ImGuiConfig());
        _guiRenderer.Initialize();
        _pass.Colors[0].ClearColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f);
        _pass.Colors[0].LoadOp = LoadOp.Clear;

        _frameTexture = _ctx.CreateTexture(
            new TextureDesc()
            {
                Format = Format.RGBA_UN8,
                Dimensions = new(512, 512, 1),
                Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment,
            },
            "Frame Texture"
        );

        _dp.Textures[0] = _frameTexture;

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
                _imageSample = _ctx!.CreateTexture(textureDesc, "ShaderRenderer: Image Sample");
            }
        }
    }

    protected override void OnDisplayScaleChanged(float scaleX, float scaleY)
    {
        if (_guiRenderer != null && scaleX != 0)
        {
            // Update the ImGui guiRenderer with the new display scale.
            _guiRenderer.DisplayScale = scaleX;
        }
    }

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        if (_guiRenderer == null)
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
        if (_ctx == null || _guiRenderer == null)
        {
            return;
        }
        var tex = _ctx.GetCurrentSwapchainTexture();
        if (tex.Empty)
        {
            return; // No swapchain texture available, nothing to render to.
        }
        _framebuffer.Colors[0].Texture = tex;
        var cmdBuf = _ctx.AcquireCommandBuffer();
        _shaderToyRenderer?.Render(cmdBuf, _toySelection, new Vector2(512, 512), _frameTexture);

        cmdBuf.BeginRendering(_pass, _framebuffer, _dp);
        _guiRenderer.BeginFrame(_framebuffer);
        ImGui.ShowDemoWindow();
        ImGui.Begin("Hello, ImGui!");
        ImGui.Text("This is a simple ImGui test application.");
        ImGui.Text($"Current time: {DateTime.Now:HH:mm:ss}");
        ImGui.Text($"Window size: {MainWindow.Size.Width}x{MainWindow.Size.Height}");
        ImGui.Text($"Display scale: {_guiRenderer.DisplayScale}");
        ImGui.End();
        ImGui.Begin("ShaderToy Renderer");
        if (
            ImGui.BeginCombo(
                "ShaderToy Renderer Options",
                _shaderToyRenderer!.ToyTypes[_toySelection]
            )
        )
        {
            if (ImGui.Selectable(_shaderToyRenderer.ToyTypes[0], _toySelection == 0))
            {
                _toySelection = 0;
            }
            if (ImGui.Selectable(_shaderToyRenderer.ToyTypes[1], _toySelection == 1))
            {
                _toySelection = 1;
            }
            if (ImGui.Selectable(_shaderToyRenderer.ToyTypes[2], _toySelection == 2))
            {
                _toySelection = 2;
            }
            ImGui.EndCombo();
        }
        ImGui.Image(
            (nint)_frameTexture.Index,
            new Vector2(512, 512),
            new Vector2(0, 0),
            new Vector2(1, 1)
        );
        ImGui.End();
        ImGui.Begin("Image Sample");
        ImGui.Text("This is an image sample loaded from Assets/image_sample.jpg.");
        ImGui.Image(
            (nint)_imageSample.Index,
            new Vector2(256, 256),
            new Vector2(0, 0),
            new Vector2(1, 1)
        );
        ImGui.End();
        _guiRenderer.EndFrame(cmdBuf);
        cmdBuf.EndRendering();
        _ctx.Submit(cmdBuf, tex);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        _shaderToyRenderer?.Dispose();
        _guiRenderer?.Dispose();
    }
}
