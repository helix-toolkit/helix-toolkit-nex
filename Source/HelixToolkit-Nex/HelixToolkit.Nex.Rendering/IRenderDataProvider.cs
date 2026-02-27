namespace HelixToolkit.Nex.Rendering;

public interface IRenderData : IInitializable
{
    BufferHandle Buffer { get; }
    ulong GpuAddress { get; }
    uint Stride { get; }
    uint Count { get; }
    bool Update();
}

public interface IMeshDrawData : IRenderData
{
    /// <summary>
    /// Gets the collection of mesh draw commands to be executed during rendering.
    /// The draw commands is ordered as following:
    /// <para>
    /// [Static Meshes without Instancing, Static Meshes with Instancing, Dynamic Meshes without Instancing, Dynamic Meshes with Instancing]
    /// </para>
    /// <para>
    /// All mesh draw commands are sorted based on their material types.
    /// Use the provided methods to query the valid value ranges for each category of mesh draw commands based on material types.
    /// </para>
    /// </summary>
    IReadOnlyList<MeshDraw> DrawCommands { get; }

    /// <summary>
    /// Gets the collection of material types available in the current context.
    /// </summary>
    IEnumerable<MaterialTypeId> MaterialTypes { get; }

    /// <summary>
    /// Gets a value indicating whether mesh draw data contains any dynamic mesh.
    /// </summary>
    bool HasDynamicMesh { get; }

    /// <summary>
    /// Gets a value indicating whether mesh draw data contains any dynamic instancing mesh.
    /// </summary>
    bool HasDynamicInstancingMesh { get; }

    /// <summary>
    /// Gets a value indicating whether mesh draw data contains any static mesh.
    /// </summary>
    bool HasStaticMesh { get; }

    /// <summary>
    /// Gets a value indicating whether mesh draw data contains any static instancing mesh.
    /// </summary>
    bool HasStaticInstancingMesh { get; }

    /// <summary>
    /// Gets the valid value range for the specified static mesh material type in draw command buffer.
    /// </summary>
    /// <param name="id">The material type for which to retrieve the valid value range.</param>
    /// <returns>A <see cref="Range"/> representing the minimum and maximum valid values for the specified material type.</returns>
    Range GetRangeStaticMesh(MaterialTypeId id);

    /// <summary>
    /// Gets the range of all static mesh draws.
    /// </summary>
    /// <returns>A <see cref="Range"/>structure representing the range of static mesh draws inside the draw buffer.</returns>
    Range RangeStaticMesh { get; }

    /// <summary>
    /// Gets the range of static mesh instancing supported for the specified material type in draw command buffer.
    /// </summary>
    /// <param name="id">The material type for which to retrieve the supported static mesh instancing range.</param>
    /// <returns>A <see cref="Range"/> representing the range of static instancing mesh draws for the specified material type inside the draw buffer</returns>
    Range GetRangeStaticMeshInstancing(MaterialTypeId id);

    /// <summary>
    /// Gets the range of static mesh instancing draws.
    /// </summary>
    /// <returns>A <see cref="Range"/> structure representing the range of static mesh instancing draws inside the draw buffer.</returns>
    Range RangeStaticMeshInstancing { get; }

    /// <summary>
    /// Returns the valid value range for the specified <see cref="MaterialType"/> when using a dynamic mesh in draw command buffer.
    /// </summary>
    /// <param name="id">The material type for which to retrieve the valid value range.</param>
    /// <returns>A <see cref="Range"/> representing the minimum and maximum valid values for the given <paramref
    /// name="id"/> in the context of a dynamic mesh.</returns>
    Range GetRangeDynamicMesh(MaterialTypeId id);

    /// <summary>
    /// Gets the current range of the dynamic mesh draws.
    /// </summary>
    /// <returns>A <see cref="Range"/> structure representing the range of dynamic mesh draws inside the draw buffer.</returns>
    Range RangeDynamicMesh { get; }

    /// <summary>
    /// Gets the valid range of dynamic mesh instancing supported for the specified material type in draw command buffer.
    /// </summary>
    /// <remarks>Use this method to determine the supported instancing limits before creating or configuring
    /// dynamic mesh instances for a particular material type.</remarks>
    /// <param name="id">The type of material for which to retrieve the supported dynamic mesh instancing range.</param>
    /// <returns>A <see cref="Range"/> representing the minimum and maximum number of dynamic mesh instances allowed for the
    /// given material type.</returns>
    Range GetRangeDynamicMeshInstancing(MaterialTypeId id);

    /// <summary>
    /// Gets the range of indices used for dynamic mesh instancing.
    /// </summary>
    /// <returns>A <see cref="Range"/> structure representing the range of dynamic mesh instancing draws inside the draw buffer.</returns>
    Range RangeDynamicMeshInstancing { get; }
}

public interface IRenderDataProvider
{
    /// <summary>
    /// Gets the collection of range light (point light, spot light) sources used for rendering the scene.
    /// </summary>
    IRenderData Lights { get; }

    /// <summary>
    /// Gets the collection of directional light data used for rendering.
    /// </summary>
    IRenderData DirectionalLights { get; }

    /// <summary>
    /// Gets the collection of mesh information used for rendering.
    /// </summary>
    IRenderData MeshInfos { get; }

    /// <summary>
    /// Gets the mesh draw data used for rendering opaque geometry.
    /// </summary>
    IMeshDrawData MeshDrawsOpaque { get; }

    /// <summary>
    /// Gets the mesh draw data used for rendering transparent objects.
    /// </summary>
    IMeshDrawData MeshDrawsTransparent { get; }

    /// <summary>
    /// Gets the shared index buffer used for rendering static mesh geometry.
    /// </summary>
    IRenderData StaticMeshIndexData { get; }

    /// <summary>
    /// Gets the buffer containing all physically based rendering (PBR) material properties for use in rendering operations.
    /// </summary>
    IRenderData PBRPropertiesBuffer { get; }

    /// <summary>
    /// Gets the render pipeline handle associated with the specified material type.
    /// </summary>
    /// <param name="materialType">The type of material for which to retrieve the render pipeline handle.</param>
    /// <returns>A <see cref="RenderPipelineHandle"/> representing the render pipeline configured for the given <paramref
    /// name="materialType"/>.</returns>
    RenderPipelineHandle GetMaterialPipeline(MaterialTypeId materialType);

    /// <summary>
    /// Initializes the current entity using the specified context.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    bool Initialize();

    /// <summary>
    /// Updates the current entity using the specified context.
    /// </summary>
    /// <param name="context">The context to use for the update operation. Cannot be <c>null</c>.</param>
    /// <returns><see langword="true"/> if the update was successful; otherwise, <see langword="false"/>.</returns>
    bool Update();

    /// <summary>
    /// Retrieves the <see cref="Geometry"/> instance associated with the specified geometry identifier.
    /// </summary>
    /// <param name="geometryId">The unique identifier of the geometry to retrieve.</param>
    /// <returns>The <see cref="Geometry"/> corresponding to <paramref name="geometryId"/>, or <see langword="null"/> if no
    /// geometry with the specified identifier exists.</returns>
    Geometry? GetGeometry(uint geometryId);
}
