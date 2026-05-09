namespace HelixToolkit.Nex.Rendering.Components;

[Flags]
public enum MeshVariant : uint
{
    None = 0,
    Dynamic = 1,
    Instancing = 1 << 1,
    Hitable = 1 << 2,
    All = Dynamic | Instancing | Hitable,
}

public static class MeshVariantExtensions
{
    public static string ToLiteral(this MeshVariant variant)
    {
        var parts = new List<string>();
        if (variant.HasFlag(MeshVariant.Dynamic))
        {
            parts.Add("Dynamic");
        }
        if (variant.HasFlag(MeshVariant.Instancing))
        {
            parts.Add("Instancing");
        }
        if (variant.HasFlag(MeshVariant.Hitable))
        {
            parts.Add("Hitable");
        }
        return string.Join("|", parts);
    }

    public static string ToLiteralShort(this MeshVariant features)
    {
        var parts = new List<string>();
        if (features.HasFlag(MeshVariant.Dynamic))
        {
            parts.Add("Dyn");
        }
        if (features.HasFlag(MeshVariant.Instancing))
        {
            parts.Add("Inst");
        }
        if (features.HasFlag(MeshVariant.Hitable))
        {
            parts.Add("Hit");
        }
        return string.Join("|", parts);
    }
    public static bool IsDynamic(this MeshDraw meshDraw)
    {
        return ((MeshVariant)meshDraw.DrawType).HasFlag(MeshVariant.Dynamic);
    }

    public static bool IsInstancing(this MeshDraw meshDraw)
    {
        return ((MeshVariant)meshDraw.DrawType).HasFlag(MeshVariant.Instancing);
    }

    public static bool IsHitable(this MeshDraw meshDraw)
    {
        return ((MeshVariant)meshDraw.DrawType).HasFlag(MeshVariant.Hitable);
    }
}

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
    public MeshVariant Variant
    {
        get
        {
            MeshVariant variant = 0;
            if (Instancing is not null)
            {
                variant |= MeshVariant.Instancing;
            }
            if (Hitable)
            {
                variant |= MeshVariant.Hitable;
            }
            if (Geometry?.IsDynamic == true)
            {
                variant |= MeshVariant.Dynamic;
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
            + $"Features: {Variant.ToLiteral()}; Cullable: {Cullable};";
    }
}

public readonly struct TransparentComponent { }
