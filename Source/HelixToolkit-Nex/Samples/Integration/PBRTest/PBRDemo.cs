using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
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
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using Microsoft.Extensions.Logging;

namespace PBRTest;

internal struct IndexComponent
{
    public int Index { get; init; }
}

/// <summary>
/// Holds the editable ImGui state for a single PBR sphere in the demo grid.
/// All fields mirror the corresponding <see cref="PBRMaterialProperties"/> properties.
/// </summary>
internal sealed class PBRSphereInfo
{
    public required string Name { get; init; }
    public required MeshNode Node { get; init; }
    public required PBRMaterialProperties MaterialProperties { get; init; }

    // ---- Color ----
    public Vector3 Albedo;
    public Vector3 Emissive;
    public Vector3 Ambient;

    // ---- Surface ----
    public float Metallic;
    public float Roughness;
    public float Ao;
    public float Reflectance;
    public float VertexColorMix;

    // ---- Clear Coat ----
    public float ClearCoatStrength;
    public float ClearCoatRoughness;

    // ---- Transparency ----
    public float Opacity;

    /// <summary>Pulls all values from <see cref="MaterialProperties"/> into the editable fields.</summary>
    public void PullFromMaterial()
    {
        var p = MaterialProperties.Properties;
        Albedo = p.Albedo;
        Emissive = p.Emissive;
        Ambient = p.Ambient;
        Metallic = p.Metallic;
        Roughness = p.Roughness;
        Ao = p.Ao;
        Reflectance = p.Reflectance;
        VertexColorMix = p.VertexColorMix;
        ClearCoatStrength = p.ClearCoatStrength;
        ClearCoatRoughness = p.ClearCoatRoughness;
        Opacity = p.Opacity;
    }

    /// <summary>Pushes all editable fields back into <see cref="MaterialProperties"/> and notifies the renderer.</summary>
    public void PushToMaterial()
    {
        MaterialProperties.Properties.Albedo = Albedo;
        MaterialProperties.Properties.Emissive = Emissive;
        MaterialProperties.Properties.Ambient = Ambient;
        MaterialProperties.Properties.Metallic = Metallic;
        MaterialProperties.Properties.Roughness = Roughness;
        MaterialProperties.Properties.Ao = Ao;
        MaterialProperties.Properties.Reflectance = Reflectance;
        MaterialProperties.Properties.VertexColorMix = VertexColorMix;
        MaterialProperties.Properties.ClearCoatStrength = ClearCoatStrength;
        MaterialProperties.Properties.ClearCoatRoughness = ClearCoatRoughness;
        MaterialProperties.Properties.Opacity = Opacity;
        MaterialProperties.NotifyUpdated();
    }
}

