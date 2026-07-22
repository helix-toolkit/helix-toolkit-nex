using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vortice.DXGI;
using WinRT;
using WinRT.Interop;
using NativeDxgiSwapChain = Windows.Win32.Graphics.Dxgi.IDXGISwapChain;
using NativeSwapChainPanel = Windows.Win32.System.WinRT.Xaml.ISwapChainPanelNative;
using Size = HelixToolkit.Nex.Maths.Size;

namespace HelixToolkit.Nex.WinUI;

/// <summary>
/// WinUI 3 control that hosts the HelixToolkit.Nex 3D engine output.
/// Uses <see cref="SwapChainPanel"/> with a DXGI swap chain for composition
/// and keyed mutex synchronization for Vulkan-to-D3D11 interop.
/// <para>
/// The engine is provided externally via <see cref="Engine"/> so that multiple viewports
/// can share a single engine instance. Each viewport creates its own
/// <see cref="_renderContext"/>. Assign a <see cref="ViewportClient"/> to supply camera
/// and scene data each frame. The optional <see cref="BeforeRender"/> event is raised
/// as a read-only notification after the client update.
/// </para>
/// </summary>
public partial class HelixViewport : UserControl, IDisposable
{
    /// <summary>
    /// The WinUI 3 IID from <c>microsoft.ui.xaml.media.dxinterop.h</c>.
    /// </summary>
    private static readonly Guid _swapChainPanelNativeIid = new(
        "63aad0b8-7c24-40ff-85a8-640d944cc325"
    );
    private static readonly ILogger _logger = LogManager.Create<HelixViewport>();

    private SwapChainPanel? _swapChainPanel;
    private D3D11DeviceManager? _d3d11Manager;
    private IDXGIDevice3? _dxgiDevice;
    private IDXGIAdapter? _dxgiAdapter;
    private IDXGIFactory2? _dxgiFactory;
    private long _lastTimestamp;
    private bool _sizeChanged = true;
    private KeyedMutexSyncInfo _vulkanSyncInfo;
    private readonly ViewportLifecycle<ViewportSession> _lifecycle;

    public HelixViewport()
    {
        _lifecycle = new ViewportLifecycle<ViewportSession>(
            ActivateSession,
            DeactivateSession,
            DisposeTerminalResources
        );
        _swapChainPanel = new SwapChainPanel();
        Content = _swapChainPanel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        _swapChainPanel.PointerPressed += OnPointerPressed;
        _swapChainPanel.PointerReleased += OnPointerReleased;
        _swapChainPanel.PointerMoved += OnPointerMoved;
        _swapChainPanel.PointerExited += OnPointerExited;
        _swapChainPanel.PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastTimestamp = 0;
        _lifecycle.Load(CreateResources);
        _sizeChanged = _lifecycle.Session is null;
    }

    /// <summary>
    /// Lazily creates the D3D11 and DXGI resources retained across unload.
    /// </summary>
    private void EnsureDeviceResources()
    {
        if (_d3d11Manager is not null)
        {
            return;
        }

        _d3d11Manager = new D3D11DeviceManager();
        _dxgiDevice = _d3d11Manager.Device.QueryInterface<IDXGIDevice3>();
        _dxgiDevice.GetAdapter(out _dxgiAdapter).CheckError();
        _dxgiFactory = _dxgiAdapter.GetParent<IDXGIFactory2>();
    }

    private void SetEngine(Engine.Engine? engine)
    {
        if (_engine == engine)
        {
            return;
        }

        if (_lifecycle.State == ViewportLifecycleState.Loaded)
        {
            _lifecycle.Replace(() =>
            {
                SetEngineCore(engine);
                return CreateResources();
            });
            return;
        }

        SetEngineCore(engine);
    }

    /// <summary>
    /// Applies the original render-context replacement sequence.
    /// </summary>
    private void SetEngineCore(Engine.Engine? engine)
    {
        Disposer.DisposeAndRemove(ref _renderContext);
        _renderArgs = null;
        _engine = engine;
        if (_engine is null)
        {
            return;
        }
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderContext.RenderParams.EnableGammaCorrection = true; // Must enable gamma correction.
        _renderArgs = new(_renderContext);
    }

    private void SetClient(IViewportClient? client)
    {
        _viewportClient = client;
    }

