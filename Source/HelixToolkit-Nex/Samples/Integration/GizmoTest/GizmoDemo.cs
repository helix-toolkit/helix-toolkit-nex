using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using Microsoft.Extensions.Logging;
using Viewport = HelixToolkit.Nex.ImGui.Viewport;

/// <summary>
/// Gizmo demo: renders a transform gizmo inside an ImGui-hosted offscreen 3D viewport using the
/// engine-hosted gizmo service and automatic pick routing. The manager is obtained from
/// <see cref="Engine.Engine.Gizmos"/> (Req 8.1) rather than constructed directly, and the
/// <see cref="GizmoRenderNode"/> is auto-registered by the engine on first gizmo creation. The gizmo
/// is created once through the factory (<see cref="GizmoManager.TryCreateGizmo"/> +
/// <see cref="GizmoManager.TrySetGizmo"/>) and driven per frame via
/// <see cref="GizmoManager.UpdateInstance"/>, which publishes/refreshes its <c>GizmoDrawInfo</c>
/// component; the render node gathers every such entity and draws its handles.
/// <para>
/// Click picks are issued with <c>routeGizmoPicks: true</c>, so the engine routes a decoded gizmo pick
/// to the gizmo service and begins the drag automatically (Req 8.2) — the sample no longer decodes the
/// entity-id pixel or classifies gizmo picks by hand to begin the drag. A scene pick resolves the
/// scene entity via <see cref="PickingResponse.TryGetPickingResult"/> and never begins a gizmo drag
/// (Req 8.3, 8.4); a no-hit begins no drag and resolves no scene entity (Req 8.7). Dragging an axis
/// handle translates a manipulated target mesh. An ImGui control panel changes gizmo mode, space,
/// occlusion mode and handle pixel size at runtime by updating the gizmo definition.
/// </para>
/// </summary>
internal sealed partial class GizmoDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<GizmoDemo>();

    // ImGui-integrated rendering
    private const string ViewportTextureName = "GizmoViewport";
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();
    private ImGuiRenderer? _imGuiRenderer;
    public ImGuiRenderer? ImGui => _imGuiRenderer;
    private Size _viewportSize = new Size(1, 1);

    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private Node? _root;

    // Camera
    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;
    private Viewport? _viewport;

    // Gizmo (obtained from Engine.Gizmos; the render node is auto-registered by the engine).
    private GizmoManager? _gizmoManager;
    private GizmoRenderNode? _gizmoNode;

    // The single factory-created gizmo instance set on the carrier entity.
    private GizmoInstanceHandle _gizmoInstance = GizmoInstanceHandle.None;

    // The mesh node manipulated by the gizmo, and its world matrix (source of truth).
    private MeshNode? _targetNode;
    private Matrix4x4 _targetWorldMatrix = Matrix4x4.CreateTranslation(0, 0, 0);
    private static readonly Matrix4x4 InitialTargetMatrix = Matrix4x4.CreateTranslation(0, 0, 0);

    private Node? _lightNode;

    // True while an async hover-pick request is outstanding (throttles hover picks to one at a time).
    private bool _hoverPickInFlight;

    // --- GUI / gizmo-definition state ---
    // These drive the current GizmoDefinition; changing any of them republishes the instance via
    // TryUpdateDefinition (a shape-key-unchanged republish, so no handle rebuild for space/occlusion/
    // size changes).
    private GizmoMode _gizmoMode = GizmoMode.Translate;
    private GizmoSpace _gizmoSpace = GizmoSpace.World;
    private GizmoOcclusionMode _occlusionMode = GizmoOcclusionMode.AlwaysOnTop;
    private float _handlePixelSize = 100f;
    private string _lastPickInfo = "No pick yet";

    public GizmoDemo(IContext context)
    {
        _context = context;
    }

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera
        {
            Position = new Vector3(0, 5, -30),
            Target = Vector3.Zero,
            FarPlane = 500,
        };
        _orbitController = new OrbitCameraController(_camera);

        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .WithFPS()
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .Build();

        // Obtain the gizmo manager from the engine (Req 8.1). Accessing Engine.Gizmos lazily creates
        // the single service instance and auto-registers a GizmoRenderNode (Req 4.6), so the sample no
        // longer constructs a GizmoManager or wires the render node manually. Retrieve the registered
        // node so the control panel can toggle its occlusion mode (pass depth state) at runtime.
        _gizmoManager = _engine.Gizmos;
        _gizmoNode = _engine.GetRenderNode<GizmoRenderNode>();
        if (_gizmoNode is not null)
        {
            _gizmoNode.OcclusionMode = _occlusionMode;
        }

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _viewport = new Viewport(_renderContext, _orbitController) { PickCallback = Pick };

        // Offscreen render target for the 3D viewport (displayed inside an ImGui window)
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
                res.Context.Context.CreateRenderTarget2D(
                    GraphicsSettings.IntermediateTargetFormat,
                    (uint)res.Context.WindowSize.Width,
                    (uint)res.Context.WindowSize.Height,
                    debugName: ViewportTextureName
                )
        );

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        // ImGui
        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());
        _imGuiPass.Colors[0].ClearColor = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;

        BuildScene();
    }

    private void BuildScene()
    {
        var geometryManager = _engine!.ResourceManager.Geometries;
        var materialPool = _engine.ResourceManager.PBRPropertyManager;
        var world = _worldDataProvider!.World;

        _root = new Node(world, "GizmoRoot");

        // --- Gizmo carrier entity ---
        // The GizmoRenderNode is a pure consumer: each frame it gathers every entity in the render
        // world that carries a GizmoDrawInfo component and draws its handles. Create a carrier entity
        // here; the factory-created gizmo is set on it below (after the target exists), which publishes
        // the GizmoDrawInfo component.
        var gizmoEntityNode = new Node(world, "GizmoManagerEntity");
        _root.AddChild(gizmoEntityNode);

        // --- Manipulated target: a box at the origin ---
        var boxBuilder = new MeshBuilder(true, true, true);
        boxBuilder.AddBox(Vector3.Zero, 6f, 6f, 6f);
        var boxGeom = boxBuilder.ToMesh().ToGeometry();
        bool succ = geometryManager.Add(boxGeom);
        System.Diagnostics.Debug.Assert(succ, "Failed to add box geometry");

        var targetMaterial = materialPool.Create("PBR");
        targetMaterial.Properties.Albedo = new Vector3(0.2f, 0.5f, 0.9f);
        targetMaterial.Properties.Metallic = 0.2f;
        targetMaterial.Properties.Roughness = 0.6f;
        targetMaterial.Properties.Ao = 1.0f;
        targetMaterial.Properties.Opacity = 1.0f;

        _targetNode = new MeshNode(world, "GizmoTarget")
        {
            Geometry = boxGeom,
            MaterialProperties = targetMaterial,
        };
        _root.AddChild(_targetNode);

        // Create the gizmo once through the factory (its handle set is built once and cached) and set
        // it on the carrier entity, which publishes the GizmoDrawInfo component so the gizmo renders
        // (Req 8.1, 8.5). Subsequent frames only call UpdateInstance to refresh origin/sizing.
        if (_gizmoManager!.TryCreateGizmo(BuildDefinition(), out _gizmoInstance))
        {
            _gizmoManager.TrySetGizmo(gizmoEntityNode.Entity, _gizmoInstance);
        }

        // --- A static reference sphere so the scene is not empty ---
        var sphereBuilder = new MeshBuilder(true, true, true);
        sphereBuilder.AddSphere(new Vector3(20, 0, 0), 5f, 48, 48);
        var sphereGeom = sphereBuilder.ToMesh().ToGeometry();
        succ = geometryManager.Add(sphereGeom);
        System.Diagnostics.Debug.Assert(succ, "Failed to add sphere geometry");

        var greyMaterial = materialPool.Create("PBR");
        greyMaterial.Properties.Albedo = new Vector3(0.6f, 0.6f, 0.6f);
        greyMaterial.Properties.Metallic = 0.3f;
        greyMaterial.Properties.Roughness = 0.5f;
        greyMaterial.Properties.Ao = 1.0f;
        greyMaterial.Properties.Opacity = 1.0f;

        var sphereNode = new MeshNode(world, "ReferenceSphere")
        {
            Geometry = sphereGeom,
            MaterialProperties = greyMaterial,
        };
        _root.AddChild(sphereNode);

        // --- Directional light ---
        _lightNode = new Node(world, "Sun");
        _lightNode.Entity.Set(
            new DirectionalLightInfo
            {
                Color = new Color(1.0f, 1.0f, 1.0f),
                Intensity = 2.0f,
                Direction = Vector3.Normalize(new Vector3(0.3f, -1.0f, 0.5f)),
            }
        );
        _root.AddChild(_lightNode);

        ApplyTargetTransform();
    }

    /// <summary>
    /// Builds the current <see cref="GizmoDefinition"/> from the sample's mode/space/occlusion/size
    /// state and the manipulated target association. Handle geometry depends only on the mode, so
    /// changing space, occlusion, or pixel size republishes the instance without a handle rebuild.
    /// </summary>
    private GizmoDefinition BuildDefinition() =>
        new(
            _gizmoMode,
            _gizmoSpace,
            _targetNode is not null ? (uint)_targetNode.Entity.Id : 0u,
            new GizmoHandleConfiguration(_handlePixelSize, _occlusionMode)
        );

    /// <summary>
    /// Applies the current definition state to the tracked gizmo instance, republishing its
    /// <c>GizmoDrawInfo</c> component. A no-op before the instance is created.
    /// </summary>
    private void ApplyDefinition()
    {
        if (_gizmoManager is null || !_gizmoInstance.IsValid)
            return;

        _gizmoManager.TryUpdateDefinition(_gizmoInstance, BuildDefinition());
    }

    /// <summary>
    /// Decomposes <see cref="_targetWorldMatrix"/> and writes it to the manipulated node's transform so
    /// the visible object follows gizmo drags. <see cref="Transform"/> exposes TRS (no full-matrix
    /// setter), so we decompose; a non-decomposable matrix falls back to translation only.
    /// </summary>
    private void ApplyTargetTransform()
    {
        if (_targetNode is null)
            return;

        if (Matrix4x4.Decompose(_targetWorldMatrix, out var scale, out var rotation, out var translation))
        {
            _targetNode.Transform.Scale = scale;
            _targetNode.Transform.Rotation = rotation;
            _targetNode.Transform.Translation = translation;
        }
        else
        {
            _targetNode.Transform.Translation = _targetWorldMatrix.Translation;
        }
        _targetNode.NotifyTransformChanged();
    }

    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _imGuiRenderer is null || _gizmoManager is null)
            return;

        _orbitController!.ViewportHeight = _viewportSize.Height;
        _orbitController!.ViewportWidth = _viewportSize.Width;
        _lightNode!.Entity.Update<DirectionalLightInfo>(light =>
        {
            light.Direction = _camera.LookDir;
            return light;
        });
        _renderContext.Update(_viewportSize, _camera);

        // Drive the gizmo for this frame: refresh only its origin and screen-derived sizing from the
        // current target and camera and republish its GizmoDrawInfo component. The cached handle set is
        // reused (no rebuild) while the definition is unchanged. The overlay node gathers that component
        // from the render world each frame, so this must run before RenderOffscreen.
        _gizmoManager.UpdateInstance(
            _gizmoInstance,
            _renderContext.CameraParams,
            _viewportSize,
            _targetWorldMatrix
        );

        // Keep the visible target node in sync with the manipulated matrix.
        ApplyTargetTransform();

        // --- ImGui frame ---
        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawGui(width, height, _renderContext.FinalOutputTexture);
        _imGuiRenderer.EndFrame();
        _engine.BeginFrame();
        // --- 3D scene (offscreen) ---
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider!,
            ViewportTextureName
        );
        // --- ImGui composite pass ---
        var swapchain = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchain;
        _imGuiDeps.PushTexture(_renderContext.FinalOutputTexture);
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        _engine.Submit(cmdBuf, swapchain);
        _imGuiDeps.PopTexture();
    }

    /// <summary>
    /// Left-click pick callback (viewport-relative pixel coordinates). Uses the asynchronous
    /// <see cref="Engine.Engine.CreatePickingRequest(RenderContext, Vector2, Action{PickingResponse}, bool)"/>
    /// path with <c>routeGizmoPicks: true</c>: the entity-id readback is recorded with the frame's
    /// command buffer and delivered a frame or two later. Before the callback runs, the engine routes a
    /// decoded gizmo pick to the gizmo service and begins the drag automatically (Req 8.2), so the
    /// sample does not decode the pixel or classify gizmo picks by hand.
    /// </summary>
    public void Pick(int x, int y)
    {
        if (_engine is null || _renderContext is null)
            return;

        _engine.CreatePickingRequest(
            _renderContext,
            new Vector2(x, y),
            OnClickPickResponse,
            routeGizmoPicks: true
        );
    }

    /// <summary>
    /// Async click-pick result. Gizmo picks were already routed to the gizmo service by the engine
    /// (Req 8.2), which begins the drag; when that happened the manager reports it is dragging. A scene
    /// pick resolves the scene entity via <see cref="PickingResponse.TryGetPickingResult"/> — which
    /// succeeds only for a scene pixel (world id &gt; 0), never for a gizmo pixel or a no-hit — and
    /// never begins a gizmo drag (Req 8.3, 8.4). A no-hit begins no drag and resolves no scene entity
    /// (Req 8.7).
    /// </summary>
    private void OnClickPickResponse(PickingResponse response)
    {
        if (_gizmoManager is null || _renderContext is null)
            return;

        // Gizmo pick: the engine's routing already began the drag on the gizmo service before this
        // callback ran, so just reflect the active drag and stop (Req 8.2).
        if (_gizmoManager.IsDragging)
        {
            _lastPickInfo = "Dragging gizmo handle";
            return;
        }

        // Scene pick: TryGetPickingResult succeeds only for a scene entity (world id > 0); gizmo pixels
        // (world id 0) and no-hits return false, so this resolves the same scene entity as before and
        // never begins a gizmo drag (Req 8.3, 8.4, 8.7).
        if (response.TryGetPickingResult(out var result))
        {
            bool isTarget = _targetNode is not null && result.Entity.Id == _targetNode.Entity.Id;
            _lastPickInfo = isTarget
                ? $"Selected target (entity {result.Entity})"
                : $"Picked entity {result.Entity}";
            _logger.LogInformation(
                "Scene pick: entity {Entity} at {Pos} (target={IsTarget})",
                result.Entity,
                result.WorldPosition,
                isTarget
            );
        }
        else
        {
            _lastPickInfo = "No hit";
        }
    }

    /// <summary>
    /// Schedules an asynchronous hover pick at the given viewport pixel, throttled to a single
    /// outstanding request at a time so hover picks don't flood the readback ring. The result is
    /// delivered to <see cref="OnHoverPickResponse"/>.
    /// </summary>
    private void RequestHoverPick(int x, int y)
    {
        if (_engine is null || _renderContext is null || _hoverPickInFlight)
            return;

        _hoverPickInFlight = true;
        _engine.CreatePickingRequest(_renderContext, new Vector2(x, y), OnHoverPickResponse);
    }

    /// <summary>
    /// Async hover-pick result: highlights the gizmo handle under the pointer (or clears the highlight
    /// when the pixel is not a gizmo handle). Hover picks are not routed (highlighting is not a drag),
    /// so the sample decodes the hovered pixel and routes it to the per-instance highlight overlay via
    /// <see cref="GizmoManager.ResolveHighlight"/>, which highlights the resolved handle on the owning
    /// gizmo and clears every other instance's highlight (or clears all when nothing resolves).
    /// </summary>
    private void OnHoverPickResponse(PickingResponse response)
    {
        _hoverPickInFlight = false;
        if (_gizmoManager is null)
            return;

        EntityIdDecodeResult decoded = Utils.UnpackEntityId(response.Data);
        _gizmoManager.ResolveHighlight(
            decoded.Kind == EntityIdPickKind.Gizmo,
            decoded.OwningEntityId,
            decoded.Handle
        );
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _imGuiRenderer?.Dispose();
        _worldDataProvider?.Dispose();
        _renderContext?.Teardown();
        _engine?.Dispose();
    }
}
