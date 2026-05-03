using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects all <see cref="BillboardComponent"/> entities from the ECS world each frame,
/// packs their data into GPU buffers grouped by material type, and exposes per-material
/// dispatch information so the <c>BillboardCullNode</c> compute shader can frustum-cull
/// and stamp the correct entity ID on each billboard.
/// </summary>
internal sealed class BillboardData(IContext context, World world) : Initializable, IBillboardData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(BillboardData));
    private static readonly ILogger _logger = LogManager.Create<BillboardData>();
    private readonly Dictionary<MaterialTypeId, BillboardDataEntry> _billboardsByMaterial = [];
    private EntityCollection? _entities;
    private long _lastBufferUpdateTicks;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private bool _needRebuilt = true;

    public IContext Context { get; } = context;
    public World World { get; } = world;

    public override string Name { get; } = nameof(BillboardData);

    public IReadOnlyDictionary<MaterialTypeId, BillboardDataEntry> Data => _billboardsByMaterial;

    public BufferHandle Buffer => BufferHandle.Null;

    public ulong GpuAddress => 0;

    public uint Stride => 0;

    public uint Count { private set; get; } = 0;

    public uint TotalBillboardCount { private set; get; } = 0;

    protected override ResultCode OnInitializing()
    {
        _needRebuilt = true;
        _entities = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<BillboardComponent>()
            .Has<WorldTransform>()
            .Build();
        _entities.EntityChanged += OnEntityChanged;
        _entities.EntityAdded += OnAddOrRemovedChanged;
        _entities.EntityRemoved += OnAddOrRemovedChanged;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        foreach (var entry in _billboardsByMaterial.Values)
        {
            entry.Dispose();
        }
        _billboardsByMaterial.Clear();
        Disposer.DisposeAndRemove(ref _entities);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks && !_needRebuilt)
        {
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        Context.WaitAll(false);
        Rebuild();
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return true;
    }

    /// <summary>
    /// Iterates over all billboard entities, groups them by material type,
    /// and records per-entity dispatch information.
    /// Also updates GPU buffers for each entity's BillboardGeometry.
    /// </summary>
    private void Rebuild()
    {
        if (_entities is null)
        {
            return;
        }
        using var t = _tracer.BeginScope(nameof(Rebuild));
        foreach (var entry in _billboardsByMaterial.Values)
        {
            entry.Clear();
        }
        TotalBillboardCount = 0;
        Count = 0;

        foreach (var entity in _entities)
        {
            ref var nodeInfo = ref entity.Get<NodeInfo>();
            if (!nodeInfo.Enabled)
                continue;
            ref var bb = ref entity.Get<BillboardComponent>();
            if (!bb.Valid)
                continue;

            // Update GPU buffers for this entity's BillboardGeometry
            if (bb.BillboardGeometry!.BufferDirty)
            {
                bb.BillboardGeometry.UpdateBuffers(Context);
            }

            TotalBillboardCount += (uint)bb.BillboardCount;
            if (
                bb.BillboardMaterialName is not null
                && BillboardMaterialRegistry.TryGetTypeId(
                    bb.BillboardMaterialName,
                    out var materialId
                )
            )
            {
                bb.BillboardMaterialId = materialId;
            }
            if (
                !_billboardsByMaterial.TryGetValue(bb.BillboardMaterialId, out var entry)
                || entry.IsDisposed
            )
            {
                entry = new BillboardDataEntry(Context, bb.BillboardCount, bb.BillboardMaterialId);
                _billboardsByMaterial[bb.BillboardMaterialId] = entry;
            }
            entry.AddEntity(entity);
            ++Count;
        }

        foreach (var entry in _billboardsByMaterial.Values)
        {
            if (entry.BillboardCount == 0)
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
