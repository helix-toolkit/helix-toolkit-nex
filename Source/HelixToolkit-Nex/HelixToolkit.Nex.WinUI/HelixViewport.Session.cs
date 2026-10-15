using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Vulkan;

namespace HelixToolkit.Nex.WinUI;

/// <summary>
/// Owns the native resources used by one loaded viewport surface.
/// </summary>
internal sealed class ViewportSession : IDisposable
{
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11Resource? _backBufferResource;
    private ID3D11Resource? _renderTargetResource;
    private SharedTextureResult? _sharedTexture;
    private ImportedVulkanTexture? _importedTexture;
    private IDXGIKeyedMutex? _keyedMutex;
    private bool _disposed;

    private ViewportSession() { }

    /// <summary>
    /// Gets the swap chain displayed by the viewport panel.
    /// </summary>
    public IDXGISwapChain1 SwapChain => _swapChain!;

    /// <summary>
    /// Gets the imported Vulkan render target.
    /// </summary>
    public ImportedVulkanTexture ImportedTexture => _importedTexture!;

    /// <summary>
    /// Gets the keyed mutex shared by rendering and presentation.
    /// </summary>
    public IDXGIKeyedMutex KeyedMutex => _keyedMutex!;

    /// <summary>
    /// Gets the swap-chain back-buffer resource.
    /// </summary>
    public ID3D11Resource BackBufferResource => _backBufferResource!;

    /// <summary>
    /// Gets the shared render-target resource.
    /// </summary>
    public ID3D11Resource RenderTargetResource => _renderTargetResource!;

    /// <summary>
    /// Gets synchronization values used by Vulkan rendering.
    /// </summary>
    public KeyedMutexSyncInfo VulkanSyncInfo { get; private set; }

    /// <summary>
    /// Gets synchronization values used by the D3D back-buffer copy.
    /// </summary>
    public KeyedMutexSyncInfo CopySyncInfo { get; private set; }

    /// <summary>
    /// Creates a fully initialized viewport session.
    /// </summary>
    /// <param name="context">The Vulkan context importing the shared texture.</param>
    /// <param name="d3d11Manager">The D3D11 device manager.</param>
    /// <param name="dxgiFactory">The DXGI factory creating the swap chain.</param>
    /// <param name="dxgiDevice">The DXGI device used by the swap chain.</param>
    /// <param name="width">The surface width.</param>
    /// <param name="height">The surface height.</param>
    /// <returns>A complete loaded-surface session.</returns>
    public static ViewportSession Create(
        IContext context,
        D3D11DeviceManager d3d11Manager,
        IDXGIFactory2 dxgiFactory,
        IDXGIDevice3 dxgiDevice,
        uint width,
        uint height
    )
    {
        var session = new ViewportSession();
        try
        {
            session.Initialize(context, d3d11Manager, dxgiFactory, dxgiDevice, width, height);
            return session;
        }
        catch (Exception error)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(
                    "Viewport session creation and cleanup both failed.",
                    error,
                    cleanupError
                );
            }

            throw;
        }
    }

    /// <summary>
    /// Acquires the native resources in the same order as the original viewport.
    /// </summary>
    private void Initialize(
        IContext context,
        D3D11DeviceManager d3d11Manager,
        IDXGIFactory2 dxgiFactory,
        IDXGIDevice3 dxgiDevice,
        uint width,
        uint height
    )
    {
        var swapChainDescription = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = Vortice.DXGI.Format.R8G8B8A8_UNorm,
            SwapEffect = SwapEffect.FlipSequential,
            SampleDescription = new(1u, 0u),
            BufferUsage = Usage.Backbuffer,
            BufferCount = 2u,
        };

        _swapChain = dxgiFactory.CreateSwapChainForComposition(
            dxgiDevice,
            swapChainDescription
        );
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0u);
        _sharedTexture = SharedTextureFactory.CreateForWinUI(d3d11Manager, width, height);
        _backBufferResource = _backBuffer.QueryInterface<ID3D11Resource>();
        _renderTargetResource = _sharedTexture.Texture.QueryInterface<ID3D11Resource>();
        _keyedMutex = _renderTargetResource.QueryInterface<IDXGIKeyedMutex>();
        _importedTexture = VulkanExternalMemoryImporter.Import(
            context,
            _sharedTexture.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11Texture,
            VkFormat.R8G8B8A8Unorm,
            width,
            height
        );

        VulkanSyncInfo = new KeyedMutexSyncInfo
        {
            AcquireKey = 0,
            ReleaseKey = 1,
            Timeout = 1000,
            SyncType = KeyedMutexSyncType.D3D11SharedFence,
            AcquireSyncHandle = _importedTexture.Memory.Handle,
            ReleaseSyncHandle = _importedTexture.Memory.Handle,
        };
        CopySyncInfo = new KeyedMutexSyncInfo
        {
            AcquireKey = 1,
            ReleaseKey = 0,
            Timeout = 500,
            SyncType = KeyedMutexSyncType.D3D11SharedFence,
        };
    }

    /// <summary>
    /// Releases every resource owned by this loaded-surface session.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var failures = new List<Exception>();
        TryDispose(ref _keyedMutex, failures);
        TryDispose(ref _renderTargetResource, failures);
        TryDispose(ref _backBufferResource, failures);
        TryDispose(ref _importedTexture, failures);
        TryDispose(ref _sharedTexture, failures);
        TryDispose(ref _backBuffer, failures);
        TryDispose(ref _swapChain, failures);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more viewport session resources could not be released.",
                failures
            );
        }
    }

    /// <summary>
    /// Attempts one resource disposal and records its failure.
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
