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
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using Microsoft.Extensions.Logging;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;
using Viewport = HelixToolkit.Nex.ImGui.Viewport;

/// <summary>
/// Comprehensive GPU line rendering demo, mirroring the Points sample:
/// - Multiple line entities (axes, grid, helix, wireframe box, animated wave)
/// - GPU picking (click a line set to select its entity)
/// - ImGui controls for line width, color, material, enable/disable
/// - Orbit camera
/// <para>
/// All line geometry is built as a LINE LIST of DISJOINT 2-vertex segments:
/// the vertex buffer holds pairs [A0,A1, B0,B1, ...] and LineCount = Vertices.Count / 2.
/// </para>
/// </summary>
internal sealed class LinesDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<LinesDemo>();
    private const string ViewportTextureName = "ViewportTexture";

    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    // Camera
    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;
    private Viewport? _viewport;
    private long _lastTimestamp;

    // ImGui composite state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();
    private Size _viewportSize = new(1, 1);

    // Scene entities
    private readonly List<LineEntry> _lineSets = [];
    private Entity _selectedEntity = Entity.Null;
    private int _pickedEntityId;

    // Global tunables
    private float _globalLineWidth = 2.0f;
    private float _animTime;

    // Custom line material types registered by this demo
    private string[] _materialTypes = [];

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public LinesDemo(IContext context)
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

        // Register custom line material shaders before building the engine
        RegisterCustomLineMaterials();

        // Build the engine with the line rendering node (added by WithDefaultNodes -> WithLine)
        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes()
            .WithFPS()
            .WithSMAA()
            .WithBloom()
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return _context.CreateRenderTarget2D(
                    GraphicsSettings.IntermediateTargetFormat,
                    (uint)_renderContext.WindowSize.Width,
                    (uint)_renderContext.WindowSize.Height,
                    debugName: ViewportTextureName
                );
            }
        );

        // Reusable 3D viewport: wires the render context and orbit controller, and
        // forwards pick clicks to the existing Pick handler. Button mappings match the
        // Viewport defaults (Left=pick, Right=rotate, Middle=pan). DpiScale is left at the
        // default of 1 because the demo already converts display size to logical units.
        _viewport = new Viewport(_renderContext, _orbitController) { PickCallback = Pick };

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
    // Custom line material registration
    // ------------------------------------------------------------------

    /// <summary>
    /// Registers several custom line material shaders to demonstrate the
    /// <see cref="LineMaterialRegistry"/> extensibility.
    /// <para>
    /// Each body REPLACES the template's outputColor(); the built-in "Default" does NOT
    /// feather or premultiply, so every custom body does its own edge feathering across the
    /// line width (v_uv.y in [-1,1]) and premultiplied-alpha output.
    /// </para>
    /// </summary>
    private void RegisterCustomLineMaterials()
    {
        var materials = new List<string>();
        // Collect the built-in default first
        materials.Add("Default");

        // 1. Gradient — colorize along the segment using v_uv.x (in [-1,1] -> [0,1]).
        LineMaterialRegistry.Register(
            name: "Gradient",
            getLineColorImpl: """
                float t = clamp(getUV().x * 0.5 + 0.5, 0.0, 1.0);
                vec4 c = getColor();
                // Blend from the base color toward a complementary hue along the segment.
                vec3 grad = mix(c.rgb, c.gbr, t);
                // Feather across the line width and premultiply.
                float edge = clamp(1.0 - abs(getUV().y), 0.0, 1.0);
                float feather = (getLineWidth() > 0.0)
                    ? clamp(1.0 / max(getLineWidth() * 0.5, 1e-6), 0.0, 1.0) : 0.0;
                float a = (feather <= 0.0) ? step(0.0, edge) : smoothstep(0.0, feather, edge);
                a *= c.a;
                return vec4(grad * a, a);
            """
        );
        materials.Add("Gradient");

        // 2. Dashed — discard fragments based on an animated dash pattern along v_uv.x.
        LineMaterialRegistry.Register(
            name: "Dashed",
            getLineColorImpl: """
                float t = getUV().x * 0.5 + 0.5;                 // [0,1] along the segment
                float timeMs = float(getTimeMs() % 100000) / 1000.0;
                float pattern = fract(t * 16.0 - timeMs * 2.0);  // scrolling dashes
                if (pattern > 0.5) discard;                       // 50% duty cycle gaps
                vec4 c = getColor();
                // Feather across the line width and premultiply.
                float edge = clamp(1.0 - abs(getUV().y), 0.0, 1.0);
                float feather = (getLineWidth() > 0.0)
                    ? clamp(1.0 / max(getLineWidth() * 0.5, 1e-6), 0.0, 1.0) : 0.0;
                float a = (feather <= 0.0) ? step(0.0, edge) : smoothstep(0.0, feather, edge);
                a *= c.a;
                return vec4(c.rgb * a, a);
            """
        );
        materials.Add("Dashed");

        // 3. Glow — boost brightness toward the segment center (small |v_uv.y|).
        LineMaterialRegistry.Register(
            name: "Glow",
            getLineColorImpl: """
                vec4 c = getColor();
                float center = 1.0 - clamp(abs(getUV().y), 0.0, 1.0);
                float glow = pow(center, 2.0);
                vec3 rgb = c.rgb * (1.0 + 1.5 * glow);
                // Feather across the line width and premultiply.
                float edge = clamp(1.0 - abs(getUV().y), 0.0, 1.0);
                float feather = (getLineWidth() > 0.0)
                    ? clamp(1.0 / max(getLineWidth() * 0.5, 1e-6), 0.0, 1.0) : 0.0;
                float a = (feather <= 0.0) ? step(0.0, edge) : smoothstep(0.0, feather, edge);
                a *= c.a;
                return vec4(rgb * a, a);
            """
        );
        materials.Add("Glow");

        _materialTypes = materials.ToArray();

        _logger.LogInformation(
            "Registered {Count} custom line material types.",
            _materialTypes.Length - 1
        );
    }

    // ------------------------------------------------------------------
    // Scene building
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        var world = _worldDataProvider!.World;
        _root = new Node(world) { Name = "Root" };

        // Directional light so any future meshes are lit.
        var lightNode = new Node(world) { Name = "DirectionalLight" };
        lightNode.Entity.Set(
            new DirectionalLightInfo
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

        // 1. Axes — 3 colored segments from origin.
        AddLineSet("Axes", GenerateAxes(10f), new Color4(1f, 1f, 1f, 1f), "Default", thickness: 3f);

        // 2. Grid — ground grid on the XZ plane.
        AddLineSet(
            "Grid",
            GenerateGrid(20, 1f, new Color4(0.4f, 0.4f, 0.45f, 1f)),
            new Color4(0.4f, 0.4f, 0.45f, 1f),
            "Default",
            thickness: 1f
        );

        // 3. Helix — connected disjoint segments with per-vertex gradient colors.
        AddLineSet(
            "Helix",
            GenerateHelix(256, 4f, 12f, 4, new Vector3(15, 0, 0)),
            new Color4(1f, 0.5f, 0.2f, 1f),
            "Gradient",
            thickness: 3f
        );

        // 4. Wireframe Box — 12 edges of a cube as 12 segments.
        AddLineSet(
            "Wireframe Box",
            GenerateWireframeBox(new Vector3(-15, 3, 0), 6f, new Color4(0.3f, 1f, 0.5f, 1f)),
            new Color4(0.3f, 1f, 0.5f, 1f),
            "Glow",
            thickness: 4f
        );

        // 5. Animated Wave — dynamic segments whose endpoints animate over time.
        AddLineSet(
            "Animated Wave",
            GenerateWave(48, 16f, 0f, new Vector3(0, -5, 15), null),
            new Color4(0.9f, 0.85f, 0.2f, 1f),
            "Dashed",
            thickness: 2f
        );
    }

    private void AddLineSet(
        string name,
        Geometry geo,
        Color4 color,
        string materialName,
        float thickness
    )
    {
        var world = _worldDataProvider!.World;
        var node = world.CreateLineNode(name);
        _root!.AddChild(node);

        node.Geometry = geo;
        node.LineColor = color;
        node.LineThickness = thickness;
        node.LineMaterialName = materialName;
        node.Hitable = true;

        _lineSets.Add(
            new LineEntry(
                name,
                node,
                geo,
                color,
                thickness,
                Array.IndexOf(_materialTypes, materialName)
            )
        );
        _engine!.Add(geo);
    }

    // ------------------------------------------------------------------
    // Line geometry generators (all as disjoint 2-vertex segments)
    // ------------------------------------------------------------------

    private static void AddSegment(Geometry geo, Vector3 a, Vector3 b, Vector4 ca, Vector4 cb)
    {
        geo.Vertices.Add(a.ToVector4(1));
        geo.Vertices.Add(b.ToVector4(1));
        geo.VertexColors.Add(ca);
        geo.VertexColors.Add(cb);
    }

    private Geometry GenerateAxes(float length)
    {
        var geo = new Geometry();
        var red = new Vector4(1f, 0.1f, 0.1f, 1f);
        var green = new Vector4(0.1f, 1f, 0.1f, 1f);
        var blue = new Vector4(0.2f, 0.4f, 1f, 1f);
        AddSegment(geo, Vector3.Zero, new Vector3(length, 0, 0), red, red);
        AddSegment(geo, Vector3.Zero, new Vector3(0, length, 0), green, green);
        AddSegment(geo, Vector3.Zero, new Vector3(0, 0, length), blue, blue);
        return geo;
    }

    private Geometry GenerateGrid(int halfLines, float spacing, Color4 color)
    {
        var geo = new Geometry();
        var c = new Vector4(color.Red, color.Green, color.Blue, color.Alpha);
        float extent = halfLines * spacing;
        for (int i = -halfLines; i <= halfLines; i++)
        {
            float p = i * spacing;
            // Lines parallel to X axis (vary Z)
            AddSegment(geo, new Vector3(-extent, 0, p), new Vector3(extent, 0, p), c, c);
            // Lines parallel to Z axis (vary X)
            AddSegment(geo, new Vector3(p, 0, -extent), new Vector3(p, 0, extent), c, c);
        }
        return geo;
    }

    private Geometry GenerateHelix(
        int samples,
        float radius,
        float height,
        int turns,
        Vector3 center
    )
    {
        var geo = new Geometry();
        // Sample the helix into points, then emit connected disjoint segments by
        // DUPLICATING shared endpoints so each segment is its own 2-vertex pair.
        var pts = new Vector3[samples];
        var cols = new Vector4[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            float angle = t * turns * MathF.PI * 2f;
            float y = t * height - height * 0.5f;
            float r = radius * (0.5f + 0.5f * MathF.Sin(t * MathF.PI));
            pts[i] = center + new Vector3(MathF.Cos(angle) * r, y, MathF.Sin(angle) * r);
            cols[i] = new Vector4(t, 0.5f, 1f - t, 1f);
        }
        for (int i = 0; i < samples - 1; i++)
            AddSegment(geo, pts[i], pts[i + 1], cols[i], cols[i + 1]);
        return geo;
    }

    private Geometry GenerateWireframeBox(Vector3 center, float size, Color4 color)
    {
        var geo = new Geometry();
        var c = new Vector4(color.Red, color.Green, color.Blue, color.Alpha);
        float h = size * 0.5f;
        // 8 cube corners
        var v = new Vector3[8];
        v[0] = center + new Vector3(-h, -h, -h);
        v[1] = center + new Vector3(h, -h, -h);
        v[2] = center + new Vector3(h, h, -h);
        v[3] = center + new Vector3(-h, h, -h);
        v[4] = center + new Vector3(-h, -h, h);
        v[5] = center + new Vector3(h, -h, h);
        v[6] = center + new Vector3(h, h, h);
        v[7] = center + new Vector3(-h, h, h);

        // 12 edges as 12 disjoint segments
        int[,] edges =
        {
            { 0, 1 },
            { 1, 2 },
            { 2, 3 },
            { 3, 0 }, // back face
            { 4, 5 },
            { 5, 6 },
            { 6, 7 },
            { 7, 4 }, // front face
            { 0, 4 },
            { 1, 5 },
            { 2, 6 },
            { 3, 7 }, // connecting edges
        };
        for (int e = 0; e < 12; e++)
            AddSegment(geo, v[edges[e, 0]], v[edges[e, 1]], c, c);
        return geo;
    }

    private Geometry GenerateWave(
        int side,
        float width,
        float time,
        Vector3 center,
        Geometry? cache
    )
    {
        // A row of animated segments: for each column, a vertical-ish segment whose
        // endpoints rise and fall over time. Rebuilt each frame (dynamic geometry).
        var geo = cache ?? new Geometry(isDynamic: true);
        int segCount = side;
        geo.Vertices.Resize(segCount * 2);
        geo.VertexColors.Resize(segCount * 2);
        for (int i = 0; i < segCount; i++)
        {
            float u = (float)i / (segCount - 1) - 0.5f;
            float x = u * width;
            float phase = u * 12f - time * 3f;
            float y0 = MathF.Sin(phase) * 2.0f;
            float y1 = y0 + 1.5f + 0.5f * MathF.Cos(phase * 1.3f);
            float hue = (MathF.Sin(phase) + 1f) * 0.5f;
            var a = center + new Vector3(x, y0, 0);
            var b = center + new Vector3(x, y1, 0);
            var ca = new Vector4(hue, 0.6f + 0.4f * (1f - hue), 1f - hue * 0.5f, 1f);
            var cb = new Vector4(1f - hue, 0.5f, hue, 1f);
            geo.Vertices[i * 2 + 0] = a.ToVector4(1);
            geo.Vertices[i * 2 + 1] = b.ToVector4(1);
            geo.VertexColors[i * 2 + 0] = ca;
            geo.VertexColors[i * 2 + 1] = cb;
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

        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawUI(
            _renderContext.FinalOutputTexture,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );
        _imGuiRenderer.EndFrame();

        _orbitController?.Update(dt);

        // Animate the wave line set
        UpdateWave();

        // Render context setup
        _renderContext!.Update(_viewportSize, _camera);

        // 3D render (offscreen)
        _engine.BeginFrame();
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider,
            ViewportTextureName
        );

        // ImGui composite to swapchain
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        using var s1 = _imGuiDeps.PushTextureScoped(_renderContext.FinalOutputTexture);

        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        _engine.Submit(cmdBuf, swapchainTex);
    }

    private void UpdateWave()
    {
        // The "Animated Wave" is the last line set (index 4)
        if (_lineSets.Count < 5)
            return;
        var entry = _lineSets[4];
        int side = entry.Lines.Vertices.Count / 2;
        var geo = GenerateWave(side, 16f, _animTime, new Vector3(0, -5, 15), entry.Lines);
        // Re-set the geometry so the node re-uploads the dynamic buffers.
        entry.Node.Geometry = geo;
    }

    // ------------------------------------------------------------------
    // GPU Picking
    // ------------------------------------------------------------------

    private void Pick(int x, int y)
    {
        if (_renderContext?.ResourceSet is null || _worldDataProvider is null)
            return;
        _engine!.CreatePickingRequest(
            _renderContext,
            new Vector2(x, y),
            result =>
            {
                if (result.TryGetPickingResult(out var pickingResult))
                {
                    _pickedEntityId = pickingResult.Entity.Id;
                    _selectedEntity = pickingResult.Entity;
                }
                else
                {
                    _pickedEntityId = 0;
                    _selectedEntity = Entity.Null;
                }
            }
        );
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
        _viewport?.Draw(offscreenTex, new Vector2(0, panelY), new Vector2(viewportW, panelH));

        // Keep the offscreen target and aspect ratio in sync with the measured size.
        if (_viewport is not null)
        {
            var measured = _viewport.ViewportSize;
            if (measured.Width > 0 && measured.Height > 0)
                _viewportSize = measured;
        }

        DrawControlPanel(new Vector2(viewportW, panelY), new Vector2(panelWidth, panelH));
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
            if (Gui.SliderFloat("Line Width (px)", ref _globalLineWidth, 1f, 32f))
                ApplyGlobalLineWidth();
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
                foreach (var ls in _lineSets)
                {
                    if (ls.Node.Entity == _selectedEntity)
                    {
                        name = ls.Name;
                        break;
                    }
                }
                Gui.Text($"Selected: {name}");
                Gui.Text($"Entity ID: {_pickedEntityId}");
                if (_selectedEntity.TryGet<LineDrawInfo>(out var li))
                    Gui.Text($"Segment Count: {li.LineCount}");
            }
            else
            {
                Gui.TextDisabled("Click in the viewport to pick a line set.");
            }
        }

        Gui.Separator();

        // --- Per-set controls ---
        if (Gui.CollapsingHeader("Line Sets", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _lineSets.Count; i++)
            {
                var entry = _lineSets[i];
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
                        _selectedEntity.Remove<BorderHighlightOverlay>();
                    _selectedEntity = entry.Node.Entity;
                    if (_selectedEntity.Valid)
                        _selectedEntity.Set(BorderHighlightOverlay.Default);
                }

                if (open)
                {
                    Gui.Text($"Segments: {entry.Lines.Vertices.Count / 2}");

                    var col = new Vector3(entry.Color.Red, entry.Color.Green, entry.Color.Blue);
                    if (Gui.ColorEdit3("Color", ref col))
                    {
                        entry.Color = new Color4(col.X, col.Y, col.Z, 1f);
                        entry.Node.LineColor = entry.Color;
                    }

                    if (Gui.SliderFloat("Width", ref entry.Thickness, 1f, 32f))
                        entry.Node.LineThickness = entry.Thickness;

                    if (
                        Gui.Combo(
                            "Material",
                            ref entry.MaterialNameIndex,
                            _materialTypes,
                            _materialTypes.Length
                        )
                    )
                    {
                        entry.Node.LineMaterialName = _materialTypes[entry.MaterialNameIndex];
                    }

                    Gui.TreePop();
                }
                Gui.PopID();
            }
        }

        Gui.Separator();

        // --- Stats ---
        if (Gui.CollapsingHeader("Statistics", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            int totalSegments = 0;
            foreach (var ls in _lineSets)
                if (ls.Node.Enabled)
                    totalSegments += ls.Lines.Vertices.Count / 2;
            Gui.Text($"Total active segments: {totalSegments:N0}");
            Gui.Text($"Line set entities: {_lineSets.Count}");
        }

        Gui.End();
    }

    private void ApplyGlobalLineWidth()
    {
        foreach (var entry in _lineSets)
        {
            entry.Thickness = _globalLineWidth;
            entry.Node.LineThickness = _globalLineWidth;
        }
    }

    // ------------------------------------------------------------------
    // Camera input forwarding
    // ------------------------------------------------------------------

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
/// Tracks a single line set entity for the demo UI.
/// </summary>
internal sealed class LineEntry
{
    public string Name { get; }
    public LineNode Node { get; }
    public Geometry Lines { get; set; }
    public Color4 Color { get; set; }
    public float Thickness;
    public int MaterialNameIndex;

    public LineEntry(
        string name,
        LineNode node,
        Geometry lines,
        Color4 color,
        float thickness,
        int materialNameIndex
    )
    {
        Name = name;
        Node = node;
        Lines = lines;
        Color = color;
        Thickness = thickness;
        MaterialNameIndex = materialNameIndex;
    }
}
