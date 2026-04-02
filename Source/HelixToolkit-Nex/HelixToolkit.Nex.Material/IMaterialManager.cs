namespace HelixToolkit.Nex.Material;

public readonly struct MaterialPropertyCreator(MaterialTypeId id, IMaterialPropertyManager pool)
{
    public readonly MaterialTypeId MaterialTypeId = id;
    private readonly IMaterialPropertyManager _pool = pool;

    public MaterialProperties Create()
    {
        return _pool.Create(MaterialTypeId);
    }
}

public interface IMaterialManager : IDisposable
{
    int Count { get; }

    /// <summary>
    /// Gets the render pipeline handle associated with the specified material type.
    /// </summary>
    /// <param name="materialType">The type of material for which to retrieve the render pipeline handle.</param>
    /// <param name="type">The pass type for which to retrieve the render pipeline handle (e.g., opaque, transparent).</param>
    /// <returns>A <see cref="RenderPipelineHandle"/> representing the render pipeline configured for the given <paramref
    /// name="materialType"/>.</returns>
    RenderPipelineHandle GetMaterialPipeline(MaterialTypeId materialType, MaterialPassType type);

    /// <summary>
    /// Retrieves the PBR material associated with the specified material type identifier.
    /// </summary>
    /// <param name="materialType">The identifier of the material type for which to retrieve the PBR material.</param>
    /// <returns>The <see cref="PBRMaterial"/> associated with the specified material type identifier,  or <see langword="null"/>
    /// if no material is found for the given identifier.</returns>
    PBRMaterial? GetMaterial(MaterialTypeId materialType);

    /// <summary>
    /// Creates a new material using the specified name and builder function.
    /// </summary>
    /// <param name="name">The name of the material to be created. Cannot be null or empty.</param>
    /// <param name="builderFunc">A function that takes material name and returns a <param name="Material"/> instance.
    /// This function is responsible for constructing the material based on the provided name and pipeline description.</param>
    /// <returns>A <see cref="MaterialPropertyCreator"/> that can be used to further configure the created material.</returns>
    MaterialPropertyCreator CreateMaterial(string name, Func<string, PBRMaterial> builderFunc);

    /// <summary>
    /// Creates physically-based rendering (PBR) materials from the registry.
    /// </summary>
    /// <remarks>This method initializes and registers PBR materials based on <see cref="MaterialTypeRegistry"/>. It should
    /// be called during the setup phase to ensure all registered materials are available for rendering.</remarks>
    /// <returns>Number of materials has been created.</returns>
    int CreatePBRMaterialsFromRegistry();

    /// <summary>
    /// Destroys a material resource and frees its GPU pipeline.
    /// </summary>
    /// <param name="handle">Reference to the handle to destroy.</param>
    void DestroyMaterial(MaterialTypeId id);

    /// <summary>
    /// Clear and destroy all materials and their associated GPU pipelines from the manager.
    /// </summary>
    void Clear();
}
