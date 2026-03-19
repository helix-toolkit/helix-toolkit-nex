namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Marks a mesh entity for wireframe rendering.
/// When present on an entity that also has a <see cref="MeshComponent"/>,
/// the <c>WireframePostEffect</c> will draw the mesh's edges as coloured lines
/// overlaid on the scene colour during the post-processing stage.
/// </summary>
public struct WireframeComponent
{
    /// <summary>
    /// The colour of the wireframe lines.
    /// </summary>
    public Color4 Color;

    public WireframeComponent(Color4 color)
    {
        Color = color;
    }

    /// <summary>
    /// A default green wireframe.
    /// </summary>
    public static readonly WireframeComponent Default = new(new Color4(0, 1, 0, 1));
}
