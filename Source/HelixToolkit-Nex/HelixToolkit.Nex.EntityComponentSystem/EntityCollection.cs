using ZLinq;
using ZLinq.Linq;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Provide a collection of entities defined by the filters.
/// Collection changes when entity or component state changes according to the defined filter at build time.
/// </summary>
public sealed class EntityCollection : IDisposable, IEnumerable<Entity>
{
    public static RuleBuilder Create(in World world)
    {
        return new RuleBuilder(world);
    }

    private readonly RuleBuilder _builder;
    private readonly HashSet<Entity> _entities = [];
    private readonly FastList<Subscription> _subscriptions = [];
    public World World => _builder.World;

    internal int WorldId => World.Id;

    public int Count => _entities.Count;

    public event EventHandler<Entity>? EntityAdded;
    public event EventHandler<Entity>? EntityRemoved;
    public event EventHandler<EntityChangedEvent>? EntityChanged;
    public IReadOnlySet<Entity> Entities => _entities;

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
                AddEntity(entity);
            }
        }
        _subscriptions.Add(ECSEventBus.Register<WorldDisposingEvent>(World, HandleWorldDisposing));
        _subscriptions.Add(
            ECSEventBus.Register<EntityBeforeDisposeEvent>(World, HandleEntityDisposing)
        );
    }

    public bool Has(Entity entity)
    {
        return _entities.Contains(entity);
    }

    private void HandleWorldDisposing(World _, WorldDisposingEvent msg)
    {
        Dispose();
    }

    private void HandleEntityDisposing(World _, EntityBeforeDisposeEvent msg)
    {
        var entity = msg.Entity;
        RemoveEntity(entity);
    }

    private void AddEntity(Entity entity)
    {
        if (_disposed)
        {
            return;
        }
        if (_entities.Contains(entity))
        {
            return;
        }
        _entities.Add(entity);
        EntityAdded?.Invoke(this, entity);
    }

    private void RemoveEntity(Entity entity)
    {
        if (_disposed || !_entities.Contains(entity))
        {
            return;
        }
        _entities.Remove(entity);
        EntityRemoved?.Invoke(this, entity);
    }

    private void Builder_EntityRemoved(object? sender, Entity entity)
    {
        RemoveEntity(entity);
    }

    private void Builder__EntityAdded(object? sender, Entity entity)
    {
        AddEntity(entity);
    }

    private void Builder__EntityChanged(object? sender, EntityChangedEvent msg)
    {
        if (!_entities.Contains(msg.Entity))
        {
            return;
        }
        EntityChanged?.Invoke(this, msg);
    }

    public bool Contains(Entity entity)
    {
        return _entities.Contains(entity);
    }

    #region Enumerable
    public struct Enumerator(HashSet<Entity> entities) : IEnumerator<Entity>
    {
        private readonly HashSet<Entity> _entities = entities;
        private ValueEnumerator<FromHashSet<Entity>, Entity> _enumerator = entities
            .AsValueEnumerable()
            .GetEnumerator();

        public readonly Entity Current => _enumerator.Current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Reset()
        {
            _enumerator = _entities.AsValueEnumerable().GetEnumerator();
        }

        public void Dispose()
        {
            _enumerator.Dispose();
        }
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_entities);
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
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();
        EntityAdded = null;
        EntityRemoved = null;
        EntityChanged = null;
        _builder.EntityAdded -= Builder__EntityAdded;
        _builder.EntityRemoved -= Builder_EntityRemoved;
        _builder.EntityChanged -= Builder__EntityChanged;
        _builder.Dispose();
        _entities.Clear();
    }

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    #endregion
}
