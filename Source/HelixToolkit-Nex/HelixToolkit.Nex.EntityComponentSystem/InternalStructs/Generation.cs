namespace HelixToolkit.Nex.ECS;

[StructLayout(LayoutKind.Explicit)]
internal readonly struct Generation(byte worldId, byte worldGeneration, ushort entityGeneration)
    : IEquatable<Generation>
{
    [FieldOffset(0)]
    private readonly ushort _worldValue;

    [FieldOffset(0)]
    private readonly uint _value;

    [FieldOffset(0)]
    public readonly byte WorldId = worldId;

    [FieldOffset(1)]
    public readonly byte WorldGeneration = worldGeneration;

    [FieldOffset(2)]
    public readonly ushort EntityGeneration = entityGeneration;

    public bool Valid => Value != 0;

    public uint Value => _value;

    public bool HasWorld => _worldValue > 0;

    public ushort WorldValue => _worldValue;

    static Generation()
    {
        Debug.Assert(NativeHelper.SizeOf<Generation>() == 4);
    }

    public static bool operator ==(Generation a, Generation b) => a.Value == b.Value;

    public static bool operator !=(Generation a, Generation b) => a.Value != b.Value;

    public override bool Equals(object? obj) => obj is Generation gen && gen.Value == Value;

    public override int GetHashCode() => (int)Value;

    public override string ToString() => $"Generation {WorldGeneration}:{EntityGeneration}";

    public bool Equals(Generation other)
    {
        return Value == other.Value;
    }
}
