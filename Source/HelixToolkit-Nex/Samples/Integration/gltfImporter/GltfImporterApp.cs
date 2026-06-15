using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.glTF;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.ImGui;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.PostEffects;
using HelixToolkit.Nex.Scene;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using NativeFileDialogSharp;
using SDL3;
using ApplicationBase = HelixToolkit.Nex.Sample.Application.Application;
using ApplicationConfig = HelixToolkit.Nex.Sample.Application.ApplicationConfig;
using EngineBuilder = HelixToolkit.Nex.Engine.EngineBuilder;
using Gui = ImGuiNET.ImGui;
using NexEngine = HelixToolkit.Nex.Engine.Engine;
using WorldDataProvider = HelixToolkit.Nex.Engine.WorldDataProvider;

namespace HelixToolkit.Nex.Sample.GltfImporter;

internal class GltfImporterApp : ApplicationBase
{
    private static readonly ILogger _logger = LogManager.Create<GltfImporterApp>();
    private const string ViewportTextureName = "ViewportTexture";

    // Engine infrastructure
    private IContext? _context;
    private NexEngine? _engine;
    private RenderContext? _renderContext;
    private WorldDataProvider? _worldDataProvider;
    private ImGuiRenderer? _imGuiRenderer;
    private Color4 _background = new(0.01f, 0.01f, 0.01f, 1);

    // Camera
    private Camera _camera = new PerspectiveCamera();
    private ICameraController? _cameraController;

    // UI panels
    private SelectionManager? _selectionManager;
    private SceneTreePanel? _sceneTreePanel;
    private PropertiesPanel? _propertiesPanel;
    private ViewportPanel? _viewportPanel;

    // Application state
    private Node? _currentModelRoot;
    private ResourceManifest? _currentResourceManifest;
    private Node? _mainRoot;
    private Node? _lightNode;
    private Size _viewportSize = new(1, 1);

    // Import diagnostics state (consumed by error/warning windows in task 9.1)
    private IReadOnlyList<ImportDiagnostic> _importDiagnostics = [];
    private bool _showErrorWindow;
    private bool _showWarningWindow;

    // ImGui swapchain rendering state
    private readonly Framebuffer _imGuiFramebuffer = new();
    private readonly RenderPass _imGuiPass = new();
    private readonly Dependencies _imGuiDeps = new();

    // Post effects
    private readonly BorderHighlightPostEffect _borderHighlight = new();

    // Layout constants
    private const float ScenePanelWidth = 250f;
    private const float PropertiesPanelWidth = 300f;

    // Viewport-relative mouse tracking for camera input (used as fallback pointer location)
    private Vector2 _pointerLocation;

    // Timing
    private long _lastTimestamp = 0;

    // Importer config
    private readonly ImporterConfig _importConfig = new()
    {
        DefaultShadingMode = Shaders.Frag.PBRShadingMode.CAD,
    };

    public override string Name => "glTF Importer";

    public GltfImporterApp()
        : base(
            new ApplicationConfig()
            {
                WindowResizable = true,
                WindowWidth = 1280,
                WindowHeight = 1080,
            }
        )
    { }

    protected override void Initialize()
    {
        base.Initialize();

        // Create Vulkan graphics context
        _context = VulkanBuilder.Create(
            new VulkanContextConfig
            {
                TerminateOnValidationError = true,
                ForceIntegratedGPU = false,
                OnCreateSurface = CreateSurface,
            },
            MainWindow.Instance,
            0
        );

        var windowSize = MainWindow.Size;
        _context.RecreateSwapchain(windowSize.Width, windowSize.Height);

        // Initialize camera
        _camera = new PerspectiveCamera()
        {
            Position = new Vector3(0, 2, -5),
            Target = new Vector3(0, 0, 0),
            FarPlane = 10000,
            NearPlane = 0.01f,
        };
        _cameraController = new OrbitCameraController(_camera);

        // Build engine (offscreen rendering with post-effects)
        _engine = EngineBuilder
            .Create(_context)
            .WithDefaultNodes(false)
            .WithFXAA()
            .WithToneMappingMode(Shaders.ToneMappingMode.Reinhard)
            .WithTransparent(Engine.TransparentMode.WBOIT)
            .RenderToCustomTarget(GraphicsSettings.IntermediateTargetFormat)
            .WithPostEffects(effects =>
            {
                effects.AddEffect(_borderHighlight);
            })
            .Build();

        // Create render context
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();

        // Create the offscreen render target texture for the 3D viewport
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

        // Create world data provider
        _worldDataProvider = _engine.CreateWorldDataProvider();
        _worldDataProvider.Initialize();

        _mainRoot = new Node(_worldDataProvider.World, "MainRoot");

        // Initialize ImGui renderer
        _imGuiRenderer = new ImGuiRenderer(_context, new ImGuiConfig());
        _imGuiRenderer.Initialize(_context.GetSwapchainFormat());

        // Configure the ImGui render pass for the swapchain
        _imGuiPass.Colors[0].ClearColor = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
        _imGuiPass.Colors[0].LoadOp = LoadOp.Clear;
        _imGuiPass.Colors[0].StoreOp = StoreOp.Store;

        // Initialize UI panels
        _selectionManager = new SelectionManager();
        _sceneTreePanel = new SceneTreePanel(_selectionManager);
        _propertiesPanel = new PropertiesPanel(
            _selectionManager,
            _worldDataProvider,
            _renderContext
        );
        _viewportPanel = new ViewportPanel(_selectionManager, _cameraController);

        SetupLighting();
    }

