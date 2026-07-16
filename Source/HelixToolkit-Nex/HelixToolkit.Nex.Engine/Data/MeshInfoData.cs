namespace HelixToolkit.Nex.Engine.Data;

internal sealed class MeshInfoData : Initializable, IRenderData
{
    public const int InitialBufferSize = 64;
    private static readonly EventBus _eventBus = EventBus.Instance;
    private static readonly ILogger _logger = LogManager.Create<MeshInfoData>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(MeshInfoData));
    private readonly IResourceManager _resourceManager;
    private ElementBuffer<MeshInfo>? _buffer;

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private readonly IEventSubscription _sub;

    public IContext Context => _resourceManager.Context;

    public BufferHandle Buffer => _buffer is null ? BufferHandle.Null : _buffer.Buffer;

    public uint Stride { get; } = MeshInfo.SizeInBytes;

    public uint Count => _buffer is null ? 0 : (uint)_buffer.Count;
    public ulong GpuAddress => _buffer is null ? 0 : _buffer.Buffer.GpuAddress;

    public override string Name { get; } = nameof(MeshInfoData);

    public MeshInfoData(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager;
        _sub = _eventBus.Subscribe<GeometryUpdatedEvent>(
            (e) =>
            {
                _logger.LogTrace("Geometry [{ID}] updated. Op: {TYPE}", e.GeometryId, e.ChangeType);
                _lastDataUpdateTicks = Stopwatch.GetTimestamp();
            }
        );
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
        using var t = _tracer.BeginScope(nameof(Update));
        // Make sure GPU is not using the buffer before updating.
        // Must not reset the fence here, engine will handle it.
        Context.WaitAll(reset: false);
        _resourceManager.Geometries.UploadMeshInfoDynamic(_buffer);
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        _buffer = new ElementBuffer<MeshInfo>(
            Context,
            InitialBufferSize,
            BufferUsageBits.Storage,
            true,
            "MeshInfo"
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
}
