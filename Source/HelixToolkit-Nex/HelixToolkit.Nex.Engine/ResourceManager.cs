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
public sealed class ResourceManager : Initializable, IResourceManager
{
    private readonly FastList<IRenderData> _renderDatas = [];
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
        SamplerRepository =
            services.GetService<ISamplerRepository>() ?? new SamplerRepository(Context);
        TextureRepository =
            services.GetService<ITextureRepository>() ?? new TextureRepository(Context);
        FontAtlasRepository =
            services.GetService<IFontAtlasRepository>() ?? new FontAtlasRepository();
        StaticMeshIndexData = new StaticMeshIndexData(this);
        PBRPropertyData = new PBRPropertyData(this);
        MeshInfoData = new MeshInfoData(this);
        _renderDatas.Add(PBRPropertyData);
        _renderDatas.Add(StaticMeshIndexData);
        _renderDatas.Add(MeshInfoData);
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
            DirtyGeometryCount = Geometries
                .GetAll()
                .Count(g => g.BufferDirty != GeometryBufferType.None),
        };
    }

    protected override ResultCode OnInitializing()
    {
        foreach (var renderData in _renderDatas)
        {
            var result = renderData.Initialize();
            if (result != ResultCode.Ok)
            {
                return result;
            }
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var renderData in _renderDatas)
        {
            renderData.Dispose();
        }
        Geometries.Clear();
        PBRPropertyManager.Clear();
        PBRMaterialManager.Clear();
        PointMaterialManager.Clear();
        BillboardMaterialManager.Clear();
        ShaderRepository.Clear();
        SamplerRepository.Clear();
        TextureRepository.Clear();
        FontAtlasRepository.Clear();

        return ResultCode.Ok;
    }

    public bool Update()
    {
        // BeginFrame all geometries with dirty buffers
        foreach (var geometry in Geometries.GetAll())
        {
            if (geometry.BufferDirty != GeometryBufferType.None)
            {
                geometry.UpdateBuffers(Context);
            }
            geometry.TryCompletePendingBufferUpdate();
        }
        foreach (var renderData in _renderDatas)
        {
            renderData.Update();
        }
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