/// <summary>
/// PBR material property manipulation demo.
/// Renders a grid of spheres with individually controllable PBR material properties
/// via an ImGui control panel.
/// </summary>
internal partial class PBRDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<PBRDemo>();
    private const string ViewportTextureName = "ViewportTextureName";

    // Grid dimensions: rows = metallic steps, cols = roughness steps
    private const int GridRows = 4;
    private const int GridCols = 5;
    private const float SphereSpacing = 3.0f;
    private const float SphereRadius = 1.1f;

    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    private Camera _camera = new PerspectiveCamera();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _lastTimestamp;

    // ImGui swapchain rendering state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    private Size _viewportSize = new(1, 1);

    // Camera controller (orbit)
    private OrbitCameraController? _orbitController;
    private bool _isRotating;
    private bool _isPanning;

    // Sphere grid data
    private readonly List<PBRSphereInfo> _spheres = [];

    // Currently selected sphere index (-1 = none)
    private int _selectedIndex = -1;

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public PBRDemo(IContext context)
    {
        _context = context;
    }

    public void Initialize(int width, int height)
    {
        float gridWidth = (GridCols - 1) * SphereSpacing;
        float gridDepth = (GridRows - 1) * SphereSpacing;

        _camera = new PerspectiveCamera()
        {
            Position = new Vector3(gridWidth * 0.5f, gridDepth * 0.5f, -20f),
            Target = new Vector3(gridWidth * 0.5f, gridDepth * 0.5f, 0f),
            FarPlane = 300,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .RenderToCustomTarget(RenderSettings.IntermediateTargetFormat)
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return _context.CreateRenderTarget2D(
                    RenderSettings.IntermediateTargetFormat,
                    (uint)_renderContext.WindowSize.Width,
                    (uint)_renderContext.WindowSize.Height,
                    debugName: ViewportTextureName
                );
            }
        );

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        BuildScene();

        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        _imGuiPass.Colors[0].ClearColor = new Color4(0.10f, 0.10f, 0.10f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
    }

    private void BuildScene()
    {
        var geometryManager = _engine!.ResourceManager.Geometries;
        var materialPool = _engine.ResourceManager.PBRPropertyManager;

        _root = new Node(_worldDataProvider!.World, "PBRRoot");

        // Shared sphere geometry
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, SphereRadius, 32, 32);
        var sphereGeo = meshBuilder.ToMesh().ToGeometry();
        geometryManager.Add(sphereGeo);

        float gridWidth = (GridCols - 1) * SphereSpacing;
        float gridDepth = (GridRows - 1) * SphereSpacing;

        // Build a grid: rows vary metallic (0→1), cols vary roughness (0.05→1)
        for (int row = 0; row < GridRows; row++)
        {
            float metallic = row / (float)(GridRows - 1);

            for (int col = 0; col < GridCols; col++)
            {
                float roughness = MathF.Max(0.05f, col / (float)(GridCols - 1));

                var mat = materialPool.Create(PBRShadingMode.PBR);
                mat.Properties.Albedo = new Vector3(0.7f, 0.3f, 0.1f);
                mat.Properties.Metallic = metallic;
                mat.Properties.Roughness = roughness;
                mat.Properties.Ao = 1.0f;
                mat.Properties.Opacity = 1.0f;
                mat.Properties.Emissive = Vector3.Zero;
                mat.NotifyUpdated();

                var name = $"Sphere_M{metallic:F2}_R{roughness:F2}";
                var sphereNode = new MeshNode(_worldDataProvider!.World, name);
                sphereNode.Transform = new Transform
                {
                    Translation = new Vector3(col * SphereSpacing, row * SphereSpacing, 0f),
                };
                sphereNode.Geometry = sphereGeo;
                sphereNode.MaterialProperties = mat;
                _root.AddChild(sphereNode);

                var info = new PBRSphereInfo
                {
                    Name = name,
                    Node = sphereNode,
                    MaterialProperties = mat,
                };
                info.PullFromMaterial();
                sphereNode.Entity.Set(new IndexComponent { Index = _spheres.Count });
                _spheres.Add(info);
            }
        }

        // Directional light from upper-left
        var sunNode = new Node(_worldDataProvider.World, "Sun");
        sunNode.Entity.Set(
            new DirectionalLightComponent
            {
                Direction = Vector3.Normalize(new Vector3(-1f, -1.5f, 1f)),
                Color = new Color4(1f, 0.98f, 0.9f, 1f),
                Intensity = 2.5f,
            }
        );
        sunNode.Transform = new Transform();
        _root.AddChild(sunNode);

        // Fill light from the right
        var fillNode = new Node(_worldDataProvider.World, "FillLight");
        fillNode.Entity.Set(
            new RangeLightComponent(RangeLightType.Point)
            {
                Position = new Vector3(gridWidth + 6f, gridDepth * 0.5f, -8f),
                Color = new Color4(0.4f, 0.6f, 1f, 1f),
                Intensity = 4.0f,
                Range = 60f,
            }
        );
        fillNode.Transform = new Transform
        {
            Translation = new Vector3(gridWidth + 6f, gridDepth * 0.5f, -8f),
        };
        _root.AddChild(fillNode);

        // Rim light from behind
        var rimNode = new Node(_worldDataProvider.World, "RimLight");
        rimNode.Entity.Set(
            new RangeLightComponent(RangeLightType.Point)
            {
                Position = new Vector3(gridWidth * 0.5f, gridDepth + 4f, 10f),
                Color = new Color4(1f, 0.85f, 0.5f, 1f),
                Intensity = 3.0f,
                Range = 70f,
            }
        );
        rimNode.Transform = new Transform
        {
            Translation = new Vector3(gridWidth * 0.5f, gridDepth + 4f, 10f),
        };
        _root.AddChild(rimNode);
    }

    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _imGuiRenderer is null)
            return;

        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();

        _imGuiRenderer.BeginFrame(new Vector2(width, height));

        DrawGui(
            _renderContext.FinalOutputTexture,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );

        _imGuiRenderer.EndFrame();

        _orbitController?.Update(delta);

        _renderContext.Update(_viewportSize, _camera);

        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider!,
            ViewportTextureName
        );

        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = _renderContext.FinalOutputTexture;

        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        _engine.Submit(cmdBuf, swapchainTex);
    }

    // -----------------------------------------------------------------------
    // Input forwarding
    // -----------------------------------------------------------------------

    public void OnViewportMouseDown(int button, float vx, float vy)
    {
        if (_orbitController is null)
            return;
        if (button == 0)
        {
            var result = _renderContext!.Pick((int)vx, (int)vy);
            if (result is null)
            {
                return;
            }
            _selectedIndex = (int)result.Entity.Get<IndexComponent>().Index;
        }
        else if (button == 1)
        {
            _isRotating = true;
            _orbitController.OnRotateBegin(vx, vy);
        }
        else if (button == 2)
        {
            _isPanning = true;
            _orbitController.OnPanBegin(vx, vy);
        }
    }

    public void OnViewportMouseUp(int button)
    {
        if (button == 1)
            _isRotating = false;
        else if (button == 2)
            _isPanning = false;
    }

    public void OnViewportMouseMove(float vx, float vy)
    {
        if (_orbitController is null)
            return;
        if (_isRotating)
            _orbitController.OnRotateDelta(vx, vy);
        if (_isPanning)
            _orbitController.OnPanDelta(vx, vy);
    }

    public void OnViewportMouseWheel(float delta)
    {
        _orbitController?.OnZoomDelta(delta);
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing)
        {
            foreach (var s in _spheres)
                s.MaterialProperties.Dispose();
            _spheres.Clear();
            _imGuiRenderer?.Dispose();
            _renderContext?.Dispose();
            _worldDataProvider?.Dispose();
            _engine?.Dispose();
        }
        _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
