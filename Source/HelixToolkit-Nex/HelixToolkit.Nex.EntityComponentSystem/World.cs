using System.Reflection;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// World object. Use World.CreateWorld() to create an new world.
/// </summary>
public sealed class World : IEnumerable<Entity>, IDisposable
{
    public const int MaxNumberOfWorlds = byte.MaxValue;

    private struct WorldProxy
    {
        /// <summary>
        /// Gets or sets the generation for this world slot.
        /// </summary>
        /// <value>
        /// The generation.
        /// </value>
        public byte Generation { private set; get; }
        public World? World { set; get; }

        public void Reset()
        {
            World = null;
            ++Generation;
        }
    }

    private static readonly FastList<WorldProxy> _worlds = [];
    private static readonly IdHelper _worldIdGen = new();
    private static readonly ReaderWriterLockSlim _worldsLock = new();

    private readonly FastList<EntityState> _entityState = new(1024);
    private readonly IdHelper _entityIdGen = new();
    private readonly object _lock = new();
    public byte Generation { private set; get; }

    public byte Id { private set; get; }
    internal static int WorldCapacity => _worlds.Capacity;
    internal int EntityStateCapcity => _entityState.Capacity;

    /// <summary>
    /// Gets the total entity count.
    /// </summary>
    /// <value>
    /// The count.
    /// </value>
    public int Count => _entityIdGen.Count;

    /// <summary>
    /// Creates the world.
    /// </summary>
    /// <returns></returns>
    public static World CreateWorld()
    {
        _worldsLock.EnterWriteLock();
        try
        {
            var nextId = _worldIdGen.GetNextId();
            if (nextId >= Limits.MaxWorldId)
            {
                throw new Exception(
                    $"Number of worlds exceeds maximum supported size of {Limits.MaxWorldId}."
                );
            }
            // Monotonically increasing. Preserves worlds generation. Max number of worlds is only 255.
            _worlds.Resize(_worldIdGen.MaxId + 1, true);
            var worldArray = _worlds.GetInternalArray();
            Debug.Assert(worldArray[nextId].World == null);
            worldArray[nextId].Reset();
            var generation = worldArray[nextId].Generation;
            worldArray[nextId].World = new World((byte)nextId, generation);
            ECSEventBus.Send(nextId, new WorldCreatedEvent());
            return worldArray[nextId].World
                ?? throw new InvalidOperationException($"Failed to create an new world.");
        }
        finally
        {
            _worldsLock.ExitWriteLock();
        }
    }

    public static bool TryCreateWorld(out World? world)
    {
        try
        {
            world = CreateWorld();
            return true;
        }
        catch (Exception)
        {
            world = null;
            return false;
        }
    }

    public static World? GetWorldById(int worldId)
    {
        return GetWorldInternal(worldId);
    }

    internal static bool TryGetWorld(in Generation generation, out World? world)
    {
        world = GetWorld(generation);
        return world != null;
    }

    /// <summary>
    /// Gets the world by Id. This is only used by world internal managers.
    /// </summary>
    /// <param name="worldId">The world identifier.</param>
    /// <returns></returns>
    internal static World? GetWorldInternal(int worldId)
    {
        if (worldId == 0)
        {
            return null;
        }
        _worldsLock.EnterReadLock();
        try
        {
            return _worlds.Count > worldId ? _worlds[worldId].World : null;
        }
        finally
        {
            _worldsLock.ExitReadLock();
        }
    }

    internal static World? GetWorld(in Generation entityGeneration)
    {
        if (!entityGeneration.Valid)
        {
            return null;
        }
        _worldsLock.EnterReadLock();
        try
        {
            if (_worlds.Count > entityGeneration.WorldId)
            {
                var w = _worlds[entityGeneration.WorldId].World;
                if (w != null && w.Generation == entityGeneration.WorldGeneration)
                {
                    return w;
                }
            }
            return null;
        }
        finally
        {
            _worldsLock.ExitReadLock();
        }
    }

    private static void RemoveWorld(int worldId)
    {
        _worldsLock.EnterWriteLock();
        try
        {
            _worlds.GetInternalArray()[worldId].World = null;
            _worldIdGen.ReleaseId(worldId);
        }
        finally
        {
            _worldsLock.ExitWriteLock();
        }
    }

    private World(byte id, byte generation)
    {
        Id = id;
        Generation = generation;
        RegisterEvents();
    }

    #region Entity
    /// <summary>
    /// Creates the entity.
    /// </summary>
    /// <returns></returns>
    public Entity CreateEntity()
    {
        lock (_lock)
        {
            var entityId = _entityIdGen.GetNextId();
            if (entityId >= Limits.MaxEntityId)
            {
                throw new Exception(
                    $"Number of entities in world {Id} exceeds maximum supported size of {Limits.MaxEntityId}."
                );
            }
            _entityState.Resize(Math.Max(_entityState.Count, entityId + 1), true);
            _entityState.GetInternalArray()[entityId].Reset(Id, Generation);
            return new Entity(this, entityId);
        }
    }

