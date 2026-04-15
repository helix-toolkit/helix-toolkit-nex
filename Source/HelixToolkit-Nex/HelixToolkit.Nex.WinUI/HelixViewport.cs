using System.Runtime.InteropServices;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Vulkan;
using Size = HelixToolkit.Nex.Maths.Size;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.WinUI;

/// <summary>
/// COM interface for setting a DXGI swap chain on a <see cref="SwapChainPanel"/>.
/// </summary>
[
    ComImport,
    Guid("63aad0b8-7c24-40ff-85a8-640d944cc325"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
]
internal partial interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain([In] IntPtr swapchain);

    [PreserveSig]
    ulong Release();
}

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
public sealed class HelixViewport : UserControl, IDisposable
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
            viewport.SetEngine((Engine.Engine?)e.NewValue);
        }
    );
    public Engine.Engine? Engine
    {
        get { return (Engine.Engine)GetValue(EngineDp); }
        set { SetValue(EngineDp, value); }
    }

    public static readonly DependencyProperty ViewportClientDp = HelixProperty.Register<
        HelixViewport,
        IViewportClient?
    >(
        "ViewportClient",
        null,
        (d, e) =>
        {
            if (d is not HelixViewport viewport)
            {
                return;
            }
            viewport._viewportClient = (IViewportClient?)e.NewValue;
        }
    );

    /// <summary>
    /// Gets or sets the <see cref="IViewportClient"/> that provides per-frame camera
    /// updates and scene data for this viewport. When <c>null</c>, no frames are rendered.
    /// </summary>
    public IViewportClient? ViewportClient
    {
        get { return (IViewportClient?)GetValue(ViewportClientDp); }
        set { SetValue(ViewportClientDp, value); }
    }
    #endregion
    private SwapChainPanel? _swapChainPanel;
    private D3D11DeviceManager? _d3d11Manager;
    private IDXGIDevice3? _dxgiDevice;
    private IDXGIAdapter? _dxgiAdapter;
    private IDXGIFactory2? _dxgiFactory;
    private IDXGISwapChain1? _swapchain;
    private ID3D11Texture2D? _backbuffer;
    private ID3D11Resource? _backbufferResource;
    private ID3D11Resource? _renderTargetResource;
    private SharedTextureResult? _sharedTexture;
    private ImportedVulkanTexture? _importedTexture;
    private IDXGIKeyedMutex? _keyedMutex;
    private long _lastTimestamp;
    private ViewportRenderingEventArgs? _renderArgs;
    private Engine.Engine? _engine;
    private IViewportClient? _viewportClient;
    private bool _sizeChanged = true;
    private RenderContext? _renderContext;
    private bool _disposed;

    /// <summary>
    /// Raised each frame after <see cref="IViewportClient.Update"/> but before rendering.
    /// This is a <b>read-only notification</b>; use <see cref="ViewportClient"/> to
    /// provide the camera and scene data.
    /// </summary>
    public event EventHandler<ViewportRenderingEventArgs>? BeforeRender;

    private KeyedMutexSyncInfo _vulkanSyncInfo;
    private KeyedMutexSyncInfo _copySyncInfo;

    public HelixViewport()
    {
        _swapChainPanel = new SwapChainPanel();
        Content = _swapChainPanel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 1. D3D11 device for shared texture interop
        _d3d11Manager = new D3D11DeviceManager();

        // 2. DXGI device, adapter, factory for swap chain creation
        _dxgiDevice = _d3d11Manager.Device.QueryInterface<IDXGIDevice3>();
        _dxgiDevice.GetAdapter(out _dxgiAdapter).CheckError();
        _dxgiFactory = _dxgiAdapter.GetParent<IDXGIFactory2>();

        // 4. Create shared resources at the current control size
        var width = (uint)ActualWidth;
        var height = (uint)ActualHeight;
        if (width > 0 && height > 0)
        {
            CreateResources(width, height);
        }
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
        if (_engine is null)
        {
            return;
        }
        _renderContext = _engine.CreateRenderContext();
        _renderContext.Initialize();
        _renderArgs = new(_renderContext);
        if (IsLoaded && Width > 0 && Height > 0)
        {
            CreateResources((uint)Width, (uint)Height);
        }
    }

    private void CreateResources(uint width, uint height)
    {
        if (_engine is null || _dxgiFactory is null)
        {
            return;
        }
        _logger.LogInformation(
            "Creating resources for HelixViewport with size {Width}x{Height}.",
            width,
            height
        );

        var context = _engine.Context;

        // 1. DXGI swap chain for composition
        var swapchainDesc = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
            SwapEffect = SwapEffect.FlipSequential,
            SampleDescription = new(1u, 0u),
            BufferUsage = Usage.Backbuffer,
            BufferCount = 2u,
        };

        _swapchain = _dxgiFactory.CreateSwapChainForComposition(_dxgiDevice, swapchainDesc);

        _backbuffer = _swapchain.GetBuffer<ID3D11Texture2D>(0u);
        SetSwapChainOnPanel(_swapChainPanel!, _swapchain);

        // 2. Shared D3D11 render target (NT handle + keyed mutex)
        _sharedTexture = SharedTextureFactory.CreateForWinUI(_d3d11Manager!, width, height);
        _backbufferResource = _backbuffer.QueryInterface<ID3D11Resource>();
        _renderTargetResource = _sharedTexture.Texture.QueryInterface<ID3D11Resource>();

        #region Get keyed mutex for render target texture and setup syncing
        _keyedMutex = _renderTargetResource.QueryInterface<IDXGIKeyedMutex>();
        _vulkanSyncInfo = new KeyedMutexSyncInfo
        {
            AcquireKey = 0, // Vulkan goes first
            ReleaseKey = 1, // Release key for copy to back buffer to run
            Timeout = 1000,
            SyncType = KeyedMutexSyncType.D3D11SharedFence,
        };
        _copySyncInfo = new KeyedMutexSyncInfo
        {
            AcquireKey = 1,
            ReleaseKey = 0, // Release key for Vulkan to run
            Timeout = 500,
            SyncType = KeyedMutexSyncType.D3D11SharedFence,
        };
        #endregion
        // 3. Import into Vulkan as R8G8B8A8Unorm
        _importedTexture = VulkanExternalMemoryImporter.Import(
            context,
            _sharedTexture.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11Texture,
            VkFormat.R8G8B8A8Unorm,
            width,
            height
        );

        _vulkanSyncInfo.AcquireSyncHandle = _importedTexture.Memory.Handle;
        _vulkanSyncInfo.ReleaseSyncHandle = _importedTexture.Memory.Handle;

        // 4. Wire up render context
        _renderContext!.WindowSize = new Size((int)width, (int)height);
        _renderContext.FinalOutputTexture = _importedTexture.Handle;

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (
            _disposed
            || Engine is null
            || _renderContext is null
            || _d3d11Manager is null
            || _keyedMutex is null
            || _renderArgs is null
        )
            return;

        // Compute delta time
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float delta =
            _lastTimestamp == 0
                ? 0f
                : (float)(now - _lastTimestamp) / System.Diagnostics.Stopwatch.Frequency;
        _lastTimestamp = now;
        _renderArgs.DeltaTime = delta;

        _renderContext.WindowSize = new Size((int)ActualWidth, (int)ActualHeight);

        // Pull per-frame data from the viewport client
        if (_viewportClient is null)
            return;

        _viewportClient.Update(_renderContext, delta);

        var dataProvider = _viewportClient.DataProvider;
        if (dataProvider is null)
            return;

        // Notify optional subscribers (read-only)
        BeforeRender?.Invoke(this, _renderArgs);

        EnsureSize();
        var context = Engine.Context;

        // Render offscreen
        var cmdBuf = Engine.RenderOffscreen(_renderContext, dataProvider);
        var submitHandle = context.Submit(cmdBuf, TextureHandle.Null, _vulkanSyncInfo);
        context.Wait(submitHandle);

        // Keyed mutex acquire → copy → release → present
        _keyedMutex.AcquireSync(_copySyncInfo.AcquireKey, (int)_copySyncInfo.Timeout);
        _d3d11Manager.DeviceContext.CopyResource(_backbufferResource, _renderTargetResource);
        _keyedMutex.ReleaseSync(_copySyncInfo.ReleaseKey);
        _swapchain?.Present(0u, 0u);
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed || Engine is null)
            return;

        _sizeChanged = true;
    }

    private void ReleaseResources()
    {
        _logger.LogInformation("Releasing resources for HelixViewport.");
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnCompositionRendering;

        if (Engine is not null)
            Engine.Context.Wait(default);

        Disposer.DisposeAndRemove(ref _keyedMutex);
        Disposer.DisposeAndRemove(ref _renderTargetResource);
        Disposer.DisposeAndRemove(ref _backbufferResource);
        Disposer.DisposeAndRemove(ref _importedTexture);
        Disposer.DisposeAndRemove(ref _sharedTexture);
        Disposer.DisposeAndRemove(ref _backbuffer);
        Disposer.DisposeAndRemove(ref _swapchain);
    }

    private static void SetSwapChainOnPanel(SwapChainPanel panel, IDXGISwapChain1 swapchain)
    {
        var panelNativePtr = Marshal.GetComInterfaceForObject<
            SwapChainPanel,
            ISwapChainPanelNative
        >(panel);
        try
        {
            var panelNative = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(panelNativePtr);
            panelNative.SetSwapChain(swapchain.NativePointer);
        }
        finally
        {
            Marshal.Release(panelNativePtr);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseResources();

        Disposer.DisposeAndRemove(ref _renderContext);

        // We do NOT dispose Engine — it is externally owned
        Disposer.DisposeAndRemove(ref _dxgiFactory);
        Disposer.DisposeAndRemove(ref _dxgiAdapter);
        Disposer.DisposeAndRemove(ref _dxgiDevice);

        Disposer.DisposeAndRemove(ref _d3d11Manager);

        GC.SuppressFinalize(this);
    }
}
