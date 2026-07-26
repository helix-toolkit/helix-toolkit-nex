using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Direct3D9;
using Vortice.Vulkan;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// Owns the native resources used by one loaded viewport surface.
/// </summary>
internal sealed class ViewportSession : IDisposable
{
    private IDirect3DTexture9? _backBuffer;
    private IDirect3DSurface9? _surface;
    private SharedTextureResult? _sharedTexture;
    private ImportedVulkanTexture? _importedTexture;
    private nint _sharedHandle;
    private bool _disposed;

    private ViewportSession() { }

    /// <summary>
    /// Gets the D3D9 surface used as the <see cref="System.Windows.Interop.D3DImage"/> back buffer.
    /// </summary>
    public nint SurfacePointer => (nint)_surface!;

    /// <summary>
    /// Gets the imported Vulkan render target.
    /// </summary>
    public ImportedVulkanTexture ImportedTexture => _importedTexture!;

    /// <summary>
    /// Creates a fully initialized viewport session.
    /// </summary>
    /// <param name="context">The Vulkan context importing the shared texture.</param>
    /// <param name="d3d9Manager">The D3D9 device manager owning the shared back buffer.</param>
    /// <param name="d3d11Manager">The D3D11 device manager opening the shared texture.</param>
    /// <param name="width">The surface width.</param>
    /// <param name="height">The surface height.</param>
    /// <returns>A complete loaded-surface session.</returns>
    public static ViewportSession Create(
        IContext context,
        D3D9DeviceManager d3d9Manager,
        D3D11DeviceManager d3d11Manager,
        uint width,
        uint height
    )
    {
        var session = new ViewportSession();
        try
        {
            session.Initialize(context, d3d9Manager, d3d11Manager, width, height);
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
        D3D9DeviceManager d3d9Manager,
        D3D11DeviceManager d3d11Manager,
        uint width,
        uint height
    )
    {
        // 1. D3D9 shared back buffer (X8R8G8B8)
        _backBuffer = d3d9Manager.Device.CreateTexture(
            width,
            height,
            1u,
            Usage.RenderTarget,
            Vortice.Direct3D9.Format.X8R8G8B8,
            Pool.Default,
            ref _sharedHandle
        );

        // 2. Surface level 0 for D3DImage.SetBackBuffer
        _surface = _backBuffer.GetSurfaceLevel(0u);

        // 3. Open on D3D11 and get KMT handle
        _sharedTexture = SharedTextureFactory.CreateForWpf(d3d11Manager, _sharedHandle);

        // 4. Import into Vulkan as B8G8R8A8Unorm
        _importedTexture = VulkanExternalMemoryImporter.Import(
            context,
            _sharedTexture.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT,
            VkFormat.B8G8R8A8Unorm,
            width,
            height
        );
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
        TryDispose(ref _importedTexture, failures);
        TryDispose(ref _sharedTexture, failures);
        TryDispose(ref _surface, failures);
        TryDispose(ref _backBuffer, failures);
        _sharedHandle = 0;

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
