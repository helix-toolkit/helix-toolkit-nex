namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Named quality presets for the <see cref="SsaoPostEffect"/>.
///
/// Each preset maps to a recommended hemisphere <c>Sample_Count</c> defined by the
/// strictly increasing preset table <see cref="SsaoPresets.SampleCounts"/>
/// (<c>Low</c>=8, <c>Medium</c>=16, <c>High</c>=32, <c>Ultra</c>=64). Assigning a
/// preset authoritatively overwrites the effect's sample count.
/// </summary>
public enum SsaoQuality
{
    /// <summary>Lowest quality preset (Sample_Count 8).</summary>
    Low = 0,

    /// <summary>Default, balanced quality preset (Sample_Count 16).</summary>
    Medium = 1,

    /// <summary>High quality preset (Sample_Count 32).</summary>
    High = 2,

    /// <summary>Highest quality preset (Sample_Count 64).</summary>
    Ultra = 3,
}

/// <summary>
/// Debug visualization modes for the <see cref="SsaoPostEffect"/>.
/// </summary>
public enum SsaoDebugMode : uint
{
    /// <summary>Standard multiplicative composite over the scene color (default).</summary>
    Normal = 0,

    /// <summary>Writes the raw grayscale ambient-occlusion factor for tuning.</summary>
    RawAO = 1,
}

/// <summary>
/// Preset lookup tables for the <see cref="SsaoPostEffect"/>.
/// </summary>
internal static class SsaoPresets
{
    /// <summary>
    /// Strictly increasing hemisphere sample-count table indexed by <see cref="SsaoQuality"/>:
    /// <c>Low</c>=8, <c>Medium</c>=16, <c>High</c>=32, <c>Ultra</c>=64.
    /// </summary>
    internal static readonly int[] SampleCounts = [8, 16, 32, 64];
}
