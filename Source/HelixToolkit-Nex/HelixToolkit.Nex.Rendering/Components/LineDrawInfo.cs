namespace HelixToolkit.Nex.Rendering.Components;

public struct LineDrawInfo(
    Geometry? geometry = null,
    string materialTypeName = "Default",
    bool cullable = true,
    bool hitable = true
)
{
    /// <summary>
    /// Creates a line draw info with default property values. Without this constructor,
    /// <c>new LineDrawInfo()</c> would zero-initialize the struct and skip the property initializers.
    /// </summary>
    public LineDrawInfo()
        : this(null) { }

    public Geometry? Geometry { get; set; } = geometry;
    public Color4 LineColor { set; get; } = Color4.White;

    /// <summary>
    /// Line width/thickness, interpreted according to <see cref="RenderSpace"/>: a fixed
    /// screen-space pixel width (<see cref="RenderSpace.Screen"/>, default) or a world-space
    /// diameter that is projected to screen space (<see cref="RenderSpace.World"/>). The line
    /// vertex shader clamps a screen-space width to [1.0, 64.0] px; a world-space width has no upper limit (minimum 1 px)
    /// (Requirement 2.3). Defaults to 1.0.
    /// </summary>
    public float LineThickness { set; get; } = 1.0f;

    /// <summary>
    /// Gets or sets whether <see cref="LineThickness"/> is interpreted as a fixed screen-space
    /// pixel width (<see cref="RenderSpace.Screen"/>, default) or a world-space diameter
    /// projected to screen space (<see cref="RenderSpace.World"/>).
    /// </summary>
    public RenderSpace RenderSpace { set; get; } = RenderSpace.Screen;

    /// <summary>
    /// Gets or sets the shape of segment ends that are not connected to another segment.
    /// Defaults to <see cref="LineCap.Butt"/>.
    /// </summary>
    public LineCap Cap { set; get; } = LineCap.Butt;

    /// <summary>
    /// Gets or sets how connected segments are joined. Consecutive segments are connected when the
    /// end vertex of one equals the start vertex of the next. Defaults to <see cref="LineJoin.Miter"/>;
    /// <see cref="LineJoin.None"/> renders every segment independently.
    /// </summary>
    public LineJoin Join { set; get; } = LineJoin.Miter;

    /// <summary>
    /// Gets or sets the maximum ratio of miter length to line width for <see cref="LineJoin.Miter"/>
    /// joins; sharper joins fall back to a bevel (same meaning as SVG <c>stroke-miterlimit</c>).
    /// Values below 1 are treated as 1. Defaults to 4.
    /// </summary>
    public float MiterLimit { set; get; } = 4.0f;

    /// <summary>
    /// Gets or sets the index of the bindless texture used when rendering lines.
    /// </summary>
    public uint TextureIndex { set; get; }

    /// <summary>
    /// Gets or sets the index of the bindless sampler used when rendering lines.
    /// </summary>
    public uint SamplerIndex { set; get; }

    public readonly int LineCount => Geometry?.Vertices.Count / 2 ?? 0;

    /// <summary>
    /// Gets or sets the name of the line material.
    /// If specified, this name is used to look up the material in the <see cref="LineMaterialRegistry"/>.
    /// If not found, it falls back to use default material.
    /// </summary>
    public string LineMaterialTypeName { get; set; } = materialTypeName;

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
        Geometry is not null && Geometry.Valid && !string.IsNullOrEmpty(LineMaterialTypeName);
}
