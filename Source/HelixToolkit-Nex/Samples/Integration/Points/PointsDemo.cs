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
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using Microsoft.Extensions.Logging;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

/// <summary>
/// Comprehensive point cloud rendering demo with:
/// - Multiple point cloud entities (sphere, helix, random cluster, animated wave)
/// - GPU picking (click a point to select its entity)
/// - ImGui controls for point size, colour, min screen size, add/remove clouds
/// - Orbit camera with keyboard (WASD) fallback
/// </summary>
internal sealed class PointsDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<PointsDemo>();

    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    // Camera
    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;
    private long _lastTimestamp;

    // ImGui composite state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();
    private Size _viewportSize = new(1, 1);

    // Scene entities
    private readonly List<PointCloudEntry> _pointClouds = [];
    private Entity _selectedEntity = Entity.Null;
    private int _pickedEntityId;
    private uint _pickedInstanceIdx;

    // Global tunables
    private float _globalPointSize = 0.1f;
    private float _minScreenSize = 1f;
    private float _animTime;
    private bool _fixedSize = false;

    // Post effects
    private readonly Smaa _smaa = new();
    private readonly ToneMapping _toneMapping = new();
    private readonly BorderHighlightPostEffect _borderHighlight = new();
    private readonly ShowFPS _showFPS = new();

    private PointCullNode? _pointCullNode;

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public PointsDemo(IContext context)
    {
        _context = context;
    }

    // ------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera
        {
            Position = new Vector3(0, 12, -25),
            Target = Vector3.Zero,
            FarPlane = 500,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        // Build the engine with the point rendering node
        _pointCullNode = new PointCullNode { MaxPoints = 500_000, MinScreenSize = _minScreenSize };

        _engine = EngineBuilder
            .Create(_context)
            .AddNode(new PrepareNode())
            .AddNode(new DepthPassNode())
            .AddNode(new FrustumCullNode())
            .AddNode(new ForwardPlusLightCullingNode())
            .AddNode(new ForwardPlusOpaqueNode())
            .AddNode(_pointCullNode)
            .AddNode(new PointRenderNode())
            .WithPostEffects(effects =>
            {
                effects.AddEffect(_smaa);
                effects.AddEffect(_toneMapping);
                effects.AddEffect(_borderHighlight);
                effects.AddEffect(_showFPS);
            })
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        // Build the scene
        BuildScene();

        // ImGui setup
        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        _imGuiPass.Colors[0].ClearColor = new Color4(0.08f, 0.08f, 0.10f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
    }

    // ------------------------------------------------------------------
    // Scene building helpers
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        var world = _worldDataProvider!.World;
        _root = new Node(world) { Name = "Root" };

        // Add a directional light so the viewport isn't pure black if someone
        // toggles on a mesh later.
        var lightNode = new Node(world) { Name = "DirectionalLight" };
        lightNode.Entity.Set(
            new DirectionalLightComponent
            {
                Light = new DirectionalLight
                {
                    Direction = Vector3.Normalize(new Vector3(0.5f, -1f, 0.5f)),
                    Color = new Vector3(1f, 0.98f, 0.95f),
                    Intensity = 0.8f,
                },
            }
        );
        _root.AddChild(lightNode);

        // 1. Sphere point cloud
        AddPointCloud(
            "Sphere",
            GenerateSphere(5_000, 5f, Vector3.Zero),
            new Color4(0.2f, 0.7f, 1.0f, 1.0f)
        );

        // 2. Helix point cloud
        AddPointCloud(
            "Helix",
            GenerateHelix(3_000, 4f, 10f, 3, new Vector3(15, 0, 0)),
            new Color4(1.0f, 0.4f, 0.2f, 1.0f)
        );

        // 3. Random cluster
        AddPointCloud(
            "Random Cluster",
            GenerateRandomCluster(8_000, 6f, new Vector3(-15, 3, 0)),
            new Color4(0.3f, 1.0f, 0.3f, 1.0f)
        );

        // 4. Animated wave (points will be updated each frame)
        AddPointCloud(
            "Animated Wave",
            GenerateWave(4_000, 10f, 10f, 0f, new Vector3(0, -5, 15)),
            new Color4(1.0f, 0.9f, 0.2f, 1.0f)
        );
    }

    private void AddPointCloud(string name, Geometry geo, Color4 tint)
    {
        var world = _worldDataProvider!.World;
        var node = new Node(world) { Name = name };
        _root!.AddChild(node);

        // Snapshot the original (untinted) colors
        var originalColors = new List<Vector4>(geo.VertexColors.Count);
        for (int i = 0; i < geo.VertexColors.Count; i++)
            originalColors.Add(geo.VertexColors[i]);

        // Apply tint to all points
        for (int i = 0; i < geo.VertexColors.Count; i++)
        {
            var c = originalColors[i];
            geo.VertexColors[i] = new Vector4(
                c.X * tint.Red,
                c.Y * tint.Green,
                c.Z * tint.Blue,
                c.W * tint.Alpha
            );
        }

        node.Entity.Set(
            new PointCloudComponent(
                geo,
                Color.White,
                hitable: true,
                fixedSize: _fixedSize,
                size: _globalPointSize
            )
        );

        _pointClouds.Add(new PointCloudEntry(name, node, geo, tint, originalColors));
        _engine!.ResourceManager.Geometries.Add(geo, out _);
    }

    // ------------------------------------------------------------------
    // Point generators
    // ------------------------------------------------------------------

    private Geometry GenerateSphere(int count, float radius, Vector3 center)
    {
        var geo = new Geometry();
        geo.Vertices.Capacity = count;
        geo.VertexColors.Capacity = count;
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            // Uniform sphere distribution using rejection sampling
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
            float brightness = 0.5f + 0.5f * (p.Y / radius); // gradient by height
            geo.Vertices.Add((center + p).ToVector4(1));
            geo.VertexColors.Add(new Vector4(brightness, brightness, 1f, 1f));
        }
        return geo;
    }

    private Geometry GenerateHelix(int count, float radius, float height, int turns, Vector3 center)
    {
        var geo = new Geometry();
        geo.Vertices.Capacity = count;
        geo.VertexColors.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            float angle = t * turns * MathF.PI * 2f;
            float y = t * height - height * 0.5f;
            float r = radius * (0.5f + 0.5f * MathF.Sin(t * MathF.PI)); // taper at ends
            geo.Vertices.Add(
                (center + new Vector3(MathF.Cos(angle) * r, y, MathF.Sin(angle) * r)).ToVector4(1)
            );
            geo.VertexColors.Add(new Vector4(t, 0.5f, 1f - t, 1f));
        }
        return geo;
    }

    private Geometry GenerateRandomCluster(int count, float extent, Vector3 center)
    {
        var geo = new Geometry();
        var rng = new Random(123);
        for (int i = 0; i < count; i++)
        {
            // Gaussian-ish distribution via sum of uniform randoms
            float Gauss() =>
                (float)(rng.NextDouble() + rng.NextDouble() + rng.NextDouble()) / 3f * 2f - 1f;
            var p = new Vector3(Gauss(), Gauss(), Gauss()) * extent;
            float d = p.Length() / extent;
            geo.Vertices.Add((center + p).ToVector4(1));
            geo.VertexColors.Add(new Vector4(0.5f + d * 0.5f, 1f - d, 0.3f, 1f));
        }
        return geo;
    }

    private Geometry GenerateWave(
        int countSqrt,
        float width,
        float depth,
        float time,
        Vector3 center,
        Geometry? cache = null
    )
    {
        int count = countSqrt;
        // Generate as a grid, sqrt(count) x sqrt(count)
        int side = (int)MathF.Sqrt(count);
        var geo = cache ?? new Geometry(isDynamic: true);
        geo.Vertices.Resize(side * side);
        geo.VertexColors.Resize(side * side);
        for (int iz = 0; iz < side; iz++)
        {
            for (int ix = 0; ix < side; ix++)
            {
                float u = (float)ix / (side - 1) - 0.5f;
                float v = (float)iz / (side - 1) - 0.5f;
                float x = u * width;
                float z = v * depth;
                float dist = MathF.Sqrt(x * x + z * z);
                float y = MathF.Sin(dist * 2f - time * 3f) * 1.5f * MathF.Exp(-dist * 0.15f);
                float hue = (MathF.Sin(dist * 0.5f - time) + 1f) * 0.5f;
                geo.Vertices[iz * side + ix] = ((center + new Vector3(x, y, z)).ToVector4(1));
                geo.VertexColors[iz * side + ix] = (
                    new Vector4(hue, 0.6f + 0.4f * (1f - hue), 1f - hue * 0.5f, 1f)
                );
            }
        }
        geo.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
        return geo;
    }

    // ------------------------------------------------------------------
    // Render loop
    // ------------------------------------------------------------------

    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _worldDataProvider is null)
            return;
        if (_imGuiRenderer is null)
            return;

        // Delta time
        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float dt = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _animTime += dt;

        _orbitController?.Update(dt);

        // Animate the wave point cloud
        UpdateWave();

        // Update min screen size on the render node
        if (_pointCullNode is not null)
            _pointCullNode.MinScreenSize = _minScreenSize;

        // Render context setup
        if (!_viewportSize.IsEmpty)
            _renderContext.WindowSize = _viewportSize;

        var aspect = _viewportSize.IsEmpty
            ? (float)width / height
            : (float)_viewportSize.Width / _viewportSize.Height;
        _renderContext.CameraParams = _camera.ToCameraParams(aspect);
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();

        // 3D render (offscreen)
        var cmdBuf = _engine.RenderOffscreen(_renderContext, _worldDataProvider);

        var offscreenTex = _renderContext.ResourceSet!.Textures[
            SystemBufferNames.TextureColorF16Current
        ];

        // ImGui composite to swapchain
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = offscreenTex;

        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawUI(
            offscreenTex,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );
        _imGuiRenderer.EndFrame();
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        _context.Submit(cmdBuf, swapchainTex);
    }

    private void UpdateWave()
    {
        // The "Animated Wave" is the 4th point cloud (index 3)
        if (_pointClouds.Count < 4)
            return;
        var entry = _pointClouds[3];
        var newPts = GenerateWave(
            entry.Points.Vertices.Count,
            10f,
            10f,
            _animTime,
            new Vector3(0, -5, 15),
            entry.Points
        );

        // Update the stored original colors from the freshly generated wave
        var origColors = entry.OriginalColors;
        if (origColors.Count != newPts.VertexColors.Count)
        {
            origColors.Clear();
            origColors.Capacity = newPts.VertexColors.Count;
            for (int i = 0; i < newPts.VertexColors.Count; i++)
                origColors.Add(newPts.VertexColors[i]);
        }
        else
        {
            for (int i = 0; i < newPts.VertexColors.Count; i++)
                origColors[i] = newPts.VertexColors[i];
        }

        // Apply tint from the original colors
        for (int i = 0; i < newPts.VertexColors.Count; i++)
        {
            var orig = origColors[i];
            newPts.VertexColors[i] = new Vector4(
                orig.X * entry.Tint.Red,
                orig.Y * entry.Tint.Green,
                orig.Z * entry.Tint.Blue,
                orig.W * entry.Tint.Alpha
            );
        }
        entry.Points = newPts;
    }

    // ------------------------------------------------------------------
    // GPU Picking
    // ------------------------------------------------------------------

    private void Pick(int x, int y)
    {
        if (_renderContext?.ResourceSet is null || _worldDataProvider is null)
            return;

        _context.TryPick(
            _renderContext.ResourceSet.Textures[SystemBufferNames.TextureEntityId],
            (uint)_renderContext.WindowSize.Width,
            (uint)_renderContext.WindowSize.Height,
            x,
            y,
            out var entityId,
            out var entityVer,
            out var instanceIdx
        );

        _pickedEntityId = entityId;
        _pickedInstanceIdx = instanceIdx;

        // Deselect previous
        if (_selectedEntity.Valid)
            _selectedEntity.Remove<BorderHighlightComponent>();

        var entity = _worldDataProvider.World.GetEntity(entityId, entityVer);
        _selectedEntity = entity;

        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightComponent.Default);
            _logger.LogInformation("Picked entity {Id} (instance {Idx})", entityId, instanceIdx);
        }
    }

    // ------------------------------------------------------------------
    // ImGui UI
    // ------------------------------------------------------------------

    private void DrawUI(TextureHandle offscreenTex, float displayW, float displayH)
    {
        const float panelWidth = 320f;
        float menuH = Gui.GetFrameHeight();
        float panelY = menuH;
        float panelH = displayH - menuH;
        float viewportW = displayW - panelWidth;

        // Main menu bar
        if (Gui.BeginMainMenuBar())
        {
            if (Gui.BeginMenu("File"))
            {
                Gui.MenuItem("Exit");
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }

        // 3D Viewport
        Draw3DViewport(offscreenTex, new Vector2(0, panelY), new Vector2(viewportW, panelH));

        // Control panel
        DrawControlPanel(new Vector2(viewportW, panelY), new Vector2(panelWidth, panelH));
    }

    private void Draw3DViewport(TextureHandle offscreenTex, Vector2 pos, Vector2 size)
    {
        var flags =
            ImGuiNET.ImGuiWindowFlags.NoResize
            | ImGuiNET.ImGuiWindowFlags.NoMove
            | ImGuiNET.ImGuiWindowFlags.NoCollapse
            | ImGuiNET.ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiNET.ImGuiWindowFlags.NoTitleBar
            | ImGuiNET.ImGuiWindowFlags.NoScrollbar
            | ImGuiNET.ImGuiWindowFlags.NoScrollWithMouse;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.PushStyleVar(ImGuiNET.ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin("##Viewport", flags);
        Gui.PopStyleVar();

        var contentSize = Gui.GetContentRegionAvail();
        if (contentSize.X > 0 && contentSize.Y > 0)
        {
            _viewportSize = new Size((int)contentSize.X, (int)contentSize.Y);
            var canvasPos = Gui.GetCursorScreenPos();
            Gui.Image((nint)offscreenTex.Index, contentSize);

            bool hovered = Gui.IsItemHovered();
            if (hovered)
            {
                var mouse = Gui.GetMousePos();
                var rel = new Vector2(mouse.X - canvasPos.X, mouse.Y - canvasPos.Y);

                // Left-click: pick
                if (Gui.IsMouseClicked(ImGuiNET.ImGuiMouseButton.Left))
                    Pick((int)rel.X, (int)rel.Y);

                // Right-drag: rotate
                if (Gui.IsMouseClicked(ImGuiNET.ImGuiMouseButton.Right))
                    _orbitController?.OnRotateBegin(rel.X, rel.Y);

                // Middle-drag: pan
                if (Gui.IsMouseClicked(ImGuiNET.ImGuiMouseButton.Middle))
                    _orbitController?.OnPanBegin(rel.X, rel.Y);

                // Motion
                if (Gui.IsMouseDown(ImGuiNET.ImGuiMouseButton.Right))
                    _orbitController?.OnRotateDelta(rel.X, rel.Y);
                if (Gui.IsMouseDown(ImGuiNET.ImGuiMouseButton.Middle))
                    _orbitController?.OnPanDelta(rel.X, rel.Y);

                // Scroll: zoom
                var io = Gui.GetIO();
                if (MathF.Abs(io.MouseWheel) > 0.001f)
                    _orbitController?.OnZoomDelta(io.MouseWheel);
            }
        }
        Gui.End();
    }

    private void DrawControlPanel(Vector2 pos, Vector2 size)
    {
        var flags =
            ImGuiNET.ImGuiWindowFlags.NoResize
            | ImGuiNET.ImGuiWindowFlags.NoMove
            | ImGuiNET.ImGuiWindowFlags.NoCollapse
            | ImGuiNET.ImGuiWindowFlags.NoBringToFrontOnFocus;

        Gui.SetNextWindowPos(pos);
        Gui.SetNextWindowSize(size);
        Gui.Begin("Controls##Panel", flags);

        // --- Global settings ---
        if (Gui.CollapsingHeader("Global Settings", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            var range = _fixedSize ? (1f, 5f) : (0.1f, 0.5f);
            if (Gui.SliderFloat("Point Size", ref _globalPointSize, range.Item1, range.Item2))
                ApplyGlobalPointSize();
            Gui.SliderFloat("Min Screen Size (px)", ref _minScreenSize, 0.05f, 10f);
            if (Gui.Checkbox("Fixed Size (no perspective scaling)", ref _fixedSize))
            {
                _globalPointSize = _fixedSize ? 2 : 0.1f; // reset to default when toggling
                ApplyGlobalPointSize();
            }
            // Camera position
            var camPos = _camera.Position;
            Gui.Text($"Camera: ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})");
        }

        Gui.Separator();

        // --- Picking result ---
        if (Gui.CollapsingHeader("Picking", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_selectedEntity.Valid)
            {
                string name = "Unknown";
                foreach (var pc in _pointClouds)
                {
                    if (pc.Node.Entity == _selectedEntity)
                    {
                        name = pc.Name;
                        break;
                    }
                }
                Gui.Text($"Selected: {name}");
                Gui.Text($"Entity ID: {_pickedEntityId}");
                Gui.Text($"Instance Idx: {_pickedInstanceIdx}");

                if (_selectedEntity.TryGet<PointCloudComponent>(out var pc2))
                    Gui.Text($"Point Count: {pc2.PointCount}");
            }
            else
            {
                Gui.TextDisabled("Click in the viewport to pick a point cloud.");
            }
        }

        Gui.Separator();

        // --- Per-cloud controls ---
        if (Gui.CollapsingHeader("Point Clouds", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _pointClouds.Count; i++)
            {
                var entry = _pointClouds[i];
                Gui.PushID(i);

                bool enabled = entry.Node.Enabled;
                if (Gui.Checkbox($"##en{i}", ref enabled))
                    entry.Node.Enabled = enabled;
                Gui.SameLine();

                bool selected = _selectedEntity.Valid && entry.Node.Entity == _selectedEntity;
                if (selected)
                    Gui.PushStyleColor(ImGuiNET.ImGuiCol.Text, new Vector4(1, 1, 0, 1));

                bool open = Gui.TreeNodeEx(entry.Name, ImGuiNET.ImGuiTreeNodeFlags.None);

                if (selected)
                    Gui.PopStyleColor();

                if (Gui.IsItemClicked(ImGuiNET.ImGuiMouseButton.Left))
                {
                    if (_selectedEntity.Valid)
                        _selectedEntity.Remove<BorderHighlightComponent>();
                    _selectedEntity = entry.Node.Entity;
                    if (_selectedEntity.Valid)
                        _selectedEntity.Set(BorderHighlightComponent.Default);
                }

                if (open)
                {
                    Gui.Text($"Points: {entry.Points.Vertices.Count}");
                    var tint = new Vector3(entry.Tint.Red, entry.Tint.Green, entry.Tint.Blue);
                    if (Gui.ColorEdit3("Tint", ref tint))
                    {
                        entry.Tint = new Color4(tint.X, tint.Y, tint.Z, 1f);
                        ApplyTint(entry);
                    }
                    Gui.TreePop();
                }
                Gui.PopID();
            }
        }

        Gui.Separator();

        // --- Post effects ---
        if (Gui.CollapsingHeader("Post Effects"))
        {
            bool smaaEnabled = _smaa.Enabled;
            if (Gui.Checkbox("SMAA", ref smaaEnabled))
                _smaa.Enabled = smaaEnabled;

            bool tmEnabled = _toneMapping.Enabled;
            if (Gui.Checkbox("Tone Mapping", ref tmEnabled))
                _toneMapping.Enabled = tmEnabled;

            bool bhEnabled = _borderHighlight.Enabled;
            if (Gui.Checkbox("Border Highlight", ref bhEnabled))
                _borderHighlight.Enabled = bhEnabled;

            bool fpsEnabled = _showFPS.Enabled;
            if (Gui.Checkbox("Show FPS", ref fpsEnabled))
                _showFPS.Enabled = fpsEnabled;
        }

        Gui.Separator();

        // --- Stats ---
        if (Gui.CollapsingHeader("Statistics", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            int totalPoints = 0;
            foreach (var pc in _pointClouds)
                if (pc.Node.Enabled)
                    totalPoints += pc.Points.Vertices.Count;
            Gui.Text($"Total active points: {totalPoints:N0}");
            Gui.Text($"Point cloud entities: {_pointClouds.Count}");

            var data = _worldDataProvider?.PointCloudData;
            if (data is not null)
            {
                Gui.Text($"GPU buffer points: {data.TotalPointCount:N0}");
                Gui.Text($"Entity count: {data.Count}");
            }
        }

        Gui.End();
    }

    private void ApplyGlobalPointSize()
    {
        foreach (var entry in _pointClouds)
        {
            entry.Node.Entity.Update<PointCloudComponent>(x =>
            {
                x.FixedSize = _fixedSize;
                x.Size = _globalPointSize;
                return x;
            });
        }
    }

    private void ApplyTint(PointCloudEntry entry)
    {
        // Re-derive tinted colors from the stored originals
        for (int i = 0; i < entry.Points.VertexColors.Count; i++)
        {
            var orig = entry.OriginalColors[i];
            entry.Points.VertexColors[i] = new Vector4(
                orig.X * entry.Tint.Red,
                orig.Y * entry.Tint.Green,
                orig.Z * entry.Tint.Blue,
                orig.W * entry.Tint.Alpha
            );
        }
        entry.Points.MarkDirty(GeometryBufferType.VertexColor);
    }

    // ------------------------------------------------------------------
    // Camera input forwarding
    // ------------------------------------------------------------------

    // Right-drag viewport tracking
    private bool _isRotating,
        _isPanning;

    public void OnKeyboardInput(bool w, bool s, bool a, bool d, bool space, bool ctrl, bool shift)
    {
        // Orbit controller doesn't use keyboard, but reserve for future FP mode
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    private bool _disposed;

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _imGuiRenderer?.Dispose();
            _worldDataProvider?.Dispose();
            _renderContext?.Teardown();
            _engine?.Dispose();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tracks a single point cloud entity for the demo UI.
/// </summary>
internal sealed class PointCloudEntry
{
    public string Name { get; }
    public Node Node { get; }
    public Geometry Points { get; set; }
    public Color4 Tint { get; set; }

    /// <summary>
    /// Stores the untinted (original) vertex colors so that tint can be
    /// re-applied non-destructively any number of times.
    /// </summary>
    public List<Vector4> OriginalColors { get; set; }

    public PointCloudEntry(
        string name,
        Node node,
        Geometry points,
        Color4 tint,
        List<Vector4> originalColors
    )
    {
        Name = name;
        Node = node;
        Points = points;
        Tint = tint;
        OriginalColors = originalColors;
    }
}
