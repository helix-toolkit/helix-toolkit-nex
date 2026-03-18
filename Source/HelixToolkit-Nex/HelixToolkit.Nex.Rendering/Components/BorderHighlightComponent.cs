namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Marks a mesh entity for border-highlight rendering.
/// When present on an entity that also has a <see cref="MeshComponent"/>,
/// the <c>BorderHighlightPostEffect</c> will draw a coloured outline around
/// the mesh silhouette during the post-processing stage.
/// </summary>
public struct BorderHighlightComponent
{
    /// <summary>
    /// The colour of the highlight outline.
    /// </summary>
    public Color4 Color;

    /// <summary>
    /// Half-width of the outline in texels (at full screen resolution).
    /// Defaults to 2.
    /// </summary>
    public float Thickness;

    public BorderHighlightComponent(Color4 color, float thickness = 2f)
    {
        Color = color;
        Thickness = thickness;
    }

    /// <summary>
    /// A default yellow highlight with 2-texel thickness.
    /// </summary>
    public static readonly BorderHighlightComponent Default = new(new Color4(1, 1, 0, 1), 2f);
}