    private void SetupLighting()
    {
        if (_worldDataProvider is null)
            return;
        // Add a directional light to the scene
        _lightNode = new Node(_worldDataProvider.World, "DirectionalLight");
        _lightNode.Entity.Set(
            new DirectionalLightComponent() { Color = Color.White, Intensity = 1.0f }
        );
        _mainRoot?.AddChild(_lightNode);
    }

    protected override void HandleResize(int width, int height)
    {
        // Skip swapchain recreation when minimized (either dimension is 0)
        if (width <= 0 || height <= 0)
            return;

        _context?.RecreateSwapchain(width, height);
        base.HandleResize(width, height);
    }

    protected override void OnTick()
    {
        if (
            _engine is null
            || _renderContext is null
            || _imGuiRenderer is null
            || _context is null
            || _worldDataProvider is null
            || _cameraController is null
            || _viewportPanel is null
            || _sceneTreePanel is null
            || _propertiesPanel is null
        )
            return;

        // Timing
        if (_lastTimestamp == 0)
            _lastTimestamp = Stopwatch.GetTimestamp();
        float delta = (float)(Stopwatch.GetTimestamp() - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = Stopwatch.GetTimestamp();

        var windowSize = MainWindow.Size;
        float displayWidth = windowSize.Width / _imGuiRenderer.DisplayScale;
        float displayHeight = windowSize.Height / _imGuiRenderer.DisplayScale;

        // --- ImGui begin frame ---
        _imGuiRenderer.BeginFrame(new Vector2(windowSize.Width, windowSize.Height));

        // --- Draw menu bar ---
        if (Gui.BeginMainMenuBar())
        {
            if (Gui.BeginMenu("File"))
            {
                if (Gui.MenuItem("Open"))
                {
                    var result = Dialog.FileOpen("gltf,glb");
                    if (result.IsOk && !string.IsNullOrEmpty(result.Path))
                    {
                        LoadFile(result.Path);
                    }
                }
                Gui.EndMenu();
            }
            Gui.EndMainMenuBar();
        }

        // --- Layout calculation ---
        float menuBarHeight = Gui.GetFrameHeight();
        float panelY = menuBarHeight;
        float panelHeight = displayHeight - menuBarHeight;
        float viewportWidth = displayWidth - ScenePanelWidth - PropertiesPanelWidth;

        // --- Draw panels ---
        _sceneTreePanel.Draw(
            _currentModelRoot,
            new Vector2(0f, panelY),
            new Vector2(ScenePanelWidth, panelHeight)
        );

        _viewportPanel.Draw(
            _engine,
            _renderContext.FinalOutputTexture,
            new Vector2(ScenePanelWidth, panelY),
            new Vector2(viewportWidth, panelHeight),
            _renderContext,
            _worldDataProvider,
            _currentModelRoot
        );

        _propertiesPanel.Draw(
            new Vector2(ScenePanelWidth + viewportWidth, panelY),
            new Vector2(PropertiesPanelWidth, panelHeight)
        );

        // --- Update viewport size from panel content region ---
        var newViewportSize = _viewportPanel.ContentSize;
        if (
            newViewportSize.Width != _viewportSize.Width
            || newViewportSize.Height != _viewportSize.Height
        )
        {
            _viewportSize = newViewportSize;
        }

        // --- Import error modal window ---
        if (_showErrorWindow)
        {
            Gui.OpenPopup("Import Error");
        }

        if (Gui.BeginPopupModal("Import Error", ImGuiWindowFlags.AlwaysAutoResize))
        {
            Gui.TextUnformatted("The file could not be imported. The following issues were found:");
            Gui.Separator();

            foreach (var diagnostic in _importDiagnostics)
            {
                Gui.TextUnformatted($"[{diagnostic.Severity}] {diagnostic.Message}");
            }

            Gui.Separator();
            if (Gui.Button("Dismiss"))
            {
                _showErrorWindow = false;
                _importDiagnostics = [];
                Gui.CloseCurrentPopup();
            }

            Gui.EndPopup();
        }

        // --- Import warning window (non-modal) ---
        if (_showWarningWindow)
        {
            if (Gui.Begin("Import Warnings", ref _showWarningWindow))
            {
                Gui.TextUnformatted("The model was imported with the following warnings:");
                Gui.Separator();

                foreach (var diagnostic in _importDiagnostics)
                {
                    Gui.TextUnformatted($"[{diagnostic.Severity}] {diagnostic.Message}");
                }
            }
            Gui.End();
        }

        // --- ImGui end frame ---
        _imGuiRenderer.EndFrame();

        // --- Update camera controller ---
        _cameraController.Update(delta);

        // --- Update light direction to match camera (optional, for better default lighting) ---
        ref var dirLight = ref _lightNode!.Entity.Get<DirectionalLightComponent>();
        dirLight.Direction = Vector3.Normalize(_camera.LookDir);
        _lightNode.NotifyComponentChanged<DirectionalLightComponent>();

        // --- Update render context for the 3D viewport ---
        _renderContext.RenderParams.BackgroundColor = _background;
        _renderContext.Update(_viewportSize, _camera);
        _renderContext.SetPointer(_pointerLocation);

        // --- Step 1: Execute 3D render graph (offscreen) ---
        _engine.BeginFrame();
        var cmdBuf = _engine.RenderOffscreen(
            _renderContext,
            _worldDataProvider,
            ViewportTextureName
        );

        // --- Step 2: ImGui composite pass — renders to swapchain ---
        var swapchainTex = _context.GetCurrentSwapchainTexture();
        _imGuiFramebuffer.Colors[0].Texture = swapchainTex;
        _imGuiDeps.PushTexture(_renderContext.FinalOutputTexture);
        _imGuiRenderer.Render(cmdBuf, _imGuiPass, _imGuiFramebuffer, _imGuiDeps);

        // --- Submit & present ---
        _engine.Submit(cmdBuf, swapchainTex);
        _imGuiDeps.PopTexture();
    }

    // -------------------------------------------------------------------
    // Input handling — forward SDL events to ImGui IO
    // -------------------------------------------------------------------

    protected override void OnMouseMove(int x, int y, int xrel, int yrel)
    {
        if (_imGuiRenderer is null)
            return;

        var io = Gui.GetIO();
        io.AddMousePosEvent(x / _imGuiRenderer.DisplayScale, y / _imGuiRenderer.DisplayScale);

        // Track pointer location for render context (used by pointer ring effect)
        _pointerLocation = new Vector2(x, y);
    }

    protected override void OnMouseButtonDown(SDL_Button button)
    {
        base.OnMouseButtonDown(button);
        var io = Gui.GetIO();

        // Forward to ImGui — ViewportPanel handles camera/picking via ImGui internally
        switch (button)
        {
            case SDL_Button.Left:
                io.AddMouseButtonEvent(0, true);
                break;
            case SDL_Button.Right:
                io.AddMouseButtonEvent(1, true);
                break;
            case SDL_Button.Middle:
                io.AddMouseButtonEvent(2, true);
                break;
        }
    }

    protected override void OnMouseButtonUp(SDL_Button button)
    {
        var io = Gui.GetIO();

        // Forward to ImGui — ViewportPanel handles release via ImGui internally
        switch (button)
        {
            case SDL_Button.Left:
                io.AddMouseButtonEvent(0, false);
                break;
            case SDL_Button.Right:
                io.AddMouseButtonEvent(1, false);
                break;
            case SDL_Button.Middle:
                io.AddMouseButtonEvent(2, false);
                break;
        }
    }

    protected override void OnMouseWheel(int deltaX, int deltaY)
    {
        var io = Gui.GetIO();
        io.AddMouseWheelEvent(deltaX, deltaY);
    }

    protected override void OnKeyDown(SDL_Scancode scancode, bool repeat)
    {
        var io = Gui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), true);

