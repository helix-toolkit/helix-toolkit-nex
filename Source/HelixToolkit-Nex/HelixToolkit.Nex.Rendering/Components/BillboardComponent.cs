namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// ECS component that describes one or more billboards attached to an entity.
/// <para>
/// Each entity with a <see cref="BillboardComponent"/> contributes its billboards to the
/// GPU billboard buffer managed by the billboard data provider. The component stores CPU-side
/// billboard data that is collected and uploaded each frame.
/// </para>
/// </summary>
public struct BillboardComponent
{
    /// <summary>
    /// Gets or sets the billboard geometry containing per-billboard instance data
    /// (positions, sizes, UV rects, colors).
    /// </summary>
    public BillboardGeometry? BillboardGeometry { get; set; }

    /// <summary>
    /// Gets or sets the uniform tint color for all billboards when per-billboard colors
    /// are not provided in the BillboardGeometry. Defaults to white.
    /// </summary>
    public Color4 Color { get; set; } = new Color4(1f, 1f, 1f, 1f);

    /// <summary>
    /// When <see langword="true"/>, billboard sizes are interpreted as
    /// fixed screen-space pixel dimensions (no perspective scaling). When <see langword="false"/>
    /// (default), they are interpreted as world-space units that are projected to screen space.
    /// </summary>
    public bool FixedSize { get; set; }

    /// <summary>
    /// Gets or sets the bindless texture index for the billboard. 0 means no texture.
    /// </summary>
    public uint TextureIndex { get; set; }

    /// <summary>
    /// Gets or sets the bindless sampler index for the billboard.
    /// </summary>
    public uint SamplerIndex { get; set; }

    /// <summary>
    /// When <see langword="true"/>, enables axis-constrained mode where the billboard rotates
    /// around the <see cref="ConstraintAxis"/> to face the camera. When <see langword="false"/>
    /// (default), the billboard uses screen-aligned mode (fully camera-facing).
    /// </summary>
    public bool AxisConstrained { get; set; }

    /// <summary>
    /// Gets or sets the world-space axis for axis-constrained mode.
    /// Defaults to (0, 1, 0) (Y-up).
    /// </summary>
    public Vector3 ConstraintAxis { get; set; } = new Vector3(0, 1, 0);

    /// <summary>
    /// Whether the billboard can be selected via GPU picking.
    /// </summary>
    public bool Hitable { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the billboard material.
    /// If specified, this name is used to look up the material in the <see cref="BillboardMaterialRegistry"/>.
    /// If not found, it falls back to use <see cref="BillboardMaterialId"/>.
    /// If both are not specified, it defaults to the default billboard material.
    /// </summary>
    public string? BillboardMaterialName { get; set; }

    /// <summary>
    /// The billboard material type ID that determines which fragment shader pipeline is used
    /// for rendering this billboard. Defaults to 0, which corresponds to the default billboard material.
    /// Register custom materials via <see cref="BillboardMaterialRegistry"/>.
    /// </summary>
    public MaterialTypeId BillboardMaterialId { get; internal set; }

    /// <summary>
    /// Gets the number of billboards defined by the <see cref="BillboardGeometry"/>, or 0 when BillboardGeometry is null.
    /// </summary>
    public readonly int BillboardCount => BillboardGeometry?.Count ?? 0;

    /// <summary>
    /// Gets a value indicating whether this component has valid billboard data.
    /// </summary>
    public readonly bool Valid => BillboardCount > 0;

    public BillboardComponent() { }

    public BillboardComponent(
        BillboardGeometry billboardGeometry,
        Color4 color,
        bool hitable = true,
        uint textureIndex = 0,
        uint samplerIndex = 0,
        bool fixedSize = false,
        bool axisConstrained = false,
        Vector3? constraintAxis = null,
        string? billboardMaterialName = default
    )
    {
        BillboardGeometry = billboardGeometry;
        Color = color;
        Hitable = hitable;
        TextureIndex = textureIndex;
        SamplerIndex = samplerIndex;
        FixedSize = fixedSize;
        AxisConstrained = axisConstrained;
        ConstraintAxis = constraintAxis ?? new Vector3(0, 1, 0);
        BillboardMaterialName = billboardMaterialName;
    }

    public override readonly string ToString()
    {
        return $"Billboard: Count={BillboardCount}; Hitable={Hitable}; FixedSize={FixedSize}; AxisConstrained={AxisConstrained}; MaterialId={BillboardMaterialId.Id}";
    }
}
