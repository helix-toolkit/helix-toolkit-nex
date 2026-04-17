namespace HelixToolkit.Nex;

public static class Limits
{
    public const uint MaxEntityId = 0x3FFFFu; // 18 bits (262143) for entity ID
    public const uint MaxWorldId = 0xFu; // 4 bits (15) for world ID
    public const uint MaxComponentTypeCount = 128u; // 128 component types (64 bits per index * 2 indices) for component type identification
    public const uint MaxInstanceCount = 0x1FFFFFu; // 21 bits (2,097,151) for instancing count in draw calls
    public const uint MaxPrimitiveCount = 0x1FFFFFu; // 21 bits (2,097,151) for primitive ID in shader packing
}
