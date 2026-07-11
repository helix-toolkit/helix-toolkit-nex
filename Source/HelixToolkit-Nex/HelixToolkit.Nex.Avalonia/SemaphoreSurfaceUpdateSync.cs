using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using HelixToolkit.Nex;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Linux <see cref="ISurfaceUpdateSync"/> implementation that updates the Avalonia
/// <see cref="CompositionDrawingSurface"/> using exported Vulkan binary semaphores. The compositor
/// waits on the engine's render-finished semaphore before reading the shared image and signals the
/// read-finished semaphore once it is done, so the next engine write can proceed — serializing
/// engine writes against composition reads over the pure Vulkan-to-Vulkan external-memory path (no
/// OpenGL/EGL/software fallback).
/// </summary>
/// <remarks>
/// <para>
/// This type is compiled unconditionally (OS-runtime-guarded like
/// <see cref="LinuxExternalMemoryBridge"/>, not gated behind a <c>WINDOWS</c> preprocessor symbol) so
/// it compile-verifies on both the <c>net8.0-windows</c> and <c>net8.0</c> target frameworks of the
/// Avalonia project. The exported semaphore file descriptors are POSIX handles only ever produced on
/// Linux with <see cref="VulkanContextConfig.EnableExternalMemoryFd"/> enabled; construction happens
/// only through <see cref="LinuxExternalMemoryBridge.CreateSurfaceSync"/>, which is Linux-guarded by
/// the control.
/// </para>
/// <para>
/// Avalonia imports each semaphore fd through
/// <see cref="ICompositionGpuInterop.ImportSemaphore(IPlatformHandle)"/> and then serializes the
/// surface update through
/// <see cref="CompositionDrawingSurface.UpdateWithSemaphoresAsync(ICompositionImportedGpuImage, ICompositionImportedGpuSemaphore, ICompositionImportedGpuSemaphore)"/>.
/// The imported semaphore objects are cached and reused across frames because importing an opaque-fd
/// semaphore consumes the file descriptor.
/// </para>
/// </remarks>
internal sealed class SemaphoreSurfaceUpdateSync : ISurfaceUpdateSync, IAsyncDisposable
{
    private static readonly ILogger _logger = LogManager.Create<SemaphoreSurfaceUpdateSync>();

    /// <summary>
    /// Avalonia known external-semaphore handle-type name for a Vulkan semaphore exported as an
    /// opaque POSIX file descriptor. Matches
    /// <c>KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor</c>; used
    /// as a literal to avoid depending on the (internal) Avalonia constant type, mirroring the
    /// image-side handle-type name.
    /// </summary>
    private const string VulkanSemaphoreOpaqueFdType = "VulkanOpaquePosixFileDescriptor";

    /// <summary>
    /// Exported fd of the render-finished semaphore the engine signals; the compositor waits on it
    /// before reading the shared image.
    /// </summary>
    private readonly int _waitSemaphoreFd;

    /// <summary>
    /// Exported fd of the read-finished semaphore the compositor signals once it is done reading; the
    /// engine waits on it before the next write.
    /// </summary>
    private readonly int _signalSemaphoreFd;

    private ICompositionImportedGpuSemaphore? _waitSemaphore;
    private ICompositionImportedGpuSemaphore? _signalSemaphore;

    /// <summary>
    /// Creates the semaphore surface update over the exported render-finished (compositor wait) and
    /// read-finished (compositor signal) semaphore file descriptors.
    /// </summary>
    /// <param name="waitSemaphoreFd">
    /// Exported fd of the semaphore the compositor waits on before reading (signaled by the engine
    /// write).
    /// </param>
    /// <param name="signalSemaphoreFd">
    /// Exported fd of the semaphore the compositor signals after reading (awaited by the next engine
    /// write).
    /// </param>
    public SemaphoreSurfaceUpdateSync(int waitSemaphoreFd, int signalSemaphoreFd)
    {
        _waitSemaphoreFd = waitSemaphoreFd;
        _signalSemaphoreFd = signalSemaphoreFd;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        CompositionDrawingSurface surface,
        ICompositionImportedGpuImage image
    )
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(image);

        // The compositor's GPU interop is required to import the exported semaphore fds. It is
        // obtained from the same compositor that owns the drawing surface.
        ICompositionGpuInterop? interop = await surface.Compositor.TryGetCompositionGpuInterop();
        if (interop is null)
        {
            // No GPU interop in this render session; the presenter already logs the unavailable
            // interop, so skip the surface update without throwing or terminating.
            _logger.LogWarning(
                "Composition GPU interop is unavailable; skipping semaphore-based surface update."
            );
            return;
        }

        // Import the exported semaphore fds once and reuse the imported objects across frames
        // (importing an opaque-fd semaphore consumes the file descriptor).
        _waitSemaphore ??= interop.ImportSemaphore(
            new PlatformHandle((nint)_waitSemaphoreFd, VulkanSemaphoreOpaqueFdType)
        );
        _signalSemaphore ??= interop.ImportSemaphore(
            new PlatformHandle((nint)_signalSemaphoreFd, VulkanSemaphoreOpaqueFdType)
        );

        // Wait on the engine's render-finished semaphore, present the frame, and signal the
        // read-finished semaphore so the next engine write can proceed.
        await surface.UpdateWithSemaphoresAsync(image, _waitSemaphore, _signalSemaphore);
    }

    /// <summary>
    /// Releases the imported compositor semaphores. Importing an opaque-fd semaphore consumes the file
    /// descriptor, so this instance is cached and reused across frames by the bridge and only disposed
    /// when the bridge releases its resources (resize or teardown).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        ICompositionImportedGpuSemaphore? wait = _waitSemaphore;
        ICompositionImportedGpuSemaphore? signal = _signalSemaphore;
        _waitSemaphore = null;
        _signalSemaphore = null;

        await DisposeSemaphoreAsync(wait).ConfigureAwait(false);
        await DisposeSemaphoreAsync(signal).ConfigureAwait(false);
    }

    /// <summary>Disposes an imported compositor semaphore, logging and swallowing any failure.</summary>
    private static async ValueTask DisposeSemaphoreAsync(ICompositionImportedGpuSemaphore? semaphore)
    {
        if (semaphore is null)
        {
            return;
        }

        try
        {
            await semaphore.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose imported composition semaphore during teardown.");
        }
    }
}