    /// <summary>
    /// Creates a session at the current rendered size when one is needed.
    /// </summary>
    private ViewportSession? CreateResources()
    {
        EnsureDeviceResources();
        var width = (uint)ActualWidth;
        var height = (uint)ActualHeight;
        if (width == 0 || height == 0)
        {
            return null;
        }

        return CreateResources(width, height);
    }

    /// <summary>
    /// Creates the loaded-surface resources for a non-zero size.
    /// </summary>
    private ViewportSession? CreateResources(uint width, uint height)
    {
        if (
            _engine is null
            || _renderContext is null
            || _d3d11Manager is null
            || _dxgiFactory is null
            || _dxgiDevice is null
        )
        {
            return null;
        }

        _logger.LogInformation(
            "Creating resources for HelixViewport with size {Width}x{Height}.",
            width,
            height
        );

        var session = ViewportSession.Create(
            _engine.Context,
            _d3d11Manager,
            _dxgiFactory,
            _dxgiDevice,
            width,
            height
        );
        try
        {
            _renderContext.WindowSize = new Size((int)width, (int)height);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (
            _lifecycle.State != ViewportLifecycleState.Loaded
            || _d3d11Manager is null
            || _renderArgs is null
        )
            return;

        EnsureSize();
        var session = _lifecycle.Session;
        if (
            session is null
            || !Render(
                (float)ActualWidth,
                (float)ActualHeight,
                session.ImportedTexture.Handle
            )
        )
        {
            return;
        }

        // Keyed mutex acquire → copy → release → present
        var copySyncInfo = session.CopySyncInfo;
        session.KeyedMutex.AcquireSync(copySyncInfo.AcquireKey, (int)copySyncInfo.Timeout);
        _d3d11Manager.DeviceContext.CopyResource(
            session.BackBufferResource,
            session.RenderTargetResource
        );
        session.KeyedMutex.ReleaseSync(copySyncInfo.ReleaseKey);
        session.SwapChain.Present(0u, 0u);
    }

    private void EnsureSize()
    {
        if (!_sizeChanged || ActualWidth == 0 || ActualHeight == 0)
            return;

        if (_lifecycle.State != ViewportLifecycleState.Loaded || Engine is null)
            return;

        Engine.WaitForIdle();
        _lifecycle.Replace(() =>
            CreateResources((uint)ActualWidth, (uint)ActualHeight)
        );
        UpdateViewportSize((float)ActualWidth, (float)ActualHeight);
        _sizeChanged = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lifecycle.State != ViewportLifecycleState.Loaded || Engine is null)
            return;

        _sizeChanged = true;

        // The session won't be created when the Loaded event fires if ActualWidth or ActualHeight is 0.
        // Create it when the size changes and both ActualWidth and ActualHeight are greater than 0.
        if (_lifecycle.Session is null && ActualWidth > 0 && ActualHeight > 0)
        {
            EnsureSize();
        }
    }

