// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Numerics;
using Arch.Core.Extensions;
using HelixToolkit.Nex;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderGraphs;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;

internal class DepthPrepassTest(IContext context) : IDisposable
{
    public const int NumSpheres = 100;
    public const int NumCubes = 1000;
    public const SampleTextureMode DebugMode = SampleTextureMode.DebugMeshId;
    private readonly IContext _context = context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _rendererManager;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ResourceManager? _resourceManager;
    private Node? _root;
    private readonly Camera _camera = new PerspectiveCamera()
    {
        Position = new Vector3(0, 0, -50),
        FarPlane = 1000,
    };
    private RenderGraph? _renderGraph;

    public void Initialize(int width, int height)
    {
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services
            .AddSingleton<IGeometryManager, GeometryManager>()
            .AddSingleton<IShaderRepository, ShaderRepository>()
            .AddSingleton<IMaterialPropertyManager, MaterialPropertyManager>()
            .AddSingleton<IMaterialManager, MaterialManager>()
            .AddSingleton<ResourceManager, ResourceManager>();
        _serviceProvider = services.BuildServiceProvider();
        _renderGraph = ForwardPlusRenderGraph.Create(_serviceProvider);
        _resourceManager = _serviceProvider.GetRequiredService<ResourceManager>();
        _rendererManager = new Renderer(_serviceProvider);

        _rendererManager!.AddNode(new DepthPassNode());
        _rendererManager!.AddNode(new DebugDepthBufferNode());
        var postEffectNode = new PostEffectsNode();
        postEffectNode.AddEffect(new ToneMapping());
        _rendererManager.AddNode(postEffectNode);
        _rendererManager!.Initialize();
        _renderContext = new RenderContext(_serviceProvider);
        _worldDataProvider = new WorldDataProvider(_serviceProvider);
        _worldDataProvider.Initialize();
        _renderContext.Data = _worldDataProvider;
        _renderContext.Initialize();
        InitializeScene();
    }

    private void InitializeScene()
    {
        var geometryManager = _serviceProvider!.GetRequiredService<IGeometryManager>();
        var materialPropertyPool = _serviceProvider!.GetRequiredService<IMaterialPropertyManager>();
        var meshbuilder = new MeshBuilder(true, true, true);
        meshbuilder.AddSphere(Vector3.Zero);
        var sphere = meshbuilder.ToMesh().ToGeometry();
        var succ = geometryManager.Add(sphere, out var sphereId);
        Debug.Assert(succ, "Failed to add geometry");
        meshbuilder = new MeshBuilder(true, true, true);
        meshbuilder.AddCube();
        var cube = meshbuilder.ToMesh().ToGeometry();
        succ = geometryManager.Add(cube, out var cubeId);
        Debug.Assert(succ, "Failed to add geometry");

        _root = new Node(_worldDataProvider!.World, "Root");
        for (int i = 0; i < NumSpheres; ++i)
        {
            var node = new Node(_worldDataProvider.World, $"Sphere_{i}");
            node.Transform = new Transform
            {
                Translation = new Vector3(
                    Random.Shared.NextSingle() * 100 - 50,
                    Random.Shared.NextSingle() * 100 - 50,
                    Random.Shared.NextSingle() * 100 - 50
                ),
                Scale = Vector3.One * (Random.Shared.NextSingle() * 0.5f + 0.1f),
            };
            var pbrProps = materialPropertyPool.Create(PBRShadingMode.PBR);
            pbrProps.Properties.Albedo = new Vector3(
                Random.Shared.NextSingle(),
                Random.Shared.NextSingle(),
                Random.Shared.NextSingle()
            );
            pbrProps.NotifyUpdated();
            node.Entity.Add(new MeshComponent(sphere, pbrProps));
            _root.AddChild(node);
        }
        var instancing = new Instancing(false);
        for (int i = 0; i < NumCubes; ++i)
        {
            instancing.Transforms.Add(
                Matrix4x4.CreateScale(Random.Shared.NextSingle() * 0.5f + 0.1f)
                    * Matrix4x4.CreateTranslation(
                        new Vector3(
                            Random.Shared.NextSingle() * 100 - 50,
                            Random.Shared.NextSingle() * 100 - 50,
                            Random.Shared.NextSingle() * 100 - 50
                        )
                    )
            );
        }
        instancing.UpdateBuffer(_context);
        var instancingNode = new Node(_worldDataProvider.World, "InstancingNode");
        var pbrPropsInstancing = materialPropertyPool.Create(PBRShadingMode.PBR);
        pbrPropsInstancing.Properties.Albedo = new Vector3(1, 0, 1);
        instancingNode.Entity.Add(new MeshComponent(cube, pbrPropsInstancing, instancing));
        _root.AddChild(instancingNode);

        var allNodes = new FastList<Node>(NumSpheres + 1);
        _root.Flatten(
            (node) =>
            {
                return node.Enabled;
            },
            allNodes
        );
        allNodes.UpdateTransforms();
    }

    public void Render(int width, int height)
    {
        var aspectRatio = (float)width / height;
        _renderContext!.WindowSize = new HelixToolkit.Nex.Maths.Size(width, height);
        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _rendererManager!.Resize(width, height);
        _rendererManager!.Render(_renderContext!, _renderGraph!);
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

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~DepthPrepassTest()
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
