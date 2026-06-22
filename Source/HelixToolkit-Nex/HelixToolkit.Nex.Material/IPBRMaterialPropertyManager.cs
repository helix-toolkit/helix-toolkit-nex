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
    /// Removes all material properties from the pool.
    /// </summary>
    void Clear();

    /// <summary>
    /// Uploads the dynamic material properties to the GPU buffer. This method should be called after any changes to the material properties
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer);

    /// <summary>
    /// Uploads the dynamic material properties to the GPU buffer, using the specified indices to determine which properties to upload.
    /// This method should be called after any changes to the material properties
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="indices"></param>
    /// <returns></returns>
    ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer, IEnumerable<uint> indices);

    /// <summary>
    /// Gets a read-only list of all active material properties in the pool. Each entry in the list contains information about the material resource,
    /// </summary>
    IReadOnlyList<PoolEntry> Objects { get; }
}
