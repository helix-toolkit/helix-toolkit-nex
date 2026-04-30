namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// A ring buffer that maintains multiple fixed-size GPU buffers, each exactly <c>sizeof(T)</c> bytes.
/// Rotates through them each frame to avoid GPU/CPU contention when uploading a single struct per frame,
/// such as per-frame uniform or push-constant data.
/// <para>
/// The number of internal buffers (ring size) should match the swapchain image count so that every
/// in-flight frame has its own private buffer.
/// </para>
/// </summary>
/// <typeparam name="T">The unmanaged struct type stored in each buffer.</typeparam>
public sealed class RingFixSizeBuffer<T> : IDisposable
    where T : unmanaged
{
    private static readonly ILogger _logger = LogManager.Create<RingFixSizeBuffer<T>>();

    private readonly IContext _context;
    private readonly BufferResource[] _buffers;
    private readonly nint[] _mappedPtr;
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
    /// Gets the <see cref="BufferResource"/> that is currently active for writing.
    /// </summary>
    public BufferHandle Current => _buffers[_currentIndex];

    /// <summary>
    /// Gets the GPU buffer handle of the current active buffer.
    /// </summary>
    public BufferHandle Buffer => Current;

    /// <summary>
    /// Gets the GPU address of the current active buffer.
    /// </summary>
    public ulong GpuAddress => _buffers[_currentIndex].GpuAddress;

    /// <summary>
    /// Gets the size in bytes of each buffer slot, which is always <c>sizeof(T)</c>.
    /// </summary>
    public static unsafe int SizeInBytes => sizeof(T);

    /// <summary>
    /// Creates a new fixed-size ring buffer with the specified number of slots.
    /// Each slot holds exactly one <typeparamref name="T"/> (i.e., <c>sizeof(T)</c> bytes).
    /// </summary>
    /// <param name="context">The graphics context for GPU buffer creation.</param>
    /// <param name="ringSize">
    /// Number of buffer slots. Should match the number of in-flight frames
    /// (e.g., <see cref="IContext.GetNumSwapchainImages"/>).
    /// </param>
    /// <param name="usage">Buffer usage flags applied to every slot.</param>
    /// <param name="debugName">Optional debug label prefix. Each slot is suffixed with its index.</param>
    public RingFixSizeBuffer(
        IContext context,
        int ringSize,
        BufferUsageBits usage = BufferUsageBits.Storage,
        bool hostVisible = true,
        string? debugName = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ringSize, 1, nameof(ringSize));
        _context = context;
        _buffers = new BufferResource[ringSize];
        _mappedPtr = new nint[ringSize];
        var storage = StorageType.Device;
        if (hostVisible)
        {
            storage |= StorageType.HostVisible;
        }
        for (int i = 0; i < ringSize; i++)
        {
            _buffers[i] = context.CreateBuffer(
                new T(),
                BufferUsageBits.Storage,
                storage,
                $"ring_{debugName ?? ""}[{i}]"
            );
            if (hostVisible)
            {
                _mappedPtr[i] = _context.GetMappedPtr(_buffers[i]);
            }
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
    /// Writes a single <typeparamref name="T"/> value into the current buffer slot.
    /// </summary>
    /// <param name="cmdBuffer">The command buffer to record the update command into.</param>
    /// <param name="value">The value to upload.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode Update(ICommandBuffer cmdBuffer, ref T value)
    {
        return cmdBuffer.UpdateBuffer(Current, ref value);
    }

    /// <summary>
    /// Advances to the next ring slot then writes a single <typeparamref name="T"/> value.
    /// Equivalent to calling <see cref="Advance"/> followed by <see cref="Write"/>.
    /// </summary>
    /// <param name="cmdBuffer">The command buffer to record the update command into.</param>
    /// <param name="value">The value to upload.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode AdvanceAndUpdate(ICommandBuffer cmdBuffer, ref T value)
    {
        Advance();
        if (!Current.Valid)
        {
            return ResultCode.InvalidState;
        }
        return Update(cmdBuffer, ref value);
    }

    /// <summary>
    /// Writes a single <typeparamref name="T"/> value into the current buffer slot.
    /// </summary>
    /// <param name="value">The value to upload.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode Update(ref T value)
    {
        if (!Current.Valid)
        {
            return ResultCode.InvalidState;
        }
        if (_mappedPtr[_currentIndex] != nint.Zero)
        {
            unsafe
            {
                *(T*)_mappedPtr[_currentIndex] = value;
            }
            return ResultCode.Ok;
        }
        return _context.Upload(Current, 0, ref value);
    }

    /// <summary>
    /// Advances to the next ring slot then writes a single <typeparamref name="T"/> value.
    /// Equivalent to calling <see cref="Advance"/> followed by <see cref="Update"/>.
    /// </summary>
    /// <param name="value">The value to upload.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode AdvanceAndUpdate(ref T value)
    {
        Advance();
        if (!Current.Valid)
        {
            return ResultCode.InvalidState;
        }
        return _context.Upload(Current, 0, ref value);
    }

    /// <summary>
    /// Resets all buffer slots, releasing GPU memory.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i].Reset();
            _mappedPtr[i] = nint.Zero;
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
