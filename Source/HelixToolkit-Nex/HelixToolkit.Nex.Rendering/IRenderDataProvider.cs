using HelixToolkit.Nex.ECS;

namespace HelixToolkit.Nex.Rendering;

public interface IRenderData : IInitializable
{
    BufferHandle Buffer { get; }
    ulong GpuAddress { get; }
    uint Stride { get; }
    uint Count { get; }
    bool Update();
}

public interface IPBRPropertyData : IRenderData
{
    // Additional properties or methods specific to PBR material properties can be defined here.
}

public interface IStaticMeshIndexData : IRenderData
{
    // Additional properties or methods specific to static mesh index data can be defined here.
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
    /// <returns>A <see cref="DrawRange"/> representing the minimum and maximum valid values for the specified material type.</returns>
    DrawRange GetRangeStaticMesh(MaterialTypeId id);

    /// <summary>
    /// Gets the range of all static mesh draws.
    /// </summary>
    /// <returns>A <see cref="DrawRange"/>structure representing the range of static mesh draws inside the draw buffer.</returns>
    DrawRange RangeStaticMesh { get; }

    /// <summary>
    /// Gets the range of static mesh instancing supported for the specified material type in draw command buffer.
    /// </summary>
    /// <param name="id">The material type for which to retrieve the supported static mesh instancing range.</param>
    /// <returns>A <see cref="DrawRange"/> representing the range of static instancing mesh draws for the specified material type inside the draw buffer</returns>
    DrawRange GetRangeStaticMeshInstancing(MaterialTypeId id);

    /// <summary>
    /// Gets the range of static mesh instancing draws.
    /// </summary>
    /// <returns>A <see cref="DrawRange"/> structure representing the range of static mesh instancing draws inside the draw buffer.</returns>
    DrawRange RangeStaticMeshInstancing { get; }

    /// <summary>
    /// Returns the valid value range for the specified <see cref="MaterialType"/> when using a dynamic mesh in draw command buffer.
    /// </summary>
    /// <param name="id">The material type for which to retrieve the valid value range.</param>
    /// <returns>A <see cref="DrawRange"/> representing the minimum and maximum valid values for the given <paramref
    /// name="id"/> in the context of a dynamic mesh.</returns>
    DrawRange GetRangeDynamicMesh(MaterialTypeId id);

    /// <summary>
    /// Gets the current range of the dynamic mesh draws.
    /// </summary>
    /// <returns>A <see cref="DrawRange"/> structure representing the range of dynamic mesh draws inside the draw buffer.</returns>
    DrawRange RangeDynamicMesh { get; }

    /// <summary>
    /// Gets the valid range of dynamic mesh instancing supported for the specified material type in draw command buffer.
    /// </summary>
    /// <remarks>Use this method to determine the supported instancing limits before creating or configuring
    /// dynamic mesh instances for a particular material type.</remarks>
    /// <param name="id">The type of material for which to retrieve the supported dynamic mesh instancing range.</param>
    /// <returns>A <see cref="DrawRange"/> representing the minimum and maximum number of dynamic mesh instances allowed for the
    /// given material type.</returns>
    DrawRange GetRangeDynamicMeshInstancing(MaterialTypeId id);

    /// <summary>
    /// Gets the range of indices used for dynamic mesh instancing.
    /// </summary>
    /// <returns>A <see cref="DrawRange"/> structure representing the range of dynamic mesh instancing draws inside the draw buffer.</returns>
    DrawRange RangeDynamicMeshInstancing { get; }
}

/// <summary>
/// Provides access to collected point cloud data for GPU rendering.
/// <para>
/// The data provider collects all <c>PointCloudComponent</c> entities each frame,
/// packs their <c>PointData</c> into a contiguous GPU buffer, and tracks per-entity
/// dispatch information so the compute shader can stamp the correct entity ID.
/// </para>
/// </summary>
public interface IPointCloudData : IRenderData
{
    /// <summary>
    /// Gets the total number of points across all collected point cloud entities.
    /// </summary>
    uint TotalPointCount { get; }

    /// <summary>
    /// Gets the per-entity dispatch records describing each point cloud's offset,
    /// count, entity identity, and per-point entity flag.
    /// </summary>
    FastList<PointCloudDispatch> Dispatches { get; }
}

/// <summary>
/// Describes a single point cloud entity's contribution to the combined GPU point buffer.
/// Used by <see cref="IPointCloudData"/> to drive per-entity compute dispatches.
/// </summary>
/// <param name="BufferOffset">Offset (in number of points) into the combined GPU buffer.</param>
/// <param name="PointCount">Number of points for this entity.</param>
/// <param name="EntityId">Entity ID for GPU picking (0 if not hitable).</param>
/// <param name="EntityVer">Entity version for GPU picking.</param>
/// <param name="TextureIndex">Texture Index</param>
/// <param name="SamplerIndex">Sampler Index</param>
public readonly record struct PointCloudDispatch(
    uint BufferOffset,
    uint PointCount,
    uint EntityId,
    uint EntityVer,
    uint TextureIndex,
    uint SamplerIndex,
    uint FixedSize
);

public interface IRenderDataProvider
{
    World World { get; }

    /// <summary>
    /// Gets the shared resource manager.
    /// </summary>
    IResourceManager ResourceManager { get; }

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
    IStaticMeshIndexData StaticMeshIndexData { get; }

    /// <summary>
    /// Gets the buffer containing all physically based rendering (PBR) material properties for use in rendering operations.
    /// </summary>
    IPBRPropertyData PBRPropertiesBuffer { get; }

    /// <summary>
    /// Gets the point cloud data collected from all <c>PointCloudComponent</c> entities.
    /// Returns <see langword="null"/> if no point cloud data provider is registered.
    /// </summary>
    IPointCloudData? PointCloudData { get; }

    /// <summary>
    /// Retrieves a PBRMaterial based on the specified material type identifier.
    /// </summary>
    /// <param name="materialType">The identifier of the material type to retrieve.</param>
    /// <returns>A <see cref="PBRMaterial"/> object corresponding to the specified material type,  or <see langword="null"/> if
    /// the material type is not found.</returns>
    PBRMaterial? GetMaterial(MaterialTypeId materialType);

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
