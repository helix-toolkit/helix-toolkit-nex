// See https://aka.ms/new-console-template for more information
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Examples;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Sample.Application;
using Microsoft.Extensions.Logging;

using var app = new App();
app.Run();

internal class App : Application
{
    public override string Name => "ForwardPlus Simple Test";
    private static readonly ILogger logger = LogManager.Create<App>();
    private IContext? _ctx;
    private ForwardPlusExample? _example;
    private Camera _camera;
    private readonly Dependencies _dependencies = Dependencies.Empty;
    private readonly RenderPass _renderPass = new();
    private readonly Framebuffer _framebuffer = new();
    private TextureResource _depthBuffer = TextureResource.Null;
    private DepthState _depthState = DepthState.Default;

    protected override void Initialize()
    {
        base.Initialize();
        // Initialize the application with a specific scene or settings
        // For example, you might want to load a 3D model or set up a camera
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

        _example = new ForwardPlusExample(_ctx);

        _example.Initialize(windowSize.Width, windowSize.Height);
        _camera = new Camera();
        _camera.Position = new Vector3(0, 0, -5);
        _camera.NearPlane = 0.1f;
        _camera.FarPlane = 100.0f;
        _camera.View = Matrix4x4.CreateLookAt(_camera.Position, Vector3.Zero, Vector3.UnitY);
        float fov = 45f * MathF.PI / 180f;
        _camera.Projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fov,
            (float)windowSize.Width / windowSize.Height,
            _camera.NearPlane,
            _camera.FarPlane
        );
        _renderPass.Colors[0] = new RenderPass.AttachmentDesc
        {
            ClearColor = new Color4(0),
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };
        _renderPass.Depth = new RenderPass.AttachmentDesc
        {
            ClearDepth = 1.0f,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
        };

        _depthBuffer = _ctx.CreateTexture(
            new TextureDesc()
            {
                Type = TextureType.Texture2D,
                Format = Format.Z_F32,
                Dimensions = new Dimensions((uint)windowSize.Width, (uint)windowSize.Height, 1),
                NumLayers = 1,
                NumSamples = 1,
                Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                NumMipLevels = 1,
                Storage = StorageType.Device,
            }
        );
    }

    protected override void OnTick()
    {
        _framebuffer.Colors[0].Texture = _ctx!.GetCurrentSwapchainTexture();
        _framebuffer.DepthStencil.Texture = _depthBuffer;
        var cmdBuffer = _ctx!.AcquireCommandBuffer();
        _example?.PreRender(cmdBuffer);
        cmdBuffer.BeginRendering(_renderPass, _framebuffer, _dependencies);
        cmdBuffer.BindDepthState(_depthState);
        _example?.Render(
            cmdBuffer,
            _camera,
            MainWindow.Size.Width,
            MainWindow.Size.Height,
            _depthBuffer.Index
        );
        cmdBuffer.EndRendering();
        _ctx.Submit(cmdBuffer, _ctx.GetCurrentSwapchainTexture());
    }

    protected override void OnDisposing()
    {
        _example?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
