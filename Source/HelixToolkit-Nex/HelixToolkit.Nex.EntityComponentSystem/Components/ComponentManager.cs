using System.Reflection;

namespace HelixToolkit.Nex.ECS;

internal static class ComponentSorting
{
    private static int[] GenerateIntervals(int n)
    {
        if (n < 2)
        {
            return Array.Empty<int>();
        }
        var t = Math.Max(1, (int)Math.Log(n, 3) - 1);
        var intervals = new int[t];
        intervals[0] = 1;
        for (var i = 1; i < t; i++)
        {
            intervals[i] = 3 * intervals[i - 1] + 1;
        }
        return intervals;
    }

    /// <summary>
    /// Ref: http://anh.cs.luc.edu/170/notes/CSharpHtml/sorting.html
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="manager"></param>
    private static void DoShellSort<T>(ComponentManager<T> manager)
        where T : ISortable<T>
    {
        int i,
            j,
            k,
            m;
        var mapping = manager.CompMapping;
        var entityMapping = manager.EntityMapping;
        var storage = manager.Storage;
        var n = storage.Count;
        var intervals = GenerateIntervals(n);
        for (k = intervals.Length - 1; k >= 0; --k)
        {
            var interval = intervals[k];
            for (m = 0; m < interval; ++m)
            {
                for (j = m + interval; j < n; j += interval)
                {
                    for (
                        i = j;
                        i >= interval
                            && storage[i].Compare(ref storage.GetInternalArray()[i - interval]);
                        i -= interval
                    )
                    {
                        var v = storage[i];
                        storage[i] = storage[i - interval];
                        storage[i - interval] = v;
                        mapping.GetInternalArray()[entityMapping[i].Entity].ComponentIndex = i;
                        mapping.GetInternalArray()[i - interval].ComponentIndex = i - interval;
                    }
                }
            }
        }
    }

    public static void Sort<T>(int worldId, ComponentManager<T> manager)
        where T : ISortable<T>
    {
        var world = World.GetWorldInternal(worldId);
        if (world == null)
        {
            return;
        }
        lock (manager.Lock)
        {
            DoShellSort(manager);
        }
    }
}

internal class ComponentManager<T> : IDisposable
{
    #region Manager Storage
    internal static readonly ComponentTypeId TypeId = ComponentTypeId.GetNexId();
    private static readonly FastList<ComponentManager<T>?> _managerStorage = [];
    private static readonly ReaderWriterLockSlim _managerStorageLock = new();
    private static Func<int, World?>? _getWorldFunc;
    private static readonly bool _isReferenceType = !typeof(T).GetTypeInfo().IsValueType;

    private static readonly bool _isFlagType = typeof(T).GetTypeInfo().IsFlagType();

    internal readonly object Lock = new();

