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
    /// <returns>A <see cref="RenderPipelineHandle"/> representing the render pipeline configured for the given <paramref
    /// name="materialType"/>.</returns>
    RenderPipelineHandle GetMaterialPipeline(MaterialTypeId materialType);

    /// <summary>
    /// Creates a material resource and optionally creates its GPU pipeline.
    /// </summary>
    /// <param name="name">Name of the material in material type registry.</param>
    /// <param name="pipelineDesc">Pipeline description for creating the material's GPU pipeline.</param>
    /// <returns>A material property creator to create material property object for newly created material.</returns>
    MaterialPropertyCreator CreateMaterial(string name, RenderPipelineDesc pipelineDesc);

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
