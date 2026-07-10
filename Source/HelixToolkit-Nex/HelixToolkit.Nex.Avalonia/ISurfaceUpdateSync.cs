using Avalonia.Rendering.Composition;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Abstracts the wait/signal step used to update an Avalonia <see cref="CompositionDrawingSurface"/>
/// with a freshly written shared engine image. The concrete implementation encapsulates the
/// platform synchronization primitive: a keyed mutex on Windows (serializing Vulkan writes against
/// the D3D11/compositor read) and an exported Vulkan semaphore on Linux.
/// </summary>
public interface ISurfaceUpdateSync
{
    /// <summary>
    /// Updates the composition drawing surface with the imported GPU image, performing the
    /// platform-specific synchronization (keyed-mutex acquire/release on Windows, semaphore
    /// wait/signal on Linux) so the compositor reads a complete frame.
    /// </summary>
    /// <param name="surface">The composition drawing surface to update.</param>
    /// <param name="image">The imported shared GPU image to present.</param>
    /// <returns>A task that completes once the surface update has been scheduled.</returns>
    Task UpdateAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image);
}