    /// <summary>
    /// Gets the entity by entity id.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <returns></returns>
    public Entity GetEntity(in int entityId)
    {
        return new Entity(this, entityId);
    }

    /// <summary>
    /// Gets the entity by entity id and entity generation.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="entityGeneration">The entity generation.</param>
    /// <returns></returns>
    public Entity GetEntity(in int entityId, ushort entityGeneration)
    {
        var generation = new Generation(Id, Generation, entityGeneration);
        return new Entity(generation, entityId);
    }

    /// <summary>
    /// Determines whether the specified entity has entity.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>
    ///   <c>true</c> if the specified entity has entity; otherwise, <c>false</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasEntity(Entity entity)
    {
        return HasEntity(entity.Id, entity.Generation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasEntity(in int entityId, in Generation generation)
    {
        return entityId < _entityState.Count
            && _entityState[entityId].Generation == generation
            && _entityState[entityId].Valid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ValidateEntity(ref Entity entity)
    {
        return HasEntity(entity.Id, entity.Generation);
    }
    #endregion

    #region State Operation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref EntityState GetState(in int entityId, in Generation entityGeneration)
    {
        if (!HasEntity(entityId, entityGeneration))
        {
            return ref EntityState.InvalidState;
        }
        Debug.Assert(_entityState.Count > entityId);
        if (_entityState[entityId].Valid)
        {
            return ref _entityState.GetInternalArray()[entityId];
        }
        else
        {
            return ref EntityState.InvalidState;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool GetStateValid(in int entityId, in Generation entityGeneration)
    {
        if (!HasEntity(entityId, entityGeneration))
        {
            return false;
        }
        return _entityState.GetInternalArray()[entityId].Valid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool SetStateValid(in int entityId, in Generation entityGeneration, bool valid)
    {
        if (!HasEntity(entityId, entityGeneration))
        {
            return false;
        }
        _entityState.GetInternalArray()[entityId].Valid = valid;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Generation GetStateGeneration(in int worldId, in int entityId)
    {
        Debug.Assert(worldId < byte.MaxValue);
        var generation = new Generation((byte)worldId, Generation, 0);
        if (
            entityId >= _entityState.Count
            || _entityState[entityId].Generation.WorldGeneration != generation.WorldGeneration
        )
        {
            return new Generation();
        }
        return _entityState.GetInternalArray()[entityId].Generation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool GetStateEnabled(in int entityId, in Generation entityGeneration)
    {
        if (!HasEntity(entityId, entityGeneration))
        {
            return false;
        }
        return _entityState.GetInternalArray()[entityId].Enabled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool SetStateEnabled(in int entityId, in Generation entityGeneration, bool enabled)
    {
        if (
            !HasEntity(entityId, entityGeneration)
            || _entityState.GetInternalArray()[entityId].Enabled == enabled
        )
        {
            return false;
        }
        _entityState.GetInternalArray()[entityId].Enabled = enabled;
        return true;
    }
    #endregion

    #region Components
    /// <summary>
    /// Checks whether the entity at the given id has the specified component type
    /// by inspecting the EntityState bitmask directly. Used internally by tag component managers.
    /// This intentionally does not check entity validity, so it can be used during
    /// EntityDisposingEvent processing when the entity state has already been invalidated.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasComponentTypeById(int entityId, in ComponentTypeId typeId)
    {
        if (entityId < 0 || entityId >= _entityState.Count)
        {
            return false;
        }
        return _entityState.GetInternalArray()[entityId].ComponentTypes.HasType(typeId);
    }

    private static void ThrowEntityInvalidException(in Entity entity)
    {
        throw new ArgumentException($"Entity {entity} is not valid or belongs to another world.");
    }

    /// <summary>
    /// Sets the component to specific entity.
    /// If component already exists, replace current component with the passed in one.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="component">The component.</param>
    /// <param name="added">Is component newly added.</param>
    /// <returns>True: Success. False: Failed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResultCode SetComponent<T>(Entity entity, ref T component, out bool added)
    {
        added = false;
        var ret = ResultCode.Invalid;
        if (ValidateEntity(ref entity))
        {
            if (IsTagType<T>())
            {
                added = !HasComponent<T>(entity);
                ret = ResultCode.Ok;
            }
            else
            {
                var manager = ComponentManager<T>.GetOrCreateManager(
                    GetWorldInternal,
                    Id,
                    defaultCapacity: IsTagType<T>() ? 0 : 128
                );
                ret = manager?.Set(entity.Id, ref component, out added) ?? ResultCode.Invalid;
            }
        }
        if (ret == ResultCode.Ok)
        {
            ref var state = ref GetState(entity.Id, entity.Generation);
            if (!state.Valid)
            {
                return ResultCode.Invalid;
            }
            state.ComponentTypes.AddType(ComponentIdProxy<T>.TypeId);
            ECSEventBus.Send(
                Id,
                new ComponentChangedEvent<T>(
                    entity.Id,
                    added ? ComponentOperations.Added : ComponentOperations.Changed,
                    GetComponentTypeId<T>()
                )
            );
        }
        return ret;
    }

    /// <summary>
    /// Gets the component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(Entity entity)
    {
        if (!ValidateEntity(ref entity))
        {
            ThrowEntityInvalidException(entity);
        }
        if (IsTagType<T>())
        {
            return ref ComponentIdProxy<T>.DefaultValue!;
        }
        var manager = ComponentManager<T>.GetManager(Id);
        if (manager == null)
        {
            ThrowEntityInvalidException(entity);
        }
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable S2259 // Null pointers should not be dereferenced
        return ref manager.Get(entity.Id);
#pragma warning restore S2259 // Null pointers should not be dereferenced
#pragma warning restore CS8602 // Dereference of a possibly null reference.
    }

    /// <summary>
    /// Determines whether the specified entity has component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns>
    ///   <c>true</c> if the specified entity has component; otherwise, <c>false</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(Entity entity)
    {
        return ValidateEntity(ref entity)
            && GetState(entity.Id, entity.Generation)
                .ComponentTypes.HasType(ComponentManager<T>.TypeId);
    }

    /// <summary>
    /// Determines whether this world [has any specific type of component].
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns>
    ///   <c>true</c> if [has any speicific type of component]; otherwise, <c>false</c>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasAnyComponent<T>()
    {
        if (Id == 0)
        {
            return false;
        }
        foreach (var entity in this)
        {
            if (entity.Has<T>())
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes the component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="keepSorted">Keeps the order of the rest of components in storage after removing</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResultCode RemoveComponent<T>(Entity entity, bool keepSorted = false)
    {
        ResultCode ret;
        if (!HasComponent<T>(entity))
        {
            return ResultCode.NotFound;
        }
        lock (_lock)
        {
            ret = ResultCode.Invalid;

            if (ValidateEntity(ref entity))
            {
                if (IsTagType<T>())
                {
                    ret = ResultCode.Ok;
                }
                else
                {
                    ret =
                        ComponentManager<T>.GetManager(Id)?.Remove(entity.Id, keepSorted)
                        ?? ResultCode.NotFound;
                }
            }

            if (ret == ResultCode.Ok)
            {
                ref var state = ref GetState(entity.Id, entity.Generation);
                if (!state.Valid)
                {
                    return ResultCode.Invalid;
                }
                state.ComponentTypes.RemoveType(ComponentManager<T>.TypeId);
            }
        }
        if (ret == ResultCode.Ok)
        {
            ECSEventBus.Send(
                Id,
                new ComponentChangedEvent<T>(
                    entity.Id,
                    ComponentOperations.Removed,
                    GetComponentTypeId<T>()
                )
            );
        }
        return ret;
    }

    /// <summary>
    /// Trims storage space specific type of component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void TrimComponentStorage<T>()
    {
        if (IsTagType<T>())
        {
            return;
        }
        ComponentManager<T>.GetManager(Id)?.TrimExcess();
    }

    /// <summary>
    /// Gets all components for this world.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Components<T> GetComponents<T>()
    {
        if (IsTagType<T>())
        {
            throw new InvalidOperationException(
                "Tag type is not valid for GetComponents operation."
            );
        }
        var manager = GetComponentManager<T>();
        return manager != null ? manager.AsComponents() : new Components<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ComponentManager<T>? GetComponentManager<T>()
    {
        if (IsTagType<T>())
        {
            return null;
        }
        return ComponentManager<T>.GetOrCreateManager(GetWorldInternal, Id);
    }

    /// <summary>
    /// Get the unique component type id.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentTypeId GetComponentTypeId<T>()
    {
        return ComponentIdProxy<T>.TypeId;
    }

    /// <summary>
    /// Manually notify the world that a component of specific type has been changed for an entity.
    /// This is only needed when you manually modify the component data and want to trigger systems that are dependent on component change events.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    public void NotifyComponentChanged<T>(Entity entity)
    {
        if (!entity.Has<T>())
        {
            return;
        }
        Send(
            new ComponentChangedEvent<T>(
                entity.Id,
                ComponentOperations.Changed,
                GetComponentTypeId<T>()
            )
        );
    }
    #endregion

    #region Sorting
    public void SortComponent<T>()
        where T : ISortable<T>
    {
        var manager = GetComponentManager<T>();
        if (manager == null || IsTagType<T>())
        {
            return;
        }
        ComponentSorting.Sort(Id, manager);
    }
    #endregion

    /// <summary>
    /// Enumerates all entities that have a component of type <typeparamref name="T"/>,
    /// in the same storage order as <see cref="GetComponents{T}"/>.
    /// </summary>
    public IEnumerable<Entity> GetComponentEntities<T>()
    {
        if (IsTagType<T>())
        {
            foreach (var entity in this)
            {
                if (entity.Has<T>())
                {
                    yield return entity;
                }
            }
        }
        else
        {
            var manager = GetComponentManager<T>();
            if (manager == null)
            {
                yield break;
            }
            foreach (var entity in manager.GetEntities())
            {
                if (entity.Valid)
                    yield return entity;
            }
        }
    }

    #region Event Handling
    private void RegisterEvents()
    {
        ECSEventBus.Register<EntityDisposingEvent>(Id, HandleEntityDisposing);
    }

    private void HandleEntityDisposing(int worldId, EntityDisposingEvent msg)
    {
        lock (_lock)
        {
            ref var state = ref GetState(msg.EntityId, msg.Generation);
            if (state.Valid)
            {
                state.Enabled = false;
                state.Reset(0, 0);
                _entityIdGen.ReleaseId(msg.EntityId);
                if (msg.EntityId == _entityState.Count - 1)
                {
                    var i = msg.EntityId;
                    for (; i >= 0; --i)
                    {
                        if (_entityState.GetInternalArray()[i].Valid)
                        {
                            break;
                        }
                    }
                    // Preserve existing generation, must not clear.
                    _entityState.Resize(i + 1, true);
                }
            }
        }
    }
    #endregion

    #region Entity Collection
    public RuleBuilder CreateCollection()
    {
        return EntityCollection.Create(this);
    }
    #endregion

    #region IEnumerable
    /// <summary>
    /// Enumerates the <see cref="Entity"/> of a <see cref="World" />.
    /// </summary>
    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly World _world;
        private readonly FastList<EntityState> _entityStates;
        private readonly int _maxIndex;

        private int _index;

        internal Enumerator(in World world)
        {
            _world = world;
            _entityStates = world._entityState;
            _maxIndex = world._entityState.Count;
            _index = -1;
        }

        #region IEnumerator

        /// <summary>
        /// Gets the <see cref="Entity"/> at the current position of the enumerator.
        /// </summary>
        /// <returns>The <see cref="Entity"/> in the <see cref="World" /> at the current position of the enumerator.</returns>
        public readonly Entity Current => new(_world, _index);

        readonly object IEnumerator.Current => Current;

        /// <summary>
        /// Advances the enumerator to the next <see cref="Entity"/> of the <see cref="World" />.
        /// </summary>
        /// <returns>true if the enumerator was successfully advanced to the next <see cref="Entity"/>; false if the enumerator has passed the end of the collection.</returns>
        public bool MoveNext()
        {
            while (++_index < _maxIndex)
            {
                if (_entityStates[_index].Valid)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first <see cref="Entity"/> in the collection.
        /// </summary>
        public void Reset()
        {
            _index = -1;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases all resources used by the <see cref="Enumerator" />.
        /// </summary>
        public void Dispose()
        {
            // Method intentionally left empty.
        }

        #endregion
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>
    /// An enumerator that can be used to iterate through the collection.
    /// </returns>
    public IEnumerator<Entity> GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>
    /// An <see cref="System.Collections.IEnumerator" /> object that can be used to iterate through the collection.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion

    #region Event Bus
    /// <summary>
    /// Sends an event across this world
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="message"></param>
    public void Send<TMessage>(in TMessage message)
    {
        ECSEventBus.Send(Id, message);
    }

    /// <summary>
    /// Register an event callback for this world
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="action"></param>
    public void Register<TMessage>(Message<TMessage> action)
    {
        ECSEventBus.Register(Id, action);
    }

    /// <summary>
    /// Unregister an event callback for this world
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="action"></param>
    public void Unregister<TMessage>(Message<TMessage> action)
    {
        ECSEventBus.Unregister(Id, action);
    }
    #endregion

    private static bool IsTagType<T>()
    {
        return typeof(T).GetTypeInfo().IsTagType();
    }

    #region Disposible
    public event EventHandler? Disposing;

    private bool _disposed = false;

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Disposing?.Invoke(this, EventArgs.Empty);
        ECSEventBus.Send(Id, new WorldDisposingEvent());
        _entityState.Clear();
        var oldId = Id;
        Id = 0;
        Generation = 0;
        RemoveWorld(oldId);
        _disposed = true;
        ECSEventBus.Send(oldId, new WorldDisposedEvent());
    }
    #endregion
}
