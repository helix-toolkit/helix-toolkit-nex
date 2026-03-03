namespace HelixToolkit.Nex;

/// <summary>
/// A lock-free, single-producer / single-consumer (SPSC) ring buffer with a fixed capacity for unmanged data types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safety contract (SPSC):</b><br/>
/// Exactly <em>one</em> thread may call <see cref="Push"/> at a time, and exactly <em>one</em>
/// (possibly different) thread may call <see cref="TryPop"/> at a time.  Using more than one
/// producer <em>or</em> more than one consumer concurrently is a data race and produces undefined
/// behavior.
/// </para>
/// <para>
/// The implementation uses <see cref="Interlocked"/> memory barriers to guarantee that the item
/// written by the producer is fully visible to the consumer before the updated head index is
/// published, and vice-versa for the tail.
/// </para>
/// <para>
/// <b>Capacity:</b> The buffer stores at most <see cref="Capacity"/> items.  Once full,
/// <see cref="Push"/> returns <see langword="false"/> without blocking or throwing.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of items stored in the buffer.</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T?[] _buffer;

    private struct Positions()
    {
        public ulong Head = 0;
        private unsafe fixed byte _data[64 - sizeof(ulong)]; // Padding to prevent false sharing between head and tail.
        public ulong Tail = 0;
    }

    private Positions _positions;

    /// <summary>
    /// Gets the maximum number of items the buffer can hold at any one time.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets a snapshot of the number of items currently in the buffer.
    /// </summary>
    /// <remarks>
    /// Because the buffer is designed for concurrent use, this value may be stale by the time
    /// the caller acts on it.  The count is guaranteed to be in the range
    /// <c>[0, <see cref="Capacity"/>]</c>.
    /// </remarks>
    public int Count
    {
        get
        {
            // Read tail before head so that a concurrent Pop between the two reads cannot make
            // the difference appear negative.
            var tail = Interlocked.Read(ref _positions.Tail);
            var head = Interlocked.Read(ref _positions.Head);
            return (int)(head - tail);
        }
    }

    /// <summary>
    /// Initializes a new <see cref="RingBuffer{T}"/> with the specified capacity.
    /// </summary>
    /// <param name="capacity">
    /// The maximum number of items the buffer can hold.  Must be greater than zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is less than or equal to zero.
    /// </exception>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Capacity must be greater than zero."
            );
        }
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Attempts to add an item to the back of the buffer.
    /// </summary>
    /// <remarks>
    /// Must only be called from a single producer thread at a time.
    /// </remarks>
    /// <param name="item">The item to add.</param>
    /// <returns>
    /// <see langword="true"/> if the item was added successfully;
    /// <see langword="false"/> if the buffer is full.
    /// </returns>
    public bool Push(T item)
    {
        var head = Interlocked.Read(ref _positions.Head);
        if ((int)(head - Interlocked.Read(ref _positions.Tail)) >= Capacity)
        {
            return false;
        }
        var idx = (int)(head % (ulong)Capacity);
        _buffer[idx] = item;
        // Ensure the item write is visible to the consumer before the head increment.
        Interlocked.MemoryBarrier();
        Interlocked.Increment(ref _positions.Head);
        return true;
    }

    /// <summary>
    /// Attempts to remove and return the item at the front of the buffer.
    /// </summary>
    /// <remarks>
    /// Must only be called from a single consumer thread at a time.
    /// </remarks>
    /// <param name="item">
    /// When this method returns <see langword="true"/>, contains the dequeued item.
    /// When this method returns <see langword="false"/>, contains <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if an item was successfully removed;
    /// <see langword="false"/> if the buffer is empty.
    /// </returns>
    public bool TryPop(out T? item)
    {
        var tail = Interlocked.Read(ref _positions.Tail);
        if (Interlocked.Read(ref _positions.Head) == tail)
        {
            item = default;
            return false;
        }
        var idx = (int)(tail % (ulong)Capacity);
        item = _buffer[idx];
        // Clear the slot to release the reference and allow GC to reclaim it.
        _buffer[idx] = default;
        // Ensure the slot read completes before the tail increment is published.
        Interlocked.MemoryBarrier();
        Interlocked.Increment(ref _positions.Tail);
        return true;
    }
}
