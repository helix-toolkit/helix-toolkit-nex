using HelixToolkit.Nex.Engine.Data;

namespace HelixToolkit.Nex.Engine;

public sealed class WorldDataProvider : IRenderDataProvider, IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<WorldDataProvider>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(WorldDataProvider));
    private readonly FastList<IRenderData> _renderDataList = [];
    private readonly LightData _lightData;
    private readonly DirectionalLightData _directionalLightData;
    private readonly MeshDrawData _meshDrawDataOpaque;
    private readonly MeshDrawData _meshDrawDataTransparent;
    private readonly Entity _preserve;

    public readonly World World = World.Create();
    public IResourceManager ResourceManager { get; }

    public IContext Context => ResourceManager.Context;

    public IRenderData Lights => _lightData;

    public IRenderData DirectionalLights => _directionalLightData;

    public IMeshDrawData MeshDrawsOpaque => _meshDrawDataOpaque;

    public IMeshDrawData MeshDrawsTransparent => _meshDrawDataTransparent;

    public IPBRPropertyData PBRPropertiesBuffer => ResourceManager.PBRPropertyData;

    public IStaticMeshIndexData StaticMeshIndexData => ResourceManager.StaticMeshIndexData;

    public IRenderData MeshInfos => ResourceManager.MeshInfoData;

    public WorldDataProvider(IServiceProvider services)
    {
        _preserve = World.Create<int>(); // Make sure entity 0 is not used, as it is reserved for "null" in some cases.
        ResourceManager = services.GetRequiredService<IResourceManager>();
        _lightData = new LightData(Context, World);
        _directionalLightData = new DirectionalLightData(Context, World);
        _meshDrawDataOpaque = new MeshDrawData(Context, World, false);
        _meshDrawDataTransparent = new MeshDrawData(Context, World, true);
        _renderDataList.Add(_lightData);
        _renderDataList.Add(_directionalLightData);
        _renderDataList.Add(_meshDrawDataOpaque);
        _renderDataList.Add(_meshDrawDataTransparent);
    }

    public bool Initialize()
    {
        using var t = _tracer.BeginScope(nameof(Initialize));
        foreach (var item in _renderDataList)
        {
            if (item.Initialize().CheckResult() != ResultCode.Ok)
            {
                return false;
            }
        }
        return true;
    }

    public bool Update()
    {
        using var t = _tracer.BeginScope(nameof(Update));
        ResourceManager.Update();
        foreach (var item in _renderDataList)
        {
            if (!item.Update())
            {
                return false;
            }
        }
        return true;
    }

    public RenderPipelineHandle GetMaterialPipeline(MaterialTypeId materialType)
    {
        return ResourceManager.Materials.GetMaterialPipeline(materialType);
    }

    public Geometry? GetGeometry(uint geometryId)
    {
        return ResourceManager.Geometries.GetGeometryById(geometryId);
    }

    #region IDisposable Support
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                World.Dispose();
                foreach (var data in _renderDataList)
                {
                    data.Dispose();
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~WorldDataProvider()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
