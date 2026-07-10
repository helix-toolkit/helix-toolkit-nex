using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Abstracts the per-viewport shared engine output resource and its synchronization, hiding the
/// platform difference between the Windows path (D3D11 shared NT-handle texture + keyed mutex) and
/// the Linux path (exportable Vulkan external memory + semaphore). The control selects one concrete
/// bridge based on the host operating system and drives it each frame: the engine renders into
/// <see cref="EngineTarget"/>, the presenter imports <see cref="CreateImportDescription"/> through
/// <c>ICompositionGpuInterop</c>, and <see cref="CreateSurfaceSync"/> serializes the compositor read.
/// </summary>
public interface IEngineOutputBridge : IDisposable
{
    /// <summary>
    /// The engine render target the offscreen frame is rendered into. Backed by the shared texture
    /// (Windows) or the exportable Vulkan image (Linux).
    /// </summary>
    TextureHandle EngineTarget { get; }

    /// <summary>
    /// Builds the platform-neutral description of the shared image for import by the Avalonia
    /// compositor through <c>ICompositionGpuInterop</c>.
    /// </summary>
    /// <returns>A description carrying the image dimensions, format, and platform handle union.</returns>
    SharedImageDescription CreateImportDescription();

    /// <summary>
    /// The synchronization info passed to <c>Engine.Submit</c> for the engine write. On Windows this
    /// carries the keyed-mutex keys/handles for the shared D3D11 texture; on Linux it stays
    /// <c>default</c> (<see cref="KeyedMutexSyncType.None"/>) because writes are serialized with a
    /// Vulkan semaphore instead.
    /// </summary>
    KeyedMutexSyncInfo EngineSyncInfo { get; }

    /// <summary>
    /// Creates the surface-update synchronization used by the presenter to serialize the compositor
    /// read of the shared image (keyed mutex on Windows, semaphore on Linux).
    /// </summary>
    /// <returns>An <see cref="ISurfaceUpdateSync"/> matching the active platform path.</returns>
    ISurfaceUpdateSync CreateSurfaceSync();

    /// <summary>
    /// Recreates the shared output resources at the new size, releasing the previous ones. Called
    /// when the control size changes to nonzero dimensions.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    void Resize(uint width, uint height);
}
