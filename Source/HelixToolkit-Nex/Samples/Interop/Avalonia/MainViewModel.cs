using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using SceneSamples;
#if WINDOWS
using HelixToolkit.Nex.Interop.DirectX;
#endif
using Format = HelixToolkit.Nex.Graphics.Format;

namespace AvaloniaInterop;

/// <summary>
/// <see cref="IViewportClient"/> that owns a <see cref="Camera"/> and delegates per-frame camera
/// manipulation to an optional callback. Modeled on the <c>InteropShared</c> sample's
/// <c>DelegateViewportClient</c>, but kept local to this project so the sample stays cross-platform
/// (the shared <c>InteropShared</c> project is <c>net8.0-windows</c> and cannot be referenced from
/// the Linux build of this app).
/// </summary>
internal sealed class DelegateViewportClient : IViewportClient
{
    private readonly MainViewModel _owner;
    private readonly Action<DelegateViewportClient, float>? _onUpdate;

    /// <summary>The camera owned by this viewport client.</summary>
    public Camera Camera { get; }

    /// <inheritdoc />
    public IRenderDataProvider? DataProvider => _owner.WorldDataProvider;

    /// <param name="owner">The owning view model (provides the shared data provider and scene tick).</param>
    /// <param name="camera">The camera this client controls.</param>
    /// <param name="onUpdate">Optional per-frame callback invoked before the camera is applied.</param>
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

    /// <inheritdoc />
    public ICameraParamsProvider Update(RenderContext context, float deltaTime)
    {
        _owner.TickSceneOnce(deltaTime);

        if (context.WindowSize.Width <= 0 || context.WindowSize.Height <= 0)
        {
            return Camera;
        }

        _onUpdate?.Invoke(this, deltaTime);
        return Camera;
    }
}

/// <summary>
/// View model for the Avalonia interop sample. Builds an externally-owned <see cref="Engine"/> on a
/// headless Vulkan context configured for the current platform (Windows: D3D11 shared-texture external
/// memory; Linux: external-memory-fd), a <see cref="SceneSamples"/> scene, an
/// <see cref="OrbitCameraController"/>, and a <see cref="DelegateViewportClient"/>. These are bound
/// onto the <c>HelixViewport</c> in <c>MainWindow.axaml</c>.
/// </summary>
/// <remarks>
/// This mirrors the <c>InteropShared</c> <c>MainViewModel</c> but is a local, cross-platform copy:
/// the platform-specific engine configuration is guarded by the <c>WINDOWS</c> compilation symbol
/// (defined only for the <c>net8.0-windows</c> target), so the Linux build never touches DirectX.
/// </remarks>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The externally-owned engine. Never disposed by the viewport control.</summary>
    public Engine? Engine => _engine;

    /// <summary>Viewport client that supplies per-frame camera and scene data.</summary>
    public IViewportClient FlyClient { get; }

    /// <summary>Camera controller that translates pointer input into camera movement.</summary>
    public ICameraController FlyCameraController { get; }

    /// <summary>
    /// Whether the on-screen pointer ring overlay is enabled. Bound two-way to the overlay checkbox
    /// and one-way onto the viewport's <c>PointerRingEnabled</c>, so raising change notification here
    /// propagates the toggle to the control.
    /// </summary>
    public bool IsPointerRingEnabled
    {
        get => _isPointerRingEnabled;
        set
        {
            if (_isPointerRingEnabled == value)
            {
                return;
            }
            _isPointerRingEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPointerRingEnabled)));
        }
    }

    private bool _isPointerRingEnabled;

    internal IRenderDataProvider? WorldDataProvider => _worldDataProvider;

    private readonly IContext? _vulkanContext;
    private readonly Engine? _engine;
    private readonly WorldDataProvider? _worldDataProvider;
    private readonly IScene? _scene;
    private readonly Node? _root;

    // Scene tick guard — only tick once per frame even though the viewport may fire repeatedly.
    private long _lastTickFrame;
    private bool _disposedValue;

    public MainViewModel()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows path: create a D3D11 device to obtain the adapter LUID, then build a headless
            // Vulkan context with Win32 external memory bound to that same adapter so the engine's Vulkan
            // output can be shared into a D3D11 NT-handle texture.
            using var d3d11 = new D3D11DeviceManager();
            _vulkanContext = VulkanBuilder.CreateHeadless(
                new VulkanContextConfig
                {
                    EnableExternalMemoryWin32 = true,
                    RequiredDeviceLuid = d3d11.AdapterLuid,
                    EnableValidation = true,
                }
            );
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux path: create a headless Vulkan context with external memory FD support so the
            // engine's Vulkan output can be shared into an FD-backed texture.
            _vulkanContext = VulkanBuilder.CreateHeadless(
                new VulkanContextConfig
                {
                    EnableExternalMemoryFd = true,
                    EnableValidation = true,
                }
            );
        }
        else
        {
            throw new PlatformNotSupportedException("This sample only supports Windows and Linux.");
        }

        // Scene + materials (before engine build).
        _scene = new MinecraftScene();
        _scene.RegisterMaterials();

        // Build the engine to render offscreen into the interop target (RGBA_UN8, matching both the
        // Windows shared-texture bridge and the Linux external-memory bridge).
        _engine = EngineBuilder
            .Create(_vulkanContext)
            .WithDefaultNodes(renderToSwapchain: false)
            .WithSMAA()
            .WithBloom()
            .WithFPS()
            .WithPostEffects(effects =>
            {
                effects.AddEffect(new BorderHighlightPostEffect());
                effects.AddEffect(new WireframePostEffect());
            })
            .RenderToCustomTarget(Format.RGBA_UN8)
            .Build();

        // World data + scene.
        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();
        _root = _scene.Build(_vulkanContext, _engine.ResourceManager, _worldDataProvider);

        // Camera + orbit controller.
        var center = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f);
        var flyCamera = new PerspectiveCamera
        {
            Position = center + new Vector3(0, 40f, -80f),
            Target = center,
            FarPlane = 1000,
        };

        FlyCameraController = new OrbitCameraController(flyCamera);
        FlyClient = new DelegateViewportClient(this, flyCamera);
    }

    internal void TickSceneOnce(float deltaTime)
    {
        long frame = Stopwatch.GetTimestamp();
        if (frame == _lastTickFrame)
        {
            return;
        }
        _lastTickFrame = frame;
        _scene!.Tick(deltaTime);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _worldDataProvider?.Dispose();
                _engine?.Teardown();
                _vulkanContext?.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
