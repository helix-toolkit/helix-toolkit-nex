// See https://aka.ms/new-console-template for more information
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
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using SceneSamples;

internal class LightCullingTest(IContext context) : IDisposable
{
    public const SampleTextureMode DebugMode = SampleTextureMode.DebugMeshId;

    private readonly IContext _context = context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _rendererManager;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private IResourceManager? _resourceManager;
    private Node? _root;

    // Camera orbits above the center of the Minecraft world
    private readonly Camera _camera = new PerspectiveCamera()
    {
        Position = new Vector3(
            MinecraftScene.WorldSizeX / 2f,
            80,
            -MinecraftScene.WorldSizeZ / 2f - 20
        ),
        Target = new Vector3(MinecraftScene.WorldSizeX / 2f, 0, MinecraftScene.WorldSizeZ / 2f),
        FarPlane = 1000,
    };
    private readonly Vector3 _initialCameraPosition = new(
        MinecraftScene.WorldSizeX / 4f,
        80,
        -MinecraftScene.WorldSizeZ / 4f - 20
    );

    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private RenderGraph? _renderGraph;

    public void Initialize(int width, int height)
    {
        RenderSettings.LogFPSInDebug = true;
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();

        _serviceProvider = services.BuildServiceProvider();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();

        // Register Minecraft block material types before the material registry is built
        MinecraftScene.RegisterMaterials();

        _resourceManager.Materials.CreatePBRMaterialsFromRegistry();
        _rendererManager = new Renderer(_serviceProvider);
        _rendererManager.AddNode(new PrepareNode());
        _rendererManager.AddNode(new DepthPassNode());
        _rendererManager.AddNode(new FrustumCullNode());
        _rendererManager.AddNode(new ForwardPlusOpaqueNode());
        _rendererManager.AddNode(new ForwardPlusLightCullingNode());
        var postEffectNode = new PostEffectsNode();
        postEffectNode.AddEffect(new ToneMapping());
        _rendererManager.AddNode(postEffectNode);
        _rendererManager!.Initialize();
        _renderGraph = new RenderGraph(_serviceProvider);
        foreach (var node in _rendererManager.RenderNodes)
        {
            node.AddToGraph(_renderGraph);
        }
        _renderContext = new RenderContext(_serviceProvider);
        _worldDataProvider = new WorldDataProvider(_serviceProvider);
        _worldDataProvider.Initialize();
        _renderContext.Data = _worldDataProvider;
        _renderContext.Initialize();

        // Delegate all scene construction to MinecraftScene
        _root = MinecraftScene.Build(_context, _resourceManager, _worldDataProvider);
    }

    public void Render(int width, int height)
    {
        var aspectRatio = (float)width / height;
        _renderContext!.WindowSize = new HelixToolkit.Nex.Maths.Size(width, height);
        RotateCamera();
        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();
        _rendererManager!.Resize(width, height);
        _rendererManager!.Render(_renderContext!, _renderGraph!);
    }

    private void RotateCamera()
    {
        float zoom = 0.5f;
        var totalTime = (float)(
            (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency
        );
        var worldCenter = new Vector3(
            MinecraftScene.WorldSizeX / 2f,
            0,
            MinecraftScene.WorldSizeZ / 2f
        );
        var rotation = Matrix4x4.CreateRotationY(totalTime * 0.05f);
        _camera.Position =
            Vector3.Transform(_initialCameraPosition - worldCenter, rotation) + worldCenter;
        _camera.Target = worldCenter + new Vector3(0, MinecraftScene.MaxTerrainHeight / 2f, 0);
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
                _rendererManager?.Dispose();
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
