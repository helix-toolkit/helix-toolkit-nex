using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Engine.Components;

namespace HelixToolkit.Nex.Engine.Data;

internal class RangeLightData : Initializable, IRenderData
{
    public const int InitialBufferSize = 16;
    private static readonly ILogger _logger = LogManager.Create<RangeLightData>();

    private EntityCollection? _entities;

    public IContext Context { get; }
    public World World { get; }

    private RingElementBuffer<Light>? _lightBuffer;
    private readonly FastList<Light> _lights = new(InitialBufferSize);

    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private HashSet<int> _pendingEntities = [];
    private bool _needRebuild = true;

    public BufferHandle Buffer => _lightBuffer is null ? BufferHandle.Null : _lightBuffer.Buffer;

    public uint Stride { get; } = Light.SizeInBytes;

    public uint Count => _lightBuffer is null ? 0 : (uint)_lightBuffer.Count;
    public ulong GpuAddress => _lightBuffer is null ? 0 : _lightBuffer.GpuAddress;

    public override string Name { get; }

    public RangeLightData(IContext context, World world)
    {
        Context = context;
        World = world;
        Name = $"{nameof(RangeLightData)}_{World.Id}";
    }

    protected override ResultCode OnInitializing()
    {
        _needRebuild = true;
        _entities = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<RangeLightComponent>()
            .Has<WorldTransform>()
            .Build();
        _entities.EntityAdded += OnLightAddRemove;
        _entities.EntityRemoved += OnLightAddRemove;
        _entities.EntityChanged += OnLightChanged;
        _lightBuffer = new RingElementBuffer<Light>(
            Context,
            (int)RenderSettings.NumFrameInFlight(Context),
            InitialBufferSize,
            BufferUsageBits.Storage,
            hostVisible: true,
            "Light"
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _entities);
        Disposer.DisposeAndRemove(ref _lightBuffer);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (_lightBuffer is null)
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
        if (_entities.Count == 0)
        {
            _lights.Clear();
            _lightBuffer.Reset();
            _lastBufferUpdateTicks = _lastDataUpdateTicks;
            return true;
        }
        _lightBuffer.Advance();
        if (_needRebuild)
        {
            _lights.Clear();
            var idx = 0;
            foreach (var entity in _entities)
            {
                ref var lightComp = ref entity.Get<RangeLightComponent>();
                ref var transform = ref entity.Get<WorldTransform>();
                var light = lightComp.Light;
                light.Direction = Vector3.TransformNormal(light.Direction, transform.Value);
                light.Position = Vector3.Transform(light.Position, transform.Value);
                Debug.Assert(light.Type != 0);
                _lights.Add(light);
                lightComp.Index = idx++;
            }
            _needRebuild = false;
        }
        else
        {
            foreach (var entityId in _pendingEntities)
            {
                var entity = World.GetEntity(entityId);
                ref var lightComp = ref entity.Get<RangeLightComponent>();
                ref var transform = ref entity.Get<WorldTransform>();
                var light = lightComp.Light;
                light.Direction = Vector3.TransformNormal(light.Direction, transform.Value);
                light.Position = Vector3.Transform(light.Position, transform.Value);
                Debug.Assert(light.Type != 0);
                Debug.Assert(lightComp.Index >= 0, "Light index must >= 0.");
                _lights.At(lightComp.Index) = light;
            }
        }
        _lightBuffer.Upload(_lights);
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        _pendingEntities.Clear();
        return true;
    }

    private void OnLightAddRemove(object? sender, int e)
    {
        _logger.LogTrace("Light added or removed. {ID}", e);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        _needRebuild = true;
    }

    private void OnLightChanged(object? sender, EntityChangedEvent e)
    {
        _logger.LogTrace("Light changed. {ID}", e);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        if (_needRebuild)
        {
            return;
        }
        if (
            e.Type == World.GetComponentTypeId<WorldTransform>()
            || e.Type == World.GetComponentTypeId<RangeLightComponent>()
        )
        {
            _pendingEntities.Add(e.EntityId);
        }
        else if (e.Type == World.GetComponentTypeId<NodeInfo>())
        {
            _needRebuild = true;
        }
    }
}
