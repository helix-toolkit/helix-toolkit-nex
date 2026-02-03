// See https://aka.ms/new-console-template for more information
using HelixToolkit.Nex;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Sample.Application;
using InstancingMeshCulling;
using Microsoft.Extensions.Logging;

// Entry point of the application
using var app = new App();
app.Run();

/// <summary>
/// Main Application class that manages the window, input, and graphics loop.
/// </summary>
internal class App : Application
{
    public override string Name => "Mesh culling Test";
    private static readonly ILogger logger = LogManager.Create<App>();

    // The Graphics Context (Vulkan in this case)
    private IContext? _ctx;

    // The main example logic class
    private InstancingMeshCullingExample? _example;

    #region 1. Initialization
    /// <summary>
    /// Called when the application starts.
    /// Setup the window, graphics context, and the scene.
    /// </summary>
    protected override void Initialize()
    {
        base.Initialize();

        // 1.1 Create the Vulkan Graphics Context
        // This handles device selection, swapchain creation, and validation layers.
        _ctx = VulkanBuilder.Create(
            new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                OnCreateSurface = CreateSurface, // Hook to create window surface (SDL/Win32/etc)
            },
            MainWindow.Instance,
            0
        );

        // 1.2 Setup Swapchain
        var windowSize = MainWindow.Size;
        _ctx?.RecreateSwapchain(windowSize.Width, windowSize.Height);
        if (_ctx == null)
        {
            throw new Exception("Failed to create Vulkan context");
        }
        // 1.3 Initialize the Culling Example
        _example = new InstancingMeshCullingExample(_ctx);
        _example.Initialize(windowSize.Width, windowSize.Height);
    }
    #endregion

    #region 2. Render Loop
    /// <summary>
    /// Called every frame.
    /// </summary>
    protected override void OnTick()
    {
        // Delegate rendering to the example logic
        _example?.Render(MainWindow.Size.Width, MainWindow.Size.Height);
    }
    #endregion

    #region 3. Cleanup
    /// <summary>
    /// Called when the application is closing.
    /// </summary>
    protected override void OnDisposing()
    {
        // Release resources in reverse order of creation
        _example?.Dispose();
        _ctx?.Dispose();
        base.OnDisposing();
    }
    #endregion
}
