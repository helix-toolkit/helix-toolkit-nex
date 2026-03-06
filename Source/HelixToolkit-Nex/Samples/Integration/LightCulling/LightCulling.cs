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
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;

internal class LightCullingTest(IContext context) : IDisposable
{
    public const int NumSpheresPerAxis = 20; // Total spheres will be NumSpheresPerAxis^3
    public const int NumCubes = 1000;
    public const SampleTextureMode DebugMode = SampleTextureMode.DebugMeshId;
    private readonly IContext _context = context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _rendererManager;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private IResourceManager? _resourceManager;
    private Node? _root;
    private readonly Camera _camera = new PerspectiveCamera()
    {
        Position = new Vector3(0, 0, -20),
        FarPlane = 100,
    };
    private Vector3 _initialCameraPosition = new(0, 0, -20);

    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    private RenderGraph? _renderGraph;

    public void Initialize(int width, int height)
    {
        RenderSettings.LogFPSInDebug = true;
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();

        _serviceProvider = services.BuildServiceProvider();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();
        MaterialTypeRegistry.Register(
            "TimeBased",
            """
            float time = getTime();
            PBRMaterial material = createPBRMaterial();
            vec3 albedo = material.albedo;
            albedo.r = fract(albedo.r + time);
            albedo.g = fract(albedo.g + time * 1.1);
            albedo.b = fract(albedo.b + time * 1.2);
            return vec4(albedo, 1);
            """
        );
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
        InitializeScene();
    }

    private void InitializeScene()
    {
        var geometryManager = _resourceManager!.Geometries;
        var materialPropertyPool = _resourceManager!.MaterialProperties;
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

        meshbuilder = new MeshBuilder(true, true, true);
        meshbuilder.AddTetrahedron(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 2);
        var tetrahron = meshbuilder.ToMesh().ToGeometry();
        succ = geometryManager.Add(tetrahron, out var tetrahedronId);
        var shadingModes = new[]
        {
            PBRShadingMode.Unlit.ToString(),
            PBRShadingMode.PBR.ToString(),
            PBRShadingMode.Normal.ToString(),
            "TimeBased",
        };
        _root = new Node(_worldDataProvider!.World, "Root");
        for (int i = 0; i < NumSpheresPerAxis; ++i)
        {
            for (int j = 0; j < NumSpheresPerAxis; ++j)
            {
                for (int z = 0; z < NumSpheresPerAxis; ++z)
                {
                    var node = new Node(_worldDataProvider.World, $"Sphere_{i}");
                    node.Transform = new Transform
                    {
                        Translation = new Vector3(
                            i * NumSpheresPerAxis - NumSpheresPerAxis * 2,
                            j * NumSpheresPerAxis - NumSpheresPerAxis * 2,
                            z * NumSpheresPerAxis - NumSpheresPerAxis * 2
                        ),
                    };
                    var pbrProps = materialPropertyPool.Create(
                        shadingModes[z % shadingModes.Length]
                    );
                    pbrProps.Properties.Albedo = new Vector3(
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle(),
                        Random.Shared.NextSingle()
                    );
                    pbrProps.NotifyUpdated();
                    node.Entity.Add(new MeshComponent(z % 2 == 0 ? sphere : tetrahron, pbrProps));
                    _root.AddChild(node);
                }
            }
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
        var pbrPropsInstancing = materialPropertyPool.Create(PBRShadingMode.Unlit);
        pbrPropsInstancing.Properties.Albedo = new Vector3(1, 0, 1);
        instancingNode.Entity.Add(new MeshComponent(cube, pbrPropsInstancing, instancing));
        _root.AddChild(instancingNode);

        var directionalLight = new Node(_worldDataProvider.World, "DirectionalLight");
        directionalLight.Entity.Add(
            new DirectionalLight
            {
                Color = Vector3.One,
                Intensity = 1,
                Direction = Vector3.Normalize(new Vector3(1, -1, -1)),
            }
        );
        _root.AddChild(directionalLight);

        var allNodes = new FastList<Node>(NumSpheresPerAxis ^ 3 + 1);
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
        RotateCamera();
        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();
        _rendererManager!.Resize(width, height);
        _rendererManager!.Render(_renderContext!, _renderGraph!);
    }

    private void RotateCamera()
    {
        var totalTime = (float)(
            (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency
        );
        float speed = 0.1f;
        var rotation = Matrix4x4.CreateRotationY(totalTime * speed);
        _camera.Position = Vector3.Transform(_initialCameraPosition, rotation);
        _camera.Target = Vector3.Zero;
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
