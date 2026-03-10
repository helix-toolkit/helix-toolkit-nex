using HelixToolkit.Nex.Shaders.Frag;
using static HelixToolkit.Nex.Pool<
    HelixToolkit.Nex.Material.MaterialPropertyResource,
    HelixToolkit.Nex.Shaders.PBRProperties
>;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Defines the contract for a material pool that manages material resources with automatic ID assignment and lifecycle management.
/// </summary>
public interface IMaterialPropertyManager : IDisposable
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
    MaterialProperties Create(string materialName);

    /// <summary>
    /// Creates a new <see cref="MaterialProperties"/> instance configured for the specified physically based rendering
    /// (PBR) shading mode.
    /// </summary>
    /// <param name="shadingMode">The PBR shading mode to use when initializing the material properties.</param>
    /// <returns>A <see cref="MaterialProperties"/> object representing the material configuration for the given shading mode.</returns>
    MaterialProperties Create(PBRShadingMode shadingMode)
    {
        return Create(shadingMode.ToString());
    }

    /// <summary>
    /// Creates a new <see cref="MaterialProperties"/> instance configured for the specified physically based rendering
    /// </summary>
    /// <param name="materialTypeId">Material Id</param>
    /// <returns></returns>
    MaterialProperties Create(MaterialTypeId materialTypeId);

    /// <summary>
    /// Removes all material properties from the pool.
    /// </summary>
    void Clear();

    IReadOnlyList<PoolEntry> Objects { get; }
}
