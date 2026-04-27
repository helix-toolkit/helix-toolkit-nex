namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// A ring buffer that maintains multiple <see cref="ElementBuffer{T}"/> instances
/// and rotates through them each frame to avoid GPU/CPU contention.
/// <para>
/// The GPU reads from the previous frame's buffer while the CPU writes to the
/// current frame's buffer — providing zero-stall, lock-free uploads for per-frame
/// data such as point clouds, particle systems, or dynamic instance data.
/// </para>
/// <para>
/// The number of internal buffers (ring size) should match the swapchain image
/// count so that every in-flight frame has its own private buffer.
/// </para>
/// </summary>
/// <typeparam name="T">The unmanaged element type stored in each buffer.</typeparam>
public sealed class RingElementBuffer<T> : IDisposable
    where T : unmanaged
{
    private static readonly ILogger _logger = LogManager.Create<RingElementBuffer<T>>();

    private readonly ElementBuffer<T>[] _buffers;
    private int _currentIndex;
    private bool _disposed;

    /// <summary>
    /// Gets the number of buffer slots in the ring.
    /// </summary>
    public int RingSize => _buffers.Length;

    /// <summary>
    /// Gets the index of the current (writable) buffer slot.
    /// </summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Gets the <see cref="ElementBuffer{T}"/> that is currently active for writing.
    /// After calling <see cref="Advance"/>, this becomes the next slot in the ring.
    /// </summary>
    public ElementBuffer<T> Current => _buffers[_currentIndex];

    /// <summary>
    /// Gets the GPU buffer handle of the current active buffer.
    /// </summary>
    public BufferResource Buffer => Current.Buffer;

    /// <summary>
    /// Gets the GPU address of the current active buffer.
    /// </summary>
    public ulong GpuAddress => Current.Buffer.GpuAddress;

    /// <summary>
    /// Gets the element count of the current active buffer.
    /// </summary>
    public int Count => Current.Count;

    /// <summary>
    /// Gets the capacity (in elements) of the current active buffer.
    /// </summary>
    public int Capacity => Current.Capacity;

    /// <summary>
    /// Creates a new ring buffer with the specified number of slots.
    /// </summary>
    /// <param name="context">The graphics context for GPU buffer creation.</param>
    /// <param name="ringSize">
    /// Number of buffer slots. Should match the number of in-flight frames
    /// (e.g., <see cref="IContext.GetNumSwapchainImages"/>).
    /// </param>
    /// <param name="initialCapacity">Initial element capacity per slot.</param>
    /// <param name="usage">Buffer usage flags applied to every slot.</param>
    /// <param name="debugName">Optional debug label prefix. Each slot is suffixed with its index.</param>
    public RingElementBuffer(
        IContext context,
        int ringSize,
        int initialCapacity,
        BufferUsageBits usage = BufferUsageBits.Storage,
        string? debugName = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ringSize, 1, nameof(ringSize));
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity, nameof(initialCapacity));

        _buffers = new ElementBuffer<T>[ringSize];
        for (int i = 0; i < ringSize; i++)
        {
            _buffers[i] = new ElementBuffer<T>(
                context,
                initialCapacity,
                usage,
                isDynamic: true,
                debugName: debugName is not null ? $"{debugName}[{i}]" : null
            );
        }
    }

    /// <summary>
    /// Advances the ring to the next buffer slot. Call this once per frame
    /// <b>before</b> writing any data.
    /// </summary>
    public void Advance()
    {
        _currentIndex = (_currentIndex + 1) % _buffers.Length;
    }

    /// <summary>
    /// Advances to the next ring slot and records a GPU-side buffer copy from the
    /// previous slot into the new one. After this call the CPU only needs to patch
    /// the elements that actually changed via <see cref="WriteDynamic"/>.
    /// <para>
    /// The copy command is recorded into <paramref name="cmdBuf"/> and will execute
    /// on the GPU timeline before any subsequent render passes in the same submission.
    /// </para>
    /// </summary>
    /// <param name="cmdBuf">The command buffer to record the copy into.</param>
    /// <returns><c>true</c> if a copy was recorded; <c>false</c> if the previous
    /// slot was empty (nothing to copy).</returns>
    public bool AdvanceWithCopy(ICommandBuffer cmdBuf)
    {
        int prevIndex = _currentIndex;
        Advance();

        var prev = _buffers[prevIndex];
        var cur = Current;

        if (prev.Count <= 0 || !prev.Buffer.Valid)
        {
            return false;
        }

        // Ensure the destination slot can hold the data.
        cur.EnsureCapacity(prev.Count);

        unsafe
        {
            var byteSize = (size_t)(prev.Count * sizeof(T));
            cmdBuf.CopyBuffer(prev.Buffer, 0, cur.Buffer, 0, byteSize);
        }

        // Sync the CPU-side element count so that subsequent reads
        // (e.g. draw-call count) reflect the copied data.
        cur.Count = prev.Count;

        return true;
    }

    /// <summary>
    /// Ensures that the current buffer has at least <paramref name="minCapacity"/> elements.
    /// If the buffer is too small it is resized (the old buffer is disposed internally
    /// by <see cref="ElementBuffer{T}"/>).
    /// </summary>
    /// <param name="minCapacity">Minimum required element count.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode EnsureCapacity(int minCapacity)
    {
        return Current.EnsureCapacity(minCapacity);
    }

    /// <summary>
    /// Writes data into the current buffer using a delegate that receives a
    /// <see cref="SafeWriteContext"/> for direct mapped-memory writes.
    /// </summary>
    /// <param name="totalCount">Total element count to write.</param>
    /// <param name="writeAction">
    /// A callback that performs the actual memory writes via the supplied
    /// <see cref="SafeWriteContext"/>.
    /// </param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode WriteDynamic(int totalCount, Action<SafeWriteContext> writeAction)
    {
        return Current.WriteDynamic(totalCount, writeAction);
    }

    /// <summary>
    /// Uploads a <see cref="FastList{T}"/> to the current buffer.
    /// </summary>
    public ResultCode Upload(FastList<T> data)
    {
        return Current.Upload(data);
    }

    /// <summary>
    /// Resets all buffer slots, releasing GPU memory.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i].Reset();
        }
    }

    #region IDisposable

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    _buffers[i].Dispose();
                }
            }
            _disposed = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
