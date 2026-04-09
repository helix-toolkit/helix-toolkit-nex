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
    FastList<PointData>? points = null,
    bool hitable = true,
    bool usePerPointEntity = false,
    uint textureIndex = 0,
    uint samplerIndex = 0,
    bool fixedSize = false
) : IIndexable
{
    /// <summary>
    /// The CPU-side point data array. Each element maps to a <c>PointData</c> GPU struct.
    /// Set this and call <see cref="MarkDirty"/> (or re-assign the component) to trigger
    /// an upload.
    /// </summary>
    public FastList<PointData>? Points = points;

    /// <summary>
    /// Number of valid points in the <see cref="Points"/> array.
    /// Must be &lt;= <c>Points.Length</c>.
    /// </summary>
    public readonly int PointCount => Points?.Count ?? 0;

    /// <summary>
    /// When <see langword="true"/>, each point's <c>EntityId</c> / <c>EntityVer</c> fields
    /// in <see cref="PointData"/> are used for picking. When <see langword="false"/> (default),
    /// the owning entity's ID and version are stamped on all points, and the point index is
    /// encoded as the instance index for per-point identification.
    /// </summary>
    public bool UsePerPointEntity = usePerPointEntity;

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
    /// Gets or sets the index assigned by the point data provider.
    /// Used internally to track position in the collected buffer.
    /// </summary>
    public int Index { internal set; get; } = -1;

    /// <summary>
    /// The offset (in number of points) into the combined GPU point buffer where this
    /// entity's points begin. Set by the data provider during collection.
    /// </summary>
    public uint BufferOffset { internal set; get; } = 0;

    /// <summary>
    /// Gets a value indicating whether this component has valid point data.
    /// </summary>
    public readonly bool Valid => Points is not null && PointCount > 0;

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
        return $"PointCloud: Count={PointCount}; Hitable={Hitable}; UsePerPointEntity={UsePerPointEntity}; FixedSize={FixedSize}";
    }
}
