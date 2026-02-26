namespace HelixToolkit.Nex.Rendering.Components;

/// <summary>
/// Represents a mesh render component that associates a geometry with a material.
/// </summary>
/// <remarks>
/// This struct stores handles (IDs) to geometry and material resources rather than the actual data.
/// This allows:
/// <list type="bullet">
/// <item>Multiple nodes to share the same geometry/material (memory efficiency)</item>
/// <item>Easy swapping of geometry/material at runtime</item>
/// <item>Data-oriented design with better cache performance</item>
/// <item>Efficient serialization (just store IDs)</item>
/// </list>
/// The actual geometry and material data are stored in resource pools managed by the engine.
/// </remarks>
public readonly struct MeshComponent
{
    /// <summary>
    /// Handle to the geometry resource in the geometry pool.
    /// </summary>
    public readonly Geometry? Geometry;

    /// <summary>
    /// Represents the material properties associated with this instance.
    /// </summary>
    /// <remarks>This field is read-only and provides access to the material's physical or structural
    /// characteristics.</remarks>
    public readonly MaterialProperties? MaterialProperties;

    /// <summary>
    /// Gets the instancing mode for the associated object, if specified.
    /// </summary>
    public readonly Instancing? Instancing;

    /// <summary>
    /// Gets a value indicating whether the object is transparent.
    /// </summary>
    public bool IsTransparent { get; }

    /// <summary>
    /// Gets a value indicating whether the object can be excluded from rendering based on culling logic.
    /// </summary>
    public bool Cullable { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshComponent"/> struct.
    /// </summary>
    /// <param name="geometry">Geometry resource.</param>
    /// <param name="materialProperties">Material property resource.</param>
    public MeshComponent(
        Geometry? geometry = null,
        MaterialProperties? materialProperties = null,
        Instancing? instancing = null,
        bool isTransparent = false,
        bool cullable = true
    )
    {
        Geometry = geometry;
        MaterialProperties = materialProperties;
        Instancing = instancing;
        IsTransparent = isTransparent;
        Cullable = cullable;
    }

    /// <summary>
    /// Creates an empty MeshComponent with null handles.
    /// </summary>
    public static readonly MeshComponent Empty = new(null, null);

    /// <summary>
    /// Gets a value indicating whether this MeshComponent has valid handles.
    /// </summary>
    public bool Valid =>
        Geometry is not null
        && MaterialProperties is not null
        && Geometry.Attached
        && MaterialProperties.Valid;

    public override string ToString()
    {
        return $"GeometryId: {Geometry?.Id}; MaterialType: {MaterialProperties?.MaterialTypeId}; MaterialIndex: {MaterialProperties?.Index}; "
            + $"IsInstancing: {Instancing is not null}; IsTransparent: {IsTransparent}";
    }

    public readonly MeshComponent SetGeometry(in Geometry geometry)
    {
        return new MeshComponent(geometry, MaterialProperties);
    }

    public readonly MeshComponent SetMaterial(in MaterialProperties properties)
    {
        return new MeshComponent(Geometry, properties);
    }

    public readonly MeshComponent SetInstancing(in Instancing instancing)
    {
        return new MeshComponent(Geometry, MaterialProperties, instancing, IsTransparent);
    }

    public readonly MeshComponent SetTransparent(bool isTransparent)
    {
        return new MeshComponent(Geometry, MaterialProperties, Instancing, isTransparent);
    }

    public readonly MeshComponent SetCullable(bool cullable)
    {
        return new MeshComponent(Geometry, MaterialProperties, Instancing, IsTransparent, cullable);
    }
}