    /// <summary>
    /// Connects a prepared loaded-surface session to the WinUI render path.
    /// </summary>
    private void ActivateSession(ViewportSession? session)
    {
        if (session is null)
        {
            return;
        }

        SetSwapChainOnPanel(_swapChainPanel!, session.SwapChain);
        _vulkanSyncInfo = session.VulkanSyncInfo;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    /// <summary>
    /// Disconnects the current session before the lifecycle releases it.
    /// </summary>
    private void DeactivateSession()
    {
        _logger.LogInformation("Releasing resources for HelixViewport.");
        var failures = new List<Exception>();
        CompositionTarget.Rendering -= OnCompositionRendering;

        if (_swapChainPanel is not null)
        {
            Try(_swapChainPanel.ReleasePointerCaptures, failures);
        }
        _activeDrag = ActiveDragAction.None;
        ResetPointerLocation();

        if (_engine is not null)
        {
            Try(() => _engine.Context.Wait(default), failures);
        }

        // Detach the swap chain before releasing it because SwapChainPanel retains
        // its own COM reference.
        if (_swapChainPanel is not null && _lifecycle.Session is not null)
        {
            Try(() => SetSwapChainOnPanel(_swapChainPanel, null), failures);
        }

        _vulkanSyncInfo = default;
        _lastTimestamp = 0;
        _sizeChanged = true;

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "The viewport could not be deactivated completely.",
                failures
            );
        }
    }

    /// <summary>
    /// Sets the native DXGI swap chain used by a WinUI swap-chain panel.
    /// </summary>
    private static unsafe void SetSwapChainOnPanel(
        SwapChainPanel panel,
        IDXGISwapChain1? swapchain)
    {
        using var panelNative =
            MarshalInspectable<SwapChainPanel>.CreateMarshaler<IUnknownVftbl>(
                panel,
                _swapChainPanelNativeIid);

        var nativePanel = (NativeSwapChainPanel*)panelNative.ThisPtr;

        NativeDxgiSwapChain* nativeSwapchain = null;
        if (swapchain is not null)
        {
            nativeSwapchain =
                (NativeDxgiSwapChain*)swapchain.NativePointer;
        }

        nativePanel->SetSwapChain(nativeSwapchain);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifecycle.Unload();
    }

    #region Pointer event forwarding to camera controller

    private static ViewportMouseButton ToViewportButton(PointerPointProperties props)
    {
        if (props.IsLeftButtonPressed)
            return ViewportMouseButton.Left;
        if (props.IsMiddleButtonPressed)
            return ViewportMouseButton.Middle;
        if (props.IsRightButtonPressed)
            return ViewportMouseButton.Right;
        return ViewportMouseButton.None;
    }

    /// <summary>
    /// Determines which button was just released by comparing the current state
    /// (where the released button is no longer reported as pressed) against the
    /// active drag action.
    /// </summary>
    private ViewportMouseButton InferReleasedButton(PointerPointProperties props)
    {
        // During a release event the released button is NOT reported as pressed.
        // Match against the action that started the drag.
        if (_activeDrag == ActiveDragAction.Rotate && !IsButtonPressed(props, RotateMouseButton))
            return RotateMouseButton;
        if (_activeDrag == ActiveDragAction.Pan && !IsButtonPressed(props, PanMouseButton))
            return PanMouseButton;
        return ViewportMouseButton.None;
    }

    private static bool IsButtonPressed(PointerPointProperties props, ViewportMouseButton button) =>
        button switch
        {
            ViewportMouseButton.Left => props.IsLeftButtonPressed,
            ViewportMouseButton.Middle => props.IsMiddleButtonPressed,
            ViewportMouseButton.Right => props.IsRightButtonPressed,
            _ => false,
        };

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_swapChainPanel);
        var button = ToViewportButton(point.Properties);
        HandlePointerPressed(button, (float)point.Position.X, (float)point.Position.Y);
        if (_activeDrag != ActiveDragAction.None)
        {
            _swapChainPanel?.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_swapChainPanel);
        var button = InferReleasedButton(point.Properties);
        HandlePointerReleased(button);
        if (_activeDrag == ActiveDragAction.None)
        {
            _swapChainPanel?.ReleasePointerCapture(e.Pointer);
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_swapChainPanel);
        HandlePointerMoved((float)point.Position.X, (float)point.Position.Y);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ResetPointerLocation();
        if (ActiveDrag)
        {
            return;
        }
        HandlePointerExited();
        _swapChainPanel?.ReleasePointerCaptures();
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_swapChainPanel);
        // WinUI reports 120 units per notch, normalise to ±1.
        HandleMouseWheel(point.Properties.MouseWheelDelta / 120f);
        e.Handled = true;
    }

    #endregion

    public void Dispose()
    {
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources retained across unload without disposing the external engine.
    /// </summary>
    private void DisposeTerminalResources()
    {
        var failures = new List<Exception>();
        TryDispose(ref _renderContext, failures);
        _renderArgs = null;

        // We do NOT dispose Engine — it is externally owned
        TryDispose(ref _dxgiFactory, failures);
        TryDispose(ref _dxgiAdapter, failures);
        TryDispose(ref _dxgiDevice, failures);
        TryDispose(ref _d3d11Manager, failures);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more viewport resources could not be released.",
                failures
            );
        }
    }

    /// <summary>
    /// Attempts one cleanup action and records its failure.
    /// </summary>
    private static void Try(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    /// <summary>
    /// Attempts one terminal resource disposal and always drops the owned reference.
    /// </summary>
    private static void TryDispose<T>(ref T? resource, List<Exception> failures)
        where T : class, IDisposable
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        finally
        {
            resource = null;
        }
    }
}
