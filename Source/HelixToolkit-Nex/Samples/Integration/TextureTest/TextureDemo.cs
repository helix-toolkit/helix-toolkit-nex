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
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using HelixToolkit.Nex.Textures;
using Microsoft.Extensions.Logging;

namespace TextureTest;

// ---------------------------------------------------------------------------
// Texture set descriptor
// ---------------------------------------------------------------------------

/// <summary>
/// Describes the file paths and default PBR scalar values for one texture set.
/// Null paths mean "no texture for this slot".
/// </summary>
internal sealed record TextureSetDesc(
    string DisplayName,
    // Albedo
    string? AlbedoFile,
    // Normal map
    string? NormalFile,
    // Metallic-roughness: either a combined glTF-style file (B=metallic, G=roughness)
    // or separate metallic + roughness files that are combined at load time.
    string? MetallicRoughnessFile, // pre-combined (glTF convention)
    string? MetallicFile, // separate metallic (R channel)
    string? RoughnessFile, // separate roughness (R channel) — or GLOSS (inverted)
    string? DisplaceFile, // displacement map.
    string? BumpFile, // bump map,

    bool RoughnessIsGloss, // true → invert roughness channel

    string? AoFile,    // AO
                       // Default scalar overrides (applied on top of textures)
    float DefaultMetallic,
    float DefaultRoughness,
    float DefaultAo,
    float ClearCoatRoughness = 1f,
    float ClearCoatStrength = 0f,
    float BumpScale = 1f // multiplier for bump map effect
);

// ---------------------------------------------------------------------------
// Loaded texture set (GPU resources)
// ---------------------------------------------------------------------------

