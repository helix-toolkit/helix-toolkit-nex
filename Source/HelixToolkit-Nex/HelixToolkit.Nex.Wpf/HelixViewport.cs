using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Microsoft.Extensions.Logging;
using Vortice.Direct3D9;
using Vortice.Vulkan;
using Rect = System.Windows.Rect;

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

    private D3DImage? _d3dImage;
    private D3D9DeviceManager? _d3d9Manager;
    private D3D11DeviceManager? _d3d11Manager;
    private IDirect3DTexture9? _d3d9BackBuffer;
    private IDirect3DSurface9? _d3d9Surface;
    private nint _d3d9SharedHandle;
    private SharedTextureResult? _sharedTexture;
    private ImportedVulkanTexture? _importedTexture;
    private TimeSpan _lastRenderTime;
    private long _lastTimestamp;
    private bool _disposed;
    private bool _sizeChanged = true;

    /// <summary>
    /// Raised each frame after <see cref="IViewportClient.Update"/> but before rendering.
    /// This is a <b>read-only notification</b>; use <see cref="ViewportClient"/> to
    /// provide the camera and scene data.
    /// </summary>
    public event EventHandler<ViewportRenderingEventArgs>? BeforeRender;

    public HelixViewport()
    {
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
        if (_d3dImage is { PixelWidth: > 0, PixelHeight: > 0 })
        {
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
        ReleaseResources();
        Disposer.DisposeAndRemove(ref _renderContext);
        _engine = engine;
        if (_engine == null)
        {
            return;
        }
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderArgs = new ViewportRenderingEventArgs(_renderContext);
        if (IsLoaded && Width > 0 && Height > 0)
        {
            CreateResources((uint)Width, (uint)Height);
        }
    }

    private void SetClient(IViewportClient? client)
    {
        _viewportClient = client;
    }

    private void SetCameraController(ICameraController? controller)
    {
        if (_renderContext is null)
            return;
        _cameraController = controller;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        // 1. D3D9 device for D3DImage back buffer
        _d3d9Manager = new D3D9DeviceManager();

        // 2. D3D11 device for shared texture interop
        _d3d11Manager = new D3D11DeviceManager();

        // 3. Create shared resources at the current control size
        var width = (uint)ActualWidth;
        var height = (uint)ActualHeight;
        if (width > 0 && height > 0)
        {
            CreateResources(width, height);
        }
    }

    private void CreateResources(uint width, uint height)
    {
        if (_engine is null)
        {
            return;
        }
        if (_d3d9Manager is null)
        {
            return;
        }
        _logger.LogInformation(
            "Creating resources for viewport with size {Width}x{Height}",
            width,
            height
        );
        var context = Engine!.Context;

        // 1. D3D9 shared back buffer (X8R8G8B8)
        _d3d9BackBuffer = _d3d9Manager!.Device.CreateTexture(
            width,
            height,
            1u,
            Usage.RenderTarget,
            Vortice.Direct3D9.Format.X8R8G8B8,
            Pool.Default,
            ref _d3d9SharedHandle
        );

        // 2. Surface level 0 for D3DImage.SetBackBuffer
        _d3d9Surface = _d3d9BackBuffer.GetSurfaceLevel(0u);

        // 3. Open on D3D11 and get KMT handle
        _sharedTexture = SharedTextureFactory.CreateForWpf(_d3d11Manager!, _d3d9SharedHandle);

        // 4. Import into Vulkan as B8G8R8A8Unorm
        _importedTexture = VulkanExternalMemoryImporter.Import(
            context,
            _sharedTexture.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT,
            VkFormat.B8G8R8A8Unorm,
            width,
            height
        );

        // 5. Per-viewport render context

        _renderContext!.FinalOutputTexture = _importedTexture.Handle;

        // 6. Subscribe to the WPF render loop
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_disposed || Engine is null || _renderContext is null || _renderArgs is null)
            return;

        if (_d3dImage is null || !_d3dImage.IsFrontBufferAvailable)
            return;
        if (ActualWidth == 0 || ActualHeight == 0)
            return;
        // Avoid duplicate frames within the same WPF render tick
        var args = (RenderingEventArgs)e;
        if (_lastRenderTime == args.RenderingTime)
            return;

        // Compute delta time
        if (!Render((float)ActualWidth, (float)ActualHeight))
            return;

        //// Present through D3DImage
        _d3dImage.Lock();
        _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, (nint)_d3d9Surface);
        _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
        _d3dImage.Unlock();

        _lastRenderTime = args.RenderingTime;
        InvalidateVisual();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _sizeChanged = true;
    }

    private void EnsureSize()
    {
        if (!_sizeChanged || ActualWidth == 0 || ActualHeight == 0)
            return;

        if (_disposed || Engine is null)
            return;
        Engine.Context.Wait(default);
        ReleaseResources();
        CreateResources((uint)ActualWidth, (uint)ActualHeight);
        UpdateViewportSize((float)ActualWidth, (float)ActualHeight);
        _sizeChanged = false;
    }

    private void ReleaseResources()
    {
        _logger.LogInformation("Releasing viewport resources");
        CompositionTarget.Rendering -= OnCompositionRendering;
        _d3dImage?.Lock();
        _d3dImage?.SetBackBuffer(D3DResourceType.IDirect3DSurface9, 0);
        _d3dImage?.Unlock();
        if (Engine is not null)
            Engine.Context.Wait(default);
        Disposer.DisposeAndRemove(ref _importedTexture);

        Disposer.DisposeAndRemove(ref _sharedTexture);

        Disposer.DisposeAndRemove(ref _d3d9Surface);

        Disposer.DisposeAndRemove(ref _d3d9BackBuffer);

        _d3d9SharedHandle = 0;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        Dispose();
    }

    #region Mouse event forwarding to camera controller

    private static ViewportMouseButton ToViewportButton(System.Windows.Input.MouseButton button) => button switch
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
        if (_disposed)
            return;
        _disposed = true;

        ReleaseResources();
        Disposer.DisposeAndRemove(ref _renderContext);
        _d3d11Manager?.Dispose();
        _d3d11Manager = null;

        _d3d9Manager?.Dispose();
        _d3d9Manager = null;

        GC.SuppressFinalize(this);
    }
}
