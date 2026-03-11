namespace HelixToolkit.Nex;

public static class Limits
{
    public const uint MaxEntityId = 0xFFFFFFu; // 24 bits (16,777,215) for entity ID
    public const uint MaxEntityVersion = 0xFFFFu; // 16 bits (65,535) for entity versioning to detect stale references
    public const uint MaxComponentTypeCount = 128u; // 128 component types (64 bits per index * 2 indices) for component type identification
    public const uint MaxInstanceCount = 0xFFFFFFu; // 24 bits (16,777,215) for instancing count in draw calls
}
