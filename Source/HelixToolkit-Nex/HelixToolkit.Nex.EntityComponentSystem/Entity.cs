namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Entity structure.
/// </summary>
/// <seealso cref="System.IDisposable" />
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Entity : IDisposable, IEquatable<Entity>
{
    public static readonly Entity Null = new();

    static Entity()
    {
        Debug.Assert(NativeHelper.SizeOf<Entity>() == sizeof(int) * 2);
    }

    /// <summary>
    /// The entity version. Used to track entity generations.
    /// </summary>
    internal Generation Generation;
    public int Id { get; private set; }
    public readonly ushort Gen => Generation.EntityGeneration;
    internal readonly int WorldId => Generation.WorldId;
    private readonly ushort WorldGeneration => Generation.WorldGeneration;

    /// <summary>
    /// Gets the world.
    /// </summary>
    /// <value>
    /// The world.
    /// </value>
    public readonly World? World
    {
        get => World.GetWorld(Generation);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="Entity"/> is enabled.
    /// </summary>
    /// <value>
    ///   <c>true</c> if enabled; otherwise, <c>false</c>.
    /// </value>
    public bool Enabled
    {
        set
        {
            if (World?.SetStateEnabled(Id, Generation, value) ?? false)
            {
                ECSEventBus.Send(WorldId, new EntityEnableEvent(Id, value));
            }
        }
        get { return World?.GetStateEnabled(Id, Generation) ?? false; }
    }

    /// <summary>
    /// Gets a value indicating whether this <see cref="Entity"/> is valid.
    /// </summary>
    /// <value>
    ///   <c>true</c> if valid; otherwise, <c>false</c>.
    /// </value>
    public bool Valid
    {
        get { return World?.HasEntity(Id, Generation) ?? false; }
    }

    internal Entity(World world, int id)
    {
        if (world != null)
        {
            Id = id;
            Generation = world.GetStateGeneration(world.Id, id);
        }
        else
        {
            Id = 0;
            Generation = new Generation();
        }
    }

    internal Entity(in Generation generation, int id)
    {
        Id = id;
        Generation = generation;
    }

    /// <summary>
    /// Set entity enable or disable.
    /// </summary>
    /// <param name="enabled"></param>
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    #region Component Operation
    /// <summary>
    /// Sets the component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="component">The component.</param>
    /// <returns></returns>
    public readonly ResultCode Set<T>(ref T component)
    {
        if (component == null)
        {
            return ResultCode.Invalid;
        }
        var added = false;
        var ret = World?.SetComponent<T>(this, ref component, out added) ?? ResultCode.Invalid;
        if (ret == ResultCode.Ok)
        {
            ECSEventBus.Send(
                WorldId,
                new ComponentChangedEvent<T>(
                    Id,
                    added ? ComponentOperations.Added : ComponentOperations.Changed
                )
            );
        }
        return ret;
    }

    /// <summary>
    /// Sets the specified component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="component">The component.</param>
    /// <returns></returns>
    public readonly ResultCode Set<T>(T? component = default)
    {
        return component == null ? ResultCode.Invalid : Set(ref component);
    }

    /// <summary>
    /// Determines whether this instance has component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns>
    ///   <c>true</c> if this instance has component; otherwise, <c>false</c>.
    /// </returns>
    public readonly bool Has<T>()
    {
        return World?.HasComponent<T>(this) ?? false;
    }

    /// <summary>
    /// Gets the component by reference.
    /// <para>
    /// Warning: This function does not do entity validity check. Please use <see cref="Valid"/> before use this function.
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public readonly ref T Get<T>()
    {
        return ref World!.GetComponentManager<T>()!.Get(Id);
    }

    /// <summary>
    /// Attempts to retrieve a component of the specified type associated with the current entity.
    /// </summary>
    /// <remarks>This method checks if the current entity has a component of the specified type and retrieves
    /// it if available.</remarks>
    /// <typeparam name="T">The type of the component to retrieve.</typeparam>
    /// <param name="component">When this method returns, contains the component of type <typeparamref name="T"/> if found; otherwise, the
    /// default value for the type.</param>
    /// <returns><see langword="true"/> if the component of type <typeparamref name="T"/> is found; otherwise, <see
    /// langword="false"/>.</returns>
    public readonly bool TryGet<T>(out T component)
    {
        if (World?.HasComponent<T>(this) ?? false)
        {
            component = World.GetComponentManager<T>()!.Get(Id);
            return true;
        }
        component = default!;
        return false;
    }

    /// <summary>
    /// Gets the component index in storage.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public readonly int GetIndex<T>()
    {
        return World!.HasAnyComponent<T>() ? World!.GetComponentManager<T>()!.GetIndex(Id) : -1;
    }

    /// <summary>
    /// Removes the component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="keepSorted">Keeps the order of the rest of components in storage after removing.</param>
    /// <returns></returns>
    public readonly ResultCode Remove<T>(bool keepSorted = false)
    {
        var ret = World?.RemoveComponent<T>(this, keepSorted) ?? ResultCode.Invalid;
        if (ret == ResultCode.Ok)
        {
            ECSEventBus.Send(
                WorldId,
                new ComponentChangedEvent<T>(Id, ComponentOperations.Removed)
            );
        }
        return ret;
    }
    #endregion

    #region Operator

    public static bool operator ==(Entity a, Entity b) => a.Equals(b);

    public static bool operator !=(Entity a, Entity b) => !a.Equals(b);

    #endregion

    #region Object

    public override bool Equals(object? obj)
    {
        return obj is Entity entity && Equals(entity);
    }

    public override readonly int GetHashCode() => Id;

    public override readonly string ToString() => $"{nameof(Entity)} {World?.Id}:{Id}";

    #endregion

    #region Event Bus
    /// <summary>
    /// Sends an event
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="message"></param>
    public void Send<TMessage>(in TMessage message)
    {
        World?.Send(message);
    }

    /// <summary>
    /// Register an event callback
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="action"></param>
    public void Register<TMessage>(Message<TMessage> action)
    {
        World?.Register(action);
    }

    /// <summary>
    /// Unregister an event callback
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="action"></param>
    public void Unregister<TMessage>(Message<TMessage> action)
    {
        World?.Unregister(action);
    }
    #endregion

    #region Dispose
    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (Valid)
        {
            ECSEventBus.Send(WorldId, new EntityDisposingEvent(Id, Generation));
            Generation = default;
            Id = 0;
        }
    }

    public bool Equals(Entity other)
    {
        return other.Id == Id
            && other.Generation == Generation
            && other.WorldGeneration == WorldGeneration;
    }
    #endregion
}
