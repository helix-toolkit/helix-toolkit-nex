using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a GPU buffer for storing structured element data (SSBO) with automatic resizing capability.
/// </summary>
/// <typeparam name="T">The unmanaged element type stored in the buffer.</typeparam>
/// <remarks>
/// <para>
/// ElementBuffer is designed to simplify the management of Storage Buffers (SSBO) that contain
/// collections of unmanaged data. It automatically handles:
/// <list type="bullet">
/// <item>Buffer creation and resizing based on capacity requirements</item>
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
    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElementBuffer{T}"/> class with the specified capacity.
    /// </summary>
    /// <param name="context">The graphics context used to create GPU buffers.</param>
    /// <param name="capacity">The initial capacity (number of elements) of the buffer.</param>
    /// <param name="isDynamic">
    /// If true, creates a dynamic buffer using HOST_VISIBLE storage for frequent CPU updates via map/unmap.
    /// If false, creates a static buffer using Device storage for optimal GPU performance.
    /// </param>
    public ElementBuffer(
        IContext context,
        int capacity,
        BufferUsageBits usage = BufferUsageBits.Storage,
        bool isDynamic = false
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity, nameof(capacity));
        Context = context;
        Capacity = capacity;
        IsDynamic = isDynamic;
        Usage = usage;

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
    /// <b>Dynamic buffers:</b> If the data size exceeds capacity, a new larger buffer is created
    /// with 1.5x the required size (for growth room), and the old buffer is disposed.
    /// Data is uploaded via direct memory mapping for optimal performance.
    /// </item>
    /// <item>
    /// <b>Static buffers:</b> If the data size exceeds capacity, the buffer is recreated with
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
    /// <remarks>If the buffer is dynamic and the required capacity exceeds the current capacity, the buffer
    /// is resized with additional capacity  to optimize future growth. For static buffers, the buffer is resized to the
    /// exact required capacity.</remarks>
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
            if (IsDynamic)
            {
                // For dynamic buffers, grow with some extra capacity (1.5x)
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
            else
            {
                // For static buffers, recreate with exact size needed
                var result = ResizeBuffer(requiredCapacity);
                if (result.HasError())
                {
                    return result;
                }
            }
        }

        // Upload data to buffer
        return UploadData(data, start, count, dstOffset);
    }

    /// <summary>
    /// Ensures the buffer has at least the specified capacity, resizing if necessary.
    /// </summary>
    /// <param name="minCapacity">The minimum required capacity in number of elements.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of the operation.
    /// Returns <see cref="ResultCode.Ok"/> if capacity is sufficient or resizing succeeded.
    /// </returns>
    public ResultCode EnsureCapacity(int minCapacity)
    {
        if (minCapacity <= Capacity)
        {
            return ResultCode.Ok;
        }

        return ResizeBuffer(minCapacity);
    }

    /// <summary>
    /// Creates or recreates the internal GPU buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">The capacity in number of elements.</param>
    /// <returns>
    /// A <see cref="ResultCode"/> indicating the result of buffer creation.
    /// </returns>
    private ResultCode CreateBuffer(int capacity)
    {
        if (capacity == 0)
        {
            return ResultCode.ArgumentOutOfRange;
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
                    GraphicsSettings.EnableDebug ? $"ElementBuffer<{typeof(T).Name}>" : null
                ),
                out var newBuffer,
                GraphicsSettings.EnableDebug ? $"ElementBuffer<{typeof(T).Name}>" : null
            );

            if (result.HasError())
            {
                _logger.LogError(
                    "Failed to create ElementBuffer with capacity {CAPACITY}: {REASON}",
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
    /// Resizes the buffer by disposing the old buffer and creating a new one with the specified capacity.
    /// </summary>
    /// <param name="newCapacity">The new capacity in number of elements.</param>
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

        // Create new buffer with updated capacity
        return CreateBuffer(newCapacity);
    }

    private ResultCode UploadData(T[] data, int start, int count, int dstOffset = 0)
    {
        if (Buffer.Empty)
        {
            _logger.LogError("Cannot upload data: buffer is not initialized.");
            return ResultCode.InvalidState;
        }
        if (start + count >= data.Length)
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
                    "Cannot upload data: destination offset {DST_OFFSET} plus count {COUNT} exceeds buffer capacity {CAPACITY}.",
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

    public static implicit operator BufferResource(ElementBuffer<T> elementBuffer)
    {
        return elementBuffer.Buffer;
    }

    public static implicit operator BufferHandle(ElementBuffer<T> elementBuffer)
    {
        return elementBuffer.Buffer.Handle;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                if (!Buffer.Empty)
                {
                    Buffer.Dispose();
                    Buffer = BufferResource.Null;
                }
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
