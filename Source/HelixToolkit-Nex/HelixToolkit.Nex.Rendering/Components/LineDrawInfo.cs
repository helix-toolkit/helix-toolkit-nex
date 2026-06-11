namespace HelixToolkit.Nex.Rendering.Components;

public struct LineDrawInfo(
    Geometry? geometry = null,
    string materialTypeName = "",
    bool cullable = true,
    bool hitable = true
)
{
    public Geometry? Geometry { get; set; } = geometry;
    public Color4 LineColor { set; get; } = Color4.White;

    public float LineThickness { set; get; }

    public readonly int LineCount => Geometry?.Vertices.Count / 2 ?? 0;

    /// <summary>
    /// Gets or sets the name of the line material.
    /// If specified, this name is used to look up the material in the <see cref="LineMaterialRegistry"/>.
    /// If not found, it falls back to use <see cref="LineMaterialId"/>.
    /// If both are not specified, it defaults to the default line material."/>
    /// </summary>
    public string? LineMaterialName { get; set; } = materialTypeName;

    /// <summary>
    /// The line material type ID that determines which fragment shader pipeline is used
    /// for rendering this line object. Defaults to 0, which corresponds to the default line material (e.g., a simple
    /// (circle SDF). Register custom materials via <see cref="LineMaterialRegistry"/>.
    /// </summary>
    public MaterialTypeId LineMaterialId { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether this line object is hitable.
    /// </summary>
    public bool Hitable { set; get; } = hitable;

    /// <summary>
    /// Gets or sets a value indicating whether this line object is cullable.
    /// </summary>
    public bool Cullable { set; get; } = cullable;

    /// <summary>
    /// Gets the draw stream variants for this line object based on its properties.
    /// </summary>
    public readonly DrawStreamVariants Variants
    {
        get
        {
            DrawStreamVariants variant = 0;
            if (Hitable)
            {
                variant |= DrawStreamVariants.Hitable;
            }

            if (Geometry?.IsDynamic == true)
            {
                variant |= DrawStreamVariants.Dynamic;
            }

            return variant;
        }
    }

    /// <summary>
    /// Creates an empty LineDrawInfo with null handles.
    /// </summary>
    public static readonly LineDrawInfo Empty = new();

    /// <summary>
    /// Gets a value indicating whether this LineDrawInfo has valid handles.
    /// </summary>
    public readonly bool Valid => Geometry is not null && Geometry.Valid && !string.IsNullOrEmpty(LineMaterialName);
}
