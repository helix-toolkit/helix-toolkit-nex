namespace HelixToolkit.Nex.Shaders.Frag;

/// <summary>
/// Selects the active stage of the border-highlight shader (specialization constant 0).
/// </summary>
public enum HighlightMode : uint
{
    /// <summary>
    /// Flat white fragment output used to rasterise the silhouette mask.
    /// The vertex shader is the standard mesh vertex shader; only the fragment
    /// output is overridden here so that every covered pixel becomes solid white.
    /// </summary>
    Mask = 0,

    /// <summary>
    /// Full-screen edge-detect and composite stage.
    /// Reads the scene colour texture and the silhouette mask, runs a 3×3
    /// neighbourhood sample to detect mask edges, and blends the per-entity
    /// highlight colour over the scene where an edge is found.
    /// </summary>
    Composite = 1,
}
