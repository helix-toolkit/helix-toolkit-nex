using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using Microsoft.Extensions.Logging;
using static HelixToolkit.Nex.Rendering.PostEffects.BoundingBoxPostEffect;
using Viewport = HelixToolkit.Nex.ImGui.Viewport;

namespace InstancingDemo;

/// <summary>
/// GPU instancing demonstration hosted inside an ImGui <see cref="Viewport"/>. Renders two visually
/// distinct mesh types (a box and a sphere), both drawn through the engine-level instancing API
/// (<see cref="Instancing"/>, <see cref="MeshNode.Instancing"/>, and <c>ResourceManager.InstancingManager</c>):
/// <list type="bullet">
/// <item>a <b>static</b> instanced mesh whose per-instance transforms are computed once at scene
/// construction and never change, and</item>
/// <item>a <b>dynamic</b> instanced mesh whose per-instance transforms are recomputed and re-marked
/// dirty every frame so the <c>InstancingManager</c> re-uploads its instance-transform GPU buffer
/// during the engine's frame-begin step, producing a visible animation.</item>
/// </list>
/// The scene is drawn to an offscreen target that is composited into an ImGui <see cref="Viewport"/>.
/// The viewport drives an <see cref="OrbitCameraController"/> from mouse input (right-drag to orbit,
/// middle-drag to pan, wheel to zoom) and forwards left-click picks to <see cref="Pick"/>. The picked
/// instance is highlighted with a wireframe box drawn by the <see cref="BoundingBoxPostEffect"/>.
/// The per-instance transform math lives in the pure <see cref="InstanceLayout"/> helper.
/// </summary>
internal partial class InstancingDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<InstancingDemo>();

    /// <summary>Name of the offscreen viewport render target registered with the render context.</summary>
    private const string ViewportTextureName = "InstancingViewportTexture";

    // Instance layout configuration. Both counts are >= 2 so each mesh type renders as 2+ instances.
    private const int StaticInstanceCount = 5;
    private const int DynamicInstanceCount = 5;
    private const float InstanceSpacing = 2.5f;

    // Bounds for the runtime count sliders. The minimum is 2 so each mesh type always renders as two
    // or more instances (matching the InstanceLayout correctness properties).
    private const int MinInstanceCount = 2;
    private const int MaxInstanceCount = 64;

    // Vertical separation between the static row and the dynamic row so both instanced mesh groups are
    // distinct and simultaneously visible in the framed view.
    private const float StaticRowY = 4f;
    private const float DynamicRowY = -4f;

    private readonly IContext _context;

    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;
    private Viewport? _viewport;

    private Instancing? _staticInstancing;
    private Instancing? _dynamicInstancing;

    // Scene nodes kept for the control panel / picking display.
    private MeshNode? _staticNode;
    private MeshNode? _dynamicNode;

    private InstanceLayout _layout;

    // Live instance counts driven by the control-panel sliders.
    private int _staticCount = StaticInstanceCount;
    private int _dynamicCount = DynamicInstanceCount;

    // Animation clock.
    private long _lastTimestamp;
    private float _animationTime;
    private bool _animationPaused;

    // Measured viewport region (physical pixels) the scene is rendered at; drives the camera aspect.
    private Size _viewportSize = new(1, 1);

    // Picking selection state. The bounding-box overlay is attached to this entity at the picked
    // instance index; cleared and re-applied on each successful pick.
    private Entity _selectedEntity = Entity.Null;
    private uint _selectedInstanceId;
    private PickedGeometryType _selectedKind = PickedGeometryType.None;
    private Vector3 _selectedWorldPosition;

    // ImGui swapchain compositing state.
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    private bool _disposedValue;

    public InstancingDemo(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _layout = new InstanceLayout(StaticInstanceCount, DynamicInstanceCount, InstanceSpacing);
    }

    /// <summary>Exposes the ImGui renderer so the host can forward the display scale.</summary>
    public ImGuiRenderer? ImGui => _imGuiRenderer;

    /// <summary>
    /// Initializes the engine (with the bounding-box post effect), render context and offscreen
    /// viewport target, world data provider, camera + orbit controller, the ImGui viewport, and the
    /// scene. Each step runs in sequence; if any step fails the failing step is logged and the
    /// exception propagates so no partially-initialized window is shown.
    /// </summary>
    public void Initialize(int width, int height)
    {
        _viewportSize = new Size(Math.Max(1, width), Math.Max(1, height));

        var step = "build engine";
        try
        {
            // --- Build the engine. The BoundingBoxPostEffect draws a wireframe AABB for every entity
            // carrying a BoundingBoxOverlay; SMAA + a custom intermediate target give a clean image to
            // composite into the ImGui viewport. ---
            _engine = EngineBuilder
                .Create(_context)
                .WithDefaultNodes()
                .WithPostEffects(post => post.AddEffect(new BoundingBoxPostEffect()))
                .WithSMAA()
                .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
                .Build();

            // --- Render context + offscreen viewport render target ---
            step = "create render context";
            _renderContext = _engine.CreateRenderContext();
            _renderContext.Initialize();
            _renderContext.ResourceSet.AddTexture(
                ViewportTextureName,
                _ =>
                    _context.CreateRenderTarget2D(
                        GraphicsSettings.IntermediateTargetFormat,
                        (uint)_renderContext.WindowSize.Width,
                        (uint)_renderContext.WindowSize.Height,
                        debugName: ViewportTextureName
                    )
            );

            // --- World data provider ---
            step = "create world data provider";
            _worldDataProvider = _engine.CreateWorldDataProvider();
            _worldDataProvider.Initialize();

            // --- Camera + orbit controller framing both instanced mesh groups ---
            step = "create camera";
            _camera = CreateCamera();
            _orbitController = new OrbitCameraController(_camera);

            // --- ImGui viewport: forwards mouse gestures to the orbit controller and left-clicks to Pick ---
            step = "create viewport";
            _viewport = new Viewport(_renderContext, _orbitController)
            {
                Title = "Instancing Demo",
                PickCallback = Pick,
            };

            // --- Scene ---
            step = "build scene";
            BuildScene();

            // --- ImGui renderer for the control panel + viewport compositing ---
            step = "create ImGui renderer";
            _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
            _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

            _imGuiPass.Colors[0].ClearColor = new Color4(0.10f, 0.10f, 0.10f, 1.0f);
            _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
            _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InstancingDemo initialization failed during step: {Step}.", step);
            throw;
        }
    }

    /// <summary>
    /// Creates a perspective camera positioned so both the static and dynamic instanced mesh rows fall
    /// inside the view frustum at startup.
    /// </summary>
    private static Camera CreateCamera()
    {
        float halfWidth =
            (Math.Max(StaticInstanceCount, DynamicInstanceCount) - 1) * 0.5f * InstanceSpacing;
        float verticalExtent = MathF.Abs(StaticRowY - DynamicRowY) + 2f;
        float distance = -(MathF.Max(halfWidth, verticalExtent) + 14f);

        return new PerspectiveCamera()
        {
            Position = new Vector3(0f, 0f, distance),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FarPlane = 300f,
        };
    }

    /// <summary>
    /// Builds the scene: two distinct geometries (box + sphere), one static and one dynamic
    /// <see cref="Instancing"/> populated from <see cref="_layout"/> and registered with the engine's
    /// instancing manager, two <see cref="MeshNode"/>s binding geometry + material + matching
    /// instancing, and a directional light.
    /// </summary>
    private void BuildScene()
    {
        var world = _worldDataProvider!.World;
        var geometryManager = _engine!.ResourceManager.Geometries;
        var instancingManager = _engine.ResourceManager.InstancingManager;
        var materialPool = _engine.ResourceManager.PBRPropertyManager;

        _root = new Node(world, "InstancingRoot");

        // --- Two DISTINCT geometries ---
        var boxBuilder = new MeshBuilder(true, true, true);
        boxBuilder.AddBox(Vector3.Zero, 1.4f, 1.4f, 1.4f);
        var boxGeo = boxBuilder.ToMesh().ToGeometry();
        if (!geometryManager.Add(boxGeo))
        {
            throw new InvalidOperationException(
                "Failed to add the box geometry to the resource manager."
            );
        }

        var sphereBuilder = new MeshBuilder(true, true, true);
        sphereBuilder.AddSphere(Vector3.Zero, 0.8f, 32, 32);
        var sphereGeo = sphereBuilder.ToMesh().ToGeometry();
        if (!geometryManager.Add(sphereGeo))
        {
            throw new InvalidOperationException(
                "Failed to add the sphere geometry to the resource manager."
            );
        }

        // --- Static instancing: populated ONCE with the layout's static transforms ---
        _staticInstancing = instancingManager.Create(
            isDynamic: false,
            "StaticInstancing",
            _layout.ComputeStaticTransforms()
        );

        // --- Dynamic instancing: populated with the initial (t=0) transforms ---
        _dynamicInstancing = instancingManager.Create(
            isDynamic: true,
            "DynamicInstancing",
            _layout.ComputeDynamicTransforms(0f)
        );

        // --- Static instanced mesh node (box) ---
        var staticMaterial = materialPool.Create(PBRShadingMode.PBR);
        staticMaterial.Properties.Albedo = new Vector3(0.85f, 0.35f, 0.2f);
        staticMaterial.Properties.Metallic = 0.1f;
        staticMaterial.Properties.Roughness = 0.6f;
        staticMaterial.Properties.Ao = 1.0f;
        staticMaterial.Properties.Opacity = 1.0f;
        staticMaterial.NotifyUpdated();

        _staticNode = new MeshNode(world, "StaticInstancedMesh")
        {
            Transform = new Transform { Translation = new Vector3(0f, StaticRowY, 0f) },
            Geometry = boxGeo,
            MaterialProperties = staticMaterial,
            Instancing = _staticInstancing,
        };
        _root.AddChild(_staticNode);

        // --- Dynamic instanced mesh node (sphere) ---
        var dynamicMaterial = materialPool.Create(PBRShadingMode.PBR);
        dynamicMaterial.Properties.Albedo = new Vector3(0.2f, 0.5f, 0.85f);
        dynamicMaterial.Properties.Metallic = 0.1f;
        dynamicMaterial.Properties.Roughness = 0.4f;
        dynamicMaterial.Properties.Ao = 1.0f;
        dynamicMaterial.Properties.Opacity = 1.0f;
        dynamicMaterial.NotifyUpdated();

        _dynamicNode = new MeshNode(world, "DynamicInstancedMesh")
        {
            Transform = new Transform { Translation = new Vector3(0f, DynamicRowY, 0f) },
            Geometry = sphereGeo,
            MaterialProperties = dynamicMaterial,
            Instancing = _dynamicInstancing,
        };
        _root.AddChild(_dynamicNode);

        // --- At least one light so both meshes receive non-zero shading ---
        var sunNode = new DirectionalLightNode(world, "Sun")
        {
            Direction = Vector3.Normalize(new Vector3(-1f, -1.5f, 1f)),
            Color = new Color4(1f, 0.98f, 0.9f, 1f),
            Intensity = 2.5f,
        };
        _root.AddChild(sunNode);
    }

    /// <summary>
    /// Per-frame render entry point. Builds the ImGui frame (control panel + viewport), updates the
    /// orbit camera and the dynamic instancing animation, renders the scene to the offscreen viewport
    /// target, then composites that target plus the ImGui draw data to the swapchain.
    /// </summary>
    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _imGuiRenderer is null)
        {
            return;
        }

        if (width < 1 || height < 1)
        {
            return;
        }

        // --- Elapsed seconds since the previous frame ---
        if (_lastTimestamp == 0)
        {
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
        long now = Stopwatch.GetTimestamp();
        float delta = (float)(now - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = now;

        // --- ImGui frame: control panel + viewport image (uses last frame's offscreen target) ---
        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawGui(
            _renderContext.FinalOutputTexture,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );
        _imGuiRenderer.EndFrame();

        // --- Camera + animation update ---
        _orbitController?.Update(delta);

        if (!_animationPaused)
        {
            _animationTime += delta;
        }

        // Recompute the DYNAMIC transforms and re-mark the dynamic instancing dirty so the
        // InstancingManager re-uploads its buffer during BeginFrame. The static instancing is untouched.
        if (_dynamicInstancing is not null)
        {
            _dynamicInstancing.Transforms.Clear();
            _dynamicInstancing.Transforms.AddAll(_layout.ComputeDynamicTransforms(_animationTime));
            _dynamicInstancing.MarkDirty();
        }

        // --- Render scene offscreen, then composite with ImGui to the swapchain ---
        _renderContext.Update(_viewportSize, _camera);

        try
        {
            _engine.BeginFrame();

            var cmdBuf = _engine.RenderOffscreen(
                _renderContext,
                _worldDataProvider!,
                ViewportTextureName
            );

            var swapchainTex = _context.GetCurrentSwapchainTexture();
            if (swapchainTex.Empty)
            {
                return;
            }

            _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
            using var texScope = _imGuiDeps.PushTextureScoped(_renderContext.FinalOutputTexture);

            _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

            _engine.Submit(cmdBuf, swapchainTex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "InstancingDemo skipped a frame due to a swapchain/render failure."
            );
        }
    }

    /// <summary>
    /// Window resize entry point. The swapchain is recreated by the host; the offscreen render size is
    /// driven per-frame from the measured ImGui viewport region, so this only needs to keep a valid
    /// fallback viewport size for the first frames before the viewport has been measured.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            return;
        }
        // Only used until the ImGui viewport reports its measured size in DrawGui.
        if (_viewport is null || _viewport.ViewportSize.Width <= 1)
        {
            _viewportSize = new Size(width, height);
        }
    }

    /// <summary>
    /// Picking entry point invoked by the <see cref="Viewport"/> with viewport-relative pixel
    /// coordinates. Issues an async GPU picking request; on a hit it moves the bounding-box overlay to
    /// the picked entity at the picked instance index, so the clicked instance of an instanced mesh is
    /// outlined. A miss clears the current selection.
    /// </summary>
    public void Pick(int x, int y)
    {
        if (_engine is null || _renderContext is null)
        {
            return;
        }

        _engine.CreatePickingRequest(
            _renderContext,
            new Vector2(x, y),
            response =>
            {
                // Clear any previous selection overlay first.
                ClearSelection();

                if (!response.TryGetPickingResult(out var result) || !result.Entity.Valid)
                {
                    return;
                }

                _selectedEntity = result.Entity;
                _selectedInstanceId = result.InstanceId;
                _selectedKind = result.PickGeometryType;
                _selectedWorldPosition = result.WorldPosition;

                // Attach the bounding-box overlay at the picked instance index so the
                // BoundingBoxPostEffect outlines exactly the clicked instance.
                _selectedEntity.Set(new BoundingBoxOverlay(Color.Green, result.InstanceId));
            }
        );
    }

    /// <summary>Removes the bounding-box overlay from the currently selected entity, if any.</summary>
    private void ClearSelection()
    {
        if (_selectedEntity.Valid && _selectedEntity.Has<BoundingBoxOverlay>())
        {
            _selectedEntity.Remove<BoundingBoxOverlay>();
        }
        _selectedEntity = Entity.Null;
        _selectedInstanceId = 0;
        _selectedKind = PickedGeometryType.None;
        _selectedWorldPosition = Vector3.Zero;
    }

    /// <summary>
    /// Sets the number of static (box) instances and rebuilds the layout if the value changed. The new
    /// static transforms are uploaded immediately; the dynamic transforms are recomputed each frame.
    /// </summary>
    private void SetStaticCount(int count)
    {
        count = Math.Clamp(count, MinInstanceCount, MaxInstanceCount);
        if (count == _staticCount)
        {
            return;
        }
        _staticCount = count;
        RebuildLayout();
    }

    /// <summary>
    /// Sets the number of dynamic (sphere) instances and rebuilds the layout if the value changed.
    /// </summary>
    private void SetDynamicCount(int count)
    {
        count = Math.Clamp(count, MinInstanceCount, MaxInstanceCount);
        if (count == _dynamicCount)
        {
            return;
        }
        _dynamicCount = count;
        RebuildLayout();
    }

    /// <summary>
    /// Recreates the pure <see cref="InstanceLayout"/> for the current counts, re-uploads the static
    /// instance transforms, and clears any selection that may now reference a removed instance. The
    /// dynamic instance transforms are recomputed from the new layout on the next <see cref="Render"/>.
    /// </summary>
    private void RebuildLayout()
    {
        _layout = new InstanceLayout(_staticCount, _dynamicCount, InstanceSpacing);

        if (_staticInstancing is not null)
        {
            _staticInstancing.Transforms.Clear();
            _staticInstancing.Transforms.AddAll(_layout.ComputeStaticTransforms());
            _staticInstancing.MarkDirty();
        }

        // The picked instance index may now be out of range; drop the selection to avoid a stale readout.
        ClearSelection();
    }

    /// <summary>
    /// Disposal entry point. Idempotent; disposes the ImGui renderer and GPU resources / render context
    /// before the engine, isolating per-resource failures and surfacing an aggregate error if any occur.
    /// </summary>
    public void Dispose()
    {
        if (_disposedValue)
        {
            return;
        }

        var errors = new List<Exception>();

        TryDispose(() => _imGuiRenderer?.Dispose(), "ImGui renderer", errors);
        TryDispose(() => _staticInstancing?.Dispose(), "static instancing", errors);
        TryDispose(() => _dynamicInstancing?.Dispose(), "dynamic instancing", errors);
        TryDispose(() => _renderContext?.Dispose(), "render context", errors);
        TryDispose(() => _worldDataProvider?.Dispose(), "world data provider", errors);
        TryDispose(() => _engine?.Dispose(), "engine", errors);

        _imGuiRenderer = null;
        _staticInstancing = null;
        _dynamicInstancing = null;
        _renderContext = null;
        _worldDataProvider = null;
        _engine = null;
        _root = null;
        _viewport = null;
        _orbitController = null;

        _disposedValue = true;
        GC.SuppressFinalize(this);

        if (errors.Count > 0)
        {
            throw new AggregateException(
                $"InstancingDemo disposal completed, but {errors.Count} resource(s) failed to dispose.",
                errors
            );
        }
    }

    /// <summary>
    /// Disposes a single owned resource, isolating any failure so the remaining resources are still
    /// disposed. A thrown exception is logged via <see cref="_logger"/> and collected into
    /// <paramref name="errors"/> rather than propagated.
    /// </summary>
    private static void TryDispose(Action dispose, string name, List<Exception> errors)
    {
        try
        {
            dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InstancingDemo failed to dispose the {Resource}.", name);
            errors.Add(ex);
        }
    }
}
