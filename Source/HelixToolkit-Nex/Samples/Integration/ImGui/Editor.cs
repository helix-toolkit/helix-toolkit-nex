using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using Microsoft.Extensions.Logging;
using SceneSamples;

namespace ImGuiTest;

internal partial class Editor : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<Editor>();

    private readonly IContext _context;
    private IServiceProvider? _serviceProvider;
    private Renderer? _renderer;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private IResourceManager? _resourceManager;
    private RenderGraph? _renderGraph;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;
    private IScene _scene = new MinecraftScene();

    private Camera _camera = new PerspectiveCamera();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _lastTimestamp = 0;
    private Entity _selectedEntity = Entity.Null;

    // ImGui swapchain rendering state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    // Post Effects
    private readonly Fxaa _fxaa = new() { Enabled = false };
    private readonly Smaa _smaa = new();
    private readonly Bloom _bloom = new();
    private readonly BorderHighlightPostEffect _borderHighlight = new();
    private readonly WireframePostEffect _wireframe = new();
    private readonly ToneMapping _toneMapping = new();
    private readonly ShowFPS _showFPS = new();

    /// <summary>
    /// Tracks the ImGui 3D viewport content region size from the previous frame.
    /// The render graph uses this size (not the window size) to allocate offscreen
    /// textures and compute the projection aspect ratio.
    /// Initialized to a sensible default; updated every frame inside <see cref="Draw3DViewport"/>.
    /// </summary>
    private Size _viewportSize = new(1, 1);

    public Editor(IContext context)
    {
        _context = context;
    }

    public void Initialize(int width, int height)
    {
        _camera = new PerspectiveCamera()
        {
            Position = new Vector3(_scene.WorldSizeX / 2f, 60, -_scene.WorldSizeZ / 2f - 20),
            Target = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f),
            FarPlane = 1000,
        };
        RenderSettings.LogFPSInDebug = true;

        // --- Service setup ---
        var services = new ServiceCollection { new ServiceDescriptor(typeof(IContext), _context) };
        services.AddSingleton<IResourceManager, ResourceManager>();
        _serviceProvider = services.BuildServiceProvider();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();

        _scene.RegisterMaterials();
        _resourceManager.Materials.CreatePBRMaterialsFromRegistry();

        // --- 3D render graph (offscreen — no swapchain write) ---
        _renderer = new Renderer(_serviceProvider);
        _renderer.AddNode(new PrepareNode());
        _renderer.AddNode(new DepthPassNode());
        _renderer.AddNode(new FrustumCullNode());
        _renderer.AddNode(new ForwardPlusOpaqueNode() { UseLightCulling = true });
        _renderer.AddNode(new ForwardPlusLightCullingNode());
        var postEffectNode = new PostEffectsNode();

        postEffectNode.AddEffect(_fxaa);
        postEffectNode.AddEffect(_smaa);
        postEffectNode.AddEffect(_bloom);
        postEffectNode.AddEffect(_borderHighlight);
        postEffectNode.AddEffect(_wireframe);
        postEffectNode.AddEffect(_toneMapping);
        postEffectNode.AddEffect(_showFPS);

        _renderer.AddNode(postEffectNode);
        _renderer!.Initialize();

        _renderGraph = new RenderGraph(_serviceProvider);
        foreach (var node in _renderer.RenderNodes)
        {
            node.AddToGraph(_renderGraph);
        }
        _renderGraph.Compile();

        _renderContext = new RenderContext(_serviceProvider);
        _renderContext.ResourceSet = new RenderGraphResourceSet();
        _worldDataProvider = new WorldDataProvider(_serviceProvider);
        _worldDataProvider.Initialize();
        _renderContext.Data = _worldDataProvider;
        _renderContext.Initialize();

        // Build the 3D scene
        _root = _scene.Build(_context, _resourceManager, _worldDataProvider);

        // --- ImGui setup ---
        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        // Configure the ImGui render pass for the swapchain
        _imGuiPass.Colors[0].ClearColor = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;
    }

    public void Render(int width, int height)
    {
        if (_renderer is null || _renderContext is null || _renderGraph is null)
            return;
        if (_imGuiRenderer is null)
            return;

        // --- Tick scene ---
        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _scene.Tick(delta);

        // --- Update render context for the 3D viewport ---
        // Use the ImGui viewport size (from the previous frame) for render graph resource
        // allocation and the camera projection. This decouples the 3D rendering resolution
        // from the swapchain / window size.
        if (!_viewportSize.IsEmpty)
        {
            _renderContext.WindowSize = _viewportSize;
        }
        var aspectRatio = _viewportSize.IsEmpty
            ? (float)width / height
            : (float)_viewportSize.Width / _viewportSize.Height;

        _renderContext.CameraParams = _camera.ToCameraParams(aspectRatio);
        // FinalOutputTexture is not used by the offscreen graph, but the resource set
        // still expects it to be non-null for system resource setup.
        _renderContext.FinalOutputTexture = _context.GetCurrentSwapchainTexture();

        // --- Step 1: Execute 3D render graph (offscreen) ---
        var cmdBuf = _renderer.RenderOffscreen(_renderContext, _renderGraph);

        // Retrieve the offscreen texture that the graph produced
        var offscreenTexHandle = _renderContext.ResourceSet!.Textures[
            SystemBufferNames.TextureColorF16Current
        ];

        // --- Step 2: ImGui composite pass — renders to swapchain ---
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = offscreenTexHandle;

        _imGuiRenderer.BeginFrame(new Vector2(width, height));

        // --- ImGui UI ---
        DrawMainMenuBar();
        DrawLayout(
            offscreenTexHandle,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );

        _imGuiRenderer.EndFrame();
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        // --- Submit & present ---
        _context.Submit(cmdBuf, swapchainTex);
    }

    public void Pick(int x, int y)
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
            out var entityVar,
            out var instanceIdx
        );
        var entity = _worldDataProvider.World.GetEntity((int)entityId, entityVar);
        _logger.LogInformation($"Picked entity {entity} (instance {instanceIdx})");
        SelectEntity(entity);
    }

    /// <summary>
    /// Gets the ImGui renderer for input forwarding from the application shell.
    /// </summary>
    public ImGuiRenderer? ImGui => _imGuiRenderer;

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _imGuiRenderer?.Dispose();
                _worldDataProvider?.Dispose();
                _renderer?.Dispose();
                _renderGraph?.Dispose();
                _resourceManager?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
