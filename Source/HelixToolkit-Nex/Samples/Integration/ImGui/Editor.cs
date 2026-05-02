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
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;
using Microsoft.Extensions.Logging;
using SceneSamples;
using static HelixToolkit.Nex.Rendering.PostEffects.BorderHighlightPostEffect;
using static HelixToolkit.Nex.Rendering.PostEffects.WireframePostEffect;

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
    private WorldDataProvider? _worldDataProvider;
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
    private FXAANode? _fxaa;
    private SMAANode? _smaa;
    private BloomNode? _bloom;
    private FPSNode? _showFPS;
    private readonly BorderHighlightPostEffect _borderHighlight = new();
    private readonly WireframePostEffect _wireframe = new();

    /// <summary>
    /// Tracks the ImGui 3D viewport content region size from the previous frame.
    /// The render graph uses this size (not the window size) to allocate offscreen
    /// textures and compute the projection aspect ratio.
    /// Initialized to a sensible default; updated every frame inside <see cref="Draw3DViewport"/>.
    /// </summary>
    private Size _viewportSize = new(1, 1);

    // --- Camera controllers ---
    private CameraControllerMode _cameraMode = CameraControllerMode.Orbit;
    private OrbitCameraController? _orbitController;
    private TurntableCameraController? _turntableController;
    private FirstPersonCameraController? _firstPersonController;
    private ICameraController? _activeController;

    // Viewport-relative mouse tracking for camera input
    private bool _isRotating;
    private bool _isPanning;
    private Vector2 _pointerLocation;

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

        // --- Camera controllers ---
        _orbitController = new OrbitCameraController(_camera);
        _turntableController = new TurntableCameraController(_camera);
        _firstPersonController = new FirstPersonCameraController(_camera) { MoveSpeed = 20f };
        _activeController = _orbitController;

        RenderSettings.LogFPSInDebug = true;

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
            .RenderToCustomTarget(RenderSettings.IntermediateTargetFormat)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(_borderHighlight);
                effects.AddEffect(_wireframe);
            })
            .Build();

        _fxaa = _engine.GetRenderNode<FXAANode>();
        _fxaa!.Enabled = false; // Start with FXAA off to better see the difference when toggling
        _smaa = _engine.GetRenderNode<SMAANode>();
        _bloom = _engine.GetRenderNode<BloomNode>();
        _showFPS = _engine.GetRenderNode<FPSNode>();

        // --- Per-viewport state and scene data ---
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();

        // Create the offscreen render target texture for the 3D viewport and add it to the system resource set
        _renderContext.ResourceSet.AddTexture(
            ViewportTextureName,
            res =>
            {
                return res.Context.Context.CreateRenderTarget2D(
                    RenderSettings.IntermediateTargetFormat,
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
        if (_engine is null || _renderContext is null)
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
            width / _imGuiRenderer.DisplayScale,
            height / _imGuiRenderer.DisplayScale
        );

        _imGuiRenderer.EndFrame();
        _scene.Tick(delta);

        // Update the active camera controller
        _activeController?.Update(delta);

        // --- Update render context for the 3D viewport ---
        // Use the ImGui viewport size (from the previous frame) for render graph resource
        // allocation and the camera projection. This decouples the 3D rendering resolution
        // from the swapchain / window size.
        _renderContext!.Update(_viewportSize, _camera);
        _renderContext.SetPointer(_pointerLocation);

        // --- Step 1: Execute 3D render graph (offscreen) ---
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider!,
            ViewportTextureName
        );

        // --- Step 2: ImGui composite pass — renders to swapchain ---
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.Textures[0] = _renderContext.FinalOutputTexture;
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);
        // --- Submit & present ---
        _engine.Submit(cmdBuf, swapchainTex);
    }

    public void Pick(int x, int y)
    {
        if (_renderContext?.ResourceSet is null || _worldDataProvider is null)
            return;
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Remove<BorderHighlightComponent>();
            _selectedEntity.Remove<WireframeComponent>();
        }
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
            return;
        }
        Debug.Assert(
            _worldDataProvider.World.Id == worldId,
            "Picked world ID does not match current world"
        );
        var entity = _worldDataProvider.World.GetEntity((int)entityId);
        _logger.LogInformation($"Picked entity {entity} (instance {instanceIdx})");
        _selectedEntity = entity;

        // Apply highlight to new selection
        if (_selectedEntity.Valid)
        {
            _selectedEntity.Set(BorderHighlightComponent.Default);
            _selectedEntity.Set(new WireframeComponent() { Color = new Color4(1f, 0f, 0f, 1f) });
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
                CameraControllerMode.FirstPerson => _firstPersonController,
                _ => _orbitController,
            };
        }
    }

    /// <summary>
    /// Gets the currently active camera controller (never null after Initialize).
    /// </summary>
    internal ICameraController? ActiveController => _activeController;

    /// <summary>
    /// Gets the orbit camera controller for property editing in the GUI.
    /// </summary>
    internal OrbitCameraController? OrbitController => _orbitController;

    /// <summary>
    /// Gets the turntable camera controller for property editing in the GUI.
    /// </summary>
    internal TurntableCameraController? TurntableController => _turntableController;

    /// <summary>
    /// Gets the first-person camera controller for property editing in the GUI.
    /// </summary>
    internal FirstPersonCameraController? FirstPersonController => _firstPersonController;

    // -------------------------------------------------------------------
    // Input forwarding from the application shell
    // -------------------------------------------------------------------

    /// <summary>
    /// Handles a mouse button press over the 3D viewport.
    /// </summary>
    /// <param name="button">0 = left, 1 = right, 2 = middle</param>
    /// <param name="viewportX">X position relative to the viewport.</param>
    /// <param name="viewportY">Y position relative to the viewport.</param>
    public void OnViewportMouseDown(int button, float viewportX, float viewportY)
    {
        if (_activeController is null)
            return;

        if (button == 1) // right = rotate
        {
            _isRotating = true;
            _activeController.OnRotateBegin(viewportX, viewportY);
        }
        else if (button == 2) // middle = pan
        {
            _isPanning = true;
            _activeController.OnPanBegin(viewportX, viewportY);
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
        if (_activeController is null)
            return;

        if (_isRotating)
            _activeController.OnRotateDelta(viewportX, viewportY);
        if (_isPanning)
            _activeController.OnPanDelta(viewportX, viewportY);
    }

    /// <summary>
    /// Handles mouse scroll wheel over the viewport.
    /// </summary>
    public void OnViewportMouseWheel(float delta)
    {
        _activeController?.OnZoomDelta(delta);
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
        if (_firstPersonController is not null && _cameraMode == CameraControllerMode.FirstPerson)
        {
            _firstPersonController.SetMovementInput(forward, backward, left, right, up, down);
            _firstPersonController.IsSprinting = sprint;
        }
    }
}
