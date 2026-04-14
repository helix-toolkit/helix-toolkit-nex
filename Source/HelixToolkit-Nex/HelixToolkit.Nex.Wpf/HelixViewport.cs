using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D9;
using Vortice.Vulkan;
using static Silk.NET.Core.Native.SilkMarshal;
using Rect = System.Windows.Rect;
using Size = HelixToolkit.Nex.Maths.Size;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// WPF control that hosts the HelixToolkit.Nex 3D engine output.
/// Uses D3DImage with a D3D9 back buffer surface shared into Vulkan
/// via VK_KHR_external_memory_win32.
/// <para>
/// The engine is provided externally via <see cref="Engine"/> so that multiple viewports
/// can share a single engine instance. Each viewport creates its own
/// <see cref="_renderContext"/>. Subscribe to <see cref="Rendering"/> to set the camera,
/// provide a <see cref="WorldDataProvider"/>, and perform per-frame scene updates.
/// </para>
/// </summary>
public unsafe class HelixViewport : FrameworkElement, IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<HelixViewport>();
    #region Dependency Properties
    public static readonly DependencyProperty EngineDp = HelixProperty.Register<
        HelixViewport,
        Engine.Engine?
    >(
        "Engine",
        null,
        (d, e) =>
        {
            if (d is not HelixViewport viewport)
            {
                return;
            }
            viewport.SetEngine((Engine.Engine)e.NewValue);
        }
    );
    public Engine.Engine? Engine
    {
        get { return (Engine.Engine)GetValue(EngineDp); }
        set { SetValue(EngineDp, value); }
    }
    #endregion
    private D3DImage? _d3dImage;
    private D3D9DeviceManager? _d3d9Manager;
    private D3D11DeviceManager? _d3d11Manager;
    private ComPtr<IDirect3DTexture9> _d3d9BackBuffer;
    private ComPtr<IDirect3DSurface9> _d3d9Surface;
    private nint _d3d9SharedHandle;
    private SharedTextureResult? _sharedTexture;
    private ImportedVulkanTexture? _importedTexture;
    private TimeSpan _lastRenderTime;
    private long _lastTimestamp;
    private bool _disposed;
    private Engine.Engine? _engine;
    private bool _sizeChanged = true;

    private ViewportRenderingEventArgs? _renderArgs;

    /// <summary>Per-viewport render context (window size, camera, final output texture).</summary>
    private RenderContext? _renderContext;

    /// <summary>
    /// Raised each frame before rendering. Subscribers should set the camera on
    /// <see cref="ViewportRenderingEventArgs.RenderContext"/> and provide a
    /// <see cref="ViewportRenderingEventArgs.WorldDataProvider"/>.
    /// If no WorldDataProvider is set, the frame is skipped.
    /// </summary>
    public event EventHandler<ViewportRenderingEventArgs>? Rendering;

    public HelixViewport()
    {
        _d3dImage = new D3DImage();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
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

    private void SetEngine(Engine.Engine engine)
    {
        if (_engine == engine)
        {
            return;
        }
        ReleaseResources();
        _engine = engine;
        if (IsLoaded && Width > 0 && Height > 0)
        {
            CreateResources((uint)Width, (uint)Height);
        }
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
        void* d3d9SharedPtr = null;
        ThrowHResult(
            _d3d9Manager!.Device.CreateTexture(
                width,
                height,
                1u,
                D3D9.UsageRendertarget,
                Silk.NET.Direct3D9.Format.X8R8G8B8,
                Pool.Default,
                ref _d3d9BackBuffer,
                ref d3d9SharedPtr
            )
        );
        _d3d9SharedHandle = (nint)d3d9SharedPtr;

        // 2. Surface level 0 for D3DImage.SetBackBuffer
        ThrowHResult(_d3d9BackBuffer.GetSurfaceLevel(0u, ref _d3d9Surface));

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
        if (_renderContext is null)
        {
            _renderContext = Engine.CreateRenderContext();
            _renderContext.Initialize();
            _renderArgs = new ViewportRenderingEventArgs(_renderContext);
        }
        _renderContext.FinalOutputTexture = _importedTexture.Handle;

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
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float delta =
            _lastTimestamp == 0
                ? 0f
                : (float)(now - _lastTimestamp) / System.Diagnostics.Stopwatch.Frequency;
        _lastTimestamp = now;

        // Let the subscriber set camera, world data provider, and do per-frame updates
        _renderArgs.DeltaTime = delta;
        _renderContext.WindowSize = new Size((int)ActualWidth, (int)ActualHeight);

        Rendering?.Invoke(this, _renderArgs);

        if (_renderArgs.WorldDataProvider is null)
            return;

        EnsureSize();

        var context = Engine.Context;

        // Render offscreen
        var cmdBuf = Engine.RenderOffscreen(_renderContext, _renderArgs.WorldDataProvider);
        var submitHandle = context.Submit(cmdBuf, TextureHandle.Null);
        context.Wait(submitHandle);

        //// Present through D3DImage
        _d3dImage.Lock();
        _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, (nint)_d3d9Surface.Handle);
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
        // Dispose per-viewport render context (we own it)
        Disposer.DisposeAndRemove(ref _renderContext);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseResources();

        _d3d11Manager?.Dispose();
        _d3d11Manager = null;

        _d3d9Manager?.Dispose();
        _d3d9Manager = null;

        GC.SuppressFinalize(this);
    }
}
