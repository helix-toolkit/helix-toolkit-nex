using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DataEntries;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects all <see cref="PointCloudComponent"/> entities from the ECS world each frame,
/// packs their <see cref="PointData"/> into a contiguous GPU buffer, and exposes per-entity
/// dispatch information so the <c>PointRenderNode</c> compute shader can frustum-cull
/// and stamp the correct entity ID on each point.
/// </summary>
internal sealed class PointCloudData(IContext context, World world) : Initializable, IPointCloudData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(PointCloudData));
    private static readonly ILogger _logger = LogManager.Create<PointCloudData>();
    private readonly Dictionary<MaterialTypeId, PointCloudDataEntry> _pointsByMaterial = [];
    private EntityCollection? _entities;
    private long _lastBufferUpdateTicks;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private bool _needRebuilt = true;

    public IContext Context { get; } = context;
    public World World { get; } = world;

    public override string Name { get; } = nameof(PointCloudData);

    public IReadOnlyDictionary<MaterialTypeId, PointCloudDataEntry> Data => _pointsByMaterial;

    public BufferHandle Buffer => BufferHandle.Null; // Not used directly; the render node accesses the vertex buffer directly.

    public ulong GpuAddress => 0; // Not used directly; the render node accesses the vertex buffer directly.

    public uint Stride => 0;

    public uint Count { private set; get; } = 0;

    public uint TotalPointCount { private set; get; } = 0;

    protected override ResultCode OnInitializing()
    {
        _needRebuilt = true;
        _entities = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<PointCloudComponent>()
            .Has<WorldTransform>()
            .Build();
        _entities.EntityChanged += OnEntityChanged;
        _entities.EntityAdded += OnAddOrRemovedChanged;
        _entities.EntityRemoved += OnAddOrRemovedChanged;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var entry in _pointsByMaterial.Values)
        {
            entry.Dispose();
        }
        _pointsByMaterial.Clear();
        Disposer.DisposeAndRemove(ref _entities);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        // Rotate to the next buffer slot so the GPU can keep reading the
        // previous frame's data while we overwrite this one.

        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks && !_needRebuilt)
        {
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        Rebuild();
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    /// <summary>
    /// Iterates over all point cloud entities, packs their points into the GPU buffer,
    /// and records per-entity dispatch information.
    /// </summary>
    private void Rebuild()
    {
        if (_entities is null)
        {
            return;
        }
        using var t = _tracer.BeginScope(nameof(Rebuild));
        foreach (var entry in _pointsByMaterial.Values)
        {
            entry.Clear();
        }
        TotalPointCount = 0;
        Count = 0;

        foreach (var entity in _entities)
        {
            ref var nodeInfo = ref entity.Get<NodeInfo>();
            if (!nodeInfo.Enabled)
                continue;
            ref var pc = ref entity.Get<PointCloudComponent>();
            if (!pc.Valid)
                continue;
            TotalPointCount += (uint)pc.PointCount;
            if (
                pc.PointMaterialName is not null
                && PointMaterialRegistry.TryGetTypeId(pc.PointMaterialName, out var materialId)
            )
            {
                pc.PointMaterialId = materialId;
            }
            if (
                !_pointsByMaterial.TryGetValue(pc.PointMaterialId, out var entry)
                || entry.IsDisposed
            )
            {
                entry = new PointCloudDataEntry(Context, pc.PointCount, pc.PointMaterialId);
                _pointsByMaterial[pc.PointMaterialId] = entry;
            }
            entry.AddEntity(entity);
            ++Count;
        }

        foreach (var entry in _pointsByMaterial.Values)
        {
            if (entry.PointCount == 0)
            {
                entry.Dispose();
            }
        }
        _needRebuilt = false;
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        _needRebuilt = true;
    }

    private void OnAddOrRemovedChanged(object? sender, int e)
    {
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        _needRebuilt = true;
    }
}
