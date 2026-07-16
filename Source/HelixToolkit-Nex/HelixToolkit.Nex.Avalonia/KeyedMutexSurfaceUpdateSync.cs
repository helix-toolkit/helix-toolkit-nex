using Avalonia.Rendering.Composition;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Windows <see cref="ISurfaceUpdateSync"/> implementation that updates the Avalonia
/// <see cref="CompositionDrawingSurface"/> using keyed-mutex synchronization. The compositor read
/// acquires the keyed-mutex key released by the engine's Vulkan write and releases it back so the
/// next engine write can acquire the shared texture again, serializing engine writes against
/// composition reads of the shared D3D11 NT-handle texture.
/// </summary>
/// <remarks>
/// This type is used only by the Windows runtime path (the D3D11 shared-texture / keyed-mutex
/// composition path driven by <see cref="WindowsSharedTextureBridge"/>).
/// </remarks>
internal sealed class KeyedMutexSurfaceUpdateSync : ISurfaceUpdateSync
{
    private readonly uint _acquireKey;
    private readonly uint _releaseKey;

    /// <summary>
    /// Creates the keyed-mutex surface update.
    /// </summary>
    /// <param name="acquireKey">The keyed-mutex key the compositor read acquires (the key released by the engine write).</param>
    /// <param name="releaseKey">The keyed-mutex key the compositor read releases (so the next engine write can acquire).</param>
    public KeyedMutexSurfaceUpdateSync(uint acquireKey, uint releaseKey)
    {
        _acquireKey = acquireKey;
        _releaseKey = releaseKey;
    }

    /// <inheritdoc />
    public Task UpdateAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(image);

        // Acquire the shared texture under the keyed mutex, present the imported image, and release
        // the mutex back to the engine write's acquire key.
        return surface.UpdateWithKeyedMutexAsync(image, _acquireKey, _releaseKey);
    }
}
