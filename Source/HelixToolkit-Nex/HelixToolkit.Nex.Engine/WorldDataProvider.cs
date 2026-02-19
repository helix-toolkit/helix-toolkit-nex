using HelixToolkit.Nex.Engine.Data;

namespace HelixToolkit.Nex.Engine;

public sealed class WorldDataProvider : IRenderDataProvider, IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<WorldDataProvider>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(WorldDataProvider));
    private readonly FastList<IRenderData> _renderDataList = [];
    private readonly LightData _lightData;
    private readonly DirectionalLightData _directionalLightData;
    private readonly MeshInfoData _meshInfoData;
    private readonly MeshDrawData _meshDrawDataOpaque;
    private readonly MeshDrawData _meshDrawDataTransparent;
    private readonly PBRPropertyData _pbrPropertyData;
    private readonly StaticMeshIndexData _staticMeshIndexData;

    public readonly World World = World.Create();
    public readonly ResourceManager ResourceManager;

    public IRenderData Lights => _lightData;

    public IRenderData DirectionalLights => _directionalLightData;

    public IRenderData MeshInfos => _meshInfoData;

    public IMeshDrawData MeshDrawsOpaque => _meshDrawDataOpaque;

    public IMeshDrawData MeshDrawsTransparent => _meshDrawDataTransparent;

    public IRenderData PBRPropertiesBuffer => _pbrPropertyData;

    public IRenderData StaticMeshIndexData => _staticMeshIndexData;

    public WorldDataProvider(IServiceProvider services)
    {
        ResourceManager = services.GetRequiredService<ResourceManager>();
        _lightData = new LightData(services, World);
        _directionalLightData = new DirectionalLightData(services, World);
        _meshInfoData = new MeshInfoData(services);
        _meshDrawDataOpaque = new MeshDrawData(services, World, false);
        _meshDrawDataTransparent = new MeshDrawData(services, World, true);
        _pbrPropertyData = new PBRPropertyData(services);
        _staticMeshIndexData = new StaticMeshIndexData(services);
        _renderDataList.Add(_lightData);
        _renderDataList.Add(_directionalLightData);
        _renderDataList.Add(_meshInfoData);
        _renderDataList.Add(_meshDrawDataOpaque);
        _renderDataList.Add(_meshDrawDataTransparent);
        _renderDataList.Add(_pbrPropertyData);
        _renderDataList.Add(_staticMeshIndexData);
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
        _staticMeshIndexData?.Update(); // Update static mesh index data before other data, as it may be used by other data
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
