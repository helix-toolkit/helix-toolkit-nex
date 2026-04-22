using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.Rendering;

public interface IResourceManager : IInitializable
{
    /// <summary>
    /// Gets the current graphics context associated with the operation.
    /// </summary>
    IContext Context { get; }

    /// <summary>
    /// Gets the material manager used to access and manage materials within the system.
    /// </summary>
    IPBRMaterialManager PBRMaterialManager { get; }

    /// <summary>
    /// Gets the manager responsible for handling point materials in the system.
    /// </summary>
    IPointMaterialManager PointMaterialManager { get; }

    /// <summary>
    /// Gets the geometry pool for managing geometry resources.
    /// </summary>
    IGeometryManager Geometries { get; }

    /// <summary>
    /// Gets the material pool for managing material resources.
    /// </summary>
    IPBRMaterialPropertyManager PBRPropertyManager { get; }

    /// <summary>
    /// Gets the repository used to manage and retrieve shader resources.
    /// </summary>
    IShaderRepository ShaderRepository { get; }

    /// <summary>
    /// Gets the sampler repository used to manage and retrieve sampler instances.
    /// </summary>
    ISamplerRepository SamplerRepository { get; }

    /// <summary>
    /// Gets the repository used to access and manage textures.
    /// </summary>
    ITextureRepository TextureRepository { get; }

    /// <summary>
    /// Gets the global index data buffer associated with the static mesh.
    /// </summary>
    IStaticMeshIndexData StaticMeshIndexData { get; }

    /// <summary>
    /// Gets the PBR property buffer data.
    /// </summary>
    IPBRPropertyData PBRPropertyData { get; }

    /// <summary>
    /// Gets the mesh info data buffer.
    /// </summary>
    IRenderData MeshInfoData { get; }

    /// <summary>
    /// Update the resource manager, performing necessary updates to GPU resources based on changes in the underlying data.
    /// </summary>
    /// <returns></returns>
    bool Update();
}
