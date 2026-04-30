using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using SceneSamples;

namespace Interop.Common;

/// <summary>
/// <see cref="IViewportClient"/> that owns a <see cref="Camera"/> and delegates
/// per-frame camera manipulation to an optional callback.
/// Shares a single <see cref="IRenderDataProvider"/> and scene tick guard via the owning view model.
/// </summary>
internal sealed class DelegateViewportClient : IViewportClient
{
    private readonly MainViewModel _owner;
    private readonly Action<DelegateViewportClient, float>? _onUpdate;

    /// <summary>The camera owned by this viewport client.</summary>
    public Camera Camera { get; }

    public IRenderDataProvider? DataProvider => _owner.WorldDataProvider;

    /// <param name="owner">The owning view model (provides data provider and scene tick).</param>
    /// <param name="camera">The camera this client controls.</param>
    /// <param name="onUpdate">
    /// Optional per-frame callback invoked before the camera is applied to the render context.
    /// Use this to animate the camera (e.g. orbit, follow path).
    /// </param>
    public DelegateViewportClient(
        MainViewModel owner,
        Camera camera,
        Action<DelegateViewportClient, float>? onUpdate = null
    )
    {
        _owner = owner;
        Camera = camera;
        _onUpdate = onUpdate;
    }

    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        _owner.TickSceneOnce(deltaTime);

        if (context.WindowSize.Width <= 0 || context.WindowSize.Height <= 0)
            return Camera;

        _onUpdate?.Invoke(this, deltaTime);
        return Camera;
    }
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    public Engine? Engine => _engine;

    /// <summary>Viewport client for the orbiting fly-through camera.</summary>
    public IViewportClient FlyClient { get; }

    /// <summary>Viewport client for the static overhead camera.</summary>
    public IViewportClient OverheadClient { get; }

    /// <summary>Camera controller for the fly-through viewport.</summary>
    public ICameraController FlyCameraController { get; }

    /// <summary>Camera controller for the overhead viewport.</summary>
    public ICameraController OverheadCameraController { get; }

    [ObservableProperty]
    public partial bool IsPointerRingEnabled { set; get; }

    internal IRenderDataProvider? WorldDataProvider => _worldDataProvider;

    private IContext? _vulkanContext;
    private Engine? _engine;
    private WorldDataProvider? _worldDataProvider;
    private IScene? _scene;
    private Node? _root;

    // Scene tick guard — only tick once per frame even though two viewports fire Rendering
    private long _lastTickFrame;
    private bool _disposedValue;

    public MainViewModel(EngineInteropTarget target)
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
        _scene.RegisterMaterials();

        // 4. Build engine
        _engine = EngineBuilder
            .Create(_vulkanContext)
            .WithDefaultNodes()
            .WithPostEffects(effects =>
            {
                effects.AddEffect(new Smaa());
                effects.AddEffect(new Bloom());
                effects.AddEffect(new BorderHighlightPostEffect());
                effects.AddEffect(new WireframePostEffect());
                effects.AddEffect(new ShowFPS());
            })
            .WithInteropTarget(target)
            .Build();
        // 5. World data + scene
        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        // 6. Cameras
        var center = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f);

        var flyCamera = new PerspectiveCamera
        {
            Position = center + new Vector3(0, 40f, -80f),
            Target = center,
            FarPlane = 1000,
        };

        var overheadCamera = new PerspectiveCamera
        {
            Position = new Vector3(center.X, 50, center.Z),
            Target = center,
            Up = -Vector3.UnitZ,
            FarPlane = 1000,
        };

        // 7. Camera controllers (user-driven orbit for both viewports)
        FlyCameraController = new OrbitCameraController(flyCamera);
        OverheadCameraController = new OrbitCameraController(overheadCamera);

        // 8. Create viewport clients (each owns its camera)
        FlyClient = new DelegateViewportClient(this, flyCamera);
        OverheadClient = new DelegateViewportClient(this, overheadCamera);
    }

    [RelayCommand]
    private async Task LoadSceneAsync()
    {
        if (_root is not null)
        {
            return;
        }
        _root = await _scene!.BuildAsync(
            _vulkanContext!,
            _engine!.ResourceManager,
            _worldDataProvider!
        );
    }

    internal void TickSceneOnce(float deltaTime)
    {
        long frame = Stopwatch.GetTimestamp();
        if (frame == _lastTickFrame)
            return;
        _lastTickFrame = frame;
        _scene!.Tick(deltaTime);
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
