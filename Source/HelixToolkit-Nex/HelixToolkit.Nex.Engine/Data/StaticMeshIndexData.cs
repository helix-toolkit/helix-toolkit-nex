namespace HelixToolkit.Nex.Engine.Data;

public sealed class StaticMeshIndexData : Initializable, IStaticMeshIndexData
{
    public const int InitialBufferSize = 1024 * 10;
    private static readonly EventBus _eventBus = EventBus.Instance;
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(StaticMeshIndexData));
    private static readonly ILogger _logger = LogManager.Create(nameof(StaticMeshIndexData));

    private readonly IContext _context;
    private readonly ResourceManager _resourceManager;

    private ElementBuffer<uint>? _buffer;

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private readonly IEventSubscription _sub;

    public BufferHandle Buffer => _buffer is null ? BufferHandle.Null : _buffer.Buffer;

    public uint Stride { get; } = MeshInfo.SizeInBytes;

    public uint Count => _buffer is null ? 0 : (uint)_buffer.Count;
    public ulong GpuAddress => _buffer is null ? 0 : _buffer.Buffer.GpuAddress;

    public override string Name => nameof(StaticMeshIndexData);

    public StaticMeshIndexData(ResourceManager resourceManager)
    {
        _context = resourceManager.Context;
        _resourceManager = resourceManager;
        _sub = _eventBus.Subscribe<GeometryUpdatedEvent>(
            (e) =>
            {
                var geometry = _resourceManager.Geometries.GetGeometryById(e.GeometryId);
                if (
                    geometry is not null
                    && !geometry.IsDynamic
                    && (e.ChangeType == GeometryChangeOp.Added)
                )
                {
                    _logger.LogDebug(
                        "Static mesh is updated. Id: {ID}; Op: {OP};",
                        geometry.Id,
                        e.ChangeType
                    );
                    _lastDataUpdateTicks = Stopwatch.GetTimestamp();
                }
            }
        );
    }

    protected override ResultCode OnInitializing()
    {
        _buffer = new ElementBuffer<uint>(
            _context,
            InitialBufferSize,
            BufferUsageBits.Storage | BufferUsageBits.Index,
            true,
            "StaticMeshIndices"
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _sub.Dispose();
        _buffer?.Dispose();
        _buffer = null;
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (!Buffer.Valid || _buffer is null)
        {
            return false;
        }
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks)
        {
            return true;
        }
        _logger.LogInformation("Updating static mesh index buffer...");
        using var t = _tracer.BeginScope(nameof(Update));
        _buffer.WriteDynamic(
            _resourceManager.Geometries.TotalStaticIndexCount,
            (ctx) => _resourceManager.Geometries?.UploadStaticMeshIndices(ref ctx)
        );
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }
}
