using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
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
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using static HelixToolkit.Nex.Rendering.PostEffects.WireframePostEffect;
using Viewport = HelixToolkit.Nex.ImGui.Viewport;

namespace ImGuiTest;

/// <summary>
/// Enumerates the available camera controller modes in the editor.
/// </summary>
internal enum CameraControllerMode
{
    Orbit,
    Turntable,
    FirstPerson,
}

internal partial class Editor : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<Editor>();
    private const string ViewportTextureName = "ViewportTexture";
    private readonly IContext _context;
    private Engine? _engine;
    private RenderContext? _renderContext;
    private RenderContext? _cullRenderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Node? _root;
    private IScene _scene = new MinecraftScene();

    private Camera _camera = new PerspectiveCamera();
    private Camera _cullCamera = new PerspectiveCamera();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _lastTimestamp = 0;
    private Entity _selectedEntity = Entity.Null;

    // ImGui swapchain rendering state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    // Post Effects
    private FXAANode? _fxaa;
    private SMAANode? _smaa;
    private BloomNode? _bloom;
    private FPSNode? _showFPS;
    private FrustumCullNode? _cullNode;
    private readonly BorderHighlightPostEffect _borderHighlight = new();
    private readonly WireframePostEffect _wireframe = new();

    // --- Camera controllers ---
    private CameraControllerMode _cameraMode = CameraControllerMode.Orbit;
    private OrbitCameraController? _orbitController;
    private TurntableCameraController? _turntableController;
    private WalkaroundCameraController? _walkaroundController;
    private OrbitCameraController? _cullCameraController;
    private CameraFrustumVisual? _cullCameraFrustumVisual;
    private ICameraController? _activeController;

    // Reusable viewport regions
    private Viewport? _viewport;
    private Viewport? _cullViewport;

    private bool _perInstance = false;

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
        _cullCamera = new PerspectiveCamera()
        {
            Position = new Vector3(_scene.WorldSizeX / 2f, 60, -_scene.WorldSizeZ / 2f - 20),
            Target = new Vector3(_scene.WorldSizeX / 2f, 0, _scene.WorldSizeZ / 2f),
            FarPlane = 1000,
        };
        // --- Camera controllers ---
        _orbitController = new OrbitCameraController(_camera);
        _turntableController = new TurntableCameraController(_camera);
        _walkaroundController = new WalkaroundCameraController(_camera) { MoveSpeed = 20f };
        _activeController = _orbitController;
        _cullCameraController = new OrbitCameraController(_cullCamera);

        // Register Minecraft block material types before the material registry is built
        _scene.RegisterMaterials();

        // --- Build engine via EngineBuilder (offscreen — no swapchain write) ---
        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .WithBloom()
            .WithSMAA()
            .WithFXAA()
            .WithFPS()
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(_borderHighlight);
                effects.AddEffect(_wireframe);
                effects.AddEffect(new BoundingBoxPostEffect());
                effects.AddEffect(new CameraFrustumVisual());
            })
            .Build();

        _fxaa = _engine.GetRenderNode<FXAANode>();
        _fxaa!.Enabled = false; // Start with FXAA off to better see the difference when toggling
        _smaa = _engine.GetRenderNode<SMAANode>();
        _bloom = _engine.GetRenderNode<BloomNode>();
        _showFPS = _engine.GetRenderNode<FPSNode>();
        _cullNode = _engine.GetRenderNode<FrustumCullNode>();
        _cullCameraFrustumVisual = _engine.GetPostEffect<CameraFrustumVisual>();
        _cullCameraFrustumVisual!.FarPlaneDistance = 60;

        // --- Per-viewport state and scene data ---
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _cullRenderContext = _engine.CreateRenderContext();
        _cullRenderContext.Initialize();

        // Create the offscreen render target texture for the 3D viewport and add it to the system resource set
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return res.Context.Context.CreateRenderTarget2D(
                    GraphicsSettings.IntermediateTargetFormat,
                    (uint)res.Context.WindowSize.Width,
                    (uint)res.Context.WindowSize.Height,
                    debugName: ViewportTextureName
                );
            }
        );
        _cullRenderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return res.Context.Context.CreateRenderTarget2D(
                    GraphicsSettings.IntermediateTargetFormat,
                    (uint)res.Context.WindowSize.Width,
                    (uint)res.Context.WindowSize.Height,
                    debugName: ViewportTextureName
                );
            }
        );
        _renderContext.PointerRing.Enabled = 1;
        _renderContext.PointerRing.OuterDistThreshold = 0.6f;
        _renderContext.PointerRing.InnerDistThreshold = 0.4f;
        _renderContext.PointerRing.ColorMix = 0.4f;

        // Reusable viewport regions. The main viewport forwards input to the active
        // camera controller and reports the pointer (PointerRing) by default; the culling
        // viewport is a passive display only (no controller, no pick, no pointer reporting).
        _viewport = new Viewport(_renderContext, _activeController)
        {
            PickCallback = Pick,
            Title = "Main Viewport",
        };
        // A distinct WindowId is required: ImGui identifies windows by name, so two viewports
        // sharing the default "##Viewport" id would collide into one window (the second one then
        // measures a zero-width content region).
        _cullViewport = new Viewport(_cullRenderContext)
        {
            ReportPointerToRenderContext = false,
            WindowId = "##CullingViewport",
            Title = "Culling Visualizer",
            CameraController = _cullCameraController,
        };

        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        // Build the 3D scene
        _root = _scene.Build(_context, _engine.ResourceManager, _worldDataProvider);

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
        if (_engine is null || _renderContext is null || _cullRenderContext is null)
            return;
        if (_imGuiRenderer is null)
            return;

        // --- Tick scene ---
        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _imGuiRenderer.BeginFrame(new Vector2(width, height));
        // --- ImGui UI ---
        DrawMainMenuBar();
        DrawLayout(
            _renderContext.FinalOutputTexture,
            _cullRenderContext.FinalOutputTexture,
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );

        _imGuiRenderer.EndFrame();
        _scene.Tick(delta);

        // Update the active camera controller
        _activeController?.Update(delta);
        _cullCameraController?.Update(delta);

        // --- Update render context for the 3D viewport ---
        // Use the ImGui viewport size (from the previous frame) for render graph resource
        // allocation and the camera projection. This decouples the 3D rendering resolution
        // from the swapchain / window size.
        _renderContext!.Update(_viewport!.ViewportSize, _camera);
        _cullRenderContext.Update(_cullViewport!.ViewportSize, _cullCamera);

        _cullCameraFrustumVisual!.Data = new CameraFrustumVisual.CameraFrustumVisualInfo()
        {
            InversedViewProjection =
                _camera.CreateInverseProjection(
                    (float)_viewport.ViewportSize.Width / _viewport.ViewportSize.Height
                ) * _camera.CreateInverseView(),
        };

        // --- Step 1: Execute 3D render graph (offscreen) ---
        _engine.BeginFrame();
        _cullNode!.Enabled = true;
        _cullCameraFrustumVisual.Enabled = false;
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider!,
            ViewportTextureName
        );


        _cullNode!.Enabled = false; // Disable culling for the frustum visualizer pass
        _cullCameraFrustumVisual.Enabled = true; // Enable the frustum visualizer for the culling viewport pass
        cmdBuf = _engine.RenderOffscreen(
            _cullRenderContext!,
            _worldDataProvider!,
            ViewportTextureName,
            cmdBuf
        );

        // --- Step 2: ImGui composite pass — renders to swapchain ---
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        using var _ = _imGuiDeps.PushTextureScoped(_cullRenderContext.FinalOutputTexture);
        using var __ = _imGuiDeps.PushTextureScoped(_renderContext.FinalOutputTexture);
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);
        // --- Submit & present ---
        _engine.Submit(cmdBuf, swapchainTex);
    }

    public void Pick(int x, int y)
    {
        if (_renderContext?.ResourceSet is null || _worldDataProvider is null)
            return;
        _engine!.CreatePickingRequest(_renderContext, new Vector2(x, y), HandlePickingResponse);
    }

    private void HandlePickingResponse(PickingResponse response)
    {
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Remove<BorderHighlightOverlay>();
            _selectedEntity.Remove<WireframeOverlay>();
        }
        if (!response.TryGetPickingResult(out var result))
        {
            return;
        }
        _logger.LogInformation($"Picked entity {result.Entity} (primitive {result.PrimitiveId})");
        _selectedEntity = result.Entity;
        // Apply highlight to new selection
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightOverlay.Default);
            _selectedEntity.Set(
                new WireframeOverlay()
                {
                    Color = new Color4(1f, 0f, 0f, 1f),
                    InstancingIndex = _perInstance ? (int)result.InstanceId : -1,
                }
            );
        }
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
                _renderContext?.Teardown();
                _engine?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    // -------------------------------------------------------------------
    // Camera controller management
    // -------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the current camera controller mode. Setting this property
    /// switches the active controller. The new controller re-derives its state
    /// from the camera's current position/target so the transition is seamless.
    /// </summary>
    internal CameraControllerMode CameraMode
    {
        get => _cameraMode;
        set
        {
            if (_cameraMode == value)
                return;
            _cameraMode = value;

            // Recreate the controller from the camera's current state so
            // the viewpoint is preserved across mode switches.
            _activeController = _cameraMode switch
            {
                CameraControllerMode.Orbit => _orbitController,
                CameraControllerMode.Turntable => _turntableController,
                CameraControllerMode.FirstPerson => _walkaroundController,
                _ => _orbitController,
            };
        }
    }

    /// <summary>
    /// Forwards keyboard state to the first-person controller.
    /// Call this each frame from the application shell.
    /// </summary>
    public void OnKeyboardInput(
        bool forward,
        bool backward,
        bool left,
        bool right,
        bool up,
        bool down,
        bool sprint
    )
    {
        if (_walkaroundController is not null && _cameraMode == CameraControllerMode.FirstPerson)
        {
            _walkaroundController.SetMovementInput(forward, backward, left, right, up, down);
            _walkaroundController.IsSprinting = sprint;
        }
    }
}
