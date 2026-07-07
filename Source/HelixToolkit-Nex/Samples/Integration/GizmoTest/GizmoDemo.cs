using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.ECS;
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

    // The manipulable targets in the scene. Each target owns its own TransformManipulator; the gizmo
    // instance is bound to exactly one target's manipulator at a time via GizmoManager.BindTarget.
    // Clicking a target in the viewport rebinds the gizmo to it (SetActiveTarget), and "Swap Target"
    // cycles through them. The bound manipulator is the single source of truth for the target
    // transform, so the demo keeps no bookkeeping matrix and applies no transform by hand.
    private readonly List<GizmoTarget> _targets = [];
    private int _activeTargetIndex = -1;

    // A custom manipulator that drives a non-transform property (the active target's material Metallic
    // scalar) instead of the node transform, demonstrating custom manipulation end to end (Req 9.4,
    // 9.5). It is bound to the gizmo instance on demand via the "Custom Manipulator" toggle; when
    // active the gizmo drag edits the material value rather than moving the node.
    private MaterialScalarManipulator? _customManipulator;
    private bool _customActive;

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
        var world = _worldDataProvider!.World;

        _root = new Node(world, "GizmoRoot");

        // --- Gizmo carrier entity ---
        // The GizmoRenderNode is a pure consumer: each frame it gathers every entity in the render
        // world that carries a GizmoDrawInfo component and draws its handles. Create a carrier entity
        // here; the factory-created gizmo is set on it below (after the target exists), which publishes
        // the GizmoDrawInfo component.
        var gizmoEntityNode = new Node(world, "GizmoManagerEntity");
        _root.AddChild(gizmoEntityNode);

        // --- Manipulable targets ---
        // One box geometry and one sphere geometry are added to the geometry manager once and shared
        // across several nodes; each node gets its own material (color), world position, and its own
        // TransformManipulator. Each geometry is centered at the origin and positioned via the node
        // transform so the bound manipulator reports the node's world position as the gizmo origin.
        var boxBuilder = new MeshBuilder(true, true, true);
        boxBuilder.AddBox(Vector3.Zero, 6f, 6f, 6f);
        var boxGeom = boxBuilder.ToMesh().ToGeometry();
        bool succ = geometryManager.Add(boxGeom);
        System.Diagnostics.Debug.Assert(succ, "Failed to add box geometry");

        var sphereBuilder = new MeshBuilder(true, true, true);
        sphereBuilder.AddSphere(Vector3.Zero, 4f, 48, 48);
        var sphereGeom = sphereBuilder.ToMesh().ToGeometry();
        succ = geometryManager.Add(sphereGeom);
        System.Diagnostics.Debug.Assert(succ, "Failed to add sphere geometry");

        // A spread of targets to exercise click-to-select and runtime rebinding. Click any object in
        // the viewport to bind the gizmo to it; "Swap Target" cycles through them in order.
        AddTarget("Box A", boxGeom, new Vector3(-20f, 0f, 0f), new Vector3(0.2f, 0.5f, 0.9f));
        AddTarget("Sphere B", sphereGeom, new Vector3(-7f, 0f, 0f), new Vector3(0.9f, 0.4f, 0.3f));
        AddTarget("Box C", boxGeom, new Vector3(7f, 0f, 0f), new Vector3(0.3f, 0.8f, 0.4f));
        AddTarget("Sphere D", sphereGeom, new Vector3(20f, 0f, 0f), new Vector3(0.9f, 0.8f, 0.3f));
        AddTarget("Box E", boxGeom, new Vector3(0f, 12f, 0f), new Vector3(0.7f, 0.4f, 0.9f));

        // Create the gizmo once through the factory (its handle set is built once and cached) and set
        // it on the carrier entity, which publishes the GizmoDrawInfo component so the gizmo renders
        // (Req 8.1, 8.5). Subsequent frames only call UpdateInstance to refresh origin/sizing. Bind the
        // first target as the initial target so the gizmo origin starts on it (Req 9.1); clicking any
        // target rebinds the gizmo to it at runtime.
        if (_gizmoManager!.TryCreateGizmo(BuildDefinition(), out _gizmoInstance))
        {
            _gizmoManager.TrySetGizmo(gizmoEntityNode.Entity, _gizmoInstance);
            SetActiveTarget(0);
        }

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
    }

    /// <summary>
    /// Gets the currently active gizmo target, or <see langword="null"/> when none is selected.
    /// </summary>
    private GizmoTarget? ActiveTarget =>
        _activeTargetIndex >= 0 && _activeTargetIndex < _targets.Count
            ? _targets[_activeTargetIndex]
            : null;

    /// <summary>
    /// Creates a mesh node for <paramref name="geometry"/> at <paramref name="position"/> with a solid
    /// PBR color, wraps it in its own <see cref="TransformManipulator"/>, and registers it as a
    /// selectable gizmo target. The node geometry is centered at the origin, so the manipulator reports
    /// the node's world position as the gizmo origin.
    /// </summary>
    private void AddTarget(string name, Geometry geometry, Vector3 position, Vector3 color)
    {
        var world = _worldDataProvider!.World;

        var material = _engine!.ResourceManager.PBRPropertyManager.Create("PBR");
        material.Properties.Albedo = color;
        material.Properties.Metallic = 0.2f;
        material.Properties.Roughness = 0.6f;
        material.Properties.Ao = 1.0f;
        material.Properties.Opacity = 1.0f;

        var node = new MeshNode(world, name)
        {
            Geometry = geometry,
            MaterialProperties = material,
        };
        node.Transform.Translation = position;
        node.NotifyTransformChanged();
        _root!.AddChild(node);

        _targets.Add(
            new GizmoTarget
            {
                Name = name,
                Node = node,
                Manipulator = new TransformManipulator(node),
            }
        );
    }

    /// <summary>
    /// Makes the target at <paramref name="index"/> the active gizmo target by binding its
    /// <see cref="TransformManipulator"/> to the gizmo instance (Req 9.2). The next frame's
    /// <see cref="GizmoManager.UpdateInstance"/> reads the gizmo origin from the newly bound
    /// manipulator's transform (Req 9.3). Binding a transform manipulator also disables the custom
    /// manipulator override, and the custom manipulator is re-created for the newly active node.
    /// </summary>
    private void SetActiveTarget(int index)
    {
        if (_gizmoManager is null || !_gizmoInstance.IsValid)
            return;
        if (index < 0 || index >= _targets.Count)
            return;

        GizmoTarget target = _targets[index];
        if (_gizmoManager.BindTarget(_gizmoInstance, target.Manipulator))
        {
            _activeTargetIndex = index;
            // A custom manipulator drives one node's material; recreate it for the newly active node so
            // the "Custom Manipulator" toggle affects whatever object is currently selected.
            _customManipulator = new MaterialScalarManipulator(target.Node);
            _customActive = false;
        }
    }

    /// <summary>
    /// Selects the target whose mesh node carries <paramref name="entity"/>, if any, binding the gizmo
    /// to it. Returns <see langword="true"/> when a target matched and was selected.
    /// </summary>
    private bool TrySelectTargetByEntity(Entity entity)
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            if (_targets[i].Node.Entity.Id == entity.Id)
            {
                SetActiveTarget(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cycles the active gizmo target to the next one in the list at runtime (Req 9.2), wrapping around.
    /// </summary>
    private void SwapTarget()
    {
        if (_targets.Count == 0)
            return;

        int next = (_activeTargetIndex + 1) % _targets.Count;
        SetActiveTarget(next);
    }

    /// <summary>
    /// Toggles between the custom (non-transform) manipulator and the active target's transform
    /// manipulator on the gizmo instance (Req 9.4, 9.5). When enabled, <see cref="GizmoManager.BindTarget"/>
    /// binds the <see cref="MaterialScalarManipulator"/> so subsequent drag deltas drive the active
    /// target's material scalar; when disabled it rebinds that target's transform manipulator.
    /// </summary>
    private void ToggleCustomManipulator()
    {
        if (_gizmoManager is null || !_gizmoInstance.IsValid)
            return;

        GizmoTarget? active = ActiveTarget;
        if (active is null)
            return;

        if (_customActive)
        {
            if (_gizmoManager.BindTarget(_gizmoInstance, active.Manipulator))
                _customActive = false;
        }
        else if (_customManipulator is not null
            && _gizmoManager.BindTarget(_gizmoInstance, _customManipulator))
        {
            _customActive = true;
        }
    }

    /// <summary>
    /// A selectable object the gizmo can manipulate: its scene node and the
    /// <see cref="TransformManipulator"/> that owns its transform.
    /// </summary>
    private sealed class GizmoTarget
    {
        public required string Name { get; init; }
        public required MeshNode Node { get; init; }
        public required TransformManipulator Manipulator { get; init; }
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
            _viewportSize
        );

        // The bound manipulator owns write-back to its node and is the single source of truth for the
        // target transform; the control panel reads it directly for display, so the demo keeps no
        // bookkeeping matrix here.

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
        // (world id 0) and no-hits return false, so this resolves the scene entity and never begins a
        // gizmo drag (Req 8.3, 8.4, 8.7). If the picked entity is one of the manipulable targets, bind
        // the gizmo to it so clicking an object switches the gizmo's target (Req 9.2, 9.3).
        if (response.TryGetPickingResult(out var result))
        {
            if (TrySelectTargetByEntity(result.Entity))
            {
                _lastPickInfo = $"Selected target '{ActiveTarget?.Name}' (entity {result.Entity})";
                _logger.LogInformation(
                    "Selected gizmo target {Target} (entity {Entity}) at {Pos}",
                    ActiveTarget?.Name,
                    result.Entity,
                    result.WorldPosition
                );
            }
            else
            {
                _lastPickInfo = $"Picked entity {result.Entity} (not a target)";
                _logger.LogInformation(
                    "Scene pick: entity {Entity} at {Pos} (not a gizmo target)",
                    result.Entity,
                    result.WorldPosition
                );
            }
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
