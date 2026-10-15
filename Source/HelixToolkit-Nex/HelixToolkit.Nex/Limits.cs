namespace HelixToolkit.Nex;

public static class Limits
{
    public const uint MaxEntityId = 0x3FFFFu; // 18 bits (262143) for entity ID
    public const uint MaxWorldId = 0xFu; // 4 bits (15) for world ID
    public const uint MaxComponentTypeCount = 128u; // 128 component types (64 bits per index * 2 indices) for component type identification
    public const uint MaxInstanceCount = 0x1FFFFFu; // 21 bits (2,097,151) for instancing count in draw calls
    public const uint MaxPrimitiveCount = 0x1FFFFFu; // 21 bits (2,097,151) for primitive ID in shader packing
    /// <summary>
    /// Hard upper bound on <see cref="Config.MaxLightsPerTile"/>. Each per-tile sub-count
    /// (opaque and transparent) is stored in a single byte of the packed
    /// <c>LightGridTile.lightCount</c>, so the per-sub-list capacity cannot exceed 255.
    /// </summary>
    public const uint MaxLightsPerTileLimit = byte.MaxValue;
    /// <summary>
    /// Maximum number of scene range lights considered for culling. Stored light indices are
    /// constrained to a 16-bit ushort, so the global range-light set is limited to 65535 so that
    /// no index written into the light-index buffer can exceed 65535.
    /// </summary>
    public const uint MaxRangeLightCount = ushort.MaxValue;
}
