namespace HelixToolkit.Nex;

public sealed class Pool<ObjectType, ImplObjectType> where ObjectType : new()
{
    const uint32_t kListEndSentinel = 0xFFFFFFFF; // Sentinel value to indicate the end of the list
    public struct PoolEntry(ImplObjectType obj)
    {
        public uint32_t gen = 1;
        public uint32_t nextFree = kListEndSentinel; // Index of the next free object in the pool
        public ImplObjectType? obj = obj; // The actual object in the pool       
    }

    uint32_t gen_ = 1;
    uint32_t freeListHead_ = kListEndSentinel;

    public int32_t Count { private set; get; }

    readonly FastList<PoolEntry> objects_ = new(1024); // Initial capacity of 1024    

    public IReadOnlyList<PoolEntry> Objects => objects_;

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

    public void Destroy(ref Handle<ObjectType> handle)
    {
        Destroy(handle);
        handle = new Handle<ObjectType>(); // Reset the handle after destruction
    }

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

    public Handle<ObjectType> GetHandle(int32_t index)
    {
        if (index < 0 || index >= objects_.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index out of range.");
        }
        var entry = objects_[index];
        return new Handle<ObjectType>((uint32_t)index, entry.gen);
    }

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

    public void Clear()
    {
        objects_.Clear();
        freeListHead_ = kListEndSentinel;
        Count = 0;
    }
}
