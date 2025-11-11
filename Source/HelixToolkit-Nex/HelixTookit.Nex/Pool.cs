namespace HelixToolkit.Nex;

/// <summary>
/// A generic object pool implementation that manages reusable objects with generational handles.
/// </summary>
/// <typeparam name="ObjectType">The type used for handle creation (marker type).</typeparam>
/// <typeparam name="ImplObjectType">The actual implementation type of objects stored in the pool.</typeparam>
/// <remarks>
/// This pool uses a free-list algorithm for efficient allocation and deallocation of objects.
/// Each object is associated with a generation number to prevent the ABA problem and ensure handle validity.
/// </remarks>
public sealed class Pool<ObjectType, ImplObjectType> where ObjectType : new()
{
    const uint32_t kListEndSentinel = 0xFFFFFFFF; // Sentinel value to indicate the end of the list

    /// <summary>
    /// Represents an entry in the object pool containing the object and metadata.
    /// </summary>
    /// <param name="obj">The object instance stored in this entry.</param>
    public struct PoolEntry(ImplObjectType obj)
    {
        /// <summary>
        /// The generation number for this entry, used to detect stale handles.
        /// </summary>
        public uint32_t gen = 1;

        /// <summary>
 /// Index of the next free object in the free list, or <see cref="kListEndSentinel"/> if none.
        /// </summary>
        public uint32_t nextFree = kListEndSentinel; // Index of the next free object in the pool

        /// <summary>
        /// The actual object stored in this pool entry.
        /// </summary>
      public ImplObjectType? obj = obj; // The actual object in the pool       
  }

  uint32_t gen_ = 1;
    uint32_t freeListHead_ = kListEndSentinel;

    /// <summary>
 /// Gets the current number of active (non-freed) objects in the pool.
    /// </summary>
    public int32_t Count { private set; get; }

    readonly FastList<PoolEntry> objects_ = new(1024); // Initial capacity of 1024    

    /// <summary>
    /// Gets a read-only view of all pool entries (both active and freed).
    /// </summary>
    public IReadOnlyList<PoolEntry> Objects => objects_;

    /// <summary>
    /// Creates a new object in the pool and returns a handle to it.
    /// </summary>
    /// <param name="obj">The object to add to the pool.</param>
    /// <returns>A <see cref="Handle{T}"/> that can be used to retrieve or destroy the object.</returns>
    public Handle<ObjectType> Create(in ImplObjectType obj)
    {
        int idx;
        if (freeListHead_ != kListEndSentinel)
        {
            // No free objects, create a new one
            idx = (int32_t)freeListHead_;
            freeListHead_ = objects_[idx].nextFree;
            ref var entry = ref objects_.GetInternalArray()[idx];
            entry.obj = obj;
        }
        else
        {
            idx = objects_.Count;
            objects_.Add(new PoolEntry(obj));
        }
        ++Count;
        return new Handle<ObjectType>((uint32_t)idx, objects_[idx].gen);
    }

    /// <summary>
    /// Destroys an object in the pool and resets the handle.
    /// </summary>
  /// <param name="handle">Reference to the handle to destroy. Will be set to an empty handle after destruction.</param>
    public void Destroy(ref Handle<ObjectType> handle)
    {
        Destroy(handle);
        handle = new Handle<ObjectType>(); // Reset the handle after destruction
    }

    /// <summary>
    /// Destroys an object in the pool identified by the handle.
    /// </summary>
    /// <param name="handle">The handle to the object to destroy.</param>
    /// <exception cref="ArgumentException">Thrown if the handle is invalid or has an incorrect generation.</exception>
    public void Destroy(Handle<ObjectType> handle)
    {
        if (handle.Empty)
        {
            return;
        }
        int32_t idx = (int32_t)handle.Index;
        if (idx < 0 || idx >= objects_.Count || objects_[idx].gen != handle.Gen)
        {
            throw new ArgumentException("Invalid handle for destruction.");
        }
        ref var entry = ref objects_.GetInternalArray()[idx];
        if (entry.obj is IDisposable disposableObj)
        {
            disposableObj.Dispose(); // Dispose the object if it implements IDisposable
        }
        entry.obj = default; // Clear the object reference
        entry.gen = gen_++;
        entry.nextFree = freeListHead_;
        freeListHead_ = (uint32_t)idx;
        --Count;
    }

    /// <summary>
    /// Retrieves the object associated with the given handle.
    /// </summary>
    /// <param name="handle">The handle to the object.</param>
    /// <returns>The object, or null if not found.</returns>
    /// <exception cref="ArgumentException">Thrown if the handle is invalid or has an incorrect generation.</exception>
    public ImplObjectType? Get(in Handle<ObjectType> handle)
    {
        if (handle.Empty)
        {
            throw new ArgumentException("Invalid handle for retrieval.");
        }
        int32_t idx = (int32_t)handle.Index;
        return idx < 0 || idx >= objects_.Count || objects_[idx].gen != handle.Gen
            ? throw new ArgumentException("Invalid handle for retrieval.")
            : objects_.GetInternalArray()[idx].obj;
    }

    /// <summary>
    /// Gets a handle for an object at a specific index in the pool.
    /// </summary>
    /// <param name="index">The index of the object in the pool.</param>
 /// <returns>A <see cref="Handle{T}"/> for the object at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is out of range.</exception>
    public Handle<ObjectType> GetHandle(int32_t index)
    {
        if (index < 0 || index >= objects_.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index out of range.");
        }
        var entry = objects_[index];
        return new Handle<ObjectType>((uint32_t)index, entry.gen);
    }

    /// <summary>
    /// Finds an object in the pool and returns its handle.
    /// </summary>
    /// <param name="obj">The object to find.</param>
    /// <returns>A <see cref="Handle{T}"/> for the object, or an empty handle if not found.</returns>
    public Handle<ObjectType> FindObject(in ImplObjectType obj)
    {
        for (int32_t i = 0; i < objects_.Count; i++)
        {
            HxDebug.Assert(objects_[i].obj != null, "Object in pool should not be null.");
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (objects_[i].obj.Equals(obj))
            {
                return new Handle<ObjectType>((uint32_t)i, objects_[i].gen);
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        return new Handle<ObjectType>();
    }

    /// <summary>
  /// Clears all objects from the pool.
    /// </summary>
    public void Clear()
    {
        objects_.Clear();
        freeListHead_ = kListEndSentinel;
        Count = 0;
    }
}
