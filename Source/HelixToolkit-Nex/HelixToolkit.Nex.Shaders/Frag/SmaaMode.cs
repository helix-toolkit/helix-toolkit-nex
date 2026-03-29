namespace HelixToolkit.Nex.Shaders.Frag;

/// <summary>
/// Selects the active stage of the SMAA (Subpixel Morphological Anti-Aliasing) shader
/// (specialization constant 0).
/// </summary>
public enum SmaaMode : uint
{
    /// <summary>
    /// Edge-detection pass: detects luminance-based edges in the colour buffer and
    /// writes a two-channel edge mask (R = horizontal edge, G = vertical edge).
    /// </summary>
    EdgeDetection = 0,

    /// <summary>
    /// Blending-weight computation pass: reads the edge mask, searches for crossing
    /// patterns along both axes, and computes per-pixel MLAA blending weights stored
    /// in an RGBA intermediate texture.
    /// </summary>
    BlendingWeights = 1,

    /// <summary>
    /// Neighbourhood-blending pass: combines the original colour with its neighbours
    /// using the precomputed blending weights to produce the final anti-aliased output.
    /// </summary>
    NeighbourhoodBlending = 2,
}
