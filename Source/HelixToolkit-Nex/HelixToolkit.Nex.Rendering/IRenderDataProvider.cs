using HelixToolkit.Nex.ECS;
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
    /// Gets the mesh draw stream registry that manages all draw streams for rendering mesh geometry.
    /// </summary>
    IDrawStreamRegistry<MeshDraw> MeshDrawStreams { get; }

    /// <summary>
    /// Gets the line draw stream registry that manages all draw streams for rendering lines.
    /// </summary>
    IDrawStreamRegistry<LineDraw> LineDrawStreams { get; }

    /// <summary>
    /// Gets the point draw stream registry that manages all draw streams for rendering points.
    /// </summary>
    IDrawStreamRegistry<PointDraw> PointDrawStreams { get; }

    /// <summary>
    /// Gets the shared index buffer used for rendering static mesh geometry.
    /// </summary>
    IStaticMeshIndexData StaticMeshIndexData { get; }

    /// <summary>
    /// Gets the buffer containing all physically based rendering (PBR) material properties for use in rendering operations.
    /// </summary>
    IPBRPropertyData PBRPropertiesBuffer { get; }

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