    /// <summary>
    /// Gets the or create manager.
    /// </summary>
    /// <param name="getWorldFunc"></param>
    /// <param name="worldId">The world identifier.</param>
    /// <param name="defaultCapacity">The default capacity.</param>
    /// <returns></returns>
    public static ComponentManager<T>? GetOrCreateManager(
        Func<int, World?> getWorldFunc,
        int worldId,
        int defaultCapacity = 128
    )
    {
        _getWorldFunc = getWorldFunc;
        var manager = GetManager(worldId);
        if (manager != null || worldId == 0)
        {
            return manager;
        }
        _managerStorageLock.EnterWriteLock();
        try
        {
            _managerStorage.Resize(Math.Max(_managerStorage.Count, worldId + 1));
            if (_managerStorage[worldId] == null)
            {
                _managerStorage[worldId] = new ComponentManager<T>(worldId, defaultCapacity);
            }
            return _managerStorage[worldId];
        }
        finally
        {
            _managerStorageLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the manager.
    /// </summary>
    /// <param name="worldId">The world identifier.</param>
    /// <returns></returns>
    public static ComponentManager<T>? GetManager(int worldId)
    {
        _managerStorageLock.EnterReadLock();
        try
        {
            if (worldId == 0 || _managerStorage.Count <= worldId)
            {
                return null;
            }
            return _managerStorage[worldId];
        }
        finally
        {
            _managerStorageLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes the manager.
    /// </summary>
    /// <param name="worldId">The world identifier.</param>
    public static void RemoveManager(int worldId)
    {
        if (worldId == 0)
        {
            return;
        }
        _managerStorageLock.EnterWriteLock();
        try
        {
            if (_managerStorage.Count > worldId)
            {
                _managerStorage[worldId]?.Dispose();
                _managerStorage[worldId] = null;
            }
        }
        finally
        {
            _managerStorageLock.ExitWriteLock();
        }
    }

    public static int ManagerCount
    {
        get
        {
            _managerStorageLock.EnterReadLock();
            try
            {
                return _managerStorage.Count;
            }
            finally
            {
                _managerStorageLock.ExitReadLock();
            }
        }
    }

    public static bool HasWorld(int worldId)
    {
        _managerStorageLock.EnterReadLock();
        try
        {
            return _managerStorage.Count > worldId && _managerStorage[worldId]?.Count > 0;
        }
        finally
        {
            _managerStorageLock.ExitReadLock();
        }
    }

    #endregion
    #region Internal Structs
    internal struct ComponentMappingKey(int componentIndex)
    {
        public bool Valid = true;
        public int ComponentIndex = componentIndex;
    }

    internal struct EntityMappingKey(int entity)
    {
        public int Entity = entity;
    }
    #endregion
    #region Enumerable

    public readonly struct EntityEnumerable(ComponentManager<T> pool) : IEnumerable<Entity>
    {
        private readonly ComponentManager<T> _pool = pool;

        #region IEnumerable

        public EntityEnumerator GetEnumerator() => new EntityEnumerator(_pool);

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion
    }

    public struct EntityEnumerator : IEnumerator<Entity>
    {
        private readonly World _world;
        private readonly FastList<ComponentMappingKey> _mapping;

        private int _index;

        public EntityEnumerator(ComponentManager<T> componentManager)
        {
            var w = _getWorldFunc?.Invoke(componentManager.WorldId);
            _world = w ?? throw new InvalidOperationException($"Unable to get world.");
            _mapping = componentManager.CompMapping;
            _index = -1;
        }

        #region IEnumerator

        public readonly Entity Current => new(_world, _index);

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (++_index < _mapping.Count)
            {
                if (_mapping[_index].ComponentIndex >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Reset()
        {
            _index = -1;
        }

        #endregion

        #region IDisposable

        public readonly void Dispose()
        {
            // Method intentionally left empty.
        }
        #endregion
    }
    #endregion
    #region Private Properties
    internal int WorldId { private set; get; }
    internal readonly FastList<T> Storage;
    internal readonly FastList<ComponentMappingKey> CompMapping;
    internal readonly FastList<EntityMappingKey> EntityMapping;
    private int _lastChangedIndex = 0;
    #endregion

    #region Public Properties
    /// <summary>
    /// Gets the total component count.
    /// </summary>
    /// <value>
    /// The count.
    /// </value>
    public int Count
    {
        get => Storage.Count;
    }

    /// <summary>
    /// Gets the storage capacity.
    /// </summary>
    /// <value>
    /// The capacity.
    /// </value>
    public int Capacity
    {
        get => Storage.Capacity;
    }
    #endregion
    #region Constructors
    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentManager{T}"/> class.
    /// </summary>
    /// <param name="worldId">The world identifier.</param>
    /// <param name="defaultCapcity">The default capcity.</param>
    protected ComponentManager(int worldId, in int defaultCapcity = 128)
    {
        WorldId = worldId;
        Storage = new(defaultCapcity);
        EntityMapping = new(defaultCapcity);
        CompMapping = new FastList<ComponentMappingKey>(defaultCapcity);
        ECSEventBus.Register<WorldDisposingEvent>(worldId, HandleWorldDisposing);
        ECSEventBus.Register<EntityDisposingEvent>(worldId, HandleEntityDisposing);
    }
    #endregion
    #region Public Functions
    /// <summary>
    /// Sets the specified entity and component.
    /// If component with specified entity already exists, replace existing component with the passed in component.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="component">The component.</param>
    /// <param name="added">If component is added the first time</param>
    /// <returns></returns>
    public ResultCode Set(int entity, ref T component, out bool added)
    {
        added = false;
        if (!IsValid())
        {
            return ResultCode.Invalid;
        }
        if (Has(entity))
        {
            Storage[CompMapping[entity].ComponentIndex] = component;
            return ResultCode.Ok;
        }
        AddComponent(entity, ref component);
        added = true;
        return ResultCode.Ok;
    }

    private ref T AddComponent(int entityId, ref T component)
    {
        lock (Lock)
        {
            CompMapping.Resize(Math.Max(CompMapping.Count, entityId + 1), true);
            CompMapping[entityId] = new ComponentMappingKey(Storage.Count);
            Storage.Add(component);
            EntityMapping.Add(new EntityMappingKey(entityId));
            Debug.Assert(Storage.Count == EntityMapping.Count);
            return ref Storage.GetInternalArray()[Storage.Count - 1];
        }
    }

    /// <summary>
    /// Determines whether [has] [the specified entity].
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>
    ///   <c>true</c> if [has] [the specified entity]; otherwise, <c>false</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(int entity)
    {
        if (
            !IsValid()
            || CompMapping.Count <= entity
            || !CompMapping[entity].Valid
            || CompMapping[entity].ComponentIndex >= Storage.Count
        )
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Removes the component by specified entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="keepSorted">Keeps the order of the rest of components in the storage after removing</param>
    /// <returns></returns>
    public ResultCode Remove(int entityId, bool keepSorted = false)
    {
        if (!Has(entityId))
        {
            return ResultCode.NotFound;
        }
        var componentIdx = CompMapping[entityId].ComponentIndex;
        CompMapping[entityId] = default;
        EntityMapping[componentIdx] = default;
        Debug.Assert(componentIdx < Storage.Count);
        Debug.Assert(Storage.Count == EntityMapping.Count);
        lock (Lock)
        {
            if (entityId == CompMapping.Count - 1)
            {
                var i = entityId;
                for (; i >= 0; --i)
                {
                    if (CompMapping[i].Valid)
                    {
                        break;
                    }
                }
                CompMapping.Resize(i + 1);
            }
            if (CompMapping.Count == 0)
            {
                Storage.Clear();
                EntityMapping.Clear();
                return ResultCode.Ok;
            }
            if (componentIdx < Storage.Count - 1)
            {
                if (!keepSorted)
                {
                    Storage[componentIdx] = Storage[Storage.Count - 1];
                    EntityMapping[componentIdx] = EntityMapping[EntityMapping.Count - 1];
                    CompMapping
                        .GetInternalArray()[EntityMapping[componentIdx].Entity]
                        .ComponentIndex = componentIdx;
                }
                else
                {
                    for (var i = componentIdx; i < Storage.Count - 1; ++i)
                    {
                        Storage[i] = Storage[i + 1];
                        EntityMapping[i] = EntityMapping[i + 1];
                        CompMapping.GetInternalArray()[EntityMapping[i].Entity].ComponentIndex = i;
                    }
                }
            }
            if (_isReferenceType)
            {
#pragma warning disable CS8653,CS8601 // A default expression introduces a null value for a type parameter.
                Storage[Storage.Count - 1] = default;
#pragma warning restore CS8653,CS8601 // A default expression introduces a null value for a type parameter.
            }
            Storage.Resize(Storage.Count - 1);
            EntityMapping.Resize(EntityMapping.Count - 1);
            _lastChangedIndex = Math.Min(_lastChangedIndex, componentIdx);
            return ResultCode.Ok;
        }
    }

    /// <summary>
    /// Gets the component reference by specified entity.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>component reference</returns>
    public ref T Get(int entity)
    {
        return ref Storage.GetInternalArray()[CompMapping[entity].ComponentIndex];
    }

    /// <summary>
    /// Gets the component index in storage.
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public int GetIndex(int entity)
    {
        return CompMapping[entity].ComponentIndex;
    }

    /// <summary>
    /// As the span.
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => Storage.GetInternalArray().AsSpan(0, Storage.Count);

    /// <summary>
    /// As the components.
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Components<T> AsComponents() => new(CompMapping, Storage);

    /// <summary>
    /// Gets the entities.
    /// </summary>
    /// <returns></returns>
    public EntityEnumerable GetEntities() => new(this);

    /// <summary>
    /// Gets the component with the specified entity.
    /// </summary>
    /// <value>
    /// The component.
    /// </value>
    /// <param name="entity">The entity.</param>
    /// <returns></returns>
    public ref T this[in int entity]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Get(entity);
    }

    /// <summary>
    /// Trims the excess of component storages.
    /// </summary>
    public void TrimExcess()
    {
        if (WorldId == 0)
        {
            return;
        }
        lock (Lock)
        {
            Storage.TrimExcess();
        }
    }

    /// <summary>
    /// Verifies the storage. Only used for testing.
    /// </summary>
    /// <returns></returns>
    internal bool VerifyStorage()
    {
        lock (Lock)
        {
            if (CompMapping.Count == 0 || CompMapping[CompMapping.Count - 1].Valid)
            {
                for (var i = 0; i < CompMapping.Count; ++i)
                {
                    if (CompMapping[i].Valid)
                    {
                        if (EntityMapping[CompMapping[i].ComponentIndex].Entity == i)
                        {
                            continue;
                        }
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
    }
    #endregion

    #region Private Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValid()
    {
        return WorldId != 0;
    }

    private void HandleWorldDisposing(int worldId, WorldDisposingEvent msg)
    {
        Dispose();
    }

    private void HandleEntityDisposing(int worldId, EntityDisposingEvent msg)
    {
        Remove(msg.EntityId);
    }
    #endregion

    #region IDisposable Support
    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (WorldId == 0)
        {
            return;
        }
        var worldId = WorldId;
        WorldId = 0;
        ECSEventBus.Unregister<WorldDisposingEvent>(worldId, HandleWorldDisposing);
        ECSEventBus.Unregister<EntityDisposingEvent>(worldId, HandleEntityDisposing);
        CompMapping.Clear();
        CompMapping.TrimExcess();
        Storage.Clear();
        Storage.TrimExcess();
        RemoveManager(worldId);
    }
    #endregion
}
