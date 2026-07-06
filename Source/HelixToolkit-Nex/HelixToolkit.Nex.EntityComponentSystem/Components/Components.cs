using System.Diagnostics.CodeAnalysis;
using ZLinq;
using ZLinq.Linq;

namespace HelixToolkit.Nex.ECS;

/// <summary>
/// Struct-based enumerator for iterating over valid entity IDs
/// without heap allocation.
/// </summary>
public struct EntityEnumerator<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
    )]
T
> : IEnumerator<Entity>
{
    private readonly World? _world = null;
    private readonly FastList<EntityMappingKey>? _mapping = null;
    private readonly HashSet<Entity>? _entities;
    private FromHashSet<Entity> _entityHashIter;
    private Entity _next = Entity.Null;
    private int _index = -1;

    internal EntityEnumerator(World? world, FastList<EntityMappingKey> mapping)
    {
        _world = world;
        _mapping = mapping;
    }

    internal EntityEnumerator(HashSet<Entity> entities)
    {
        _entities = entities;
        _entityHashIter = entities.AsValueEnumerable().Enumerator;
    }

    public EntityEnumerator() { }

    public readonly Entity Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_world is not null && _mapping is not null)
            {
                return _world.GetEntity(_mapping[_index].Entity);
            }
            else
            {
                return _next;
            }
        }
    }

    readonly object IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_world is not null && _mapping is not null)
        {
            var mappingArray = _mapping.GetInternalArray();
            var mappingCount = _mapping.Count;
            while (++_index < mappingCount)
            {
                if (mappingArray[_index].Entity > 0)
                {
                    return true;
                }
            }
            return false;
        }
        else if (_entities is not null)
        {
            return _entityHashIter.TryGetNext(out _next);
        }
        return false;
    }

    public void Reset()
    {
        _index = -1;
        if (_entities is not null)
        {
            _entityHashIter.Dispose();
            _entityHashIter = _entities.AsValueEnumerable().Enumerator;
        }
    }

    public void Dispose()
    {
        _entityHashIter.Dispose();
    }

    public static readonly EntityEnumerator<T> Empty = new();
}

public struct ComponentEntities<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
    )]
T
>(EntityEnumerator<T> enumerator) : IEnumerable<Entity>
{
    public readonly EntityEnumerator<T> GetEnumerator()
    {
        return enumerator;
    }

    readonly IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator()
    {
        return enumerator;
    }

    readonly IEnumerator IEnumerable.GetEnumerator()
    {
        return enumerator;
    }

    public static readonly ComponentEntities<T> Empty = new();
}

public interface IComponents<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
    )]
T
>
{
    ref T this[Entity entity] { get; }

    T[] GetInternalArray();
    ComponentEntities<T> GetEntities();

    MappingEnumerator<T> GetEnumerator();

    int Count { get; }
}

public struct EmptyComponents<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
    )]
T
> : IComponents<T>
{
    public readonly ref T this[Entity entity]
    {
        get =>
            throw new InvalidOperationException("EmptyComponents does not contain any components.");
    }

    public readonly T[] GetInternalArray()
    {
        return Array.Empty<T>();
    }

    public readonly ComponentEntities<T> GetEntities()
    {
        return ComponentEntities<T>.Empty;
    }

    public readonly MappingEnumerator<T> GetEnumerator() =>
        throw new InvalidOperationException("EmptyComponents does not contain any components.");

    public readonly int Count => 0;

    public static readonly EmptyComponents<T> Empty = new();
}

/// <summary>
/// Enumerates components in storage using the mapping's ComponentIndex,
/// skipping invalid (unassigned) mapping entries.
/// </summary>
public struct MappingEnumerator<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
    )]
T
>
{
    private readonly FastList<EntityMappingKey>? _mapping;
    private readonly FastList<T>? _storage;
    private int _index;

    internal MappingEnumerator(FastList<EntityMappingKey> mapping, FastList<T> storage)
    {
        Debug.Assert(mapping.Count == storage.Count);
        _mapping = mapping;
        _storage = storage;
        _index = -1;
    }

    public readonly ref T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _storage!.At(_index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_mapping is null || _storage is null)
        {
            return false;
        }
        Debug.Assert(_mapping.Count == _storage.Count);
        var mappingArray = _mapping.GetInternalArray();
        var mappingCount = _mapping.Count;
        while (++_index < mappingCount)
        {
            ref var key = ref mappingArray[_index];
            if (key.Valid)
            {
                return true;
            }
        }
        return false;
    }

    public static readonly MappingEnumerator<T> Empty = new();
}
