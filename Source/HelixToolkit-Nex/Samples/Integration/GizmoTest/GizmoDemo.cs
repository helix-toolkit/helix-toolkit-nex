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
/// Gizmo demo: renders a transform gizmo inside an ImGui-hosted offscreen 3D viewport and drives it
/// through the <see cref="GizmoManager"/> each frame. The manager publishes its gizmo as a
/// <c>GizmoDrawInfo</c> component on an entity in the render world; the <see cref="GizmoRenderNode"/>
/// gathers every such entity each frame and draws its handles. Picks are classified from the shared
/// entity-id target with the unified <see cref="Utils.UnpackEntityId(ulong)"/> decode: a gizmo pixel
/// (world id 0, <c>Gizmo</c> encoding type) resolves to an owning gizmo entity plus
/// <c>GizmoHandleId</c>, while a scene pixel (world id &gt; 0) resolves a normal scene entity.
/// Dragging an axis handle translates a manipulated target mesh. An ImGui control panel changes gizmo
/// mode, space, occlusion mode and handle pixel size at runtime.
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

    // Gizmo
    private GizmoManager? _gizmoManager;
    private GizmoRenderNode? _gizmoNode;

    // The mesh node manipulated by the gizmo, and its world matrix (source of truth).
    private MeshNode? _targetNode;
    private Matrix4x4 _targetWorldMatrix = Matrix4x4.CreateTranslation(0, 0, 0);
    private static readonly Matrix4x4 InitialTargetMatrix = Matrix4x4.CreateTranslation(0, 0, 0);

    private Node? _lightNode;

    // Drag state (driven from ImGui IO in DrawGui).
    private bool _isDraggingGizmo;

    // True while an async hover-pick request is outstanding (throttles hover picks to one at a time).
    private bool _hoverPickInFlight;

    // --- GUI state ---
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

        // Create the gizmo manager and node BEFORE building the engine so the node can be registered.
        _gizmoManager = new GizmoManager { Mode = GizmoMode.Translate, Space = GizmoSpace.World };
        _handlePixelSize = _gizmoManager.DesiredPixelSize;
        _gizmoNode = new GizmoRenderNode
        {
            OcclusionMode = GizmoOcclusionMode.AlwaysOnTop,
        };

        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .WithFPS()
            .AddNode(_gizmoNode)
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .Build();

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
        // world that carries a GizmoDrawInfo component and draws its handles. Attach an entity here so
        // the manager can publish its gizmo as component data (via Update) instead of feeding the node
        // an out-of-band handle list.
        var gizmoEntityNode = new Node(world, "GizmoManagerEntity");
        _root.AddChild(gizmoEntityNode);
        _gizmoManager!.AttachEntity(gizmoEntityNode.Entity);

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

        // Drive the gizmo for this frame: rebuild handles for the current target and publish them as
        // the GizmoDrawInfo component on the managed entity. The overlay node gathers that component
        // from the render world each frame, so this must run before RenderOffscreen.
        _gizmoManager.Update(_renderContext.CameraParams, _viewportSize, _targetWorldMatrix);

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
    /// <see cref="Engine.Engine.CreatePickingRequest"/> path: the entity-id readback is recorded with
    /// the frame's command buffer and the callback is invoked a frame or two later without stalling
    /// the CPU. The packed R/G words are classified in the callback with the unified
    /// <see cref="Utils.UnpackEntityId(ulong)"/> decode and the encoding-type discriminator.
    /// </summary>
    public void Pick(int x, int y)
    {
        if (_engine is null || _renderContext is null)
            return;

        _engine.CreatePickingRequest(_renderContext, new Vector2(x, y), OnClickPickResponse);
    }

    /// <summary>
    /// Async click-pick result. Classifies the pixel with <see cref="Utils.UnpackEntityId(ulong)"/>:
    /// a gizmo decode (<see cref="EntityIdPickKind.Gizmo"/>) resolves the owning gizmo entity and
    /// <c>GizmoHandleId</c> and begins an axis drag using the pick coordinate's world ray; a scene
    /// decode (<see cref="EntityIdPickKind.Scene"/>) resolves a normal scene pick for object selection
    /// exactly as before.
    /// </summary>
    private void OnClickPickResponse(PickingResponse response)
    {
        if (_gizmoManager is null || _renderContext is null)
            return;

        // Classify the pick from the shared entity-id target. World id > 0 is a scene pick; world id 0
        // selects an alternate encoding (Gizmo) via the encoding-type discriminator.
        EntityIdDecodeResult decoded = Utils.UnpackEntityId(response.Data);

        // Gizmo pick: resolve the owning gizmo entity + handle id, then begin an axis drag.
        if (
            _gizmoManager.TryResolvePick(in decoded, out var resolution)
            && _renderContext.TryUnProject(response.Coord.X, response.Coord.Y, out var ray)
            && _gizmoManager.BeginDrag(in resolution, in ray)
        )
        {
            _isDraggingGizmo = true;
            _lastPickInfo = $"Dragging handle {resolution.Handle.Axis} ({resolution.Handle.Mode})";
            return;
        }

        // Scene pick (world id > 0): resolve the scene entity exactly as before.
        if (decoded.Kind == EntityIdPickKind.Scene && response.TryGetPickingResult(out var result))
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
    /// when the pixel is not a gizmo handle).
    /// </summary>
    private void OnHoverPickResponse(PickingResponse response)
    {
        _hoverPickInFlight = false;
        if (_gizmoManager is null)
            return;

        // Classify the hovered pixel with the unified decode; highlight the resolved handle when the
        // pixel is a gizmo pick that maps to a tracked gizmo, otherwise clear the highlight.
        EntityIdDecodeResult decoded = Utils.UnpackEntityId(response.Data);
        _gizmoManager.HoveredHandle =
            _gizmoManager.TryResolvePick(in decoded, out var resolution)
                ? resolution.Handle
                : null;
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
