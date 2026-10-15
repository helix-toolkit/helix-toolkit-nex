using System.Collections.ObjectModel;
using System.Numerics;

namespace HelixToolkit.Nex;

/// <summary>
/// Derives all bit-packing constants (masks, bit widths, shift amounts) from <see cref="Limits"/>
/// for use in GLSL shader placeholder replacement and C# unpacking logic.
/// </summary>
public static class LimitsShaderConstants
{
    // ── Bit widths (derived from max values) ──────────────────────────────

    /// <summary>Number of bits for world ID (4).</summary>
    public static readonly int WorldIdBits;

    /// <summary>Number of bits for entity ID (16).</summary>
    public static readonly int EntityIdBits;

    /// <summary>Number of bits for instance count (22).</summary>
    public static readonly int InstanceCountBits;

    /// <summary>Number of bits for index count / primitive ID (22).</summary>
    public static readonly int IndexCountBits;

    // ── X / Y channel layout ──────────────────────────────────────────────

    /// <summary>Low bits of instance index stored in X channel (12).</summary>
    public static readonly int InstanceLowBits;

    /// <summary>High bits of instance index stored in Y channel (10).</summary>
    public static readonly int InstanceHighBits;

    // ── Shift amounts ─────────────────────────────────────────────────────

    /// <summary>Shift for entity ID within X channel (4).</summary>
    public static readonly int EntityIdShift;

    /// <summary>Shift for instance-low within X channel (20).</summary>
    public static readonly int InstanceLowShift;

    /// <summary>Shift for primitive ID within Y channel (10).</summary>
    public static readonly int PrimitiveIdShift;

    // ── Masks (as uint) ───────────────────────────────────────────────────

    /// <summary>Mask for world ID (0xF).</summary>
    public static readonly uint WorldIdMask;

    /// <summary>Mask for entity ID (0xFFFF).</summary>
    public static readonly uint EntityIdMask;

    /// <summary>Mask for instance-low bits (0xFFF).</summary>
    public static readonly uint InstanceLowMask;

    /// <summary>Mask for instance-high bits (0x3FF).</summary>
    public static readonly uint InstanceHighMask;

    /// <summary>Mask for full instance count (0x3FFFFF).</summary>
    public static readonly uint InstanceCountMask;

    /// <summary>Mask for index count / primitive ID (0x3FFFFF).</summary>
    public static readonly uint IndexCountMask;

    static LimitsShaderConstants()
    {
        // Validate that each Limits constant is of the form (1 << n) - 1
        ValidatePowerOfTwoMinusOne(Limits.MaxWorldId, nameof(Limits.MaxWorldId));
        ValidatePowerOfTwoMinusOne(Limits.MaxEntityId, nameof(Limits.MaxEntityId));
        ValidatePowerOfTwoMinusOne(Limits.MaxInstanceCount, nameof(Limits.MaxInstanceCount));
        ValidatePowerOfTwoMinusOne(Limits.MaxPrimitiveCount, nameof(Limits.MaxPrimitiveCount));

        // Derive bit widths
        WorldIdBits = (int)BitOperations.Log2(Limits.MaxWorldId + 1);
        EntityIdBits = (int)BitOperations.Log2(Limits.MaxEntityId + 1);
        InstanceCountBits = (int)BitOperations.Log2(Limits.MaxInstanceCount + 1);
        IndexCountBits = (int)BitOperations.Log2(Limits.MaxPrimitiveCount + 1);

        // Derive channel layout
        InstanceLowBits = 32 - WorldIdBits - EntityIdBits;
        InstanceHighBits = InstanceCountBits - InstanceLowBits;

        // Derive shift amounts
        EntityIdShift = WorldIdBits;
        InstanceLowShift = WorldIdBits + EntityIdBits;
        PrimitiveIdShift = InstanceHighBits;

        // Derive masks
        WorldIdMask = Limits.MaxWorldId;
        EntityIdMask = Limits.MaxEntityId;
        InstanceLowMask = (1u << InstanceLowBits) - 1;
        InstanceHighMask = (1u << InstanceHighBits) - 1;
        InstanceCountMask = Limits.MaxInstanceCount;
        IndexCountMask = Limits.MaxPrimitiveCount;

        // Validate channel constraints
        if (WorldIdBits + EntityIdBits + InstanceLowBits != 32)
        {
            throw new InvalidOperationException(
                $"X channel overflow: WorldIdBits ({WorldIdBits}) + EntityIdBits ({EntityIdBits}) + InstanceLowBits ({InstanceLowBits}) != 32"
            );
        }

        if (InstanceHighBits + IndexCountBits != 32)
        {
            throw new InvalidOperationException(
                $"Y channel overflow: InstanceHighBits ({InstanceHighBits}) + IndexCountBits ({IndexCountBits}) != 32"
            );
        }
    }

    /// <summary>
    /// Returns a dictionary mapping GLSL placeholder tokens to their formatted values (with <c>u</c> suffix).
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetGlslPlaceholders()
    {
        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>
            {
                ["LIMITS_WORLD_ID_MASK"] = $"0x{WorldIdMask:X}u",
                ["LIMITS_ENTITY_ID_MASK"] = $"0x{EntityIdMask:X}u",
                ["LIMITS_INSTANCE_LOW_MASK"] = $"0x{InstanceLowMask:X}u",
                ["LIMITS_INSTANCE_HIGH_MASK"] = $"0x{InstanceHighMask:X}u",
                ["LIMITS_INSTANCE_COUNT_MASK"] = $"0x{InstanceCountMask:X}u",
                ["LIMITS_INDEX_COUNT_MASK"] = $"0x{IndexCountMask:X}u",
                ["LIMITS_ENTITY_ID_SHIFT"] = $"{EntityIdShift}u",
                ["LIMITS_INSTANCE_LOW_SHIFT"] = $"{InstanceLowShift}u",
                ["LIMITS_INSTANCE_LOW_BITS"] = $"{InstanceLowBits}u",
                ["LIMITS_INSTANCE_HIGH_SHIFT"] = $"{PrimitiveIdShift}u",
            }
        );
    }

    private static void ValidatePowerOfTwoMinusOne(uint value, string name)
    {
        // A value of the form (1 << n) - 1 has all lower bits set,
        // so value & (value + 1) == 0 and value > 0.
        if (value == 0 || (value & (value + 1)) != 0)
        {
            throw new InvalidOperationException(
                $"{name} (0x{value:X}) is not of the form (1 << n) - 1."
            );
        }
    }
}
