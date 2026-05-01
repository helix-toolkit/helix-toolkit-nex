using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Engine.Components;
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
using HelixToolkit.Nex.Shaders;
using Microsoft.Extensions.Logging;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

/// <summary>
/// Billboard rendering demo with SDF text, ImGui controls, and GPU picking.
/// Demonstrates:
/// - SDF font atlas loading from embedded resources
/// - BillboardGeometry for per-glyph data (one entity per text string)
/// - Multiple SDF material variants (default, outline, shadow)
/// - GPU picking of individual glyph billboards
/// - ImGui controls for font size, color, text editing, and dynamic text creation
/// </summary>
internal sealed class BillboardDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<BillboardDemo>();
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
    private long _lastTimestamp;

    // ImGui composite state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();
    private Size _viewportSize = new(1, 1);

    // SDF font atlas
    private SDFFontAtlas? _atlas;

    // Scene entities
    private readonly List<TextEntry> _textEntries = [];
    private Entity _selectedEntity = Entity.Null;
    private int _pickedEntityId;
    private uint _pickedInstanceIdx;

    // Global tunables
    private float _globalFontSize = 0.5f;
    private float _minScreenSize = 1f;
    private bool _fixedSize;

    // New text input state
    private string _newText = "New Text";
    private Vector3 _newPosition = new(0, -3, 0);
    private Vector4 _newColor = new(1f, 1f, 1f, 1f);

    // Post effects
    private readonly Smaa _smaa = new();
    private readonly BorderHighlightPostEffect _borderHighlight = new();
    private readonly ShowFPS _showFPS = new();

    private BillboardCullNode? _billboardCullNode;

    // Registered SDF material variant IDs
    private MaterialTypeId _outlineMaterialId;

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public BillboardDemo(IContext context)
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
            Position = new Vector3(0, 5, -15),
            Target = Vector3.Zero,
            FarPlane = 500,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        // Register custom SDF material variants before building the engine
        RegisterSDFMaterialVariants();

        // Build the engine with billboard rendering nodes
        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes()
            .RenderToCustomTarget(RenderSettings.IntermediateTargetFormat)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(_smaa);
                effects.AddEffect(_borderHighlight);
                effects.AddEffect(_showFPS);
            })
            .Build();
        _billboardCullNode = _engine.GetRenderNode<BillboardCullNode>();
        _billboardCullNode.MinScreenSize = _minScreenSize;
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

        // Load the SDF font atlas
        LoadFontAtlas();

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
    // SDF material variant registration
    // ------------------------------------------------------------------

    private void RegisterSDFMaterialVariants()
    {
        // Register an outlined SDF font material variant
        _outlineMaterialId = SDFFontMaterialConfig.RegisterVariant(
            "SDFFont_Outlined",
            new SDFFontMaterialConfig
            {
                OutlineColor = new Color4(0f, 0f, 0f, 1f),
                OutlineWidth = 0.15f,
            }
        );

        _logger.LogInformation(
            "Registered SDF material variants: SDFFont_Outlined (ID={Id})",
            _outlineMaterialId.Id
        );
    }

    // ------------------------------------------------------------------
    // Font atlas loading
    // ------------------------------------------------------------------

    private void LoadFontAtlas()
    {
        var textureRepo = _engine!.ResourceManager.TextureRepository;
        var samplerRepo = _engine.ResourceManager.SamplerRepository;

        // Load the embedded SDF atlas PNG as a GPU texture
        var assembly = typeof(SDFFontAtlasLoader).Assembly;
        var pngResourceName = $"{assembly.GetName().Name}.Assets.sans-regular.png";
        using var pngStream =
            assembly.GetManifestResourceStream(pngResourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{pngResourceName}' not found.");

        var textureRef = textureRepo.GetOrCreateFromStream(
            "SDFFont_Atlas",
            pngStream, false,
            "SDFFont_Atlas"
        );
        uint textureIndex = textureRef;

        // Create a linear sampler for SDF text (smooth interpolation)
        var samplerRef = samplerRepo.GetOrCreate(SamplerStateDesc.LinearClampNoMipmap);
        uint samplerIndex = samplerRef;

        // Load the built-in atlas descriptor and create the SDFFontAtlas
        _atlas = SDFFontAtlasLoader.LoadBuiltInAtlas(textureIndex, samplerIndex);

        _logger.LogInformation(
            "Loaded SDF font atlas: {W}x{H}, {G} glyphs, texture={T}, sampler={S}",
            _atlas.TextureWidth,
            _atlas.TextureHeight,
            _atlas.TextureWidth, // glyph count not directly exposed, use texture size as proxy
            textureIndex,
            samplerIndex
        );
    }

    // ------------------------------------------------------------------
    // Scene building
    // ------------------------------------------------------------------

    private void BuildScene()
    {
        var world = _worldDataProvider!.World;
        _root = new Node(world) { Name = "Root" };

        // Add a directional light
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

        // 1. "Hello Billboard!" — default SDF material, white color
        AddTextEntry(
            "Hello Billboard!",
            new Vector3(0, 5, 0),
            new Color4(1f, 1f, 1f, 1f),
            _globalFontSize,
            "SDFFont",
            editable: false
        );

        // 2. "HelixToolkit-Nex" — SDF material with outline
        AddTextEntry(
            "HelixToolkit-Nex",
            new Vector3(0, 3, 0),
            new Color4(1f, 1f, 1f, 1f),
            _globalFontSize,
            "SDFFont",
            editable: false
        );

        // 3. "GPU Picking!" — SDF material, cyan color
        AddTextEntry(
            "GPU Picking!",
            new Vector3(0, 1, 0),
            new Color4(0f, 1f, 1f, 1f),
            _globalFontSize,
            "SDFFont",
            editable: false
        );

        //// 4. "Editable Text" — SDF material, yellow color, editable via ImGui
        AddTextEntry(
            "Editable Text",
            new Vector3(0, -1, 0),
            new Color4(1f, 1f, 0f, 1f),
            _globalFontSize,
            "SDFFont",
            editable: true
        );
    }

    private void AddTextEntry(
        string text,
        Vector3 position,
        Color4 color,
        float fontSize,
        string materialName,
        bool editable
    )
    {
        var node = CreateTextEntity(text, fontSize, position, color, materialName);
        _textEntries.Add(
            new TextEntry
            {
                Name = text,
                Text = text,
                Position = position,
                Color = color,
                FontSize = fontSize,
                MaterialName = materialName,
                Node = node,
                Editable = editable,
            }
        );
    }

    /// <summary>
    /// Creates a single entity with a BillboardGeometry containing all glyphs for the text string.
    /// </summary>
    private Node CreateTextEntity(
        string text,
        float fontSize,
        Vector3 origin,
        Color4 color,
        string materialName
    )
    {
        if (_atlas is null)
            return new Node(_worldDataProvider!.World) { Name = $"Text_{text}" };

        var geo = TextLayoutHelper.LayoutGeometry(text, _atlas, fontSize, origin, isDynamic: true);

        var world = _worldDataProvider!.World;
        var node = new Node(world) { Name = $"Text_{text}" };
        _root!.AddChild(node);
        node.Entity.Set(
            new BillboardComponent
            {
                BillboardGeometry = geo,
                Color = color,
                TextureIndex = _atlas.TextureIndex,
                SamplerIndex = _atlas.SamplerIndex,
                BillboardMaterialName = materialName,
                Hitable = true,
                FixedSize = _fixedSize,
            }
        );

        return node;
    }

    /// <summary>
    /// Destroys the text entity for a text entry and removes it from the scene.
    /// </summary>
    private void DestroyTextEntity(TextEntry entry)
    {
        if (entry.Node is not null)
        {
            if (entry.Node.Entity == _selectedEntity)
            {
                _selectedEntity = Entity.Null;
            }
            _root?.RemoveChild(entry.Node);
            // Dispose the BillboardGeometry
            if (entry.Node.Entity.Valid && entry.Node.Entity.Has<BillboardComponent>())
            {
                ref var comp = ref entry.Node.Entity.Get<BillboardComponent>();
                comp.BillboardGeometry?.Dispose();
            }
            entry.Node.Dispose();
            entry.Node = null;
        }
    }

    /// <summary>
    /// Recreates the text entity for a text entry after text or parameters change.
    /// </summary>
    private void RecreateTextEntity(TextEntry entry)
    {
        DestroyTextEntity(entry);
        entry.Node = CreateTextEntity(
            entry.Text,
            entry.FontSize,
            entry.Position,
            entry.Color,
            entry.MaterialName
        );
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

        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        DrawUI(
            _renderContext.FinalOutputTexture,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );
        _imGuiRenderer.EndFrame();

        _orbitController?.Update(dt);

        // Update min screen size on the cull node
        if (_billboardCullNode is not null)
            _billboardCullNode.MinScreenSize = _minScreenSize;

        // Render context setup
        _renderContext!.Update(_viewportSize, _camera);

        // 3D render (offscreen)
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider,
            ViewportTextureName
        );

        // ImGui composite to swapchain
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = _renderContext.FinalOutputTexture;

        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        _engine.Submit(cmdBuf, swapchainTex);
    }

    // ------------------------------------------------------------------
    // GPU Picking
    // ------------------------------------------------------------------

    private void Pick(int x, int y)
    {
        if (_renderContext?.ResourceSet is null || _worldDataProvider is null)
            return;

        // Deselect previous
        if (_selectedEntity.Valid)
            _selectedEntity.Remove<BorderHighlightComponent>();

        if (
            !_context.TryPickRaw(
                _renderContext.ResourceSet.Textures[SystemBufferNames.TextureEntityId],
                (uint)_renderContext.WindowSize.Width,
                (uint)_renderContext.WindowSize.Height,
                x,
                y,
                out var worldId,
                out var entityId,
                out var instanceIdx,
                out var primitiveId
            )
        )
        {
            _logger.LogInformation("No entity picked at ({X}, {Y})", x, y);
            return;
        }

        _pickedEntityId = (int)entityId;
        _pickedInstanceIdx = instanceIdx;

        Debug.Assert(
            _worldDataProvider.World.Id == worldId,
            "Picked world ID does not match current world"
        );
        var entity = _worldDataProvider.World.GetEntity((int)entityId);
        _selectedEntity = entity;

        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightComponent.Default);
            _logger.LogInformation("Picked entity {Id} (instance {Idx})", entityId, instanceIdx);
        }
    }

    /// <summary>
    /// Finds which text entry a picked entity belongs to.
    /// </summary>
    private string FindTextGroupForEntity(Entity entity)
    {
        foreach (var entry in _textEntries)
        {
            if (entry.Node is not null && entry.Node.Entity == entity)
                return entry.Name;
        }
        return "Unknown";
    }

    // ------------------------------------------------------------------
    // ImGui UI
    // ------------------------------------------------------------------

    private void DrawUI(TextureHandle offscreenTex, float displayW, float displayH)
    {
        const float panelWidth = 360f;
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

        // --- Global Settings ---
        if (Gui.CollapsingHeader("Global Settings", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            float fontMin = 0.1f;
            float fontMax = 2.0f;
            if (_fixedSize)
            {
                fontMin = 2f;
                fontMax = 200f;
            }
            if (Gui.SliderFloat("Font Size", ref _globalFontSize, fontMin, fontMax))
                ApplyGlobalFontSize();
            Gui.SliderFloat("Min Screen Size (px)", ref _minScreenSize, fontMin, fontMax);
            if (Gui.Checkbox("Fixed Size (no perspective scaling)", ref _fixedSize))
                ApplyFixedSize();

            // Camera position
            var camPos = _camera.Position;
            Gui.Text($"Camera: ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})");
        }

        Gui.Separator();

        // --- Picking ---
        if (Gui.CollapsingHeader("Picking", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_selectedEntity.Valid)
            {
                string group = FindTextGroupForEntity(_selectedEntity);
                Gui.Text($"Selected Group: {group}");
                Gui.Text($"Entity ID: {_pickedEntityId}");
                Gui.Text($"Instance Idx: {_pickedInstanceIdx}");
            }
            else
            {
                Gui.TextDisabled("Click in the viewport to pick a billboard.");
            }
        }

        Gui.Separator();

        // --- Text Entries ---
        if (Gui.CollapsingHeader("Text Entries", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _textEntries.Count; i++)
            {
                var entry = _textEntries[i];
                Gui.PushID(i);

                bool enabled = entry.Enabled;
                if (Gui.Checkbox($"##en{i}", ref enabled))
                {
                    entry.Enabled = enabled;
                    ApplyEntryVisibility(entry);
                }
                Gui.SameLine();

                bool isSelected =
                    _selectedEntity.Valid
                    && entry.Node is not null
                    && entry.Node.Entity == _selectedEntity;
                if (isSelected)
                    Gui.PushStyleColor(ImGuiNET.ImGuiCol.Text, new Vector4(1, 1, 0, 1));

                bool open = Gui.TreeNodeEx(entry.Name, ImGuiNET.ImGuiTreeNodeFlags.None);

                if (isSelected)
                    Gui.PopStyleColor();

                if (open)
                {
                    int glyphCount = 0;
                    if (
                        entry.Node is not null
                        && entry.Node.Entity.Valid
                        && entry.Node.Entity.Has<BillboardComponent>()
                    )
                    {
                        ref var comp = ref entry.Node.Entity.Get<BillboardComponent>();
                        glyphCount = comp.BillboardCount;
                    }
                    Gui.Text($"Glyphs: {glyphCount}");
                    Gui.Text($"Material: {entry.MaterialName}");

                    // Color picker
                    var color = new Vector4(
                        entry.Color.Red,
                        entry.Color.Green,
                        entry.Color.Blue,
                        entry.Color.Alpha
                    );
                    if (Gui.ColorEdit4($"Color##col{i}", ref color))
                    {
                        entry.Color = new Color4(color.X, color.Y, color.Z, color.W);
                        ApplyEntryColor(entry);
                    }

                    // Editable text input
                    if (entry.Editable)
                    {
                        var textBuf = entry.Text;
                        if (Gui.InputText($"Text##txt{i}", ref textBuf, 256))
                        {
                            if (textBuf != entry.Text && !string.IsNullOrEmpty(textBuf))
                            {
                                entry.Text = textBuf;
                                entry.Name = textBuf;
                                RecreateTextEntity(entry);
                            }
                        }
                    }

                    Gui.TreePop();
                }
                Gui.PopID();
            }
        }

        Gui.Separator();

        // --- Add Text ---
        if (Gui.CollapsingHeader("Add Text"))
        {
            Gui.InputText("Text##new", ref _newText, 256);
            Gui.DragFloat3("Position##new", ref _newPosition, 0.1f);
            Gui.ColorEdit4("Color##new", ref _newColor);

            if (Gui.Button("Add") && !string.IsNullOrWhiteSpace(_newText))
            {
                AddTextEntry(
                    _newText,
                    _newPosition,
                    new Color4(_newColor.X, _newColor.Y, _newColor.Z, _newColor.W),
                    _globalFontSize,
                    "SDFFont",
                    editable: true
                );
                _newText = "New Text";
                _newPosition.Y -= 2f; // Offset next default position downward
            }
        }

        Gui.Separator();

        // --- Post Effects ---
        if (Gui.CollapsingHeader("Post Effects"))
        {
            bool smaaEnabled = _smaa.Enabled;
            if (Gui.Checkbox("SMAA", ref smaaEnabled))
                _smaa.Enabled = smaaEnabled;

            var toneMappingNode = _engine!.GetRenderNode<ToneMappingNode>()!;
            var tmEnabled = toneMappingNode.Enabled;
            if (Gui.Checkbox("Tone Mapping", ref tmEnabled))
                toneMappingNode.Enabled = tmEnabled;

            bool bhEnabled = _borderHighlight.Enabled;
            if (Gui.Checkbox("Border Highlight", ref bhEnabled))
                _borderHighlight.Enabled = bhEnabled;

            bool fpsEnabled = _showFPS.Enabled;
            if (Gui.Checkbox("Show FPS", ref fpsEnabled))
                _showFPS.Enabled = fpsEnabled;
        }

        Gui.Separator();

        // --- Statistics ---
        if (Gui.CollapsingHeader("Statistics", ImGuiNET.ImGuiTreeNodeFlags.DefaultOpen))
        {
            int totalGlyphs = 0;
            foreach (var entry in _textEntries)
            {
                if (
                    entry.Enabled
                    && entry.Node is not null
                    && entry.Node.Entity.Valid
                    && entry.Node.Entity.Has<BillboardComponent>()
                )
                {
                    ref var comp = ref entry.Node.Entity.Get<BillboardComponent>();
                    totalGlyphs += comp.BillboardCount;
                }
            }
            Gui.Text($"Total active glyphs: {totalGlyphs:N0}");
            Gui.Text($"Text entries: {_textEntries.Count}");

            var data = _worldDataProvider?.BillboardData;
            if (data is not null)
            {
                Gui.Text($"GPU billboard count: {data.TotalBillboardCount:N0}");
                Gui.Text($"Entity count: {data.Count}");
            }
        }

        Gui.End();
    }

    // ------------------------------------------------------------------
    // Apply helpers
    // ------------------------------------------------------------------

    private void ApplyGlobalFontSize()
    {
        foreach (var entry in _textEntries)
        {
            entry.FontSize = _globalFontSize;
            RecreateTextEntity(entry);
        }
    }

    private void ApplyFixedSize()
    {
        foreach (var entry in _textEntries)
        {
            if (entry.Node is not null)
            {
                entry.Node.Entity.Update<BillboardComponent>(comp =>
                {
                    comp.FixedSize = _fixedSize;
                    return comp;
                });
            }
        }
    }

    private void ApplyEntryColor(TextEntry entry)
    {
        if (entry.Node is not null)
        {
            entry.Node.Entity.Update<BillboardComponent>(comp =>
            {
                comp.Color = entry.Color;
                return comp;
            });
        }
    }

    private void ApplyEntryVisibility(TextEntry entry)
    {
        if (entry.Node is not null)
        {
            entry.Node.Enabled = entry.Enabled;
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
            // Dispose all BillboardGeometry instances
            foreach (var entry in _textEntries)
            {
                if (
                    entry.Node is not null
                    && entry.Node.Entity.Valid
                    && entry.Node.Entity.Has<BillboardComponent>()
                )
                {
                    ref var comp = ref entry.Node.Entity.Get<BillboardComponent>();
                    comp.BillboardGeometry?.Dispose();
                }
            }
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

// ------------------------------------------------------------------
// Data structures
// ------------------------------------------------------------------

/// <summary>
/// Tracks a single text entry in the billboard demo.
/// Each text entry now uses a single Node with a BillboardGeometry
/// containing all glyphs, instead of one entity per glyph.
/// </summary>
internal sealed class TextEntry
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    public Color4 Color { get; set; }
    public float FontSize { get; set; }
    public string MaterialName { get; set; } = "SDFFont";
    public Node? Node { get; set; }
    public bool Editable { get; set; }
    public bool Enabled { get; set; } = true;
}