/// <summary>Holds the GPU texture references for one loaded texture set.</summary>
internal sealed class LoadedTextureSet(
    TextureRef albedo,
    TextureRef normal,
    TextureRef metallicRoughness,
    TextureRef ao,
    TextureRef displace,
    TextureRef bump
) : IDisposable
{
    private bool _disposedValue;

    public TextureRef Albedo { get; } = albedo;
    public TextureRef Normal { get; } = normal;
    public TextureRef MetallicRoughness { get; } = metallicRoughness;
    public TextureRef Ao { get; } = ao;
    public TextureRef Displace { get; } = displace;

    public TextureRef Bump { get; } = bump;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            // TextureRef does not own the underlying resource — the repository does.
            // Nothing to dispose here.
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~LoadedTextureSet()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

// ---------------------------------------------------------------------------
// Demo
// ---------------------------------------------------------------------------

/// <summary>
/// PBR texture showcase demo.
/// Renders a sphere and lets the user switch between texture sets via ImGui.
/// </summary>
internal sealed partial class TextureDemo : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<TextureDemo>();
    private const string ViewportTextureName = "GuiViewportTexture";

    // ---- Texture set catalogue ----
    internal static readonly TextureSetDesc[] TextureSets =
    [
        new TextureSetDesc(
            DisplayName: "Earth",
            AlbedoFile: "Assets/Textures/Earth/2k_earth_daymap.jpg",
            NormalFile: "Assets/Textures/Earth/2k_earth_normal_map.tif",
            MetallicRoughnessFile: null,
            MetallicFile: null,
            RoughnessFile: null,
            RoughnessIsGloss: false,
            DisplaceFile: null,
            BumpFile: null,
            AoFile: null,
            DefaultMetallic: 0.0f,
            DefaultRoughness: 0.6f,
            DefaultAo: 1.0f
        ),
        new TextureSetDesc(
            DisplayName: "Metal Corroded",
            AlbedoFile: "Assets/Textures/MetalCorroded/MetalCorrodedHeavy001_COL_2K_METALNESS.jpg",
            NormalFile: "Assets/Textures/MetalCorroded/MetalCorrodedHeavy001_NRM_2K_METALNESS.jpg",
            MetallicRoughnessFile: null,
            MetallicFile: "Assets/Textures/MetalCorroded/MetalCorrodedHeavy001_METALNESS_2K_METALNESS.jpg",
            RoughnessFile: "Assets/Textures/MetalCorroded/MetalCorrodedHeavy001_ROUGHNESS_2K_METALNESS.jpg",
            RoughnessIsGloss: false,
            DisplaceFile: "Assets/Textures/MetalCorroded/MetalCorrodedHeavy001_DISP_2K_METALNESS.jpg",
            BumpFile: null,
            AoFile: null,
            DefaultMetallic: 1.0f,
            DefaultRoughness: 0.8f,
            DefaultAo: 1.0f
        ),
        new TextureSetDesc(
            DisplayName: "Ceramic Glossy Tile",
            AlbedoFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_COL_2K.png",
            NormalFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_NRM_2K.png",
            MetallicRoughnessFile: null,
            MetallicFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_REFL_2K.png",
            // GLOSS map — will be inverted to roughness
            RoughnessFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_GLOSS_2K.png",
            RoughnessIsGloss: true,
            AoFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_AO_2K.png",
            BumpFile: "Assets/Textures/CeramicGlossyTile/TilesMosaicPennyround001_BUMP_2K.png",
            DisplaceFile: null,
            DefaultMetallic: 0.0f,
            DefaultRoughness: 0.2f,
            DefaultAo: 1.0f,
            ClearCoatRoughness: 0.3f,
            ClearCoatStrength: 0.8f,
            BumpScale: 0.1f
        ),
        new TextureSetDesc(
            DisplayName: "Grass Patchy Ground",
            AlbedoFile: "Assets/Textures/GrassPatchyGround/Poliigon_GrassPatchyGround_4585_BaseColor.jpg",
            NormalFile: "Assets/Textures/GrassPatchyGround/Poliigon_GrassPatchyGround_4585_Normal.png",
            MetallicRoughnessFile: null,
            MetallicFile: "Assets/Textures/GrassPatchyGround/Poliigon_GrassPatchyGround_4585_Metallic.jpg",
            // GLOSS map — will be inverted to roughness
            RoughnessFile: "Assets/Textures/GrassPatchyGround/Poliigon_GrassPatchyGround_4585_Roughness.jpg",
            RoughnessIsGloss: false,
            AoFile: "Assets/Textures/GrassPatchyGround/Poliigon_GrassPatchyGround_4585_AmbientOcclusion.png",
            BumpFile: null,
            DisplaceFile: null,
            DefaultMetallic: 0.0f,
            DefaultRoughness: 0.2f,
            DefaultAo: 1.0f
        ),
        new TextureSetDesc(
            DisplayName: "Metal Matte",
            AlbedoFile: "Assets/Textures/MetalMatte/Poliigon_MetalPaintedMatte_7037_BaseColor.jpg",
            NormalFile: "Assets/Textures/MetalMatte/Poliigon_MetalPaintedMatte_7037_Normal.png",
            DisplaceFile: "Assets/Textures/MetalMatte/Poliigon_MetalPaintedMatte_7037_Displacement.tiff",
            MetallicRoughnessFile: "Assets/Textures/MetalMatte/Poliigon_MetalPaintedMatte_7037_ORM.jpg",
            BumpFile: null,
            RoughnessIsGloss: false,
            DefaultMetallic: 0.8f,
            DefaultRoughness: 0.6f,
            DefaultAo: 1.0f,
            MetallicFile: null,
            RoughnessFile: null,
            AoFile: "Assets/Textures/MetalMatte/Poliigon_MetalPaintedMatte_7037_ORM.jpg"
        ),
    ];

    // ---- Engine objects ----
    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;

    // ---- Camera ----
    private Camera _camera = new PerspectiveCamera();
    private OrbitCameraController? _orbitController;
    private bool _isRotating;
    private bool _isPanning;

    // ---- ImGui swapchain pass ----
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    // ---- Post effects ----
    internal readonly Fxaa Fxaa = new() { Enabled = true };
    internal readonly ShowFPS ShowFPS = new() { Enabled = true };

    // ---- Viewport ----
    private Size _viewportSize = new(1, 1);

    // ---- Scene objects ----
    private MeshNode? _sphereNode;
    private PBRMaterialProperties? _material;
    private SamplerRef _sampler = SamplerRef.Null;
    private SamplerRef _displaceSampler = SamplerRef.Null;

    // ---- Texture set state ----
    private readonly LoadedTextureSet?[] _loadedSets = new LoadedTextureSet?[TextureSets.Length];
    internal int ActiveSetIndex = 0;

    // ---- Auto-rotation ----
    internal bool AutoRotate = true;
    internal float RotationAngle;
    private long _lastTimestamp;

    // ---- Editable material scalars (mirrored for ImGui) ----
    internal float Metallic;
    internal float Roughness;
    internal float Ao;
    internal Color4 AlbedoTint = Color.White;
    internal float Opacity = 1.0f;

    public ImGuiRenderer? ImGui => _imGuiRenderer;

    public TextureDemo(IContext context)
    {
        _context = context;
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, -4f),
            Target = Vector3.Zero,
            FarPlane = 500f,
        };
        _orbitController = new OrbitCameraController(_camera);

        RenderSettings.LogFPSInDebug = true;

        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .RenderToCustomTarget(Format.RGBA_F16)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(Fxaa);
                effects.AddEffect(ShowFPS);
            })
            .Build();

        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return res.Context.Context.CreateRenderTarget2D(
                    Format.RGBA_F16,
                    (uint)res.Context.WindowSize.Width,
                    (uint)res.Context.WindowSize.Height,
                    debugName: ViewportTextureName
                );
            },
            true
        );

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        BuildScene();

        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        _imGuiPass.Colors[0].ClearColor = new Color4(0.08f, 0.08f, 0.08f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
    }

    // -------------------------------------------------------------------------
    // Scene construction
    // -------------------------------------------------------------------------

    private void BuildScene()
    {
        if (_engine is null)
        {
            return;
        }
        var materialPool = _engine.ResourceManager.PBRPropertyManager;
        var samplerRepo = _engine.ResourceManager.SamplerRepository;

        _root = new Node(_worldDataProvider!.World, "TextureTestRoot");

        // ---- Sphere geometry ----
        var meshBuilder = new MeshBuilder(true, true, true);
        meshBuilder.AddSphere(Vector3.Zero, 1.5f, 128, 128);
        var sphereGeo = meshBuilder.ToMesh().ToGeometry();
        _engine.Add(sphereGeo);

        // ---- Shared sampler ----
        _sampler = samplerRepo.GetOrCreate(SamplerStateDesc.LinearRepeat);
        _displaceSampler = samplerRepo.GetOrCreate(SamplerStateDesc.PointClamp);

        // ---- Load the initial texture set ----
        var initialSet = LoadTextureSet(ActiveSetIndex);
        _loadedSets[ActiveSetIndex] = initialSet;

        // ---- PBR material ----
        var desc = TextureSets[ActiveSetIndex];
        Metallic = desc.DefaultMetallic;
        Roughness = desc.DefaultRoughness;
        Ao = desc.DefaultAo;

        _material = materialPool.Create(PBRShadingMode.PBR);
        ApplyTextureSet(initialSet, desc);

        // ---- Sphere node ----
        _sphereNode = new MeshNode(_worldDataProvider.World, "Sphere")
        {
            Geometry = sphereGeo,
            MaterialProperties = _material,
        };
        _root.AddChild(_sphereNode);

        // ---- Lighting ----
        var sunNode = new Node(_worldDataProvider.World, "Sun");
        sunNode.Entity.Set(
            new DirectionalLightComponent
            {
                Direction = Vector3.Normalize(new Vector3(-1f, -1f, 0.5f)),
                Color = new Color4(1.0f, 0.95f, 0.85f, 1f),
                Intensity = 1.0f,
            }
        );
        sunNode.Transform = new Transform();
        _root.AddChild(sunNode);

        var fillNode = new Node(_worldDataProvider.World, "FillLight");
        fillNode.Entity.Set(
            new RangeLightComponent(RangeLightType.Point)
            {
                Position = new Vector3(-3.5f, 0f, 0f),
                Color = new Color4(1f, 0, 1.0f, 1f),
                Intensity = 2.0f,
                Range = 3f,
            }
        );
        _root.AddChild(fillNode);
    }

    // -------------------------------------------------------------------------
    // Texture set loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads (or returns cached) GPU textures for the given set index.
    /// </summary>
    private LoadedTextureSet LoadTextureSet(int index)
    {
        if (_loadedSets[index] is { } cached)
            return cached;

        var desc = TextureSets[index];
        var textureRepo = _engine!.ResourceManager.TextureRepository;

        var albedo = TryLoadFile(textureRepo, desc.AlbedoFile, $"{desc.DisplayName}_Albedo");
        var normal = TryLoadFile(textureRepo, desc.NormalFile, $"{desc.DisplayName}_Normal");
        var displace = TryLoadFile(textureRepo, desc.DisplaceFile, $"{desc.DisplayName}_Displace"); // not used in this demo
        var bump = TryLoadFile(textureRepo, desc.BumpFile, $"{desc.DisplayName}_Bump"); // not used in this demo

        TextureRef mr;
        if (desc.MetallicRoughnessFile is not null)
        {
            // Pre-combined file (e.g. glTF-style, G=metallic, B=roughness)
            mr = TryLoadFile(textureRepo, desc.MetallicRoughnessFile, $"{desc.DisplayName}_MR");
        }
        else if (
            desc.AoFile is not null
            || desc.MetallicFile is not null
            || desc.RoughnessFile is not null
        )
        {
            // Separate files — use OmrTextureCombiner to pack into R=AO, G=Roughness, B=Metallic
            mr = BuildOmrTexture(textureRepo, desc);
        }
        else
        {
            mr = TextureRef.Null;
        }

        // AO is baked into the OMR texture's R channel when separate files are used.
        // Set AO to same omr texture for simplicity, and the shader will read the R channel for AO and G/B for roughness/metallic.
        var ao = mr;

        var set = new LoadedTextureSet(albedo, normal, mr, ao, displace, bump);
        _loadedSets[index] = set;
        return set;
    }

    /// <summary>
    /// Uses <see cref="OmrTextureCombiner"/> to pack separate AO, roughness/gloss, and metallic
    /// files into a single RGBA_UN8 texture following the glTF channel convention:
    ///   R = Ambient Occlusion
    ///   G = Roughness
    ///   B = Metallic
    ///   A = 255 (fixed)
    ///
    /// The OmrTextureCombiner output slots are repurposed as:
    ///   WithOcclusion → R = AO
    ///   WithMetallic  → G = Roughness  (combiner G slot used for roughness data)
    ///   WithRoughness → B = Metallic   (combiner B slot used for metallic data)
    /// </summary>
    private TextureRef BuildOmrTexture(ITextureRepository textureRepo, TextureSetDesc desc)
    {
        var cacheKey = $"{desc.DisplayName}_OMR_Combined";

        // Return from cache if already built
        if (textureRepo.TryGet(cacheKey, out var cached) && cached is not null)
            return cached.Ref;

        try
        {
            Image? aoImg =
                desc.AoFile is not null && File.Exists(desc.AoFile)
                    ? Image.Load(desc.AoFile)
                    : null;
            Image? roughnessImg =
                desc.RoughnessFile is not null && File.Exists(desc.RoughnessFile)
                    ? Image.Load(desc.RoughnessFile)
                    : null;
            Image? metallicImg =
                desc.MetallicFile is not null && File.Exists(desc.MetallicFile)
                    ? Image.Load(desc.MetallicFile)
                    : null;

            if (aoImg is null && roughnessImg is null && metallicImg is null)
                return TextureRef.Null;

            using var _ao = aoImg;
            using var _r = roughnessImg;
            using var _m = metallicImg;

            var combiner = new OmrTextureCombiner();

            // R = AO  (WithOcclusion → output R)
            if (aoImg is not null)
                combiner.WithOcclusion(aoImg, ChannelComponent.R);

            // G = Roughness  (WithMetallic → output G, fed with roughness/gloss data)
            if (roughnessImg is not null)
                if (desc.RoughnessIsGloss)
                    combiner.WithRoughnessFromGloss(roughnessImg, ChannelComponent.R);
                else
                    combiner.WithRoughness(roughnessImg, ChannelComponent.R);

            // B = Metallic  (WithRoughness → output B, fed with metallic data)
            if (metallicImg is not null)
                combiner.WithMetallic(metallicImg, ChannelComponent.R);

            using var combined = combiner.Combine();

            return textureRepo.GetOrCreateFromImage(cacheKey, combined);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build OMR texture for {Set}", desc.DisplayName);
            return TextureRef.Null;
        }
    }

    private TextureRef TryLoadFile(ITextureRepository repo, string? filePath, string debugName)
    {
        if (filePath is null)
            return TextureRef.Null;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Texture not found: {Path}", filePath);
            return TextureRef.Null;
        }

        try
        {
            return repo.GetOrCreateFromFile(filePath, debugName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load texture: {Path}", filePath);
            return TextureRef.Null;
        }
    }

    // -------------------------------------------------------------------------
    // Texture set switching
    // -------------------------------------------------------------------------

    /// <summary>
    /// Switches the active texture set, loading it if not yet cached, and
    /// updates the live material with the new textures and default scalars.
    /// </summary>
    internal void SwitchToTextureSet(int index)
    {
        if (index == ActiveSetIndex || _material is null)
            return;

        ActiveSetIndex = index;
        var set = LoadTextureSet(index);
        var desc = TextureSets[index];

        // Reset scalars to the new set's defaults
        Metallic = desc.DefaultMetallic;
        Roughness = desc.DefaultRoughness;
        Ao = desc.DefaultAo;
        AlbedoTint = Color.White;
        Opacity = 1.0f;

        ApplyTextureSet(set, desc);
    }

    private void ApplyTextureSet(LoadedTextureSet set, TextureSetDesc desc)
    {
        if (_material is null)
            return;

        _material.AlbedoMap = set.Albedo.GetHandle().Valid ? set.Albedo : TextureRef.Null;
        _material.NormalMap = set.Normal.GetHandle().Valid ? set.Normal : TextureRef.Null;
        _material.MetallicRoughnessMap = set.MetallicRoughness.GetHandle().Valid
            ? set.MetallicRoughness
            : TextureRef.Null;
        _material.AoMap = set.Ao.GetHandle().Valid ? set.Ao : TextureRef.Null;
        _material.DisplaceMap = set.Displace.GetHandle().Valid ? set.Displace : TextureRef.Null;
        _material.Sampler = _sampler;
        _material.DisplaceSampler = _displaceSampler;
        _material.BumpMap = set.Bump.GetHandle().Valid ? set.Bump : TextureRef.Null;
        _material.BumpScale = desc.BumpScale;

        _material.Albedo = AlbedoTint;
        _material.Metallic = Metallic;
        _material.Roughness = Roughness;
        _material.Ao = Ao;
        _material.Opacity = Opacity;
        _material.Emissive = Color.Transparent;
        _material.ClearCoatRoughness = desc.ClearCoatRoughness;
        _material.ClearCoatStrength = desc.ClearCoatStrength;
    }

    // -------------------------------------------------------------------------
    // Render loop
    // -------------------------------------------------------------------------

    public void Render(int width, int height)
    {
        if (_engine is null || _renderContext is null || _imGuiRenderer is null)
            return;

        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();

        if (AutoRotate && _sphereNode is not null)
        {
            RotationAngle += delta * 15f;
            if (RotationAngle >= 360f)
                RotationAngle -= 360f;

            _sphereNode.Transform = new Transform
            {
                Rotation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    RotationAngle * MathF.PI / 180f
                ),
            };
            _sphereNode.NotifyTransformChanged();
        }
        // Draw the ImGui UI
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

        _context.Submit(cmdBuf, swapchainTex);
    }

    // -------------------------------------------------------------------------
    // Input forwarding
    // -------------------------------------------------------------------------

    public void OnViewportMouseDown(int button, float vx, float vy)
    {
        if (_orbitController is null)
            return;
        if (button == 1)
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

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _material?.Dispose();
        _imGuiRenderer?.Dispose();
        _renderContext?.Dispose();
        _worldDataProvider?.Dispose();
        _engine?.Dispose();
    }
}
