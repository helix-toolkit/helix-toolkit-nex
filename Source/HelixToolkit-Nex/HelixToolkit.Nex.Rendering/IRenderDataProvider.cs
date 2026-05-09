using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DataEntries;

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
    /// Gets the collection of material types available in the current context.
    /// </summary>
    IEnumerable<MaterialTypeId> MaterialTypes { get; }

    /// <summary>
    /// Check if any draw data exists for the specified mesh component variant.
    /// </summary>
    /// <param name="variant"></param>
    /// <returns></returns>
    bool HasAny(MeshVariant variant);
    /// <summary>
    /// Retrieves the collection of material type identifiers associated with the specified mesh variant.
    /// </summary>
    /// <param name="variant">The mesh variant for which to obtain the corresponding material type identifiers.</param>
    /// <returns>An enumerable collection of material type identifiers for the given mesh variant. The collection is empty if no
    /// material types are associated with the variant.</returns>
    IEnumerable<MaterialTypeId> GetMaterialTypes(MeshVariant variant);
    /// <summary>
    /// Retrieves the buffer handle and draw range associated with the specified material type and mesh component
    /// variant.
    /// </summary>
    /// <param name="id">The identifier of the material type for which to retrieve the buffer and range.</param>
    /// <param name="variant">The set of mesh component variant that influence the selection of the buffer and draw range.</param>
    /// <returns>A tuple containing the draw range and buffer handle corresponding to the specified material type and mesh
    /// component variant.</returns>
    (BufferHandle, DrawRange) GetBufferByMaterial(MaterialTypeId id, MeshVariant variant);

    /// <summary>
    /// Retrieves the buffer and draw range associated with the specified mesh component variant.
    /// </summary>
    /// <param name="variant">The set of mesh component variant for which to obtain the buffer and draw range.</param>
    /// <returns>A tuple containing the draw range and buffer handle corresponding to the requested variant.</returns>
    (BufferHandle, DrawRange) GetBuffer(MeshVariant variant);

    /// <summary>
    /// Retrieves the <see cref="MeshDraw"/> information associated with the specified mesh component variant, material type, and draw index.
    /// </summary>
    /// <param name="variant">The set of mesh component variant for which to obtain the mesh draw information.</param>
    /// <param name="id">The identifier of the material type for which to retrieve the mesh draw information.</param>
    /// <param name="drawIndex">The index of the draw call for which to retrieve the mesh draw information. The index must pass in the range returned by <see cref="GetRangeByMaterial(MeshVariant, MaterialTypeId)"/>.</param>
    /// <returns>The <see cref="MeshDraw"/> information corresponding to the specified mesh component variant, material type, and draw index.</returns>
    MeshDraw GetMeshDraw(MeshVariant variant, MaterialTypeId id, int drawIndex);

    /// <summary>
    /// Retrieves the mesh draw information and associated buffer handle for the specified entity.
    /// </summary>
    /// <param name="entity">The entity for which to obtain mesh draw data. Must reference a valid entity containing mesh information.</param>
    /// <returns>A tuple containing the buffer handle, the mesh draw data, and the draw index for the specified entity.</returns>
    (BufferHandle, MeshDraw, int DrawIndex) GetMeshDraw(Entity entity);

    /// <summary>
    /// Retrieves the draw range associated with the specified mesh component variant and material type.
    /// </summary>
    /// <param name="variant">The set of mesh component variant for which to obtain the draw range.</param>
    /// <param name="id">The identifier of the material type for which to retrieve the draw range.</param>
    /// <returns>The draw range corresponding to the specified mesh component variant and material type.</returns>
    DrawRange GetRangeByMaterial(MeshVariant variant, MaterialTypeId id);
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
    /// Gets the dictionary which contains per-entity dispatch records describing each point cloud's offset,
    /// count, entity identity, and per-point entity flag by their material id as key.
    /// </summary>
    IReadOnlyDictionary<MaterialTypeId, PointCloudDataEntry> Data { get; }

    /// <summary>
    /// Gets the total number of points in the collection.
    /// </summary>
    uint TotalPointCount { get; }
}

/// <summary>
/// Provides access to collected billboard data for GPU rendering.
/// <para>
/// The data provider collects all <c>BillboardComponent</c> entities each frame,
/// packs their data into a contiguous GPU buffer, and tracks per-entity
/// dispatch information so the compute shader can stamp the correct entity ID.
/// </para>
/// </summary>
public interface IBillboardData : IRenderData
{
    /// <summary>
    /// Gets the dictionary which contains per-material billboard data entries
    /// keyed by their material type ID.
    /// </summary>
    IReadOnlyDictionary<MaterialTypeId, BillboardDataEntry> Data { get; }

    /// <summary>
    /// Gets the total number of billboards in the collection.
    /// </summary>
    uint TotalBillboardCount { get; }
}

public interface IRenderDataProvider
{
    World World { get; }

    /// <summary>
    /// Gets the shared resource manager.
    /// </summary>
    IResourceManager ResourceManager { get; }

    /// <summary>
    /// Gets renderable node information for all renderable entities in current world. This includes data such as entity IDs, transforms, and enabled states,
    /// </summary>
    IRenderData NodeInfos { get; }

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
    /// Gets the billboard data collected from all <c>BillboardComponent</c> entities.
    /// Returns <see langword="null"/> if no billboard data provider is registered.
    /// </summary>
    IBillboardData? BillboardData { get; }

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
