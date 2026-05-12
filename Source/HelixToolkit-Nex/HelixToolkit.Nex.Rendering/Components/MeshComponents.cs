using HelixToolkit.Nex.Rendering.DrawStreams;

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
public struct MeshComponent
{
    ///<inheritdoc/>
    public Geometry? Geometry { set; get; }

    ///<inheritdoc/>
    public PBRMaterialProperties? MaterialProperties { set; get; }

    ///<inheritdoc/>
    public Instancing? Instancing { set; get; }

    ///<inheritdoc/>
    public bool Cullable { set; get; }

    ///<inheritdoc/>
    public bool Hitable { set; get; }

    ///<inheritdoc/>
    public DrawStreamCategory Category
    {
        get
        {
            DrawStreamCategory variant = 0;
            if (Instancing is not null)
            {
                variant |= DrawStreamCategory.Instancing;
            }

            if (Hitable)
            {
                variant |= DrawStreamCategory.Hitable;
            }

            if (Geometry?.IsDynamic == true)
            {
                variant |= DrawStreamCategory.Dynamic;
            }

            return variant;
        }
    }

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
        if (geometry?.IsDynamic == true)
        {
            throw new InvalidOperationException("Geometry must be static for StaticMeshComponent");
        }
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
        && Geometry.Valid
        && MaterialProperties.Valid;

    public override string ToString()
    {
        return $"GeometryId: {Geometry?.Id}; MaterialType: {MaterialProperties?.MaterialTypeId}; MaterialIndex: {MaterialProperties?.Index}; "
            + $"Category: {Category}; Cullable: {Cullable};";
    }
}

public readonly struct TransparentComponent { }
