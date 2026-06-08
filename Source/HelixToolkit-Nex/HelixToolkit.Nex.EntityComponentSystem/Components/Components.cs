namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Base ref struct for components.
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct Components<T>
{
    private readonly FastList<T> _storage;
    private readonly FastList<ComponentManager<T>.ComponentMappingKey> _mapping;

    public readonly int Count => _storage.Count;
    public readonly World World;

    /// <summary>
    /// Initializes a new instance of the <see cref="Components{T}" /> struct.
    /// </summary>
    /// <param name="world">World that components belongs to.</param>
    /// <param name="mapping">The mapping.</param>
    /// <param name="components">The components.</param>
    internal Components(
        World world,
        FastList<ComponentManager<T>.ComponentMappingKey> mapping,
        FastList<T> components
    )
    {
        World = world;
        _mapping = mapping;
        _storage = components;
    }

    /// <summary>
    /// Gets the component with the specified entity.
    /// </summary>
    /// <value>
    /// The component.
    /// </value>
    /// <param name="entity">The entity.</param>
    /// <returns></returns>
    public ref T this[Entity entity]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(entity.Valid);
            Debug.Assert(_mapping.Count > entity.Id);
            Debug.Assert(_storage.Count > _mapping[entity.Id].ComponentIndex);
            return ref _storage.GetInternalArray()[_mapping[entity.Id].ComponentIndex];
        }
    }

    /// <summary>
    /// Gets the component by index directly from component storage
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return ref _storage.GetInternalArray()[index]; }
    }

    public override bool Equals(object? obj)
    {
        return false;
    }

    public override int GetHashCode()
    {
        return _storage.GetHashCode();
    }

    public readonly MappingEnumerator GetEnumerator()
    {
        return new MappingEnumerator(_mapping, _storage);
    }

    public readonly T[] GetInternalArray()
    {
        return _storage.GetInternalArray();
    }

    public readonly EntityEnumerable GetEntities()
    {
        return new EntityEnumerable(_mapping);
    }

    /// <summary>
    /// Provides a struct-based enumerable for iterating over valid entity IDs
    /// without heap allocation.
    /// </summary>
    public readonly struct EntityEnumerable
    {
        private readonly FastList<ComponentManager<T>.ComponentMappingKey> _mapping;

        internal EntityEnumerable(FastList<ComponentManager<T>.ComponentMappingKey> mapping)
        {
            _mapping = mapping;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityEnumerator GetEnumerator()
        {
            return new EntityEnumerator(_mapping);
        }
    }

    /// <summary>
    /// Struct-based enumerator for iterating over valid entity IDs
    /// without heap allocation.
    /// </summary>
    public struct EntityEnumerator
    {
        private readonly FastList<ComponentManager<T>.ComponentMappingKey> _mapping;
        private int _index;

        internal EntityEnumerator(FastList<ComponentManager<T>.ComponentMappingKey> mapping)
        {
            _mapping = mapping;
            _index = -1;
        }

        public int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var mappingArray = _mapping.GetInternalArray();
            var mappingCount = _mapping.Count;
            while (++_index < mappingCount)
            {
                if (mappingArray[_index].Valid)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Enumerates components in storage using the mapping's ComponentIndex,
    /// skipping invalid (unassigned) mapping entries.
    /// </summary>
    public struct MappingEnumerator
    {
        private readonly FastList<ComponentManager<T>.ComponentMappingKey> _mapping;
        private readonly FastList<T> _storage;
        private int _index;

        internal MappingEnumerator(
            FastList<ComponentManager<T>.ComponentMappingKey> mapping,
            FastList<T> storage
        )
        {
            _mapping = mapping;
            _storage = storage;
            _index = -1;
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                ref _storage.GetInternalArray()[_mapping.GetInternalArray()[_index].ComponentIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var mappingArray = _mapping.GetInternalArray();
            var mappingCount = _mapping.Count;
            var storageCount = _storage.Count;
            while (++_index < mappingCount)
            {
                ref var key = ref mappingArray[_index];
                if (key.Valid && key.ComponentIndex >= 0 && key.ComponentIndex < storageCount)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
