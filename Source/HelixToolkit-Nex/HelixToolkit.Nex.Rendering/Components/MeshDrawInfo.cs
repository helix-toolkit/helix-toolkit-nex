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
public struct MeshDrawInfo
{
    /// <summary>
    /// Gets or sets the geometry resource associated with this mesh. The geometry contains vertex buffers, index buffers, and draw parameters.
    /// </summary>
    public Geometry? Geometry { set; get; }

    /// <summary>
    /// Gets or sets the material properties resource associated with this mesh. The material properties determine which shader pipeline is used and provide material parameters (e.g., albedo color, metallic/roughness values) for rendering.
    /// </summary>
    public PBRMaterialProperties? MaterialProperties { set; get; }

    /// <summary>
    /// Gets or sets the instancing data for this mesh. If not null, this mesh will be rendered using GPU instancing with the provided per-instance data.
    /// </summary>
    public Instancing? Instancing { set; get; }

    /// <summary>
    /// Gets or sets the bone skinning data for this mesh. If not null, the mesh vertices are skinned
    /// in a compute pass ahead of rendering. The geometry must carry joint influences
    /// (<see cref="Geometry.JointInfluences"/>).
    /// </summary>
    public Skinning? Skinning { set; get; }

    /// <summary>
    /// Gets or sets the morph target weights binding of this mesh. If not null, the morph targets of
    /// the geometry (<see cref="Geometry.MorphTargets"/>) are applied in a compute pass ahead of
    /// rendering, before skinning.
    /// </summary>
    public Morphing? Morphing { set; get; }

    /// <summary>
    /// Gets or sets a value indicating whether this mesh is cullable.
    /// Skinned and morphed meshes are never culled; see <see cref="IsCullable"/>.
    /// </summary>
    public bool Cullable { set; get; } = true;

    /// <summary>
    /// Gets a value indicating whether this mesh is frustum culled. Skinned and morphed meshes are
    /// not culled, because their bind-pose bounds do not cover the deformed pose.
    /// </summary>
    public readonly bool IsCullable => Cullable && !IsDeformed;

    /// <summary>
    /// Gets a value indicating whether this mesh uses bone skinning.
    /// </summary>
    public readonly bool IsSkinned => Skinning is not null;

    /// <summary>
    /// Gets a value indicating whether this mesh uses morph targets.
    /// </summary>
    public readonly bool IsMorphed => Morphing is not null;

    /// <summary>
    /// Gets a value indicating whether this mesh is deformed by skinning or morph targets.
    /// </summary>
    public readonly bool IsDeformed => IsSkinned || IsMorphed;

    /// <summary>
    /// Gets the buffer holding the deformed vertex positions written by the deformation pass, or a
    /// null buffer when the mesh is not deformed (yet). The skinning owns the output of a skinned mesh,
    /// otherwise the morphing does.
    /// </summary>
    public readonly BufferResource DeformedVertexBuffer =>
        Skinning?.SkinnedVertexBuffer is { Valid: true } skinned
            ? skinned
            : Morphing?.MorphedVertexBuffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the buffer holding the deformed vertex properties, paired with <see cref="DeformedVertexBuffer"/>.
    /// </summary>
    public readonly BufferResource DeformedVertexPropsBuffer =>
        Skinning?.SkinnedVertexBuffer is { Valid: true }
            ? Skinning.SkinnedVertexPropsBuffer
            : Morphing?.MorphedVertexPropsBuffer ?? BufferResource.Null;

    /// <summary>
    /// Gets or sets a value indicating whether this mesh is hitable.
    /// </summary>
    public bool Hitable { set; get; } = true;

    /// <summary>
    /// Gets the draw stream variants for this mesh based on its properties.
    /// </summary>
    public readonly DrawStreamVariants Variants
    {
        get
        {
            DrawStreamVariants variant = 0;
            if (Instancing is not null)
            {
                variant |= DrawStreamVariants.Instancing;
                if (Instancing.IsDynamic)
                {
                    variant |= DrawStreamVariants.Dynamic;
                }
            }

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
    /// Initializes a new instance of the <see cref="MeshDrawInfo"/> struct.
    /// </summary>
    /// <param name="geometry">Geometry resource.</param>
    /// <param name="materialProperties">Material property resource.</param>
    public MeshDrawInfo(
        Geometry? geometry = null,
        PBRMaterialProperties? materialProperties = null,
        Instancing? instancing = null,
        bool cullable = true,
        bool hitable = true,
        Skinning? skinning = null,
        Morphing? morphing = null
    )
    {
        Geometry = geometry;
        MaterialProperties = materialProperties;
        Instancing = instancing;
        Cullable = cullable;
        Hitable = hitable;
        Skinning = skinning;
        Morphing = morphing;
    }

    /// <summary>
    /// Creates an empty MeshComponent with null handles.
    /// </summary>
    public static readonly MeshDrawInfo Empty = new(null, null);

    /// <summary>
    /// Gets a value indicating whether this MeshComponent has valid handles.
    /// </summary>
    public readonly bool Valid =>
        Geometry is not null
        && MaterialProperties is not null
        && Geometry.Valid
        && MaterialProperties.Valid;

    public override readonly string ToString()
    {
        return $"GeometryId: {Geometry?.Id}; MaterialType: {MaterialProperties?.MaterialTypeId}; MaterialIndex: {MaterialProperties?.Index}; "
            + $"Category: {Variants}; Cullable: {IsCullable}; Skinned: {IsSkinned}; Morphed: {IsMorphed};";
    }
}

public readonly struct TransparentComponent { }

public readonly struct AlphaMaskComponent { }
