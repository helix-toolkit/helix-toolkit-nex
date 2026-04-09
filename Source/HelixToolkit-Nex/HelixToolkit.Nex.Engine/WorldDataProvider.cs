using HelixToolkit.Nex.Engine.Data;

namespace HelixToolkit.Nex.Engine;

public sealed class WorldDataProvider : IRenderDataProvider, IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<WorldDataProvider>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(WorldDataProvider));
    private readonly FastList<IRenderData> _renderDataList = [];
    private readonly RangeLightData _lightData;
    private readonly DirectionalLightData _directionalLightData;
    private readonly MeshDrawData _meshDrawDataOpaque;
    private readonly MeshDrawData _meshDrawDataTransparent;
    private readonly PointCloudData _pointCloudData;
    private readonly SceneState _sceneState;

    public World World { get; } = World.CreateWorld();
    public IResourceManager ResourceManager { get; }

    public IContext Context => ResourceManager.Context;

    public IRenderData Lights => _lightData;

    public IRenderData DirectionalLights => _directionalLightData;

    public IMeshDrawData MeshDrawsOpaque => _meshDrawDataOpaque;

    public IMeshDrawData MeshDrawsTransparent => _meshDrawDataTransparent;

    public IPBRPropertyData PBRPropertiesBuffer => ResourceManager.PBRPropertyData;

    public IStaticMeshIndexData StaticMeshIndexData => ResourceManager.StaticMeshIndexData;

    public IPointCloudData? PointCloudData => _pointCloudData;

    public IRenderData MeshInfos => ResourceManager.MeshInfoData;

    public WorldDataProvider(IServiceProvider services)
    {
        ResourceManager = services.GetRequiredService<IResourceManager>();
        _lightData = new RangeLightData(Context, World);
        _directionalLightData = new DirectionalLightData(Context, World);
        _meshDrawDataOpaque = new MeshDrawData(Context, World, false);
        _meshDrawDataTransparent = new MeshDrawData(Context, World, true);
        _pointCloudData = new PointCloudData(Context, World);
        _sceneState = new SceneState(World);
        _renderDataList.Add(_lightData);
        _renderDataList.Add(_directionalLightData);
        _renderDataList.Add(_meshDrawDataOpaque);
        _renderDataList.Add(_meshDrawDataTransparent);
        _renderDataList.Add(_pointCloudData);
    }

    public bool Initialize()
    {
        using var t = _tracer.BeginScope(nameof(Initialize));
        _sceneState.Initialize();
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
        _sceneState.Update();
        foreach (var item in _renderDataList)
        {
            if (!item.Update())
            {
                return false;
            }
        }
        return true;
    }

    public PBRMaterial? GetMaterial(MaterialTypeId materialType)
    {
        return ResourceManager.Materials.GetMaterial(materialType);
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
                _sceneState.Dispose();
                foreach (var data in _renderDataList)
                {
                    data.Dispose();
                }
                World.Dispose();
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
