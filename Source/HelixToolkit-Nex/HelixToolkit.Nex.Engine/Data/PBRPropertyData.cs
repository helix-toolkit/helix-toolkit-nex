namespace HelixToolkit.Nex.Engine.Data;

public sealed class PBRPropertyData : Initializable, IPBRPropertyData
{
    public const int InitialBufferSize = 64;
    private static readonly EventBus _eventBus = EventBus.Instance;
    private static readonly ILogger _logger = LogManager.Create<PBRPropertyData>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(PBRPropertyData));
    private readonly IContext _context;
    private readonly ResourceManager _resourceManager;

    private ElementBuffer<PBRProperties>? _buffer;

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private readonly IEventSubscription _sub;

    public BufferHandle Buffer => _buffer is null ? BufferHandle.Null : _buffer.Buffer;

    public uint Stride { get; } = Light.SizeInBytes;

    public uint Count => _buffer is null ? 0 : (uint)_buffer.Count;

    public ulong GpuAddress => _buffer is null ? 0 : _buffer.Buffer.GpuAddress;

    public override string Name { get; } = nameof(PBRPropertyData);

    public PBRPropertyData(ResourceManager resourceManager)
    {
        _context = resourceManager.Context;
        _resourceManager = resourceManager;
        _sub = _eventBus.Subscribe<MaterialPropsUpdatedEvent>(
            (e) =>
            {
                _logger.LogTrace(
                    "Material Props is changed. Index: {INDEX}; Op: {OP};",
                    e.Index,
                    e.Operation
                );
                _lastDataUpdateTicks = Stopwatch.GetTimestamp();
            }
        );
    }

    protected override ResultCode OnInitializing()
    {
        _buffer = new ElementBuffer<PBRProperties>(
            _context,
            InitialBufferSize,
            BufferUsageBits.Storage,
            true,
            "PBRProperties"
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
        using var t = _tracer.BeginScope(nameof(Update));
        var objects = _resourceManager.MaterialProperties.Objects;
        _buffer.WriteDynamic(
            objects.Count,
            ctx =>
            {
                for (var i = 0; i < objects.Count; ++i)
                {
                    ctx.Write(objects[i].Obj);
                }
            }
        );
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }
}
