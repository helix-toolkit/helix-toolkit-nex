using HelixToolkit.Nex.Shaders.Frag;
using static HelixToolkit.Nex.Pool<
    HelixToolkit.Nex.Material.MaterialPropertyResource,
    HelixToolkit.Nex.Shaders.PBRProperties
>;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Defines the contract for a material pool that manages material resources with automatic ID assignment and lifecycle management.
/// </summary>
public interface IPBRMaterialPropertyManager : IDisposable
{
    /// <summary>
    /// Gets the current number of active material properties.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Creates a new material property resource with the specified material name.
    /// </summary>
    /// <param name="materialName">Name of the material</param>
    /// <returns>A handle to the material resource.</returns>
    PBRMaterialProperties Create(string materialName);

    /// <summary>
    /// Creates a new material with the specified name and physically based rendering (PBR) properties.
    /// </summary>
    /// <param name="materialName">The name of the material to create. Cannot be null or empty.</param>
    /// <param name="properties">The PBR properties to apply to the material. Cannot be null.</param>
    /// <returns>A <see cref="PBRMaterialProperties"/> object representing the created material.</returns>
    PBRMaterialProperties Create(string materialName, PBRProperties properties) =>
        Create(materialName, ref properties);

    /// <summary>
    /// Creates a new material with the specified name and physical-based rendering (PBR) properties.
    /// </summary>
    /// <remarks>The <paramref name="properties"/> parameter is used to define the physical-based rendering
    /// attributes of the material, such as metallic and roughness values.  Ensure that the properties are properly
    /// initialized before calling this method.</remarks>
    /// <param name="materialName">The name of the material to create. Cannot be null or empty.</param>
    /// <param name="properties">The PBR properties to apply to the material. This parameter is passed by reference and may be modified during
    /// the creation process.</param>
    /// <returns>A <see cref="PBRMaterialProperties"/> object representing the created material.</returns>
    PBRMaterialProperties Create(string materialName, ref PBRProperties properties);

    /// <summary>
    /// Creates a new <see cref="PBRMaterialProperties"/> instance configured for the specified physically based rendering
    /// (PBR) shading mode.
    /// </summary>
    /// <param name="shadingMode">The PBR shading mode to use when initializing the material properties.</param>
    /// <returns>A <see cref="PBRMaterialProperties"/> object representing the material configuration for the given shading mode.</returns>
    PBRMaterialProperties Create(PBRShadingMode shadingMode) => Create(shadingMode.ToString());

    /// <summary>
    /// Creates a new instance of <see cref="PBRMaterialProperties"/> using the specified shading mode and properties.
    /// </summary>
    /// <param name="shadingMode">The shading mode to use for the material, represented as a <see cref="PBRShadingMode"/>.</param>
    /// <param name="properties">The physical-based rendering (PBR) properties to apply to the material.</param>
    /// <returns>A new <see cref="PBRMaterialProperties"/> instance configured with the specified shading mode and properties.</returns>
    PBRMaterialProperties Create(PBRShadingMode shadingMode, PBRProperties properties) =>
        Create(shadingMode.ToString(), ref properties);

    /// <summary>
    /// Creates a new instance of <see cref="PBRMaterialProperties"/> based on the specified shading mode and properties.
    /// </summary>
    /// <param name="shadingMode">The shading mode to use for the material. This determines how the material interacts with light.</param>
    /// <param name="properties">A reference to the <see cref="PBRProperties"/> structure that defines the physical-based rendering properties of
    /// the material.</param>
    /// <returns>A new <see cref="PBRMaterialProperties"/> instance configured with the specified shading mode and properties.</returns>
    PBRMaterialProperties Create(PBRShadingMode shadingMode, ref PBRProperties properties) =>
        Create(shadingMode.ToString(), ref properties);

    /// <summary>
    /// Creates a new <see cref="PBRMaterialProperties"/> instance configured for the specified physically based rendering
    /// </summary>
    /// <param name="materialTypeId">Material Id</param>
    /// <returns></returns>
    PBRMaterialProperties Create(MaterialTypeId materialTypeId);

    /// <summary>
    /// Creates a new material with the specified material type and physical-based rendering (PBR) properties.
    /// </summary>
    /// <param name="materialTypeId">The identifier of the material type to create.</param>
    /// <param name="properties">The physical-based rendering (PBR) properties to apply to the material.</param>
    /// <returns>A <see cref="PBRMaterialProperties"/> instance representing the created material.</returns>
    PBRMaterialProperties Create(MaterialTypeId materialTypeId, PBRProperties properties) =>
        Create(materialTypeId, ref properties);

    /// <summary>
    /// Creates a new material with the specified type and properties.
    /// </summary>
    /// <param name="materialTypeId">The identifier representing the type of material to create.</param>
    /// <param name="properties">The physical-based rendering (PBR) properties to apply to the material. This parameter is passed by reference
    /// and may be modified during the creation process.</param>
    /// <returns>A <see cref="PBRMaterialProperties"/> instance representing the created material.</returns>
    PBRMaterialProperties Create(MaterialTypeId materialTypeId, ref PBRProperties properties);

    /// <summary>
    /// Removes all material properties from the pool.
    /// </summary>
    void Clear();

    IReadOnlyList<PoolEntry> Objects { get; }

    /// <summary>
    /// Retrieves a reference to the <see cref="PBRMaterialProperties"/> instance at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the <see cref="PBRMaterialProperties"/> to retrieve. Must be within the valid range of the
    /// collection.</param>
    /// <returns>A reference to the <see cref="PBRMaterialProperties"/> at the specified index.</returns>
    ref PBRProperties At(int index);
}
