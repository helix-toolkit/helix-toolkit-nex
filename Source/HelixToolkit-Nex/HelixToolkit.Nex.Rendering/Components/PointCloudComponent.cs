namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// ECS component that describes a point cloud attached to an entity.
/// <para>
/// Each entity with a <see cref="PointCloudComponent"/> contributes its points to the
/// GPU point buffer managed by the point data provider. The component stores CPU-side
/// point data that is collected and uploaded each frame.
/// </para>
/// </summary>
public struct PointCloudComponent
{
    /// <summary>
    /// Gets points to be rendered as a point cloud. The geometry must contain a vertex
    /// </summary>
    public Geometry? Geometry { get; set; }

    /// <summary>
    /// Number of valid points in the <see cref="Geometry.Vertices"/>.
    /// </summary>
    public readonly int PointCount => Geometry?.Vertices.Count ?? 0;

    /// <summary>
    /// Whether the object can be selected via GPU picking.
    /// </summary>
    public bool Hitable { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the <c>PointData.Size</c> field is interpreted as a
    /// fixed screen-space size in pixels (no perspective scaling). When <see langword="false"/>
    /// (default), it is interpreted as a world-space diameter that is projected to screen space.
    /// </summary>
    public bool FixedSize { get; set; }

    /// <summary>
    /// Gets or sets the name of the point material.
    /// If specified, this name is used to look up the material in the <see cref="PointMaterialRegistry"/>.
    /// If not found, it falls back to use <see cref="PointMaterialId"/>.
    /// If both are not specified, it defaults to the default point material."/>
    /// </summary>
    public string? PointMaterialName { get; set; }

    /// <summary>
    /// The point material type ID that determines which fragment shader pipeline is used
    /// for rendering this point cloud. Defaults to 0, which corresponds to the default point material (e.g., a simple
    /// (circle SDF). Register custom materials via <see cref="PointMaterialRegistry"/>.
    /// </summary>
    public MaterialTypeId PointMaterialId { get; internal set; }

    /// <summary>
    /// Represents the size for each point in the cloud. The interpretation of this value depends on the <see cref="FixedSize"/> field:
    /// </summary>
    /// <remarks>The value of this field determines the dimensions or scale of the object.  Ensure that the
    /// value is non-negative to avoid unexpected behavior.</remarks>
    public float Size { get; set; } = 1f;

    /// <summary>
    /// Represents the color of all points in the cloud if <see cref="Geometry.VertexColors"/> is not provided.
    /// </summary>
    /// <remarks>The <see cref="Color4"/> structure typically includes RGBA (red, green, blue, alpha)
    /// components. This field can be used to define or modify the color of the object.</remarks>
    public Color4 Color { get; set; } = new Color4(1f, 0, 0, 1f);

    /// <summary>
    /// Gets a value indicating whether this component has valid point data.
    /// </summary>
    public readonly bool Valid => PointCount > 0;

    /// <summary>
    /// Gets or sets the index of the texture used in the rendering process.
    /// </summary>
    public uint TextureIndex { set; get; }

    /// <summary>
    /// Gets or sets the index of the sampler used in the operation.
    /// </summary>
    public uint SamplerIndex { set; get; }

    public PointCloudComponent() { }

    public PointCloudComponent(
        Geometry geometry,
        Color4 color,
        bool hitable = true,
        uint textureIndex = 0,
        uint samplerIndex = 0,
        bool fixedSize = false,
        string? pointMaterialName = default,
        float size = 1.0f
    )
    {
        Geometry = geometry;
        Color = color;
        Hitable = hitable;
        TextureIndex = textureIndex;
        SamplerIndex = samplerIndex;
        FixedSize = fixedSize;
        Size = size;
        PointMaterialName = pointMaterialName;
    }

    public override readonly string ToString()
    {
        return $"PointCloud: Count={PointCount}; Hitable={Hitable}; FixedSize={FixedSize}; MaterialId={PointMaterialId.Id}";
    }
}
