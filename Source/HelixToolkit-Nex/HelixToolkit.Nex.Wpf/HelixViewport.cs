using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Microsoft.Extensions.Logging;
using Rect = System.Windows.Rect;
using Size = HelixToolkit.Nex.Maths.Size;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// WPF control that hosts the HelixToolkit.Nex 3D engine output.
/// Uses D3DImage with a D3D9 back buffer surface shared into Vulkan
/// via VK_KHR_external_memory_win32.
/// <para>
/// The engine is provided externally via <see cref="Engine"/> so that multiple viewports
/// can share a single engine instance. Each viewport creates its own
/// <see cref="RenderContext"/>. Assign a <see cref="ViewportClient"/> to supply camera
/// and scene data each frame. The optional <see cref="BeforeRender"/> event is raised
/// as a read-only notification after the client update.
/// </para>
/// </summary>
public partial class HelixViewport : FrameworkElement, IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<HelixViewport>();

    private readonly D3DImage _d3dImage;
    private readonly ViewportLifecycle<ViewportSession> _lifecycle;
    private D3D9DeviceManager? _d3d9Manager;
    private D3D11DeviceManager? _d3d11Manager;
    private TimeSpan _lastRenderTime;
    private long _lastTimestamp;
    private bool _sizeChanged = true;

    public HelixViewport()
    {
        _lifecycle = new ViewportLifecycle<ViewportSession>(
            ActivateSession,
            DeactivateSession,
            DisposeTerminalResources
        );
        _d3dImage = new D3DImage();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseWheel += OnMouseWheel;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (
            _lifecycle.State == ViewportLifecycleState.Loaded
            && _lifecycle.Session is not null
            && Engine is not null
            && _d3dImage is { PixelWidth: > 0, PixelHeight: > 0 }
        )
        {
            Engine.WaitForIdle();
            _d3dImage.Lock();
            _d3dImage.AddDirtyRect(
                new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight)
            );
            _d3dImage.Unlock();
            drawingContext.DrawImage(
                _d3dImage,
                new Rect(new System.Windows.Size(ActualWidth, ActualHeight))
            );
        }
        base.OnRender(drawingContext);
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
        _renderArgs = new ViewportRenderingEventArgs(_renderContext);
    }

    private void SetClient(IViewportClient? client)
    {
        _viewportClient = client;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        _lastTimestamp = 0;
        _lastRenderTime = default;
        _lifecycle.Load(CreateResources);
        _sizeChanged = _lifecycle.Session is null;
    }

    /// <summary>
    /// Lazily creates the D3D9 and D3D11 devices retained across unload.
    /// </summary>
    private void EnsureDeviceResources()
    {
        if (_d3d9Manager is not null && _d3d11Manager is not null)
        {
            return;
        }

        try
        {
            // 1. D3D9 device for the D3DImage back buffer
            _d3d9Manager ??= new D3D9DeviceManager();

            // 2. D3D11 device for shared texture interop
            _d3d11Manager ??= new D3D11DeviceManager();
        }
        catch (Exception error)
        {
            var cleanupFailures = new List<Exception>();
            TryDispose(ref _d3d11Manager, cleanupFailures);
            TryDispose(ref _d3d9Manager, cleanupFailures);
            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, error);
                throw new AggregateException(
                    "Device resource creation and cleanup both failed.",
                    cleanupFailures
                );
            }

            throw;
        }
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
            || _d3d9Manager is null
            || _d3d11Manager is null
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
            _d3d9Manager,
            _d3d11Manager,
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

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_lifecycle.State != ViewportLifecycleState.Loaded || _renderArgs is null)
            return;

        if (!_d3dImage.IsFrontBufferAvailable)
            return;
        if (ActualWidth == 0 || ActualHeight == 0)
            return;
        // Avoid duplicate frames within the same WPF render tick
        var args = (RenderingEventArgs)e;
        if (_lastRenderTime == args.RenderingTime)
            return;

        EnsureSize();
        var session = _lifecycle.Session;
        if (
            session is null
            || !Render((float)ActualWidth, (float)ActualHeight, session.ImportedTexture.Handle)
        )
            return;

        _lastRenderTime = args.RenderingTime;
        InvalidateVisual();
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

    private void EnsureSize()
    {
        if (!_sizeChanged || ActualWidth == 0 || ActualHeight == 0)
            return;

        if (_lifecycle.State != ViewportLifecycleState.Loaded || Engine is null)
            return;

        Engine.Context.Wait(default);
        _lifecycle.Replace(() => CreateResources((uint)ActualWidth, (uint)ActualHeight));
        UpdateViewportSize((float)ActualWidth, (float)ActualHeight);
        _sizeChanged = false;
    }

    /// <summary>
    /// Connects a prepared loaded-surface session to the WPF render path.
    /// </summary>
    private void ActivateSession(ViewportSession? session)
    {
        if (session is null)
        {
            return;
        }

        SetBackBuffer(session.SurfacePointer);
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

        Try(ReleaseMouseCapture, failures);
        _activeDrag = ActiveDragAction.None;
        ResetPointerLocation();

        if (_engine is not null)
        {
            Try(() => _engine.Context.Wait(default), failures);
        }

        // Detach the back buffer before releasing the surface because D3DImage retains
        // its own COM reference.
        if (_lifecycle.Session is not null)
        {
            Try(() => SetBackBuffer(nint.Zero), failures);
        }

        _lastTimestamp = 0;
        _lastRenderTime = default;
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
    /// Points the D3DImage at a D3D9 surface, or detaches it when the pointer is zero.
    /// </summary>
    private void SetBackBuffer(nint surface)
    {
        _d3dImage.Lock();
        try
        {
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface);
        }
        finally
        {
            _d3dImage.Unlock();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        _lifecycle.Unload();
    }

    #region Mouse event forwarding to camera controller

    private static ViewportMouseButton ToViewportButton(System.Windows.Input.MouseButton button) =>
        button switch
        {
            System.Windows.Input.MouseButton.Left => ViewportMouseButton.Left,
            System.Windows.Input.MouseButton.Middle => ViewportMouseButton.Middle,
            System.Windows.Input.MouseButton.Right => ViewportMouseButton.Right,
            _ => ViewportMouseButton.None,
        };

    private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        HandlePointerPressed(ToViewportButton(e.ChangedButton), (float)pos.X, (float)pos.Y);
        if (_activeDrag != ActiveDragAction.None)
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandlePointerReleased(ToViewportButton(e.ChangedButton));
        if (_activeDrag == ActiveDragAction.None)
        {
            ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        HandlePointerMoved((float)pos.X, (float)pos.Y);
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ResetPointerLocation();
        if (ActiveDrag)
        {
            return;
        }
        HandlePointerExited();
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // WPF reports 120 units per notch; normalise to ±1.
        HandleMouseWheel(e.Delta / 120f);
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
        TryDispose(ref _d3d11Manager, failures);
        TryDispose(ref _d3d9Manager, failures);

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
