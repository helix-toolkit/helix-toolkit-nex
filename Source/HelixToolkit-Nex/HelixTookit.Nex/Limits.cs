namespace HelixToolkit.Nex;

public static class Limits
{
    public const uint MaxEntityId = 0xFFFFu; // 16 bits (65535) for entity ID
    public const uint MaxWorldId = 0xFu; // 4 bits (15) for world ID
    public const uint MaxComponentTypeCount = 128u; // 128 component types (64 bits per index * 2 indices) for component type identification
    public const uint MaxInstanceCount = 0x3FFFFFu; // 22 bits (4,194,303) for instancing count in draw calls
    public const uint MaxIndexCount = 0x3FFFFFu; // 22 bits (4,194,303) for primitive ID in shader packing
}
