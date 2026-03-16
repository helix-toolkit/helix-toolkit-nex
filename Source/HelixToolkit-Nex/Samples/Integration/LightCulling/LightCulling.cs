using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using SceneSamples;

internal class LightCullingTest(IContext context, bool largeScene = true) : IDisposable
{
    public const SampleTextureMode DebugMode = SampleTextureMode.DebugMeshId;

    private readonly IContext _context = context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _renderer;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private IResourceManager? _resourceManager;
    private Node? _root;
    private IScene _scene = largeScene ? new MinecraftLargeScene() : new MinecraftScene();

    // Camera orbits above the center of the Minecraft world
    private Camera _camera = new PerspectiveCamera();
    private Vector3 _initialCameraPosition = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _lastTimestamp = 0;
    private RenderGraph? _renderGraph;

    // Flight parameters
    private const float FlyHeight = 30f; // height above terrain base
    private const float FlySpeed = 0.08f; // radians/sec for the lemniscate parameter
    private const float FlyRadius = 80f; // half-width of the figure-eight
    private const float PitchDown = -0.18f; // constant nose-down pitch (radians)

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera()
        {
            Position = new Vector3(_scene.WorldSizeX / 2f, 80, -_scene.WorldSizeZ / 2f - 20),
            Target = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f),
            FarPlane = 1000,
        };
        _initialCameraPosition = new(_scene.WorldSizeX / 4f, 80, -_scene.WorldSizeZ / 4f - 20);
        RenderSettings.LogFPSInDebug = true;
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();

        _serviceProvider = services.BuildServiceProvider();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();

        // Register Minecraft block material types before the material registry is built
        _scene.RegisterMaterials();

        _resourceManager.Materials.CreatePBRMaterialsFromRegistry();
        _renderer = new Renderer(_serviceProvider);
        _renderer.AddNode(new PrepareNode());
        _renderer.AddNode(new DepthPassNode());
        _renderer.AddNode(new FrustumCullNode());
        _renderer.AddNode(new ForwardPlusOpaqueNode() { UseLightCulling = true });
        _renderer.AddNode(new ForwardPlusLightCullingNode());
        var postEffectNode = new PostEffectsNode();
        postEffectNode.AddEffect(new ToneMapping());
        postEffectNode.AddEffect(new ShowFPS());
        _renderer.AddNode(postEffectNode);
        _renderer.AddNode(new RenderToFinalNode(_context.GetSwapchainFormat()));
        _renderer!.Initialize();
        _renderGraph = new RenderGraph(_serviceProvider);
        foreach (var node in _renderer.RenderNodes)
        {
            node.AddToGraph(_renderGraph);
        }
        _renderGraph.Compile();
        _renderContext = new RenderContext(_serviceProvider);
        _renderContext.ResourceSet = new RenderGraphResourceSet();
        _worldDataProvider = new WorldDataProvider(_serviceProvider);
        _worldDataProvider.Initialize();
        _renderContext.Data = _worldDataProvider;
        _renderContext.Initialize();

        // Delegate all scene construction to MinecraftScene
        _root = _scene.Build(_context, _resourceManager, _worldDataProvider);
    }

    public void Render(int width, int height)
    {
        if (_lastTimestamp == 0)
        {
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _scene.Tick(delta);
        var aspectRatio = (float)width / height;
        _renderContext!.WindowSize = new HelixToolkit.Nex.Maths.Size(width, height);
        RotateCamera();
        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();
        _renderer!.Render(_renderContext!, _renderGraph!);
    }

    private void RotateCamera()
    {
        var worldCenter = new Vector3(_scene.WorldSizeX / 2f, 0f, _scene.WorldSizeZ / 2f);

        float t =
            (float)((Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency)
            * FlySpeed;

        // Lemniscate of Bernoulli in the XZ plane, centred on the world
        // x(t) = R·cos(t) / (1 + sin²(t))
        // z(t) = R·sin(t)·cos(t) / (1 + sin²(t))
        float sinT = MathF.Sin(t);
        float cosT = MathF.Cos(t);
        float denom = 1f + sinT * sinT;
        float lx = FlyRadius * cosT / denom;
        float lz = FlyRadius * sinT * cosT / denom;

        // Analytical derivative for look-ahead direction
        float denom2 = denom * denom;
        float dlx = (-FlyRadius * sinT * denom - FlyRadius * cosT * 2f * sinT * cosT) / denom2;
        float dlz =
            (
                FlyRadius * (cosT * cosT - sinT * sinT) * denom
                - FlyRadius * sinT * cosT * 2f * sinT * cosT
            ) / denom2;

        _camera.Position = worldCenter + new Vector3(lx, FlyHeight, lz);

        // Horizontal forward direction from path tangent
        var forward = new Vector3(dlx, 0f, dlz);
        if (forward.LengthSquared() < 1e-6f)
            forward = Vector3.UnitX;
        forward = Vector3.Normalize(forward);

        // Apply a fixed nose-down pitch and aim the target ahead
        var pitchedForward = Vector3.Normalize(forward + new Vector3(0f, MathF.Sin(PitchDown), 0f));
        _camera.Target = _camera.Position + pitchedForward * 10f;

        // Keep up vector locked to world Y — no banking
        _camera.Up = Vector3.UnitY;
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _worldDataProvider?.Dispose();
                _renderer?.Dispose();
                _renderGraph?.Dispose();
                _resourceManager?.Dispose();
                _context?.Dispose();
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
