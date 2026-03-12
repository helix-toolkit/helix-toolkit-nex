using static HelixToolkit.Nex.ECS.ComponentTypeSet;

namespace HelixToolkit.Nex.ECS.Utils;

public readonly record struct EntityChangedEvent(int EntityId, ComponentTypeId Type);

public sealed class RuleBuilder : IDisposable
{
    private enum OpType
    {
        And,
        Not,
    }

    private readonly struct FilterInfo(
        OpType op,
        in ComponentTypeId typeId,
        IDisposable subscription
    ) : IDisposable
    {
        public readonly OpType Op = op;
        public readonly ComponentTypeId TypeId = typeId;
        private readonly IDisposable _subscription = subscription;

        public void Dispose()
        {
            _subscription?.Dispose();
        }
    }

    public World World => _world;

    public int WorldId => World.Id;

    private readonly World _world;
    private readonly List<FilterInfo> _typeList = [];

    private ComponentTypeSet _withFilters = new();
    private ComponentTypeSet _withoutFilters = new();

    private IDisposable? _entityEnabledSubscription = null;
    private readonly IDisposable? _entityDisposingSubscription = null;

    public event EventHandler<int>? EntityAdded;
    public event EventHandler<int>? EntityRemoved;
    public event EventHandler<EntityChangedEvent>? EntityChanged;

    internal RuleBuilder(World world)
    {
        _world = world;
        _entityDisposingSubscription = Publisher.Subscribe<EntityDisposingEvent>(
            WorldId,
            (w, msg) => EntityRemoved?.Invoke(this, msg.EntityId)
        );
    }

    /// <summary>
    /// Has this type of component. Multiple <see cref="Has{T}"/> creates a logical AND filter chain.
    /// <para>
    /// Usage: <c>Has&lt;T1&gt;().Has&lt;T2&gt;().Build()</c>
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public RuleBuilder Has<T>()
    {
        return AddOrRemove<T>(OpType.And);
    }

    /// <summary>
    /// Does not has this type of component. Multiple <see cref="NotHas{T}"/> creates a logical AND NOT filter chain.
    /// <para>
    /// Usage: <c>NotHas&lt;T1&gt;().NotHas&lt;T2&gt;().Build()</c>
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public RuleBuilder NotHas<T>()
    {
        return AddOrRemove<T>(OpType.Not);
    }

    /// <summary>
    /// Only contains enabled entities.
    /// </summary>
    /// <returns></returns>
    public RuleBuilder EnabledOnly()
    {
        if (_entityEnabledSubscription == null)
        {
            _entityEnabledSubscription = Publisher.Subscribe<EntityEnableEvent>(
                World.Id,
                (w, msg) => OnEnableChanged(msg.EntityId, msg.Enabled)
            );
        }
        return this;
    }

    private RuleBuilder AddOrRemove<T>(OpType op)
    {
        var id = ComponentManager<T>.TypeId;
        var subObj = Publisher.Subscribe<ComponentChangedEvent<T>>(
            World.Id,
            (w, msg) => OnComponentChanged(msg)
        );
        var info = new FilterInfo(op, id, subObj);
        _typeList.Add(info);
        return this;
    }

    internal void OnComponentChanged<T>(in ComponentChangedEvent<T> msg)
    {
        var id = ComponentManager<T>.TypeId;
        switch (msg.Operation)
        {
            case ComponentOperations.Added:
                if (_withoutFilters.HasType(id))
                {
                    EntityRemoved?.Invoke(this, msg.EntityId);
                    break;
                }
                if (_withFilters.HasType(id) && Evaluate(World.GetEntity(msg.EntityId)))
                {
                    EntityAdded?.Invoke(this, msg.EntityId);
                    break;
                }
                break;
            case ComponentOperations.Removed:
                if (_withFilters.HasType(id))
                {
                    EntityRemoved?.Invoke(this, msg.EntityId);
                    break;
                }
                if (_withoutFilters.HasType(id) && Evaluate(World.GetEntity(msg.EntityId)))
                {
                    EntityAdded?.Invoke(this, msg.EntityId);
                    break;
                }
                break;
            case ComponentOperations.Changed:
                if (_withFilters.HasType(id) && Evaluate(World.GetEntity(msg.EntityId)))
                {
                    EntityChanged?.Invoke(this, new(msg.EntityId, msg.ComponentTypeId));
                }
                break;
        }
    }

    private void OnEnableChanged(in int entityId, bool enabled)
    {
        if (enabled && Evaluate(World.GetEntity(entityId)))
        {
            EntityAdded?.Invoke(this, entityId);
        }
        else
        {
            EntityRemoved?.Invoke(this, entityId);
        }
    }

    public bool Evaluate(Entity entity)
    {
        ref var state = ref World.GetState(entity.Id, entity.Generation);
        if (!state.Valid)
        {
            return false;
        }

        if (state.ComponentTypes.HasAnyType(_withoutFilters))
        {
            return false;
        }

        if (!state.ComponentTypes.ContainsAllTypes(_withFilters))
        {
            return false;
        }
        return true;
    }

    public EntityCollection Build()
    {
        ref var target = ref _withFilters;
        foreach (var type in _typeList)
        {
            switch (type.Op)
            {
                case OpType.And:
                    target = ref _withFilters;
                    break;
                case OpType.Not:
                    target = ref _withoutFilters;
                    break;
            }
            target.AddType(type.TypeId);
        }
        return new EntityCollection(this);
    }

    #region Disposible
    private bool _disposed = false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _entityDisposingSubscription?.Dispose();
        _entityEnabledSubscription?.Dispose();
        foreach (var t in _typeList)
        {
            t.Dispose();
        }
        _typeList.Clear();
        _withFilters.Reset();
        _withoutFilters.Reset();
        EntityAdded = null;
        EntityRemoved = null;
    }
    #endregion
}
