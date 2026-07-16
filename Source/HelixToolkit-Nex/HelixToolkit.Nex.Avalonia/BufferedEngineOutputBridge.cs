using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// An <see cref="IEngineOutputBridge"/> that rotates over several independent single-buffer bridges to
/// decouple the engine render from the compositor read. Each wrapped bridge is a full shared output
/// (its own shared texture / external-memory image and its own keyed-mutex/semaphore synchronization).
/// </summary>
/// <remarks>
/// <para>
/// With a single shared texture the engine's next write blocks on the compositor's read of the same
/// texture (they contend on one keyed mutex / semaphore), so engine and compositor run in series and
/// the frame rate collapses to roughly half the display refresh rate. Rotating the write target across
/// N buffers lets the engine render frame N+1 into a free buffer while the compositor still reads the
/// buffer presented for frame N, so the two pipeline and the frame rate can reach the refresh rate.
/// </para>
/// <para>
/// The render loop drives this by rendering into <see cref="EngineTarget"/>, capturing the current
/// buffer's import description / surface sync, then calling <see cref="AdvanceFrame"/> to rotate the
/// write target for the next tick. Two buffers are sufficient when at most one present is in flight at
/// a time (the loop awaits the previous present before issuing the next), which is the model used here.
/// </para>
/// </remarks>
internal sealed class BufferedEngineOutputBridge : IEngineOutputBridge
{
    private readonly IEngineOutputBridge[] _buffers;
    private int _writeIndex;
    private bool _disposed;

    /// <summary>Creates a buffered bridge over the supplied per-buffer bridges (at least one).</summary>
    /// <param name="buffers">The independent single-buffer bridges to rotate over.</param>
    public BufferedEngineOutputBridge(IEngineOutputBridge[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 0)
        {
            throw new ArgumentException("At least one buffer is required.", nameof(buffers));
        }
        _buffers = buffers;
    }

    /// <summary>The number of rotating output buffers.</summary>
    public int BufferCount => _buffers.Length;

    /// <inheritdoc />
    public TextureHandle EngineTarget => _buffers[_writeIndex].EngineTarget;

    /// <inheritdoc />
    public KeyedMutexSyncInfo EngineSyncInfo => _buffers[_writeIndex].EngineSyncInfo;

    /// <inheritdoc />
    public SharedImageDescription CreateImportDescription() =>
        _buffers[_writeIndex].CreateImportDescription();

    /// <inheritdoc />
    public ISurfaceUpdateSync CreateSurfaceSync() => _buffers[_writeIndex].CreateSurfaceSync();

    /// <inheritdoc />
    public void AdvanceFrame() => _writeIndex = (_writeIndex + 1) % _buffers.Length;

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IEngineOutputBridge buffer in _buffers)
        {
            buffer.Resize(width, height);
        }
        // Restart the rotation so the first post-resize frame writes buffer 0.
        _writeIndex = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (IEngineOutputBridge buffer in _buffers)
        {
            buffer.Dispose();
        }
    }
}
