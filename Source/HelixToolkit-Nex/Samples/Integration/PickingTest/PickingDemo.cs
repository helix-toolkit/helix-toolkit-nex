using System.Diagnostics;
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
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using Gui = ImGuiNET.ImGui;

/// <summary>
/// Picking demo: creates a large mesh (~1 million triangles), picks a triangle on click,
/// and displays the selected triangle as a dynamic yellow overlay mesh.
/// Supports both synchronous (blocking) and asynchronous (non-blocking) picking modes,
/// selectable at runtime via an ImGui control panel.
/// </summary>
internal sealed class PickingDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<PickingDemo>();

    // ImGui-integrated rendering
    private const string ViewportTextureName = "PickingViewport";
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

    // The large mesh geometry (kept to read triangle vertices on pick)
    private Geometry? _largeMeshGeometry;

    // Dynamic highlight triangle
    private MeshNode? _highlightNode;
    private Geometry? _highlightGeometry;
    private Entity _pickedEntity = Entity.Null;

    // Point cloud
    private Geometry? _pointCloudGeometry;

    // Dynamic highlight point (single red point)
    private PointCloudNode? _highlightPointNode;
    private Geometry? _highlightPointGeometry;

    private Node? _lightNode;

    private Vector2 _pointerLocation;
    private bool _isRotating;
    private bool _isPanning;

    // --- Picking mode ---
    private bool _useAsyncPicking = false;
    private PickingReadbackContext? _readbackCtx;

    // --- GUI state ---
    private string _lastPickInfo = "No pick yet";
    private double _lastPickMs = 0;
    private bool _continuousPicking = false;

    public PickingDemo(IContext context)
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
            .WithPostEffects(effects =>
            {
                effects.AddEffect(new WireframePostEffect());
            })
            .WithFPS()
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();

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

        _renderContext.PointerRing.Enabled = 1;
        _renderContext.PointerRing.OuterDistThreshold = 0.6f;
        _renderContext.PointerRing.InnerDistThreshold = 0.4f;
        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        // Async picking readback context (1×1 host-visible staging texture)
        _readbackCtx = new PickingReadbackContext(_context);

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
        var textureManager = _engine!.ResourceManager.TextureRepository;
        var materialPool = _engine.ResourceManager.PBRPropertyManager;
        var world = _worldDataProvider!.World;

        _root = new Node(world, "PickingRoot");

        // --- Build a large mesh with ~1 million triangles ---
        // Use a high-res sphere: thetaDiv=1002, phiDiv=1002 gives ~1M triangles
        // Each sphere ring: thetaDiv * 2 triangles, phiDiv rings => ~thetaDiv * phiDiv * 2 triangles
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 10f, 128, 128);
        meshBuilder.AddTorus(20, 2, 64, 64);
        var meshGeom3D = meshBuilder.ToMesh();
        _logger.LogInformation(
            "Large mesh: {Positions} vertices, {Triangles} triangles",
            meshGeom3D.Positions.Count,
            meshGeom3D.TriangleIndices.Count / 3
        );

        _largeMeshGeometry = meshGeom3D.ToGeometry();
        bool succ = geometryManager.Add(_largeMeshGeometry);
        Debug.Assert(succ, "Failed to add large mesh geometry");

        // Material: grey PBR
        var greyMaterial = materialPool.Create("PBR");
        greyMaterial.Properties.Albedo = new Vector3(0.6f, 0.6f, 0.6f);
        greyMaterial.Properties.Metallic = 0.3f;
        greyMaterial.Properties.Roughness = 0.5f;
        greyMaterial.Properties.Ao = 1.0f;
        greyMaterial.Properties.Opacity = 1.0f;

        var meshNode = new MeshNode(world, "LargeSphere")
        {
            Geometry = _largeMeshGeometry,
            MaterialProperties = greyMaterial,
        };
        meshNode.Entity.Set(
            new WireframePostEffect.WireframeOverlay { Color = Color.MediumPurple }
        );
        _root.AddChild(meshNode);

        var redMaterial = materialPool.Create("PBR");
        redMaterial.Properties.Albedo = new Vector3(1.0f, 0.2f, 0.2f);
        meshNode = new MeshNode(world, "LargeSphereNoHitable")
        {
            Geometry = _largeMeshGeometry,
            MaterialProperties = redMaterial,
            Hitable = false, // This copy of the mesh is not hitable
        };
        meshNode.Transform.Translation = new Vector3(0, -20, 0);
        _root.AddChild(meshNode);

        var greenMaterial = materialPool.Create("PBR");
        greenMaterial.Properties.Albedo = new Vector3(0.2f, 0.8f, 0.2f);
        greenMaterial.Properties.Opacity = 0.3f;
        meshNode = new MeshNode(world, "LargeSphereTransparentNoHitable")
        {
            Geometry = _largeMeshGeometry,
            MaterialProperties = greenMaterial,
            Hitable = false, // This copy of the mesh is not hitable
        };
        meshNode.Transform.Scale = new Vector3(3, 3, 3);
        meshNode.IsTransparent = true;
        _root.AddChild(meshNode);

        // --- Highlight triangle (dynamic, initially empty) ---
        _highlightGeometry = new Geometry(isDynamic: true);
        // Initialize with a degenerate triangle so the geometry is valid
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.Vertices.Add(Vector4.Zero);
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.VertexProps.Add(new VertexProperties { Normal = Vector3.UnitY });
        _highlightGeometry.Indices.Add(0);
        _highlightGeometry.Indices.Add(1);
        _highlightGeometry.Indices.Add(2);
        _highlightGeometry.UpdateBounds();
        succ = geometryManager.Add(_highlightGeometry);
        Debug.Assert(succ, "Failed to add highlight geometry");

        // Yellow unlit material for highlight
        var selectedMaterial = materialPool.Create("Unlit");
        selectedMaterial.Properties.Albedo = new Vector3(1.0f, 0.0f, 0.0f);
        selectedMaterial.Properties.Opacity = 1.0f;

        _highlightNode = new MeshNode(world, "HighlightTriangle")
        {
            Geometry = _highlightGeometry,
            MaterialProperties = selectedMaterial,
            Cullable = false,
        };
        _root.AddChild(_highlightNode);

        // --- Point cloud: random sphere of points ---
        _pointCloudGeometry = GeneratePointCloudSphere(10_000, 12f, new Vector3(25, 0, 0));
        succ = geometryManager.Add(_pointCloudGeometry);
        Debug.Assert(succ, "Failed to add point cloud geometry");

        var pointCloudNode = new PointCloudNode(world, "PointCloud")
        {
            Geometry = _pointCloudGeometry,
            Size = 0.15f,
            Hitable = true,
            Color = new Color4(0.2f, 0.6f, 1.0f, 1.0f),
        };
        _root.AddChild(pointCloudNode);

        // --- Highlight point (dynamic single red point, initially hidden at origin) ---
        _highlightPointGeometry = new Geometry(Topology.Point, isDynamic: true);
        _highlightPointGeometry.Vertices.Add(
            new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, 1)
        );
        _highlightPointGeometry.VertexColors.Add(new Vector4(1, 0, 0, 1));
        _highlightPointGeometry.UpdateBounds();
        succ = geometryManager.Add(_highlightPointGeometry);
        Debug.Assert(succ, "Failed to add highlight point geometry");

        _highlightPointNode = new PointCloudNode(world, "HighlightPoint")
        {
            Geometry = _highlightPointGeometry,
            Size = 0.4f,
            Color = new Color4(1f, 0f, 0f, 1f),
        };
        _root.AddChild(_highlightPointNode);

        // --- Add a directional light ---
        _lightNode = new Node(world, "Sun");
        _lightNode.Entity.Set(
            new DirectionalLightComponent
            {
                Color = new Color(1.0f, 1.0f, 1.0f),
                Intensity = 2.0f,
                Direction = Vector3.Normalize(new Vector3(0.3f, -1.0f, 0.5f)),
            }
        );
        _root.AddChild(_lightNode);
    }

    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _imGuiRenderer is null)
            return;

        _orbitController!.ViewportHeight = _viewportSize.Height;
        _orbitController!.ViewportWidth = _viewportSize.Width;
        _lightNode!.Entity.Update<DirectionalLightComponent>(light =>
        {
            light.Direction = _camera.LookDir;
            return light;
        });
        _renderContext.Update(_viewportSize, _camera);
        _renderContext.SetPointer(_pointerLocation);

        // --- Poll for async pick result from the previous frame ---
        if (_useAsyncPicking && _readbackCtx!.HasPending)
        {
            if (_readbackCtx.TryPickAsync(_renderContext, out var asyncResult))
            {
                ApplyPickResult(asyncResult, true);
            }
        }

        // --- ImGui frame ---
        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawGui(width, height, _renderContext.FinalOutputTexture);
        _imGuiRenderer.EndFrame();

        // --- 3D scene (offscreen) ---
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider!,
            ViewportTextureName
        );

        // --- Schedule async pick copy before submit ---
        bool scheduledAsyncPick = false;
        int scheduledX = 0,
            scheduledY = 0;
        if (_useAsyncPicking && _pendingPickX.HasValue && _pendingPickY.HasValue)
        {
            scheduledX = _pendingPickX.Value;
            scheduledY = _pendingPickY.Value;
            _pendingPickX = null;
            _pendingPickY = null;
            scheduledAsyncPick = cmdBuf.SchedulePickReadback(
                _renderContext,
                _readbackCtx!,
                scheduledX,
                scheduledY
            );
        }

        // --- ImGui composite pass ---
        var swapchain = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchain;
        _imGuiDeps.PushTexture(_renderContext.FinalOutputTexture);
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        // Submit — bypass Engine.Submit when we need the SubmitHandle for async picking
        if (scheduledAsyncPick)
        {
            var handle = _context.Submit(cmdBuf, swapchain);
            _readbackCtx!.SetPendingSubmit(handle, scheduledX, scheduledY);
        }
        else
        {
            _engine.Submit(cmdBuf, swapchain);
        }
        _imGuiDeps.PopTexture();
    }

    // Coordinates of a click that should be scheduled for async readback on the next render
    private int? _pendingPickX;
    private int? _pendingPickY;
    private uint _pickedEntityId;
    private uint _pickedInstanceId;
    private uint _pickedPrimitiveId;

    public void Pick(int x, int y)
    {
        if (_useAsyncPicking)
        {
            // Schedule the GPU copy to happen during the next Render call
            _pendingPickX = x;
            _pendingPickY = y;
            return;
        }
        // --- Synchronous path ---
        if (!_renderContext!.TryPick(x, y, out var result))
        {
            return;
        }
        ApplyPickResult(result, false);
    }

    /// <summary>
    /// Applies the result of a completed <see cref="PickingResult"/> (async path).
    /// </summary>
    private void ApplyPickResult(PickingResult result, bool async)
    {
        if (
            _pickedEntityId == result.Entity.Id
            && _pickedInstanceId == result.InstanceId
            && _pickedPrimitiveId == result.PrimitiveId
        )
        {
            return;
        }
        _pickedEntityId = (uint)result.Entity.Id;
        _pickedInstanceId = result.InstanceId;
        _pickedPrimitiveId = result.PrimitiveId;
        _logger.LogInformation(
            "{MODE} pick: entity {Entity}, instance {Instance}, primitive {Primitive}, pos {Pos}",
            (async ? "Async" : "Sync"),
            result.Entity,
            result.InstanceId,
            result.PrimitiveId,
            result.WorldPosition
        );
        _lastPickInfo =
            $"Entity {result.Entity} | Prim {result.PrimitiveId} | {result.PickGeometryType} | {result.WorldPosition:F2}";
        ApplyPickResultRaw(result.Entity, result.InstanceId, result.PrimitiveId);
    }

    /// <summary>
    /// Updates the highlight geometry from a raw entity + ids (shared by sync and async paths).
    /// </summary>
    private void ApplyPickResultRaw(Entity pickedEntity, uint instanceIdx, uint primitiveId)
    {
        // --- Point cloud picking ---
        if (pickedEntity.Has<PointCloudComponent>())
        {
            var pointComp = pickedEntity.Get<PointCloudComponent>();
            var pointGeo = pointComp.Geometry;
            if (pointGeo is not null && primitiveId < pointGeo.Vertices.Count)
            {
                var pointPos = pointGeo.Vertices[(int)primitiveId];
                _logger.LogInformation("Picked point {Id} at ({Pos})", primitiveId, pointPos);
                _lastPickInfo =
                    $"Point {primitiveId} @ ({pointPos.X:F2}, {pointPos.Y:F2}, {pointPos.Z:F2})";

                _highlightPointGeometry!.Vertices[0] = pointPos;
                _highlightPointGeometry.VertexColors[0] = new Vector4(1, 0, 0, 1);
                _highlightPointGeometry.MarkDirty(
                    GeometryBufferType.Vertex | GeometryBufferType.VertexColor
                );
                _highlightPointGeometry.UpdateBounds();
            }
            return;
        }

        // --- Mesh picking ---
        if (pickedEntity.Has<MeshComponent>())
        {
            var meshComp = pickedEntity.Get<MeshComponent>();
            var geometry = meshComp.Geometry;
            if (geometry is null)
                return;

            uint baseIndex = primitiveId * 3;
            if (baseIndex + 2 >= geometry.Indices.Count)
            {
                _logger.LogWarning("Primitive ID {Id} out of range", primitiveId);
                return;
            }

            uint i0 = geometry.Indices[(int)baseIndex];
            uint i1 = geometry.Indices[(int)baseIndex + 1];
            uint i2 = geometry.Indices[(int)baseIndex + 2];

            var v0 = geometry.Vertices[(int)i0];
            var v1 = geometry.Vertices[(int)i1];
            var v2 = geometry.Vertices[(int)i2];

            var p0 = new Vector3(v0.X, v0.Y, v0.Z);
            var p1 = new Vector3(v1.X, v1.Y, v1.Z);
            var p2 = new Vector3(v2.X, v2.Y, v2.Z);
            var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

            var offset = normal * 0.002f;
            var ov0 = p0 + offset;
            var ov1 = p1 + offset;
            var ov2 = p2 + offset;

            _highlightGeometry!.Vertices[0] = new Vector4(ov0, 1);
            _highlightGeometry.Vertices[1] = new Vector4(ov1, 1);
            _highlightGeometry.Vertices[2] = new Vector4(ov2, 1);
            _highlightGeometry.VertexProps[0] = new VertexProperties { Normal = normal };
            _highlightGeometry.VertexProps[1] = new VertexProperties { Normal = normal };
            _highlightGeometry.VertexProps[2] = new VertexProperties { Normal = normal };
            _highlightGeometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexProp);
            _highlightGeometry.UpdateBounds();

            _lastPickInfo =
                $"Triangle {primitiveId} | Entity {pickedEntity} | ({ov0.X:F2},{ov0.Y:F2},{ov0.Z:F2})";
            _logger.LogInformation("Highlighted triangle: ({V0}), ({V1}), ({V2})", ov0, ov1, ov2);
        }
    }

    private static Geometry GeneratePointCloudSphere(int count, float radius, Vector3 center)
    {
        var geo = new Geometry(Topology.Point);
        geo.Vertices.Capacity = count;
        geo.VertexColors.Capacity = count;
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            Vector3 p;
            do
            {
                p = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)
                );
            } while (p.LengthSquared() > 1f || p.LengthSquared() < 0.001f);

            p = Vector3.Normalize(p) * radius;
            float brightness = 0.5f + 0.5f * (p.Y / radius);
            geo.Vertices.Add((center + p).ToVector4(1));
            geo.VertexColors.Add(new Vector4(brightness, brightness, 1f, 1f));
        }
        geo.UpdateBounds();
        return geo;
    }

    private void DrawGui(int width, int height, Handle<Texture> offscreenTex)
    {
        if (Gui.BeginMainMenuBar())
        {
            if (Gui.BeginMenu("File"))
            {
                if (Gui.MenuItem("Quit"))
                    Environment.Exit(0);
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }

        const float PanelWidth = 320f;
        Gui.SetNextWindowPos(new Vector2(0, Gui.GetFrameHeight()), ImGuiCond.Always);
        Gui.SetNextWindowSize(
            new Vector2(PanelWidth, height - Gui.GetFrameHeight()),
            ImGuiCond.Always
        );

        var flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (Gui.Begin("Picking Controls##Panel", flags))
        {
            Gui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Picking Mode");
            Gui.Separator();
            Gui.Spacing();

            bool useAsync = _useAsyncPicking;
            if (Gui.RadioButton("Synchronous (blocking)", !useAsync))
                _useAsyncPicking = false;
            Gui.SameLine();
            if (Gui.RadioButton("Async (non-blocking)", useAsync))
                _useAsyncPicking = true;

            Gui.Spacing();
            Gui.TextWrapped(
                _useAsyncPicking
                    ? "GPU copy is recorded with the frame's command buffer and polled the next frame (no CPU stall)."
                    : "Context.Download blocks until GPU finishes reading the pick pixel."
            );

            Gui.Spacing();
            Gui.Checkbox("Continuous Picking", ref _continuousPicking);
            Gui.Separator();
            Gui.Spacing();

            Gui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Last Pick");
            Gui.TextWrapped(_lastPickInfo);
            Gui.Spacing();

            if (_useAsyncPicking && _readbackCtx!.HasPending)
            {
                Gui.Spacing();
                Gui.TextColored(new Vector4(1f, 0.6f, 0.1f, 1f), "Readback in flight...");
            }

            Gui.Spacing();
            Gui.Separator();
            Gui.Spacing();
            Gui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), "Controls");
            Gui.BulletText("Left click: pick");
            Gui.BulletText("Right drag: rotate");
            Gui.BulletText("Middle drag: pan");
            Gui.BulletText("Scroll: zoom");
        }
        Gui.End();

        Gui.SetNextWindowPos(new Vector2(PanelWidth, Gui.GetFrameHeight()), ImGuiCond.Always);
        Gui.SetNextWindowSize(
            new Vector2(width - PanelWidth, height - Gui.GetFrameHeight()),
            ImGuiCond.Always
        );
        var windowFlags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin("##Viewport", windowFlags);
        Gui.PopStyleVar();
        var contentSize = Gui.GetContentRegionAvail();
        if (contentSize.X > 0 && contentSize.Y > 0)
        {
            _viewportSize = new Size((int)contentSize.X, (int)contentSize.Y);
            var canvas_pos = Gui.GetCursorScreenPos();
            // Display the offscreen-rendered 3D scene as an ImGui image
            Gui.Image(
                (nint)offscreenTex.Index,
                new Vector2(width - PanelWidth, height - Gui.GetFrameHeight())
            );
            bool hovered = Gui.IsItemHovered();
            if (hovered)
            {
                var mouse_pos = Gui.GetMousePos();
                var relative_pos = new Vector2(
                    mouse_pos.X - canvas_pos.X,
                    mouse_pos.Y - canvas_pos.Y
                );

                // Left-click: picking
                if (Gui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _logger.LogInformation(
                        "Mouse clicked at viewport coords: {X}, {Y}",
                        relative_pos.X,
                        relative_pos.Y
                    );
                    Pick((int)relative_pos.X, (int)relative_pos.Y);
                }
                else if (_continuousPicking)
                {
                    Pick((int)relative_pos.X, (int)relative_pos.Y);
                }

                // Right-click: begin rotate
                if (Gui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    OnViewportMouseDown(1, relative_pos.X, relative_pos.Y);
                }

                // Middle-click: begin pan
                if (Gui.IsMouseClicked(ImGuiMouseButton.Middle))
                {
                    OnViewportMouseDown(2, relative_pos.X, relative_pos.Y);
                }

                // Mouse drag: forward to camera controller
                OnViewportMouseMove(relative_pos.X, relative_pos.Y);

                // Scroll wheel: zoom
                var io = Gui.GetIO();
                if (MathF.Abs(io.MouseWheel) > 0.001f)
                {
                    OnViewportMouseWheel(io.MouseWheel);
                }
            } // Release tracking on mouse-up (even if not hovered, to avoid stuck drags)
            if (Gui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                OnViewportMouseUp(1);
            }
            if (Gui.IsMouseReleased(ImGuiMouseButton.Middle))
            {
                OnViewportMouseUp(2);
            }
        }
        Gui.End();
    }

    /// <summary>
    /// Handles a mouse button press over the 3D viewport.
    /// </summary>
    /// <param name="button">0 = left, 1 = right, 2 = middle</param>
    /// <param name="viewportX">X position relative to the viewport.</param>
    /// <param name="viewportY">Y position relative to the viewport.</param>
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

    /// <summary>
    /// Handles mouse button release.
    /// </summary>
    public void OnViewportMouseUp(int button)
    {
        if (button == 1)
            _isRotating = false;
        else if (button == 2)
            _isPanning = false;
    }

    /// <summary>
    /// Handles mouse movement over the viewport.
    /// </summary>
    public void OnViewportMouseMove(float viewportX, float viewportY)
    {
        _pointerLocation = new Vector2(viewportX, viewportY);
        if (_orbitController is null)
            return;

        if (_isRotating)
            _orbitController.OnRotateDelta(viewportX, viewportY);
        if (_isPanning)
            _orbitController.OnPanDelta(viewportX, viewportY);
    }

    /// <summary>
    /// Handles mouse scroll wheel over the viewport.
    /// </summary>
    public void OnViewportMouseWheel(float delta)
    {
        _orbitController?.OnZoomDelta(delta);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _readbackCtx?.Dispose();
        _imGuiRenderer?.Dispose();
        _worldDataProvider?.Dispose();
        _renderContext?.Teardown();
        _engine?.Dispose();
    }
}
