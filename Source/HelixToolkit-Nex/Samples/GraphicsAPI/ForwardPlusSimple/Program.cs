// See https://aka.ms/new-console-template for more information
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Examples;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
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
    private Camera _camera = new Camera();

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
        _camera.Position = new Vector3(0, 10, -30);
    }

    protected override void OnTick()
    {
        var cmdBuffer = _ctx!.AcquireCommandBuffer();
        var target = _ctx.GetCurrentSwapchainTexture();
        _example?.Render(cmdBuffer, target, _camera, MainWindow.Size.Width, MainWindow.Size.Height);
        _ctx.Submit(cmdBuffer, target);
    }

    protected override void HandleResize(int width, int height)
    {
        _ctx?.RecreateSwapchain(width, height);
        base.HandleResize(width, height);
    }

    protected override void OnDisposing()
    {
        _example?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
}
