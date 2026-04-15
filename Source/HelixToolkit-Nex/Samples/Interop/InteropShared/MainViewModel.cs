using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using SceneSamples;
using Format = HelixToolkit.Nex.Graphics.Format;

namespace Interop.Common;

public class MainViewModel : ObservableObject, IDisposable
{
    public Engine? Engine => _engine;
    private IContext? _vulkanContext;
    private Engine? _engine;
    private WorldDataProvider? _worldDataProvider;
    private IScene? _scene;
    private Node? _root;

    // Orbit camera (circles around target)
    private Camera _orbitCamera = new PerspectiveCamera();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    // Overhead camera (static top-down view)
    private Camera _overheadCamera = new PerspectiveCamera();

    // Scene tick guard — only tick once per frame even though two viewports fire Rendering
    private long _lastTickFrame;
    private bool _disposedValue;
    private const float OrbitRadius = 80f;
    private const float OrbitHeight = 40f;
    private const float OrbitSpeed = 0.3f;

    public MainViewModel()
    {
        // 1. D3D11 device to get the adapter LUID
        using var d3d11 = new D3D11DeviceManager();

        // 2. Headless VulkanContext with external memory
        _vulkanContext = VulkanBuilder.CreateHeadless(
            new VulkanContextConfig
            {
                EnableExternalMemoryWin32 = true,
                RequiredDeviceLuid = d3d11.AdapterLuid,
                EnableValidation = true,
            }
        );
        // 3. Scene + materials (before engine build)
        _scene = new MinecraftScene();
        RenderSettings.LogFPSInDebug = true;
        _scene.RegisterMaterials();

        // 4. Build engine
        _engine = EngineBuilder
            .Create(_vulkanContext)
            .WithDefaultNodes(renderToSwapchain: false)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(new Smaa());
                effects.AddEffect(new Bloom());
                effects.AddEffect(new ToneMapping() { EnableGammaCorrection = true });
                effects.AddEffect(new BorderHighlightPostEffect());
                effects.AddEffect(new WireframePostEffect());
                effects.AddEffect(new ShowFPS());
            })
            .AddNode(new RenderToFinalNode(Format.BGRA_UN8))
            .Build();
        // 5. World data + scene
        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();
        _root = _scene.Build(_vulkanContext, _engine.ResourceManager, _worldDataProvider);

        // 6. Cameras
        _orbitCamera = new PerspectiveCamera
        {
            Position = new Vector3(
                _scene.WorldSizeX / 2f,
                OrbitHeight,
                -_scene.WorldSizeZ / 2f - OrbitRadius
            ),
            Target = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f),
            FarPlane = 1000,
        };

        _overheadCamera = new PerspectiveCamera
        {
            Position = new Vector3(_scene.WorldSizeX / 2f, 50, _scene.WorldSizeZ / 2f),
            Target = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f),
            Up = -Vector3.UnitZ,
            FarPlane = 1000,
        };
    }

    private void TickSceneOnce(float deltaTime)
    {
        long frame = Stopwatch.GetTimestamp();
        if (frame == _lastTickFrame)
            return;
        _lastTickFrame = frame;
        _scene!.Tick(deltaTime);
        UpdateOrbitCamera();
    }

    // --- Viewport 1: orbit ---
    public void OnFlyRendering(object? sender, ViewportRenderingEventArgs e)
    {
        TickSceneOnce(e.DeltaTime);

        var rc = e.RenderContext;
        if (rc.WindowSize.Width <= 0 || rc.WindowSize.Height <= 0)
            return;

        rc.CameraParams = _orbitCamera.ToCameraParams(
            (float)rc.WindowSize.Width / rc.WindowSize.Height
        );
        e.DataProvider = _worldDataProvider;
    }

    // --- Viewport 2: overhead ---
    public void OnOverheadRendering(object? sender, ViewportRenderingEventArgs e)
    {
        TickSceneOnce(e.DeltaTime);

        var rc = e.RenderContext;
        if (rc.WindowSize.Width <= 0 || rc.WindowSize.Height <= 0)
            return;

        rc.CameraParams = _overheadCamera.ToCameraParams(
            (float)rc.WindowSize.Width / rc.WindowSize.Height
        );
        e.DataProvider = _worldDataProvider;
    }

    private void UpdateOrbitCamera()
    {
        var target = new Vector3(_scene!.WorldSizeX / 2f, 0f, _scene.WorldSizeZ / 2f);
        float t =
            (float)((Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency)
            * OrbitSpeed;

        float x = target.X + OrbitRadius * MathF.Sin(t);
        float z = target.Z + OrbitRadius * MathF.Cos(t);

        _orbitCamera.Position = new Vector3(x, OrbitHeight, z);
        _orbitCamera.Target = target;
        _orbitCamera.Up = Vector3.UnitY;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _worldDataProvider?.Dispose();
                _engine?.Teardown();
                _vulkanContext?.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MainViewModel()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
