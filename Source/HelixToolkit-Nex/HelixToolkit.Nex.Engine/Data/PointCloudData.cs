using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects all <see cref="PointCloudComponent"/> entities from the ECS world each frame,
/// packs their <see cref="PointData"/> into a contiguous GPU buffer, and exposes per-entity
/// dispatch information so the <c>PointRenderNode</c> compute shader can frustum-cull
/// and stamp the correct entity ID on each point.
/// </summary>
internal sealed class PointCloudData : Initializable, IPointCloudData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(PointCloudData));
    private static readonly ILogger _logger = LogManager.Create<PointCloudData>();

    /// <summary>
    /// Ring buffer that maintains one <see cref="ElementBuffer{T}"/> per in-flight
    /// frame so the GPU can safely read from the previous frame's buffer while
    /// the CPU writes into the current one — zero stalls.
    /// </summary>
    private ElementBuffer<PointData>? _ringBuffer;
    private EntityCollection? _entities;
    private readonly FastList<PointCloudDispatch> _dispatches = new(16);
    private long _lastBufferUpdateTicks;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private bool _needRebuilt = true;
    private uint _totalPointCount;

    public IContext Context { get; }
    public World World { get; }

    public override string Name { get; } = nameof(PointCloudData);

    // --- IRenderData ---
    public BufferHandle Buffer => _ringBuffer is not null ? _ringBuffer.Buffer : BufferHandle.Null;
    public ulong GpuAddress => _ringBuffer is null ? 0 : _ringBuffer.Buffer.GpuAddress;
    public uint Stride => PointData.SizeInBytes;
    public uint Count => _ringBuffer is not null ? (uint)_ringBuffer.Count : 0;

    // --- IPointCloudData ---
    public uint TotalPointCount => _totalPointCount;
    public FastList<PointCloudDispatch> Dispatches => _dispatches;

    public PointCloudData(IContext context, World world)
    {
        Context = context;
        World = world;
    }

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

        // Use a ring of buffers (one per swapchain image) to avoid GPU/CPU contention.
        // While the GPU reads from frame N-1 the CPU writes into frame N.
        int ringSize = Math.Max((int)Context.GetNumSwapchainImages(), 2);
        _ringBuffer = new ElementBuffer<PointData>(
            Context,
            capacity: 1024,
            BufferUsageBits.Storage,
            debugName: Name,
            isDynamic: true
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _entities);
        Disposer.DisposeAndRemove(ref _ringBuffer);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (_ringBuffer is null)
        {
            return false;
        }
        // Rotate to the next buffer slot so the GPU can keep reading the
        // previous frame's data while we overwrite this one.

        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks && !_needRebuilt)
        {
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        //_ringBuffer.Advance();
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
        if (_entities is null || _ringBuffer is null)
        {
            return;
        }

        _dispatches.Clear();
        _totalPointCount = 0;

        // First pass: count total points to ensure capacity
        uint totalPoints = 0;
        foreach (var entity in _entities)
        {
            ref var nodeInfo = ref entity.Get<NodeInfo>();
            if (!nodeInfo.Enabled)
                continue;
            ref var pc = ref entity.Get<PointCloudComponent>();
            if (!pc.Valid)
                continue;
            totalPoints += (uint)pc.PointCount;
        }

        if (totalPoints == 0)
        {
            _totalPointCount = 0;
            _needRebuilt = false;
            return;
        }

        _ringBuffer.EnsureCapacity((int)totalPoints);

        // Second pass: write points and record dispatches into the current ring slot
        uint offset = 0;
        _ringBuffer.WriteDynamic(
            (int)totalPoints,
            ctx =>
            {
                foreach (var entity in _entities)
                {
                    ref var nodeInfo = ref entity.Get<NodeInfo>();
                    if (!nodeInfo.Enabled)
                        continue;
                    ref var pc = ref entity.Get<PointCloudComponent>();
                    if (!pc.Valid)
                        continue;

                    var count = (uint)pc.PointCount;

                    // If not using per-point entity, stamp the entity ID on each point
                    // The compute shader handles this via push constants; we just
                    // write the raw points. If UsePerPointEntity is true, the points
                    // already have their own entityId/entityVer.
                    ctx.Write(
                        new ReadOnlySpan<PointData>(pc.Points!.GetInternalArray(), 0, pc.PointCount)
                    );

                    _dispatches.Add(
                        new PointCloudDispatch(
                            BufferOffset: offset,
                            PointCount: count,
                            EntityId: pc.Hitable ? (uint)entity.Id : 0u,
                            EntityVer: pc.Hitable ? entity.Gen : 0u,
                            TextureIndex: pc.TextureIndex,
                            SamplerIndex: pc.SamplerIndex,
                            FixedSize: pc.FixedSize ? 1u : 0u
                        )
                    );

                    // Update the component's tracking fields
                    pc.Index = _dispatches.Count - 1;
                    pc.BufferOffset = offset;

                    offset += count;
                }
            }
        );

        _totalPointCount = offset;
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
