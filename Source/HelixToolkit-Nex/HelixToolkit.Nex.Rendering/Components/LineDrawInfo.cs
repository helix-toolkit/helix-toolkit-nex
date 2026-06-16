namespace HelixToolkit.Nex.Rendering.Components;

public struct LineDrawInfo(
    Geometry? geometry = null,
    string materialTypeName = "Default",
    bool cullable = true,
    bool hitable = true
)
{
    public Geometry? Geometry { get; set; } = geometry;
    public Color4 LineColor { set; get; } = Color4.White;

    /// <summary>
    /// Screen-space line width in pixels. The line vertex shader clamps the value to
    /// the valid range [1.0, 64.0] (Requirement 2.3). Defaults to 1.0.
    /// </summary>
    public float LineThickness { set; get; } = 1.0f;

    public readonly int LineCount => Geometry?.Vertices.Count / 2 ?? 0;

    /// <summary>
    /// Gets or sets the name of the line material.
    /// If specified, this name is used to look up the material in the <see cref="LineMaterialRegistry"/>.
    /// If not found, it falls back to use default material.
    /// </summary>
    public string LineMaterialName { get; set; } = materialTypeName;

    /// <summary>
    /// The line material type ID that determines which fragment shader pipeline is used
    /// for rendering this line object. Defaults to 0, which corresponds to the default line material.
    /// Register custom materials via <see cref="LineMaterialRegistry"/>.
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
    public readonly bool Valid =>
        Geometry is not null && Geometry.Valid && !string.IsNullOrEmpty(LineMaterialName);
}
