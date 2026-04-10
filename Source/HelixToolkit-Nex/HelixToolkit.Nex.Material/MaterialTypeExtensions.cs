namespace HelixToolkit.Nex.Material;

/// <summary>
/// Extension methods for materials to easily work with material types and specialization constants.
/// </summary>
public static class MaterialTypeExtensions
{
    /// <summary>
    /// Sets the material type specialization constant on a pipeline descriptor.
    /// </summary>
    /// <param name="desc">The pipeline descriptor to modify.</param>
    /// <param name="materialTypeId">The material type ID.</param>
    public static void SetMaterialType(this RenderPipelineDesc desc, uint materialTypeId)
    {
        const uint MATERIAL_TYPE_CONSTANT_ID = 0;
        desc.WriteSpecInfo(MATERIAL_TYPE_CONSTANT_ID, materialTypeId);
    }

    /// <summary>
    /// Sets the material type specialization constant on a pipeline descriptor by name.
    /// </summary>
    /// <param name="desc">The pipeline descriptor to modify.</param>
    /// <param name="materialTypeName">The material type name.</param>
    /// <exception cref="ArgumentException">Thrown if the material type is not registered.</exception>
    public static void SetMaterialType(this RenderPipelineDesc desc, string materialTypeName)
    {
        var typeId = PBRMaterialTypeRegistry.GetTypeId(materialTypeName);
        if (typeId == null)
        {
            throw new ArgumentException(
                $"Material type '{materialTypeName}' is not registered.",
                nameof(materialTypeName)
            );
        }
        desc.SetMaterialType(typeId.Value);
    }

    /// <summary>
    /// Gets the material type ID from the pipeline descriptor if set.
    /// </summary>
    /// <param name="desc">The pipeline descriptor.</param>
    /// <returns>The material type ID, or null if not set.</returns>
    public static uint? GetMaterialType(this RenderPipelineDesc desc)
    {
        const uint MATERIAL_TYPE_CONSTANT_ID = 0;

        // Check if the specialization constant is set
        var entry = desc.SpecInfo.Entries.FirstOrDefault(e =>
            e.ConstantId == MATERIAL_TYPE_CONSTANT_ID
        );
        if (
            entry.ConstantId == MATERIAL_TYPE_CONSTANT_ID
            && entry.Size > 0
            && desc.SpecInfo.Data.Length >= entry.Offset + sizeof(uint)
        )
        {
            return BitConverter.ToUInt32(desc.SpecInfo.Data, (int)entry.Offset);
        }

        return null;
    }
}

/// <summary>
/// Information about a material type included in an uber shader.
/// </summary>
public sealed class MaterialTypeInfo
{
    /// <summary>
    /// The material type ID (specialization constant value).
    /// </summary>
    public required uint TypeId { get; init; }

    /// <summary>
    /// The material type name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description of the material type.
    /// </summary>
    public string? Description { get; init; }
}
