namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Provide a collection of entities defined by the filters.
/// Collection changes when entity or component state changes according to the defined filter at build time.
/// </summary>
public sealed class EntityCollection : IEnumerable<Entity>, IDisposable
{
    public static RuleBuilder Create(in World world)
    {
        return new RuleBuilder(world);
    }

    private readonly RuleBuilder _builder;
    private readonly HashSet<int> _entities = [];
    public World World => _builder.World;

    internal int WorldId => World.Id;

    public int Count => _entities.Count;

    public event EventHandler<int>? EntityAdded;
    public event EventHandler<int>? EntityRemoved;
    public event EventHandler<EntityChangedEvent>? EntityChanged;

    internal EntityCollection(RuleBuilder builder)
    {
        _builder = builder;
        _builder.EntityAdded += Builder__EntityAdded;
        _builder.EntityRemoved += Builder_EntityRemoved;
        _builder.EntityChanged += Builder__EntityChanged;
        foreach (var entity in builder.World)
        {
            if (_builder.Evaluate(entity))
            {
                AddEntity(entity.Id);
            }
        }
        ECSEventBus.Register<WorldDisposingEvent>(WorldId, HandleWorldDisposing);
        ECSEventBus.Register<EntityDisposingEvent>(WorldId, HandleEntityDisposing);
    }

    private void HandleWorldDisposing(int worldId, WorldDisposingEvent msg)
    {
        Dispose();
    }

    private void HandleEntityDisposing(int worldId, EntityDisposingEvent msg)
    {
        RemoveEntity(msg.EntityId);
    }

    private void AddEntity(int id)
    {
        if (_disposed)
        {
            return;
        }
        if (_entities.Contains(id))
        {
            return;
        }
        _entities.Add(id);
        EntityAdded?.Invoke(this, id);
    }

    private void RemoveEntity(int id)
    {
        if (_disposed || !_entities.Contains(id))
        {
            return;
        }
        _entities.Remove(id);
        EntityRemoved?.Invoke(this, id);
    }

    private void Builder_EntityRemoved(object? sender, int id)
    {
        RemoveEntity(id);
    }

    private void Builder__EntityAdded(object? sender, int id)
    {
        AddEntity(id);
    }

    private void Builder__EntityChanged(object? sender, EntityChangedEvent msg)
    {
        if (!_entities.Contains(msg.EntityId))
        {
            return;
        }
        EntityChanged?.Invoke(this, msg);
    }

    #region Enumerable
    private readonly struct Enumerator(World world, HashSet<int> entities) : IEnumerator
    {
        private readonly World _world = world;
        private readonly IEnumerator<int> _entities = entities.GetEnumerator();

        public readonly object Current => _world.GetEntity(_entities.Current);

        public readonly bool MoveNext()
        {
            return _entities.MoveNext();
        }

        public readonly void Reset()
        {
            _entities.Reset();
        }
    }

    public IEnumerator<Entity> GetEnumerator()
    {
        foreach (var id in _entities)
        {
            yield return World.GetEntity(id);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(World, _entities);
    }
    #endregion

    #region Disposable
    internal bool Disposed => _disposed;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        ECSEventBus.Unregister<EntityDisposingEvent>(WorldId, HandleEntityDisposing);
        ECSEventBus.Unregister<WorldDisposingEvent>(WorldId, HandleWorldDisposing);
        EntityAdded = null;
        EntityRemoved = null;
        EntityChanged = null;
        _builder.EntityAdded -= Builder__EntityAdded;
        _builder.EntityRemoved -= Builder_EntityRemoved;
        _builder.EntityChanged -= Builder__EntityChanged;
        _builder.Dispose();
        _entities.Clear();
    }
    #endregion
}
