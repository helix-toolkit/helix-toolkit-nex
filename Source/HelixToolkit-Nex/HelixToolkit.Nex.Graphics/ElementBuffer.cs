namespace HelixToolkit.Nex.Graphics;

public struct SafeWriteContext(IntPtr mappedPtr, int remainSizeInBytes)
{
    public IntPtr MappedPtr { get; private set; } = mappedPtr;
    public int RemainSizeInBytes { get; private set; } = remainSizeInBytes;

    /// <summary>
    /// Writes the specified data to the mapped memory region if sufficient space is available.
    /// </summary>
    /// <remarks>This method performs a memory copy operation to transfer the data to the mapped memory
    /// region.  The caller must ensure that the type <typeparamref name="T"/> is unmanaged, as required by the method's
    /// constraints.</remarks>
    /// <typeparam name="T">The type of the elements in the data span. Must be an unmanaged type.</typeparam>
    /// <param name="data">A read-only span containing the data to write.</param>
    /// <returns><see langword="true"/> if the data was successfully written; otherwise, <see langword="false"/> if there is
    /// insufficient space in the mapped memory region.</returns>
    public bool Write<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        unsafe
        {
            var dataSize = data.Length * sizeof(T);
            if (dataSize > RemainSizeInBytes)
            {
                return false;
            }
            unsafe
            {
                fixed (T* dataPtr = data)
                {
                    NativeHelper.MemoryCopy(MappedPtr, (nint)dataPtr, (uint)dataSize);
                    MappedPtr += dataSize;
                    RemainSizeInBytes -= dataSize;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Writes the contents of the specified <see cref="FastList{T}"/> to the current buffer.
    /// </summary>
    /// <remarks>This method performs a memory copy operation to transfer the data from the specified list to
    /// the buffer.  The buffer's remaining size is reduced by the size of the written data. If the size of the data
    /// exceeds  the available space in the buffer, the method returns <see langword="false"/> and no data is
    /// written.</remarks>
    /// <typeparam name="T">The type of elements in the <see cref="FastList{T}"/>. Must be an unmanaged type.</typeparam>
    /// <param name="data">The <see cref="FastList{T}"/> containing the data to write. The list must not be null.</param>
    /// <returns><see langword="true"/> if the data was successfully written to the buffer; otherwise, <see langword="false"/> if
    /// there is insufficient space in the buffer.</returns>
    public ResultCode Write<T>(FastList<T> data)
        where T : unmanaged
    {
        unsafe
        {
            var dataSize = data.Count * sizeof(T);
            if (dataSize > RemainSizeInBytes)
            {
                return ResultCode.OutOfMemory;
            }
            unsafe
            {
                using var ptr = data.GetInternalArray().Pin();
                NativeHelper.MemoryCopy(MappedPtr, (nint)ptr.Pointer, (uint)dataSize);
                MappedPtr += dataSize;
                RemainSizeInBytes -= dataSize;
            }
            return ResultCode.Ok;
        }
    }

    /// <summary>
    /// Writes the specified data of an unmanaged type to the current memory-mapped region.
    /// </summary>
    /// <remarks>This method writes the data to the memory location pointed to by the current pointer  and
    /// advances the pointer by the size of the data. The remaining size in bytes is also  reduced accordingly. Ensure
    /// that the memory-mapped region has sufficient space before  calling this method.</remarks>
    /// <typeparam name="T">The type of the data to write. Must be an unmanaged type.</typeparam>
    /// <param name="data">The data to write. The value is passed by reference.</param>
    /// <returns><see langword="true"/> if the data was successfully written to the memory-mapped region;  otherwise, <see
    /// langword="false"/> if there is insufficient space remaining.</returns>
    public bool Write<T>(T data)
        where T : unmanaged
    {
        return Write(ref data);
    }

    /// <summary>
    /// Writes the specified data of an unmanaged type to the current memory-mapped region.
    /// </summary>
    /// <remarks>This method writes the data to the memory location pointed to by the current pointer  and
    /// advances the pointer by the size of the data. The remaining size in bytes is also  reduced accordingly. Ensure
    /// that the memory-mapped region has sufficient space before  calling this method.</remarks>
    /// <typeparam name="T">The type of the data to write. Must be an unmanaged type.</typeparam>
    /// <param name="data">The data to write. The value is passed by reference.</param>
    /// <returns><see langword="true"/> if the data was successfully written to the memory-mapped region;  otherwise, <see
    /// langword="false"/> if there is insufficient space remaining.</returns>
    public bool Write<T>(ref T data)
        where T : unmanaged
    {
        unsafe
        {
            var dataSize = sizeof(T);
            if (dataSize > RemainSizeInBytes)
            {
                return false;
            }
            var ptr = (T*)MappedPtr.ToPointer();
            *ptr = data;
            MappedPtr += dataSize;
            RemainSizeInBytes -= dataSize;
            return true;
        }
    }
}

/// <summary>
/// Represents a GPU buffer for storing structured element data (SSBO) with automatic resizing capability.
/// </summary>
/// <typeparam name="T">The unmanaged element type stored in the buffer.</typeparam>
/// <remarks>
/// <para>
/// ElementBuffer is designed to simplify the management of Storage Buffers (SSBO) that contain
/// collections of unmanaged data. It automatically handles:
/// <list type="bullet">
/// <item>Buffer creation and resizing based on remainSizeInBytes requirements</item>
/// <item>Different allocation strategies for dynamic vs static buffers</item>
/// <item>Efficient uploads of FastList data to GPU</item>
/// </list>
/// </para>
/// <para>
/// <b>Dynamic Buffers (IsDynamic = true):</b><br/>
/// - Use HOST_VISIBLE storage for direct CPU writes via map/unmap<br/>
/// - Resize by creating larger buffers only when needed<br/>
/// - Optimal for frequently updated data (e.g., per-frame updates)
/// </para>
/// <para>
/// <b>Static Buffers (IsDynamic = false):</b><br/>
/// - Use Device-local storage for best GPU performance<br/>
/// - Recreate the buffer when resizing is required<br/>
/// - Optimal for data that changes infrequently
/// </para>
/// </remarks>
public sealed class ElementBuffer<T> : IDisposable
    where T : unmanaged
{
    private static readonly ILogger _logger = LogManager.Create<ElementBuffer<T>>();

    public IContext Context { get; }
    public BufferResource Buffer { get; private set; } = BufferResource.Null;
    public int Capacity { get; private set; }
    public int Count { private set; get; } = 0;
    public bool IsDynamic { get; }
    public BufferUsageBits Usage { get; }

    public string? DebugName { get; }

    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElementBuffer{T}"/> class with the specified remainSizeInBytes.
    /// </summary>
    /// <param name="context">The graphics context used to create GPU buffers.</param>
    /// <param name="capacity">The initial remainSizeInBytes (number of elements) of the buffer.</param>
    /// <param name="isDynamic">
    /// If true, creates a dynamic buffer using HOST_VISIBLE storage for frequent CPU updates via map/unmap.
    /// If false, creates a static buffer using Device storage for optimal GPU performance.
    /// </param>
    public ElementBuffer(
        IContext context,
        int capacity,
        BufferUsageBits usage = BufferUsageBits.Storage,
        bool isDynamic = false,
        string? debugName = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity, nameof(capacity));
        Context = context;
        Capacity = capacity;
        IsDynamic = isDynamic;
        Usage = usage;
        DebugName = debugName;

        if (capacity > 0)
        {
            CreateBuffer(capacity);
        }
    }

    /// <summary>
    /// Uploads the contents of a FastList to the GPU buffer, automatically resizing if necessary.
    /// </summary>
    /// <param name="data">The list of elements to upload to the GPU.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of the operation.
    /// Returns <see cref="ResultCode.Ok"/> on success.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method automatically handles buffer resizing based on the <see cref="IsDynamic"/> flag:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>Dynamic buffers:</b> If the data size exceeds remainSizeInBytes, a new larger buffer is created
    /// with 1.5x the required size (for growth room), and the old buffer is disposed.
    /// Data is uploaded via direct memory mapping for optimal performance.
    /// </item>
    /// <item>
    /// <b>Static buffers:</b> If the data size exceeds remainSizeInBytes, the buffer is recreated with
    /// exactly the required size, and the old buffer is disposed.
    /// Data is uploaded via the context's staging mechanism.
    /// </item>
    /// </list>
    /// </remarks>
    public ResultCode Upload(FastList<T> data)
    {
        if (data is null)
        {
            return ResultCode.ArgumentNull;
        }
        return Upload(data.GetInternalArray(), 0, data.Count, 0);
    }

    /// <summary>
    /// Uploads a subset of data from the specified list to the destination starting at the given offset.
    /// </summary>
    /// <remarks>The method validates that the range defined by <paramref name="start"/> and <paramref
    /// name="count"/>  does not exceed the bounds of the <paramref name="data"/> list. If the range is invalid, the
    /// method  returns <see cref="ResultCode.ArgumentOutOfRange"/> without performing the upload.</remarks>
    /// <param name="data">The list containing the data to upload.</param>
    /// <param name="start">The zero-based index in the list at which to begin uploading data.</param>
    /// <param name="count">The number of elements to upload from the list.</param>
    /// <param name="dstOffset">The zero-based offset in the destination where the data will be written.</param>
    /// <returns>A <see cref="ResultCode"/> indicating the outcome of the operation.  Returns <see
    /// cref="ResultCode.ArgumentOutOfRange"/> if the specified range exceeds the bounds of the list.</returns>
    public ResultCode Upload(FastList<T> data, int start, int count, int dstOffset)
    {
        if (start + count > data.Count)
        {
            return ResultCode.ArgumentOutOfRange;
        }
        return Upload(data.GetInternalArray(), start, count, dstOffset);
    }

    /// <summary>
    /// Uploads a subset of data to the buffer, resizing the buffer if necessary.
    /// </summary>
    /// <remarks>If the buffer is dynamic and the required remainSizeInBytes exceeds the current remainSizeInBytes, the buffer
    /// is resized with additional remainSizeInBytes  to optimize future growth. For static buffers, the buffer is resized to the
    /// exact required remainSizeInBytes.</remarks>
    /// <param name="data">The array of data to upload. Cannot be <see langword="null"/>.</param>
    /// <param name="start">The zero-based index in the <paramref name="data"/> array at which to begin uploading.</param>
    /// <param name="count">The number of elements to upload, starting from <paramref name="start"/>. Must be greater than 0.</param>
    /// <param name="dstOffset">The destination buffer offset by bytes.</param>
    /// <returns>A <see cref="ResultCode"/> indicating the outcome of the operation.  Returns <see cref="ResultCode.Ok"/> if the
    /// upload is successful,  <see cref="ResultCode.ArgumentOutOfRange"/> if the specified range exceeds the bounds of
    /// the <paramref name="data"/> array,  or an error code if resizing the buffer fails.</returns>
    public ResultCode Upload(T[] data, int start, int count, int dstOffset)
    {
        if (data is null)
        {
            return ResultCode.ArgumentNull;
        }
        if (count <= 0)
        {
            return ResultCode.Ok;
        }
        if (start + count > data.Length)
        {
            return ResultCode.ArgumentOutOfRange;
        }

        var requiredCapacity = count;

        // Handle resizing based on IsDynamic flag
        if (requiredCapacity > Capacity)
        {
            EnsureCapacity(requiredCapacity);
        }

        // Upload data to buffer
        return UploadData(data, start, count, dstOffset);
    }

    /// <summary>
    /// Writes data to a dynamic buffer, resizing the buffer if necessary to accommodate the specified total count.
    /// </summary>
    /// <remarks>This method can only be used with dynamic buffers. If the buffer is not dynamic, the method
    /// logs an error and returns <see cref="ResultCode.InvalidState"/>. <para> If the specified <paramref
    /// name="totalCount"/> exceeds the current buffer capacity, the buffer is resized to ensure sufficient space. The
    /// new capacity is calculated as 1.5 times the required capacity, clamped to a valid range. </para> <para> The
    /// <paramref name="writeAction"/> is executed within the context of a mapped buffer. If the buffer cannot be
    /// mapped, the method logs an error and returns <see cref="ResultCode.InvalidState"/>. </para> <para> After the
    /// write operation, the method flushes the mapped memory to ensure data coherence and updates the buffer's element
    /// count to <paramref name="totalCount"/>. </para></remarks>
    /// <param name="totalCount">The total number of elements to write to the buffer. Must be greater than 0.</param>
    /// <param name="writeAction">An action that performs the write operation. The action is provided with a <see cref="SafeWriteContext"/> that
    /// contains the mapped pointer and buffer size for writing.</param>
    /// <returns>A <see cref="ResultCode"/> indicating the outcome of the operation. Returns <see cref="ResultCode.Ok"/> if the
    /// write operation succeeds, or an error code if the operation fails.</returns>
    public ResultCode WriteDynamic(int totalCount, Action<SafeWriteContext> writeAction)
    {
        if (!IsDynamic)
        {
            _logger.LogError("WriteDynamic can only be used with dynamic buffers.");
            return ResultCode.InvalidState;
        }
        if (totalCount <= 0)
        {
            return ResultCode.Ok;
        }
        var requiredCapacity = totalCount;
        if (requiredCapacity > Capacity)
        {
            // For dynamic buffers, grow with some extra remainSizeInBytes (1.5x)
            var newCapacity = MathUtil.Clamp(
                (int)(requiredCapacity * 1.5f),
                (int)requiredCapacity,
                int.MaxValue
            );
            var result = ResizeBuffer(newCapacity);
            if (result.HasError())
            {
                return result;
            }
        }
        unsafe
        {
            nint mappedPtr = Context.GetMappedPtr(Buffer.Handle);
            if (mappedPtr == nint.Zero)
            {
                _logger.LogError("Cannot write data: dynamic buffer is not mapped.");
                return ResultCode.InvalidState;
            }
            writeAction(new SafeWriteContext(mappedPtr, Capacity * sizeof(T)));
            // Flush mapped memory if not coherent
            Context.FlushMappedMemory(Buffer.Handle, 0, (uint)(totalCount * sizeof(T)));
            Count = totalCount;
            return ResultCode.Ok;
        }
    }

    /// <summary>
    /// Ensures the buffer has at least the specified remainSizeInBytes, resizing if necessary.
    /// </summary>
    /// <param name="minCapacity">The minimum required remainSizeInBytes in number of elements.</param>
    /// <param name="exact">Use exact capacity size. Otherwise buffer will be resized to 1.5 times of the minCapacity if current capacity is less than minCapcity.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of the operation.
    /// Returns <see cref="ResultCode.Ok"/> if remainSizeInBytes is sufficient or resizing succeeded.
    /// </returns>
    public ResultCode EnsureCapacity(int minCapacity, bool exact = false)
    {
        if (minCapacity <= Capacity)
        {
            return ResultCode.Ok;
        }
        var multiplier = IsDynamic && !exact ? 1.5f : 1.0f;
        return ResizeBuffer((int)(minCapacity * multiplier));
    }

    /// <summary>
    /// Creates or recreates the internal GPU buffer with the specified remainSizeInBytes.
    /// </summary>
    /// <param name="capacity">The remainSizeInBytes in number of elements.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of buffer creation.
    /// </returns>
    private ResultCode CreateBuffer(int capacity)
    {
        if (capacity == 0)
        {
            return ResultCode.Ok;
        }

        unsafe
        {
            uint bufferSize = (uint)capacity * (uint)sizeof(T);
            var storageType = IsDynamic ? StorageType.HostVisible : StorageType.Device;

            var result = Context.CreateBuffer(
                new BufferDesc(
                    BufferUsageBits.Storage | Usage,
                    storageType,
                    nint.Zero,
                    bufferSize,
                    GraphicsSettings.EnableDebug
                        ? $"ElementBuffer<{typeof(T).Name}>:{DebugName ?? string.Empty}"
                        : null
                ),
                out var newBuffer,
                GraphicsSettings.EnableDebug
                    ? $"ElementBuffer<{typeof(T).Name}>:{DebugName ?? string.Empty}"
                    : null
            );

            if (result.HasError())
            {
                _logger.LogError(
                    "Failed to create ElementBuffer with remainSizeInBytes {CAPACITY}: {REASON}",
                    capacity,
                    result
                );
                return result;
            }

            Buffer = newBuffer;
            Capacity = capacity;

            return ResultCode.Ok;
        }
    }

    /// <summary>
    /// Resizes the buffer by disposing the old buffer and creating a new one with the specified remainSizeInBytes.
    /// </summary>
    /// <param name="newCapacity">The new remainSizeInBytes in number of elements.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of the resize operation.
    /// </returns>
    private ResultCode ResizeBuffer(int newCapacity)
    {
        // Dispose old buffer
        if (!Buffer.Empty)
        {
            Buffer.Dispose();
            Buffer = BufferResource.Null;
        }
        if (newCapacity == 0)
        {
            return ResultCode.Ok;
        }
        // Create new buffer with updated remainSizeInBytes
        return CreateBuffer(newCapacity);
    }

    private ResultCode UploadData(T[] data, int start, int count, int dstOffset = 0)
    {
        if (Buffer.Empty)
        {
            _logger.LogError("Cannot upload data: buffer is not initialized.");
            return ResultCode.InvalidState;
        }
        if (start + count > data.Length)
        {
            _logger.LogError(
                "Cannot upload data: specified range (start={START}, count={COUNT}) exceeds data array bounds (length={LENGTH}).",
                start,
                count,
                data.Length
            );
            return ResultCode.ArgumentOutOfRange;
        }
        if (dstOffset < 0)
        {
            _logger.LogError(
                "Cannot upload data: destination offset {DST_OFFSET} can not less than zero.",
                dstOffset
            );
            return ResultCode.ArgumentOutOfRange;
        }

        unsafe
        {
            if (dstOffset % sizeof(T) != 0)
            {
                _logger.LogError(
                    "Cannot upload data: destination offset {DST_OFFSET} must be aligned to element size {ELEMENT_SIZE}.",
                    dstOffset,
                    sizeof(T)
                );
                return ResultCode.ArgumentOutOfRange;
            }
            var newCount = dstOffset / sizeof(T) + count;
            if (newCount > Capacity)
            {
                _logger.LogError(
                    "Cannot upload data: destination offset {DST_OFFSET} plus count {COUNT} exceeds buffer remainSizeInBytes {CAPACITY}.",
                    dstOffset,
                    count,
                    Capacity
                );
                return ResultCode.ArgumentOutOfRange;
            }

            Count = newCount;

            uint dataSize = (uint)(count * sizeof(T));

            if (IsDynamic)
            {
                // For dynamic buffers, use mapped memory for direct CPU writes
                nint mappedPtr = Context.GetMappedPtr(Buffer.Handle);

                if (mappedPtr == nint.Zero)
                {
                    _logger.LogError("Cannot upload data: dynamic buffer is not mapped.");
                    return ResultCode.InvalidState;
                }
                mappedPtr += dstOffset;
                // Copy data directly to mapped memory
                using var pinnedData = data.Pin();
                var srcPtr = (nint)pinnedData.Pointer + start * sizeof(T);
                NativeHelper.MemoryCopy(mappedPtr, srcPtr, dataSize);

                // Flush mapped memory if not coherent
                Context.FlushMappedMemory(Buffer.Handle, (uint)dstOffset, dataSize);

                return ResultCode.Ok;
            }
            else
            {
                // For static buffers, use staging upload mechanism
                using var pinnedData = data.Pin();
                var srcPtr = (nint)pinnedData.Pointer + start * sizeof(T);
                var result = Context.Upload(Buffer.Handle, (uint)dstOffset, srcPtr, dataSize);

                if (result.HasError())
                {
                    _logger.LogError("Failed to upload data to ElementBuffer: {REASON}", result);
                    return result;
                }

                return ResultCode.Ok;
            }
        }
    }

    public void Reset()
    {
        Count = 0;
        ResizeBuffer(0);
    }

    public static implicit operator BufferResource(ElementBuffer<T> elementBuffer)
    {
        return elementBuffer.Buffer;
    }

    public static implicit operator BufferHandle(ElementBuffer<T> elementBuffer)
    {
        return elementBuffer.Buffer.Handle;
    }

    public static implicit operator ulong(ElementBuffer<T> elementBuffer)
    {
        return elementBuffer.Buffer.GpuAddress;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Reset();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
