#if WINDOWS
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Interop.DirectX;
using Microsoft.Extensions.Logging;
using Vortice.Vulkan;
using Format = HelixToolkit.Nex.Graphics.Format;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Windows implementation of <see cref="IEngineOutputBridge"/>. Shares the engine's offscreen output
/// through a D3D11 shared NT-handle texture (created by
/// <see cref="SharedTextureFactory.CreateForWinUI"/>) that is imported into Vulkan via
/// <see cref="VulkanExternalMemoryImporter"/>, mirroring the WinUI host mechanism. The engine renders
/// into the imported Vulkan image (<see cref="EngineTarget"/>); the same NT handle is later handed to
/// the Avalonia compositor for import, and engine writes/compositor reads are serialized with
/// keyed-mutex synchronization.
/// </summary>
/// <remarks>
/// This type is compiled only for the Windows target framework (<c>net8.0-windows</c>), which is the
/// only configuration where the Windows-only <c>HelixToolkit.Nex.Interop.DirectX</c> project is
/// referenced. Callers must additionally guard construction behind
/// <see cref="OperatingSystem.IsWindows"/>.
/// <para>
/// Task 7.1 owns the D3D11 texture creation, the Vulkan import, <see cref="EngineTarget"/>, and
/// <see cref="Resize"/>. The keyed-mutex synchronization wiring together with
/// <see cref="CreateImportDescription"/> / <see cref="CreateSurfaceSync"/> are completed by task 7.2;
/// the fields those members depend on (<see cref="_sharedTexture"/>, <see cref="_importedTexture"/>,
/// <see cref="_engineSyncInfo"/>) are established here so that wiring can plug in cleanly.
/// </para>
/// </remarks>
internal sealed class WindowsSharedTextureBridge : IEngineOutputBridge
{
    private static readonly ILogger _logger = LogManager.Create<WindowsSharedTextureBridge>();

    /// <summary>
    /// Avalonia known external-image handle-type name for a Windows D3D11 shared NT-handle texture.
    /// Matches <c>KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle</c>; used as a
    /// literal to avoid depending on the (internal) Avalonia constant type.
    /// </summary>
    private const string D3D11TextureNtHandleType = "D3D11TextureNtHandle";

    // Keyed-mutex key convention (mirrors the WinUI host): the Vulkan write acquires 0 and releases
    // 1; the compositor read acquires 1 and releases 0 so the next Vulkan write can acquire again.
    private const ulong VulkanAcquireKey = 0;
    private const ulong VulkanReleaseKey = 1;
    private const uint CompositionAcquireKey = 1;
    private const uint CompositionReleaseKey = 0;

    /// <summary>Keyed-mutex acquire timeout (ms) used for the engine write.</summary>
    private const uint KeyedMutexTimeoutMs = 1000;

    /// <summary>The engine Vulkan context the shared texture is imported into.</summary>
    private readonly IContext _context;

    /// <summary>Owns the D3D11 device used to create the shared render-target texture.</summary>
    private D3D11DeviceManager? _d3d11Manager;

    /// <summary>The shared D3D11 render-target texture (NT handle + keyed mutex).</summary>
    private SharedTextureResult? _sharedTexture;

    /// <summary>The Vulkan view of the shared texture; its handle is the engine render target.</summary>
    private ImportedVulkanTexture? _importedTexture;

    /// <summary>
    /// Synchronization info passed to <c>Engine.Submit</c> for the engine write. Populated by the
    /// keyed-mutex wiring (task 7.2); left <c>default</c> until then.
    /// </summary>
    private KeyedMutexSyncInfo _engineSyncInfo;

    private uint _width;
    private uint _height;
    private bool _disposed;

    /// <summary>
    /// Creates the bridge and its initial shared output resources at the given size.
    /// </summary>
    /// <param name="context">The engine's Vulkan context (must have external-memory-win32 enabled).</param>
    /// <param name="width">Initial output width in pixels; must be greater than zero.</param>
    /// <param name="height">Initial output height in pixels; must be greater than zero.</param>
    public WindowsSharedTextureBridge(IContext context, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // The D3D11 device is created once and reused across resizes.
        _d3d11Manager = new D3D11DeviceManager();

        CreateResources(width, height);
    }

