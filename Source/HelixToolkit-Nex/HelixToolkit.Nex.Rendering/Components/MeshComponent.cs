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
public struct MeshComponent : IIndexable
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
    public readonly PBRMaterialProperties? MaterialProperties;

    /// <summary>
    /// Gets the instancing mode for the associated object, if specified.
    /// </summary>
    public readonly Instancing? Instancing;

    /// <summary>
    /// Gets a value indicating whether the object can be excluded from rendering based on culling logic.
    /// </summary>
    public bool Cullable { get; }

    /// <summary>
    /// Gets a value indicating whether the object can be interacted with or selected.
    /// </summary>
    public bool Hitable { get; }

    /// <summary>
    /// Gets or sets the index of the current draw operation.
    /// </summary>
    public int Index { internal set; get; } = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshComponent"/> struct.
    /// </summary>
    /// <param name="geometry">Geometry resource.</param>
    /// <param name="materialProperties">Material property resource.</param>
    public MeshComponent(
        Geometry? geometry = null,
        PBRMaterialProperties? materialProperties = null,
        Instancing? instancing = null,
        bool cullable = true,
        bool hitable = true
    )
    {
        Geometry = geometry;
        MaterialProperties = materialProperties;
        Instancing = instancing;
        Cullable = cullable;
        Hitable = hitable;
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
            + $"IsInstancing: {Instancing is not null}; Cullable: {Cullable};";
    }

    public readonly MeshComponent SetGeometry(in Geometry geometry)
    {
        return new MeshComponent(geometry, MaterialProperties, Instancing, Cullable, Hitable)
        {
            Index = Index,
        };
    }

    public readonly MeshComponent SetMaterial(in PBRMaterialProperties properties)
    {
        return new MeshComponent(Geometry, properties, Instancing, Cullable, Hitable)
        {
            Index = Index,
        };
    }

    public readonly MeshComponent SetInstancing(in Instancing instancing)
    {
        return new MeshComponent(Geometry, MaterialProperties, instancing, Cullable, Hitable)
        {
            Index = Index,
        };
    }

    public readonly MeshComponent SetCullable(bool cullable)
    {
        return new MeshComponent(Geometry, MaterialProperties, Instancing, cullable, Hitable)
        {
            Index = Index,
        };
    }

    public readonly MeshComponent SetHitable(bool hitable)
    {
        return new MeshComponent(Geometry, MaterialProperties, Instancing, Cullable, hitable)
        {
            Index = Index,
        };
    }

    public readonly MeshDrawType GetDrawType()
    {
        return new(Geometry is not null && Geometry.IsDynamic, Instancing is not null);
    }
}

public readonly struct MeshDrawType
{
    public readonly uint Type;

    public MeshDrawType(bool isDynamic, bool isInstancing)
    {
        if (isDynamic)
        {
            Type |= 1;
        }
        if (isInstancing)
        {
            Type |= 1 << 2;
        }
    }

    public static implicit operator uint(MeshDrawType other)
    {
        return other.Type;
    }
}

public readonly struct TransparentComponent { }
