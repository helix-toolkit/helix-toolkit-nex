using System.Diagnostics.CodeAnalysis;
using HelixToolkit.Nex.Engine.Data;
using HelixToolkit.Nex.Rendering.SDF;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Manages all rendering resources including geometries and materials for the engine.
/// </summary>
/// <remarks>
/// The ResourceManager provides:
/// <list type="bullet">
/// <item>Centralized management of geometries and materials</item>
/// <item>Automatic GPU buffer creation and updates</item>
/// <item>Resource lifecycle management with proper disposal</item>
/// <item>ID-based resource referencing to enable sharing</item>
/// </list>
///
/// <para>
/// <b>Architecture Pattern:</b>
/// Scene nodes store only handles (IDs) to resources, not the actual data.
/// This enables:
/// - Multiple nodes sharing the same geometry/material (memory efficient)
/// - Easy runtime swapping of resources
/// - Better cache performance (data-oriented design)
/// - Simple serialization (just save IDs)
/// </para>
/// </remarks>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class ResourceManager : Initializable, IResourceManager
{
    public IContext Context { get; }
    public IPBRMaterialManager PBRMaterialManager { get; }

    /// <inheritdoc />
    public IGeometryManager Geometries { get; }

    /// <inheritdoc />
    public IPBRMaterialPropertyManager PBRPropertyManager { get; }

    /// <inheritdoc />
    public IPointMaterialManager PointMaterialManager { get; }

    /// <inheritdoc />
    public IBillboardMaterialManager BillboardMaterialManager { get; }

    public ILineMaterialManager LineMaterialManager { get; }

    /// <inheritdoc />>
    public IShaderRepository ShaderRepository { get; }

    /// <inheritdoc />
    public ISamplerRepository SamplerRepository { get; }

    /// <inheritdoc />
    public ITextureRepository TextureRepository { get; }

    /// <inheritdoc />
    public IFontAtlasRepository FontAtlasRepository { get; }

    /// <inheritdoc />
    public IStaticMeshIndexData StaticMeshIndexData { get; }

    /// <inheritdoc />
    public IPBRPropertyData PBRPropertyData { get; }

    /// <inheritdoc />
    public IRenderData MeshInfoData { get; }

    public override string Name => nameof(ResourceManager);

    public ResourceManager(IServiceProvider services)
    {
        Context = services.GetRequiredService<IContext>();
        PBRPropertyManager =
            services.GetService<IPBRMaterialPropertyManager>() ?? new PBRMaterialPropertyManager();
        PBRMaterialManager =
            services.GetService<IPBRMaterialManager>()
            ?? new PBRMaterialManager(Context, PBRPropertyManager);
        Geometries = services.GetService<IGeometryManager>() ?? new GeometryManager(Context);
        ShaderRepository =
            services.GetService<IShaderRepository>() ?? new ShaderRepository(Context);
        PointMaterialManager =
            services.GetService<IPointMaterialManager>()
            ?? new PointMaterialManager(Context, ShaderRepository);
        BillboardMaterialManager =
            services.GetService<IBillboardMaterialManager>()
            ?? new BillboardMaterialManager(Context, ShaderRepository);
        LineMaterialManager = services.GetService<ILineMaterialManager>()
            ?? new LineMaterialManager(Context, ShaderRepository);
        SamplerRepository =
            services.GetService<ISamplerRepository>() ?? new SamplerRepository(Context);
        TextureRepository =
            services.GetService<ITextureRepository>() ?? new TextureRepository(Context);
        FontAtlasRepository =
            services.GetService<IFontAtlasRepository>() ?? new FontAtlasRepository();
        StaticMeshIndexData = new StaticMeshIndexData(this);
        PBRPropertyData = new PBRPropertyData(this);
        MeshInfoData = new MeshInfoData(this);
    }

    /// <summary>
    /// Gets statistics about resource usage.
    /// </summary>
    public ResourceStatistics GetStatistics()
    {
        return new ResourceStatistics
        {
            GeometryCount = Geometries.Count,
            MaterialCount = PBRMaterialManager.Count,
            MaterialPropertyCount = PBRPropertyManager.Count,
            ShaderCount = ShaderRepository.Count,
            DirtyGeometryCount = Geometries.GetDirtyCount(),
        };
    }

    protected override ResultCode OnInitializing()
    {
        var result = StaticMeshIndexData.Initialize().CheckResult();
        if (result != ResultCode.Ok)
        {
            return result;
        }

        result = PBRPropertyData.Initialize().CheckResult();
        if (result != ResultCode.Ok)
        {
            return result;
        }

        result = MeshInfoData.Initialize().CheckResult();
        if (result != ResultCode.Ok)
        {
            return result;
        }

        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Geometries.Clear();
        PBRPropertyManager.Clear();
        PBRMaterialManager.Clear();
        PointMaterialManager.Clear();
        BillboardMaterialManager.Clear();
        LineMaterialManager.Clear();
        ShaderRepository.Clear();
        SamplerRepository.Clear();
        TextureRepository.Clear();
        FontAtlasRepository.Clear();

        StaticMeshIndexData.Teardown().CheckResult();
        MeshInfoData.Teardown().CheckResult();
        PBRPropertyData.Teardown().CheckResult();
        return ResultCode.Ok;
    }

    public bool Update()
    {
        // Drain any pending GPU mipmap-generation requests on the render thread. Texture creation
        // (async uploads and deferred sync paths) only enqueues these; IContext.GenerateMipmap
        // performs an immediate command-buffer submission and must run here, not on background
        // upload continuations.
        TextureRepository.ProcessPendingMipmapGeneration();
        // Apply any geometry removals that were deferred to a frame boundary before the shared
        // static-mesh index buffer is rebuilt, so the removal and reindex happen consistently.
        Geometries.ProcessPendingRemovals();
        // BeginFrame all geometries with dirty buffers
        foreach (var geometry in Geometries)
        {
            if (geometry.BufferDirty != GeometryBufferType.None)
            {
                geometry.UpdateBuffers(Context);
            }
            geometry.TryCompletePendingBufferUpdate();
        }
        StaticMeshIndexData.Update();
        PBRPropertyData.Update();
        MeshInfoData.Update();
        return true;
    }
}

/// <summary>
/// Contains statistics about resource usage.
/// </summary>
public readonly record struct ResourceStatistics
{
    /// <summary>
    /// Total number of active geometries.
    /// </summary>
    public int GeometryCount { get; init; }

    /// <summary>
    /// Gets the number of material properties.
    /// </summary>
    public int MaterialPropertyCount { get; init; }

    /// <summary>
    /// Total number of active materials.
    /// </summary>
    public int MaterialCount { get; init; }

    /// <summary>
    /// Gets the number of shaders.
    /// </summary>
    public int ShaderCount { get; init; }

    /// <summary>
    /// Number of geometries with dirty buffers that need GPU updates.
    /// </summary>
    public int DirtyGeometryCount { get; init; }
}
