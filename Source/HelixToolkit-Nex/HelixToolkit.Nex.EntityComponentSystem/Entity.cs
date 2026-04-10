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
        return World?.SetComponent<T>(this, ref component, out _) ?? ResultCode.Invalid;
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
    /// Adds a tag component (an empty struct with no instance fields) to this entity.
    /// Tag components carry no data and are used purely as markers/flags.
    /// No underlying data storage is allocated for tag components.
    /// </summary>
    /// <typeparam name="T">The tag component type (must be an empty struct).</typeparam>
    /// <returns></returns>
    public readonly ResultCode Tag<T>()
        where T : struct
    {
        T tag = default;
        return World?.SetComponent<T>(this, ref tag, out _) ?? ResultCode.Invalid;
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
    /// Updates the component of type <typeparamref name="T"/> associated with the current entity.
    /// </summary>
    /// <remarks>This method applies the provided <paramref name="updateFunc"/> to the component of type
    /// <typeparamref name="T"/> if the entity has such a component. If the entity does not have the specified
    /// component, the method does nothing.</remarks>
    /// <typeparam name="T">The type of the component to update.</typeparam>
    /// <param name="updateFunc">A function that takes the current component of type <typeparamref name="T"/> as input and returns the updated
    /// component.</param>
    public void Update<T>(Func<T, T> updateFunc)
    {
        if (World?.HasComponent<T>(this) ?? false)
        {
            ref var component = ref Get<T>();
            var updatedComponent = updateFunc(component);
            Set(ref updatedComponent);
        }
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
        return World?.RemoveComponent<T>(this, keepSorted) ?? ResultCode.Invalid;
    }

    /// <summary>
    /// Notifies that a component of type <typeparamref name="T"/> has changed.
    /// </summary>
    /// <remarks>This method sends a <see cref="ComponentChangedEvent{T}"/> to the event bus if the component
    /// of type <typeparamref name="T"/> exists.</remarks>
    /// <typeparam name="T">The type of the component that has changed.</typeparam>
    public readonly void NotifyComponentChanged<T>()
    {
        World?.NotifyComponentChanged<T>(this);
    }

    /// <summary>
    /// Notifies the system that a component of the specified type has changed for the current entity.
    /// </summary>
    /// <remarks>This method informs the associated <see cref="World"/> instance, if available, about the
    /// change in the specified component. Ensure that the <see cref="World"/> property is not null before calling this
    /// method.</remarks>
    /// <typeparam name="T">The type of the component that has changed.</typeparam>
    /// <param name="component">The component instance that has changed.</param>
    public readonly void NotifyComponentChanged<T>(T component)
    {
        World?.NotifyComponentChanged<T>(this);
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