        // Don't forward to camera handlers if ImGui wants the keyboard
        if (io.WantCaptureKeyboard)
            return;
    }

    protected override void OnKeyUp(SDL_Scancode scancode)
    {
        var io = Gui.GetIO();
        io.AddKeyEvent(SdlScancodeToImGuiKey(scancode), false);

        // Always clear key state even when ImGui captures keyboard (prevent stuck keys)
        // Future tasks will add keyboard shortcut state clearing here
    }

    protected override void OnDisplayScaleChanged(float scale)
    {
        if (_imGuiRenderer != null && scale != 0)
        {
            _imGuiRenderer.DisplayScale = scale;
        }
        base.OnDisplayScaleChanged(scale);
    }

    // -------------------------------------------------------------------
    // File loading and scene management
    // -------------------------------------------------------------------

    /// <summary>
    /// Loads a glTF/GLB file, attaches the result to the scene, and frames the camera.
    /// On failure, stores diagnostics for display without modifying the scene.
    /// </summary>
    private void LoadFile(string filePath)
    {
        if (_worldDataProvider is null || _cameraController is null)
            return;

        var importer = new Importer();
        var result = importer.Import(filePath, _worldDataProvider, _importConfig);

        if (!result.Success)
        {
            // Import failed — store diagnostics for error window display (task 9.1)
            _importDiagnostics = result.Diagnostics;
            _showErrorWindow = true;
            _showWarningWindow = false;
            _logger.LogError(
                "Failed to import {FilePath}: {Count} diagnostic(s)",
                filePath,
                result.Diagnostics.Count
            );
            return;
        }

        // Success — deselect current entity and dispose previous model before attaching new one
        _selectionManager?.Deselect();
        DisposeCurrentModel();
        _currentModelRoot = result.RootNode;
        _currentResourceManifest = result.Resources;
        _mainRoot?.AddChild(_currentModelRoot!);
        _worldDataProvider.World.SortSceneNodes();
        _worldDataProvider.World.UpdateTransforms();
        // Compute bounding volume and frame the camera
        ComputeBoundsAndFrameCamera(_currentModelRoot!);

        // Handle warnings
        if (result.HasWarnings)
        {
            _importDiagnostics = result.Diagnostics;
            _showWarningWindow = true;
            _showErrorWindow = false;
            _logger.LogWarning(
                "Imported {FilePath} with {Count} warning(s)",
                filePath,
                result.Diagnostics.Count
            );
        }
        else
        {
            _importDiagnostics = [];
            _showWarningWindow = false;
            _showErrorWindow = false;
        }
    }

    /// <summary>
    /// Disposes the currently loaded model's root node and all its children,
    /// releasing ECS entities and GPU resources tracked by the import manifest.
    /// </summary>
    private void DisposeCurrentModel()
    {
        if (_currentModelRoot is null)
            return;
        _mainRoot?.RemoveChild(_currentModelRoot);
        _currentModelRoot.Dispose();
        _currentModelRoot = null;

        _currentResourceManifest?.DisposeAll();
        _currentResourceManifest = null;
    }

    /// <summary>
    /// Traverses the scene graph to compute an axis-aligned bounding box,
    /// then focuses the orbit camera on the bounding center at an appropriate distance.
    /// </summary>
    private void ComputeBoundsAndFrameCamera(Node rootNode)
    {
        var bound = rootNode.GetMeshBound();

        var center = (bound.Minimum + bound.Maximum) * 0.5f;
        //rootNode.Transform.Translation = -center;
        var extents = bound.Maximum - bound.Minimum;
        float radius = extents.Length() * 0.5f;

        // Set orbit distance to frame the model (use 2x radius for comfortable framing)
        float distance = Math.Max(radius * 2f, 1f);
        _cameraController!.FocusOn(center, distance);
    }

    // -------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------

    protected override void OnDisposing()
    {
        _selectionManager?.Deselect();
        _currentResourceManifest?.DisposeAll();
        _currentResourceManifest = null;
        _mainRoot?.Dispose();
        _imGuiRenderer?.Dispose();
        _worldDataProvider?.Dispose();
        _renderContext?.Teardown();
        _engine?.Dispose();
        _context?.Dispose();
        base.OnDisposing();
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static ImGuiKey SdlScancodeToImGuiKey(SDL_Scancode scancode)
    {
        return scancode switch
        {
            SDL_Scancode.W => ImGuiKey.W,
            SDL_Scancode.A => ImGuiKey.A,
            SDL_Scancode.S => ImGuiKey.S,
            SDL_Scancode.D => ImGuiKey.D,
            SDL_Scancode.Space => ImGuiKey.Space,
            SDL_Scancode.LeftControl => ImGuiKey.LeftCtrl,
            SDL_Scancode.RightControl => ImGuiKey.RightCtrl,
            SDL_Scancode.LeftShift => ImGuiKey.LeftShift,
            SDL_Scancode.RightShift => ImGuiKey.RightShift,
            SDL_Scancode.Escape => ImGuiKey.Escape,
            SDL_Scancode.Tab => ImGuiKey.Tab,
            SDL_Scancode.Return => ImGuiKey.Enter,
            SDL_Scancode.Backspace => ImGuiKey.Backspace,
            SDL_Scancode.Delete => ImGuiKey.Delete,
            SDL_Scancode.Left => ImGuiKey.LeftArrow,
            SDL_Scancode.Right => ImGuiKey.RightArrow,
            SDL_Scancode.Up => ImGuiKey.UpArrow,
            SDL_Scancode.Down => ImGuiKey.DownArrow,
            _ => ImGuiKey.None,
        };
    }
}
