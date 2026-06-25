namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// ECS component that describes a point cloud attached to an entity.
/// A <see cref="IRenderDataProvider.PointDrawStreams"/> consumes this component to
/// build <c>PointDraw</c> records for the draw-stream rendering path.
/// </summary>
public struct PointDrawInfo(
    Geometry? geometry = null,
    string materialTypeName = "Default",
    bool cullable = true,
    bool hitable = true
)
{
    /// <summary>
    /// Gets or sets the geometry whose vertices are rendered as a point cloud. Each vertex
    /// contributes exactly one point.
    /// </summary>
    public Geometry? Geometry { get; set; } = geometry;

    /// <summary>
    /// The color applied to all points when the geometry does not provide per-vertex colors.
    /// </summary>
    public Color4 PointColor { set; get; } = Color4.White;

    /// <summary>
    /// The size of each rendered point. Interpreted as a fixed screen-pixel diameter when
    /// <see cref="FixedSize"/> is <see langword="true"/>, otherwise as a distance-scaled
    /// (world-space-projected) diameter. Defaults to 1.0.
    /// </summary>
    public float PointSize { set; get; } = 1.0f;

    /// <summary>
    /// When <see langword="true"/>, <see cref="PointSize"/> is interpreted as a fixed
    /// screen-space size in pixels (no perspective scaling). When <see langword="false"/>
    /// (default), it is interpreted as a world-space diameter projected to screen space.
    /// </summary>
    public bool FixedSize { set; get; }

    /// <summary>
    /// Gets or sets the index of the bindless texture used when rendering points.
    /// </summary>
    public uint TextureIndex { set; get; }

    /// <summary>
    /// Gets or sets the index of the bindless sampler used when rendering points.
    /// </summary>
    public uint SamplerIndex { set; get; }

    /// <summary>
    /// Number of points in the cloud, equal to <see cref="Geometry.Vertices"/> count because the
    /// vertex buffer is a plain vertex list (point <c>s</c> = vertex <c>s</c>).
    /// </summary>
    public readonly int PointCount => Geometry?.Vertices.Count ?? 0;

    /// <summary>
    /// Gets or sets the name of the point material.
    /// If specified, this name is used to look up the material in the <see cref="PointMaterialRegistry"/>.
    /// If not found, it falls back to use default material.
    /// </summary>
    public string PointMaterialTypeName { get; set; } = materialTypeName;

    /// <summary>
    /// Gets or sets a value indicating whether this point object is hitable.
    /// </summary>
    public bool Hitable { set; get; } = hitable;

    /// <summary>
    /// Gets or sets a value indicating whether this point object is cullable.
    /// </summary>
    public bool Cullable { set; get; } = cullable;

    /// <summary>
    /// Gets the draw stream variants for this point object based on its properties.
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
    /// Creates an empty PointDrawInfo with null handles.
    /// </summary>
    public static readonly PointDrawInfo Empty = new();

    /// <summary>
    /// Gets a value indicating whether this PointDrawInfo has valid data: geometry present with a
    /// non-empty point list and a resolvable material name.
    /// </summary>
    public readonly bool Valid =>
        Geometry is not null
        && Geometry.Valid
        && PointCount > 0
        && !string.IsNullOrEmpty(PointMaterialTypeName);
}
