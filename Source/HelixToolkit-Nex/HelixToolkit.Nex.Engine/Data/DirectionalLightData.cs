using HelixToolkit.Nex.Engine.Components;

namespace HelixToolkit.Nex.Engine.Data;

internal class DirectionalLightData : Initializable, IRenderData
{
    private static readonly ILogger _logger = LogManager.Create<DirectionalLightData>();

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private EntityCollection? _entities;

    public World World { get; }

    public IContext Context { get; }

    private BufferResource? _buffer;
    public BufferHandle Buffer => _buffer ?? BufferHandle.Null;

    public uint Stride { get; } = DirectionalLight.SizeInBytes;

    public uint Count { private set; get; } = 0;
    public ulong GpuAddress => _buffer is null ? 0 : _buffer.GpuAddress;

    public override string Name { get; }

    public DirectionalLightData(IContext context, World world)
    {
        Context = context;
        World = world;
        Name = $"{nameof(DirectionalLightData)}_{World.Id}";
    }

    protected override ResultCode OnInitializing()
    {
        _entities = World
            .CreateCollection()
            .Has<DirectionalLightComponent>()
            .Has<WorldTransform>()
            .Build();
        _entities.EntityChanged += OnLightChanged;
        _entities.EntityAdded += OnLightChanged;
        _entities.EntityRemoved += OnLightChanged;
        var result = Context
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
        Disposer.DisposeAndRemove(ref _buffer);
        Disposer.DisposeAndRemove(ref _entities);
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
        if (_entities is null)
        {
            return false;
        }
        var lights = new DirectionalLights { LightCount = 0 };
        foreach (var entity in _entities)
        {
            ref var lightComp = ref entity.Get<DirectionalLightComponent>();
            ref var transform = ref entity.Get<WorldTransform>();
            var light = lightComp.Light;
            light.Direction = Vector3.TransformNormal(light.Direction, transform.Value);
            lights.SetLights((int)lights.LightCount++, light);
            if (lights.LightCount >= DirectionalLights.MaxLightsCount)
            {
                break;
            }
        }
        Count = lights.LightCount;
        Context.Upload(Buffer, 0, lights);
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    private void OnLightChanged(object? source, int entityId)
    {
        _logger.LogTrace("Directional Light is changed. {ID}", entityId);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    }
}
