namespace HelixToolkit.Nex.Engine.Data;

internal class LightData : Initializable, IRenderData
{
    public const int InitialBufferSize = 16;
    private static readonly ILogger _logger = LogManager.Create<LightData>();
    private static readonly QueryDescription _lightQuery = new QueryDescription().WithAll<Light>();

    public IContext Context { get; }
    public World World { get; }

    private ElementBuffer<Light>? _lightBuffer;

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();

    public BufferHandle Buffer => _lightBuffer is null ? BufferHandle.Null : _lightBuffer.Buffer;

    public uint Stride { get; } = Light.SizeInBytes;

    public uint Count => _lightBuffer is null ? 0 : (uint)_lightBuffer.Count;
    public ulong GpuAddress => _lightBuffer is null ? 0 : _lightBuffer.Buffer.GpuAddress;

    public override string Name { get; }

    public LightData(IContext context, World world)
    {
        Context = context;
        World = world;
        Name = $"{nameof(LightData)}_{World.Id}";
        world.SubscribeComponentAdded<Light>(OnLightChanged);
        world.SubscribeComponentRemoved<Light>(OnLightChanged);
        world.SubscribeComponentSet<Light>(OnLightChanged);
    }

    protected override ResultCode OnInitializing()
    {
        _lightBuffer = new ElementBuffer<Light>(
            Context,
            InitialBufferSize,
            BufferUsageBits.Storage,
            true,
            "Light"
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _lightBuffer?.Dispose();
        _lightBuffer = null;
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (!Buffer.Valid || _lightBuffer is null)
        {
            return false;
        }
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks)
        {
            return true;
        }

        var count = World.CountEntities(_lightQuery);
        if (count == 0)
        {
            _lightBuffer?.Reset();
            return true;
        }
        var q = World.Query(_lightQuery);
        _lightBuffer
            ?.WriteDynamic(
                count,
                (ctx) =>
                {
                    foreach (ref var chunk in q.GetChunkIterator())
                    {
                        foreach (var entity in chunk)
                        {
                            ref var light = ref chunk.Get<Light>(entity);
                            ctx.Write(ref light);
                        }
                    }
                }
            )
            .CheckResult();
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    private void OnLightChanged(in Entity entity, ref Light light)
    {
        _logger.LogDebug("Light changed. {LIGHT}", light);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    }
}
