namespace HelixToolkit.Nex.Engine.Data;

internal class DirectionalLightData : Initializable, IRenderData
{
    private static readonly ILogger _logger = LogManager.Create<DirectionalLightData>();
    private static readonly QueryDescription _lightQuery =
        new QueryDescription().WithAll<DirectionalLight>();

    public World World { get; }

    private readonly IContext _context;
    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = 0;

    private BufferResource _buffer = BufferResource.Null;
    public BufferHandle Buffer => _buffer;

    public uint Stride { get; } = DirectionalLight.SizeInBytes;

    public uint Count { private set; get; } = 0;
    public ulong GpuAddress => _buffer is null ? 0 : _buffer.GpuAddress;

    public override string Name { get; }

    public DirectionalLightData(IServiceProvider services, World world)
    {
        _context = services.GetRequiredService<IContext>();
        World = world;
        Name = $"{nameof(DirectionalLightData)}_{World.Id}";
        world.SubscribeComponentAdded<DirectionalLight>(OnLightChanged);
        world.SubscribeComponentRemoved<DirectionalLight>(OnLightChanged);
        world.SubscribeComponentSet<DirectionalLight>(OnLightChanged);
    }

    protected override ResultCode OnInitializing()
    {
        var result = _context
            .CreateBuffer(
                new DirectionalLights(),
                BufferUsageBits.Storage,
                StorageType.Device,
                out _buffer,
                "DirectionalLight"
            )
            .CheckResult();
        return result;
    }

    protected override ResultCode OnTearingDown()
    {
        _buffer.Dispose();
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (!Buffer.Valid)
        {
            return false;
        }
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks)
        {
            return true;
        }

        var lights = new DirectionalLights();
        var q = World.Query(_lightQuery);
        int count = 0;
        foreach (ref var chunk in q.GetChunkIterator())
        {
            foreach (var entity in chunk)
            {
                ref var light = ref chunk.Get<DirectionalLight>(entity);
                lights.SetLights(count++, ref light);
                if (count >= DirectionalLights.MaxLightsCount)
                {
                    break;
                }
            }
            if (count >= DirectionalLights.MaxLightsCount)
            {
                break;
            }
        }
        _context.Upload(Buffer, 0, lights);
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    private void OnLightChanged(in Entity entity, ref DirectionalLight light)
    {
        _logger.LogDebug("Directional light is changed. {LIGHT}", light);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    }
}
