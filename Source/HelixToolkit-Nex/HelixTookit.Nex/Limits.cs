namespace HelixToolkit.Nex;

public static class Limits
{
    public const uint MaxEntityId = 0xFFFFFFu; // 24 bits (16,777,215) for entity ID
    public const uint MaxEntityVersion = 0xFFFFFu; // 20 bits (1,048,575) for entity versioning to detect stale references
    public const uint MaxInstanceCount = 0xFFFFFu; // 20 bits (1,048,575) for instancing count in draw calls
}
