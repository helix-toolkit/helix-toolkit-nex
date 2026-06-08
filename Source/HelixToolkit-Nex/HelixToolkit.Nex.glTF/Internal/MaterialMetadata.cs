namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Specifies how the alpha value of a material is interpreted for rendering.
/// Maps directly to glTF alphaMode values.
/// </summary>
internal enum AlphaMode
{
    /// <summary>
    /// The alpha value is ignored and the rendered output is fully opaque.
    /// </summary>
    Opaque,

    /// <summary>
    /// The rendered output is either fully opaque or fully transparent depending
    /// on the alpha value and the specified alpha cutoff value.
    /// </summary>
    Mask,

    /// <summary>
    /// The alpha value is used to composite the source and destination areas.
    /// The rendered output is combined with the background using the normal painting
    /// operation (i.e. the Porter and Duff over operator).
    /// </summary>
    Blend,
}

/// <summary>
/// Contains material metadata that must be applied to the MeshNode rather than
/// the PBRMaterialProperties. The SceneBuilder uses this to configure transparency
/// and backface culling on the MeshNode.
/// </summary>
/// <param name="AlphaMode">The alpha rendering mode for this material.</param>
/// <param name="AlphaCutoff">
/// The alpha cutoff threshold when <paramref name="AlphaMode"/> is <see cref="AlphaMode.Mask"/>.
/// Fragments with alpha below this value are discarded. Default is 0.5.
/// </param>
/// <param name="DoubleSided">
/// When true, backface culling should be disabled on the MeshNode.
/// </param>
internal readonly record struct MaterialMetadata(
    AlphaMode AlphaMode,
    float AlphaCutoff,
    bool DoubleSided
)
{
    /// <summary>
    /// Default metadata: opaque, no alpha cutoff, single-sided (backface culling enabled).
    /// </summary>
    public static readonly MaterialMetadata Default = new(AlphaMode.Opaque, 0.5f, false);
}
