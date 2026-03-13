using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using SceneSamples;
using Vortice.SPIRV;

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
        float zoom = 0.8f;
        var totalTime = (float)(
            (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency
        );
        var worldCenter = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f);
        var rotation = Matrix4x4.CreateRotationY(totalTime * 0.05f);
        _camera.Position =
            Vector3.Transform(_initialCameraPosition - worldCenter, rotation) + worldCenter;
        _camera.Target = worldCenter + new Vector3(0, _scene.MaxTerrainHeight / 2f, 0);
        _camera.Position = Vector3.Lerp(_camera.Position, worldCenter, zoom);
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