    /// <inheritdoc />
    public TextureHandle EngineTarget => _importedTexture?.Handle ?? TextureHandle.Null;

    /// <inheritdoc />
    public KeyedMutexSyncInfo EngineSyncInfo => _engineSyncInfo;

    /// <inheritdoc />
    /// <remarks>
    /// Returns the shared D3D11 texture NT handle (typed as a D3D11 shared texture) for import by
    /// Avalonia's <c>ICompositionGpuInterop</c>.
    /// </remarks>
    public SharedImageDescription CreateImportDescription()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sharedTexture is null)
        {
            throw new InvalidOperationException(
                "Shared texture resources have not been created; call the constructor or Resize first."
            );
        }

        return new SharedImageDescription
        {
            Width = _width,
            Height = _height,
            Format = Format.RGBA_UN8,
            NtHandle = _sharedTexture.SharedHandle,
            ExternalHandleType = D3D11TextureNtHandleType,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a keyed-mutex surface update that acquires the key released by the engine write and
    /// releases it back so the next engine write can acquire again.
    /// </remarks>
    public ISurfaceUpdateSync CreateSurfaceSync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new KeyedMutexSurfaceUpdateSync(CompositionAcquireKey, CompositionReleaseKey);
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width == 0 || height == 0)
        {
            // Zero-size ticks are skipped by the control; ignore defensively.
            return;
        }

        if (width == _width && height == _height)
        {
            return;
        }

        ReleaseResources();
        CreateResources(width, height);
    }

    /// <inheritdoc />
    /// <remarks>Single-buffered; multi-buffering is provided by <see cref="BufferedEngineOutputBridge"/>.</remarks>
    public void AdvanceFrame()
    {
        // No-op: this bridge owns a single shared texture.
    }

    /// <summary>
    /// Creates the shared D3D11 texture and imports it into Vulkan at the given size, establishing
    /// <see cref="EngineTarget"/>.
    /// </summary>
    private void CreateResources(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Creating Windows shared texture bridge resources at {Width}x{Height}.",
            width,
            height
        );

        // 1. Shared D3D11 render target (NT handle + keyed mutex), same as the WinUI host.
        _sharedTexture = SharedTextureFactory.CreateForWinUI(_d3d11Manager!, width, height);

        // 2. Import the shared handle into Vulkan as R8G8B8A8Unorm; the returned handle is the
        //    engine render target.
        _importedTexture = VulkanExternalMemoryImporter.Import(
            _context,
            _sharedTexture.SharedHandle,
            VkExternalMemoryHandleTypeFlags.D3D11Texture,
            VkFormat.R8G8B8A8Unorm,
            width,
            height
        );

        // 3. Keyed-mutex sync for the engine write, matching the WinUI host: Vulkan acquires 0 and
        //    releases 1, handing the shared texture off to the compositor read. The shared-fence KMT
        //    handle is the imported memory handle.
        _engineSyncInfo = new KeyedMutexSyncInfo
        {
            SyncType = KeyedMutexSyncType.D3D11SharedFence,
            AcquireKey = VulkanAcquireKey,
            ReleaseKey = VulkanReleaseKey,
            AcquireSyncHandle = _importedTexture.Memory.Handle,
            ReleaseSyncHandle = _importedTexture.Memory.Handle,
            Timeout = KeyedMutexTimeoutMs,
        };

        _width = width;
        _height = height;
    }

    /// <summary>Releases the shared texture and its Vulkan import, leaving the D3D11 device intact.</summary>
    private void ReleaseResources()
    {
        // Wait for the GPU to finish reading/writing before tearing down shared resources.
        _context.Wait(default);

        Disposer.DisposeAndRemove(ref _importedTexture);
        Disposer.DisposeAndRemove(ref _sharedTexture);
        _engineSyncInfo = default;
        _width = 0;
        _height = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        ReleaseResources();
        Disposer.DisposeAndRemove(ref _d3d11Manager);

        GC.SuppressFinalize(this);
    }
}
#endif
