using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using ImGuiNET;
using Microsoft.Extensions.Logging;

namespace Transparent;

/// <summary>
/// Demonstrates Order-Independent Transparency (OIT) using Weighted Blended OIT (WBOIT).
/// Uses ImGui to let the user adjust opacity of transparent objects at runtime.
/// </summary>
internal partial class TransparentDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<TransparentDemo>();

    private readonly IContext _context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _renderer;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private IResourceManager? _resourceManager;
    private RenderGraph? _renderGraph;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    private Camera _camera = new PerspectiveCamera();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _lastTimestamp;

    // ImGui swapchain rendering state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    // Post Effects (no Bloom per requirement)
    private readonly Fxaa _fxaa = new() { Enabled = false };
    private readonly Smaa _smaa = new();
    private readonly ToneMapping _toneMapping = new();
    private readonly ShowFPS _showFPS = new();

    private Size _viewportSize = new(1, 1);

    // Camera controller
    private OrbitCameraController? _orbitController;
    private bool _isRotating;
    private bool _isPanning;

    // Transparent objects tracked for ImGui editing
    private readonly List<TransparentObjectInfo> _transparentObjects = [];

    // Opaque floor
    private MaterialProperties? _floorMaterial;
    private Node? _floorNode;

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public TransparentDemo(IContext context)
    {
        _context = context;
    }

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera()
        {
            Position = new Vector3(0, 8, -18),
            Target = new Vector3(0, 2, 0),
            FarPlane = 200,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        // --- Service setup ---
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();
        _serviceProvider = services.BuildServiceProvider();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();

        // Register default PBR materials
        _resourceManager.Materials.CreatePBRMaterialsFromRegistry();

        // --- Render graph with OIT support ---
        _renderer = new Renderer(_serviceProvider);
        _renderer.AddNode(new PrepareNode());
        _renderer.AddNode(new DepthPassNode());
        _renderer.AddNode(new FrustumCullNode());
        _renderer.AddNode(new ForwardPlusOpaqueNode() { UseLightCulling = true });
        _renderer.AddNode(new ForwardPlusLightCullingNode());
        // WBOIT transparent pass + composite
        _renderer.AddNode(
            new ForwardPlusTransparentNode() { UseWBOIT = true, UseLightCulling = true }
        );
        _renderer.AddNode(new WBOITCompositeNode());

        var postEffectNode = new PostEffectsNode();
        postEffectNode.AddEffect(_fxaa);
        postEffectNode.AddEffect(_smaa);
        // No bloom per requirement
        postEffectNode.AddEffect(_toneMapping);
        postEffectNode.AddEffect(_showFPS);

        _renderer.AddNode(postEffectNode);
        _renderer.Initialize();

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

        // Build the 3D scene
        BuildScene();

        // --- ImGui setup ---
        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        _imGuiPass.Colors[0].ClearColor = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
    }

    private void BuildScene()
    {
        var geometryManager = _resourceManager!.Geometries;
        var materialPool = _resourceManager.MaterialProperties;

        _root = new Node(_worldDataProvider!.World, "Root");

        // --- Opaque floor ---
        var floorBuilder = new MeshBuilder(true, true, true);
        floorBuilder.AddBox(Vector3.Zero, 30, 0.2f, 30);
        var floorGeo = floorBuilder.ToMesh().ToGeometry();
        geometryManager.Add(floorGeo, out _);

        _floorMaterial = materialPool.Create(PBRShadingMode.PBR);
        _floorMaterial.Properties.Albedo = new Vector3(0.6f, 0.6f, 0.6f);
        _floorMaterial.Properties.Roughness = 0.9f;
        _floorMaterial.Properties.Metallic = 0.0f;
        _floorMaterial.Properties.Ao = 1.0f;
        _floorMaterial.Properties.Opacity = 1.0f;
        _floorMaterial.NotifyUpdated();

        _floorNode = new Node(_worldDataProvider.World, "Floor");
        _floorNode.Transform = new Transform { Translation = new Vector3(0, -0.1f, 0) };
        _floorNode.Entity.Set(new MeshComponent(floorGeo, _floorMaterial));
        _root.AddChild(_floorNode);

        // --- Transparent spheres at various positions ---
        var sphereBuilder = new MeshBuilder(true, true, true);
        sphereBuilder.AddSphere(Vector3.Zero, 1.5f);
        var sphereGeo = sphereBuilder.ToMesh().ToGeometry();
        geometryManager.Add(sphereGeo, out _);

        CreateTransparentObject(
            "Red Sphere",
            sphereGeo,
            materialPool,
            new Vector3(-4, 2, 0),
            new Vector3(1.0f, 0.1f, 0.1f),
            0.4f
        );
        CreateTransparentObject(
            "Green Sphere",
            sphereGeo,
            materialPool,
            new Vector3(0, 2, 0),
            new Vector3(0.1f, 1.0f, 0.1f),
            0.5f
        );
        CreateTransparentObject(
            "Blue Sphere",
            sphereGeo,
            materialPool,
            new Vector3(4, 2, 0),
            new Vector3(0.1f, 0.2f, 1.0f),
            0.6f
        );

        // --- Transparent cubes behind the spheres ---
        var cubeBuilder = new MeshBuilder(true, true, true);
        cubeBuilder.AddBox(Vector3.Zero, 2, 2, 2);
        var cubeGeo = cubeBuilder.ToMesh().ToGeometry();
        geometryManager.Add(cubeGeo, out _);

        CreateTransparentObject(
            "Yellow Cube",
            cubeGeo,
            materialPool,
            new Vector3(-2, 2, 3),
            new Vector3(1.0f, 0.9f, 0.1f),
            0.35f
        );
        CreateTransparentObject(
            "Cyan Cube",
            cubeGeo,
            materialPool,
            new Vector3(2, 2, 3),
            new Vector3(0.1f, 0.9f, 0.9f),
            0.45f
        );

        // --- Overlapping transparent box in the middle ---
        var bigBoxBuilder = new MeshBuilder(true, true, true);
        bigBoxBuilder.AddBox(Vector3.Zero, 6, 3, 6);
        var bigBoxGeo = bigBoxBuilder.ToMesh().ToGeometry();
        geometryManager.Add(bigBoxGeo, out _);

        CreateTransparentObject(
            "Large Purple Box",
            bigBoxGeo,
            materialPool,
            new Vector3(0, 1.5f, 1.5f),
            new Vector3(0.6f, 0.1f, 0.8f),
            0.25f
        );

        // --- Directional light ---
        var lightNode = new Node(_worldDataProvider.World, "DirectionalLight");
        lightNode.Entity.Set(
            new DirectionalLightComponent()
            {
                Direction = Vector3.Normalize(new Vector3(-0.5f, -1f, 0.5f)),
                Color = new Color4(1f, 1f, 1f, 1f),
                Intensity = 2.0f,
            }
        );
        lightNode.Transform = new Transform();
        _root.AddChild(lightNode);

        // --- Point lights for more interesting lighting ---
        CreatePointLight(
            "PointLight_Left",
            new Vector3(-6, 6, -4),
            new Color4(1f, 0.5f, 0.2f, 1f),
            3.0f,
            25f
        );
        CreatePointLight(
            "PointLight_Right",
            new Vector3(6, 6, -4),
            new Color4(0.2f, 0.5f, 1f, 1f),
            3.0f,
            25f
        );
        CreatePointLight(
            "PointLight_Top",
            new Vector3(0, 10, 0),
            new Color4(1f, 1f, 1f, 1f),
            2.0f,
            30f
        );

        // Finalize transforms
        var allNodes = new FastList<Node>();
        _root.Flatten(node => node.Enabled, allNodes);
        allNodes.UpdateTransforms();
    }

    private void CreateTransparentObject(
        string name,
        Geometry geometry,
        IMaterialPropertyManager materialPool,
        Vector3 position,
        Vector3 albedo,
        float opacity
    )
    {
        var mat = materialPool.Create(PBRShadingMode.PBR);
        mat.Properties.Albedo = albedo;
        mat.Properties.Roughness = 0.3f;
        mat.Properties.Metallic = 0.0f;
        mat.Properties.Ao = 1.0f;
        mat.Properties.Opacity = opacity;
        mat.NotifyUpdated();

        var node = new Node(_worldDataProvider!.World, name);
        node.Transform = new Transform { Translation = position };
        node.Entity.Set(new MeshComponent(geometry, mat));
        // Mark as transparent for OIT rendering
        node.Entity.Set(new TransparentComponent());
        _root!.AddChild(node);

        _transparentObjects.Add(
            new TransparentObjectInfo
            {
                Name = name,
                Node = node,
                MaterialProperties = mat,
                Opacity = opacity,
                Albedo = albedo,
            }
        );
    }

    private void CreatePointLight(
        string name,
        Vector3 position,
        Color4 color,
        float intensity,
        float range
    )
    {
        var lightNode = new Node(_worldDataProvider!.World, name);
        lightNode.Transform = new Transform { Translation = position };
        lightNode.Entity.Set(
            new RangeLightComponent(RangeLightType.Point)
            {
                Position = position,
                Color = color,
                Intensity = intensity,
                Range = range,
            }
        );
        _root!.AddChild(lightNode);
    }

    public void Render(int width, int height)
    {
        if (_renderer is null || _renderContext is null || _renderGraph is null)
            return;
        if (_imGuiRenderer is null)
            return;

        // --- Tick ---
        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();

        _orbitController?.Update(delta);

        // --- Update render context ---
        if (!_viewportSize.IsEmpty)
        {
            _renderContext.WindowSize = _viewportSize;
        }
        var aspectRatio = _viewportSize.IsEmpty
            ? (float)width / height
            : (float)_viewportSize.Width / _viewportSize.Height;

        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();

        // --- Step 1: Execute 3D render graph (offscreen) ---
        var cmdBuf = _renderer.RenderOffscreen(_renderContext, _renderGraph);

        var offscreenTexHandle = _renderContext.ResourceSet!.Textures[
            SystemBufferNames.TextureColorF16Current
        ];

        // --- Step 2: ImGui composite pass ---
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = offscreenTexHandle;

        _imGuiRenderer.BeginFrame(new Vector2(width, height));

        // Draw ImGui
        DrawGui(
            offscreenTexHandle,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );

        _imGuiRenderer.EndFrame();
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        // --- Submit & present ---
        _context.Submit(cmdBuf, swapchainTex);
    }

    // -------------------------------------------------------------------
    // Input forwarding
    // -------------------------------------------------------------------

    public void OnViewportMouseDown(int button, float viewportX, float viewportY)
    {
        if (_orbitController is null)
            return;
        if (button == 1) // right = rotate
        {
            _isRotating = true;
            _orbitController.OnRotateBegin(viewportX, viewportY);
        }
        else if (button == 2) // middle = pan
        {
            _isPanning = true;
            _orbitController.OnPanBegin(viewportX, viewportY);
        }
    }

    public void OnViewportMouseUp(int button)
    {
        if (button == 1)
            _isRotating = false;
        else if (button == 2)
            _isPanning = false;
    }

    public void OnViewportMouseMove(float viewportX, float viewportY)
    {
        if (_orbitController is null)
            return;
        if (_isRotating)
            _orbitController.OnRotateDelta(viewportX, viewportY);
        if (_isPanning)
            _orbitController.OnPanDelta(viewportX, viewportY);
    }

    public void OnViewportMouseWheel(float delta)
    {
        _orbitController?.OnZoomDelta(delta);
    }

    // -------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _imGuiRenderer?.Dispose();
                _worldDataProvider?.Dispose();
                _renderer?.Dispose();
                _renderGraph?.Dispose();
                _resourceManager?.Dispose();
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

/// <summary>
/// Tracks a transparent object for ImGui editing.
/// </summary>
internal class TransparentObjectInfo
{
    public required string Name { get; init; }
    public required Node Node { get; init; }
    public required MaterialProperties MaterialProperties { get; init; }
    public float Opacity { get; set; }
    public Vector3 Albedo { get; set; }
}
