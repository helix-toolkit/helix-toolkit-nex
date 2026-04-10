namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// ECS component that describes a point cloud attached to an entity.
/// <para>
/// Each entity with a <see cref="PointCloudComponent"/> contributes its points to the
/// GPU point buffer managed by the point data provider. The component stores CPU-side
/// point data that is collected and uploaded each frame.
/// </para>
/// </summary>
public struct PointCloudComponent(
    Geometry geometry,
    Color4 color,
    bool hitable = true,
    uint textureIndex = 0,
    uint samplerIndex = 0,
    bool fixedSize = false,
    MaterialTypeId pointMaterialId = default,
    float size = 1.0f
)
{
    /// <summary>
    /// Gets points to be rendered as a point cloud. The geometry must contain a vertex
    /// </summary>
    public readonly Geometry Geometry => geometry;

    /// <summary>
    /// Number of valid points in the <see cref="Geometry.Vertices"/>.
    /// </summary>
    public readonly int PointCount => geometry.Vertices.Count;

    /// <summary>
    /// Whether the object can be selected via GPU picking.
    /// </summary>
    public bool Hitable = hitable;

    /// <summary>
    /// When <see langword="true"/>, the <c>PointData.Size</c> field is interpreted as a
    /// fixed screen-space size in pixels (no perspective scaling). When <see langword="false"/>
    /// (default), it is interpreted as a world-space diameter that is projected to screen space.
    /// </summary>
    public bool FixedSize = fixedSize;

    /// <summary>
    /// The point material type ID that determines which fragment shader pipeline is used
    /// for rendering this point cloud. Defaults to <see cref="PointMaterialId.Default"/>
    /// (circle SDF). Register custom materials via <see cref="PointMaterialRegistry"/>.
    /// </summary>
    public MaterialTypeId PointMaterialId = pointMaterialId;

    /// <summary>
    /// Represents the size for each point in the cloud. The interpretation of this value depends on the <see cref="FixedSize"/> field:
    /// </summary>
    /// <remarks>The value of this field determines the dimensions or scale of the object.  Ensure that the
    /// value is non-negative to avoid unexpected behavior.</remarks>
    public float Size = size;

    /// <summary>
    /// Represents the color of all points in the cloud if <see cref="Geometry.VertexColors"/> is not provided.
    /// </summary>
    /// <remarks>The <see cref="Color4"/> structure typically includes RGBA (red, green, blue, alpha)
    /// components. This field can be used to define or modify the color of the object.</remarks>
    public Color4 Color = color;

    /// <summary>
    /// Gets a value indicating whether this component has valid point data.
    /// </summary>
    public readonly bool Valid => PointCount > 0;

    /// <summary>
    /// Gets or sets the index of the texture used in the rendering process.
    /// </summary>
    public uint TextureIndex { internal set; get; } = textureIndex;

    /// <summary>
    /// Gets or sets the index of the sampler used in the operation.
    /// </summary>
    public uint SamplerIndex { internal set; get; } = samplerIndex;

    public override readonly string ToString()
    {
        return $"PointCloud: Count={PointCount}; Hitable={Hitable}; FixedSize={FixedSize}; MaterialId={PointMaterialId.Id}";
    }
}
