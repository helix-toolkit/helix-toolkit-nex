namespace HelixToolkit.Nex.ECS;

public readonly struct ComponentTypeId(in UInt128 bitMap) : IEquatable<ComponentTypeId>
{
    public readonly UInt128 BitMap = bitMap;
    private static UInt128 _currBitMap = 1;
    private static readonly object _lockObj = new();

    public static ComponentTypeId GetNexId()
    {
        lock (_lockObj)
        {
            if (_currBitMap == new UInt128(0x80000000, 0x00000000))
            {
                throw new NotSupportedException(
                    $"Maximum number of component types ({Limits.MaxComponentTypeCount}) exceeded. Consider using a different approach for component type identification."
                );
            }
            else
            {
                _currBitMap <<= 1;
            }
            return new ComponentTypeId(_currBitMap);
        }
    }

    public readonly bool Equals(ComponentTypeId other)
    {
        return BitMap == other.BitMap;
    }

    public static bool operator ==(ComponentTypeId a, ComponentTypeId b) => a.BitMap == b.BitMap;

    public static bool operator !=(ComponentTypeId a, ComponentTypeId b) => a.BitMap != b.BitMap;

    public override bool Equals(object? obj)
    {
        return obj is ComponentTypeId id && Equals(id);
    }

    public override int GetHashCode() => BitMap.GetHashCode();
}

internal struct ComponentTypeSet()
{
    private UInt128 _types = 0;

    public readonly UInt128 Types => _types;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddType(in ComponentTypeId id)
    {
        _types |= id.BitMap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveType(in ComponentTypeId id)
    {
        if (HasType(id))
        {
            _types &= ~id.BitMap;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasType(in ComponentTypeId id)
    {
        return (_types & id.BitMap) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasAnyType(in ComponentTypeSet set)
    {
        return (_types & set.Types) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ContainsAllTypes(in ComponentTypeId id)
    {
        return (_types & id.BitMap) == id.BitMap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ContainsAllTypes(in ComponentTypeSet set)
    {
        return (_types & set.Types) == set.Types;
    }

    public readonly bool this[ComponentTypeId id]
    {
        get => HasType(id);
    }

    public void Reset()
    {
        _types = 0;
    }
}
