using System.Runtime.CompilerServices;
using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal abstract class DrawStreamBase<DRAW_TYPE, COMP_TYPE> : Initializable, IDrawStream<DRAW_TYPE>
    where DRAW_TYPE : unmanaged
{
    private readonly ILogger _logger;
    private readonly ITracer _tracer;
    private readonly IContext _context;
    private readonly World _world;

    // GPU buffer (ring buffer for multi-frame-in-flight)
    private RingElementBuffer<DRAW_TYPE>? _buffer;
    private readonly HashSet<Entity> _entities = [];
    private readonly int _initialCapacity;

    // CPU-side draw storage grouped by material type for sorted upload
    private readonly Dictionary<MaterialTypeId, FastList<DRAW_TYPE>> _drawsByMaterial = [];
    private readonly Dictionary<MaterialTypeId, DrawRange> _materialRanges = [];

    private readonly FastList<MaterialTypeId> _materialTypes = [];

    // ECS references
    protected Components<COMP_TYPE> _components;
    protected Components<Renderable> _renderables;

    // Change tracking
    private readonly HashSet<int> _pendingUpdates = [];
    private long _lastDataChangeTicks = Stopwatch.GetTimestamp();
    private long _lastBufferUploadTicks = 0;
    private bool _needsRebuild = true;

    #region IDrawStream Properties

    public DrawStreamName StreamName { get; }
    public DrawStreamType StreamType { get; }
    public DrawStreamVariants Variants { get; }
    public bool IsInstancing { get; }
    public IndexBufferStrategy IndexBufferStrategy { get; }
    #endregion

    #region IRenderData Properties

    public BufferHandle Buffer => _buffer?.Buffer ?? BufferHandle.Null;
    public ulong GpuAddress => _buffer?.GpuAddress ?? 0;
    public abstract uint Stride { get; }
    public uint Count { get; private set; }
    public override string Name { get; }

    #endregion

    public DrawStreamBase(
        IContext context,
        World world,
        DrawStreamType type,
        DrawStreamName name,
        ILogger logger,
        int initialCapacity = 0
    )
    {
        _logger = logger;
        _context = context;
        _world = world;
        StreamType = type;
        StreamName = name;
        Variants = name.GetVariants();
        _initialCapacity = initialCapacity;
        _tracer = TracerFactory.GetTracer($"{StreamType}_{name}");
        IsInstancing = Variants.HasAllFlags(DrawStreamVariants.Instancing);
        Name = name.ToString();
        IndexBufferStrategy = Variants.HasAllFlags(DrawStreamVariants.Dynamic)
            ? IndexBufferStrategy.PerDraw
            : IndexBufferStrategy.Shared;
        _components = world.GetComponents<COMP_TYPE>();
        _renderables = world.GetComponents<Renderable>();
    }

    #region IDrawStream Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<MaterialTypeId> GetMaterialTypes() => _materialTypes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<MaterialTypeId> GetMaterialTypesCore() =>
        _materialTypes.GetInternalArray().AsSpan(0, _materialTypes.Count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DrawRange GetRangeByMaterial(MaterialTypeId materialType)
    {
        return _materialRanges.TryGetValue(materialType, out var range) ? range : DrawRange.Zero;
    }

    public bool TryGetDraw(int drawIndex, out DRAW_TYPE DRAW_TYPE)
    {
        if (drawIndex < 0 || drawIndex >= (int)Count)
        {
            DRAW_TYPE = default;
            return false;
        }
        // Walk material ranges to find which list contains this index
        foreach (var (matId, range) in _materialRanges.AsValueEnumerable())
        {
            if (drawIndex >= (int)range.Start && drawIndex < (int)range.End)
            {
                var list = _drawsByMaterial[matId];
                DRAW_TYPE = list[drawIndex - (int)range.Start];
                return true;
            }
        }
        DRAW_TYPE = default;
        return false;
    }

    public (DRAW_TYPE Draw, int SlotIndex) GetDraw(Entity entity)
    {
        if (!_entities.Contains(entity) || !entity.Has<Renderable>())
        {
            return (default, -1);
        }
        ref var renderable = ref _renderables[entity];
        if (
            renderable.DrawCmdIndex < 0
            || renderable.DrawVariants != (uint)Variants
            || renderable.DrawType != (uint)StreamType
        )
        {
            return (default, -1);
        }
        var drawIndex = renderable.DrawCmdIndex;
        if (TryGetDraw(drawIndex, out var draw))
        {
            return (draw, drawIndex);
        }
        return (default, -1);
    }

    public void Barrier(ICommandBuffer cmdBuf)
    {
        if (Count > 0 && _buffer is not null)
        {
            cmdBuf.Barrier(_buffer.Buffer);
        }
    }

    #endregion

    #region Lifecycle

    protected override ResultCode OnInitializing()
    {
        _logger.LogInformation("Initializing.");
        _buffer = new RingElementBuffer<DRAW_TYPE>(
            _context,
            (int)GraphicsSettings.MaxFrameInFlight,
            _initialCapacity,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            hostVisible: true,
            debugName: $"{StreamType}_{StreamName}"
        );

        _needsRebuild = true;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _logger.LogInformation("Tearing down.");
        Disposer.DisposeAndRemove(ref _buffer);
        _drawsByMaterial.Clear();
        _materialRanges.Clear();
        _pendingUpdates.Clear();
        _materialTypes.Clear();
        Count = 0;
        return ResultCode.Ok;
    }

    #endregion

    #region Update

    public bool Update()
    {
        if (_lastDataChangeTicks <= _lastBufferUploadTicks && !_needsRebuild)
        {
            return true;
        }

        if (_needsRebuild)
        {
            _pendingUpdates.Clear();
            return Rebuild();
        }

        return ApplyIncrementalUpdates();
    }

    /// <summary>
    /// Full rebuild: re-collects all entities matching this stream, sorts by material,
    /// uploads to GPU. O(N) where N = entity count in this stream.
    /// </summary>
    private bool Rebuild()
    {
        using var t = _tracer.BeginScope($"Rebuild");

        // Clear previous state
        foreach (var list in _drawsByMaterial.Values.AsValueEnumerable())
            list.Clear();
        _materialRanges.Clear();
        _materialTypes.Clear();
        Count = 0;

        if (_entities is null || _entities.Count == 0)
        {
            _needsRebuild = false;
            _lastBufferUploadTicks = _lastDataChangeTicks;
            return true;
        }

        // Collect draws for entities that match this stream's post-filters
        foreach (var entity in _entities.AsValueEnumerable())
        {
            if (!entity.Valid)
            {
                continue;
            }
            ref var lineComp = ref _components[entity];
            if (!IsValid(ref lineComp))
                continue;

            var draw = CreateDrawInfo(entity);
            if (GetEntityId(ref draw) == 0)
                continue;

            var matType = GetMaterialType(ref draw);
            if (!_drawsByMaterial.TryGetValue(matType, out var list))
            {
                list = new FastList<DRAW_TYPE>(_initialCapacity);
                _drawsByMaterial[matType] = list;
            }
            list.Add(draw);
            Count++;
        }

        foreach (var kvp in _drawsByMaterial.AsValueEnumerable())
        {
            if (kvp.Value.Count > 0)
                _materialTypes.Add(kvp.Key);
        }

        // Sort each material group by MeshId for better GPU cache coherence
        SortByMeshId();

        // Compute material ranges and write DrawCmdIndex/DrawCategory back to entities
        ComputeRangesAndWriteBack();

        // Upload to GPU
        UploadToGpu();

        _needsRebuild = false;
        _lastBufferUploadTicks = _lastDataChangeTicks;
        return true;
    }

    /// <summary>
    /// Incremental update: only re-uploads draws whose entity data changed (e.g., transform update
    /// causing NodeInfoIndex change). Does NOT handle structural changes (add/remove/material change).
    /// </summary>
    private bool ApplyIncrementalUpdates()
    {
        if (_pendingUpdates.Count == 0)
        {
            _lastBufferUploadTicks = _lastDataChangeTicks;
            return true;
        }

        using var t = _tracer.BeginScope("IncrementalUpdate");

        foreach (var entityId in _pendingUpdates.AsValueEnumerable())
        {
            var entity = _world.GetEntity(entityId);
            ref var renderable = ref _renderables[entity];

            // Only process if this entity belongs to this stream
            if (
                renderable.DrawType != (int)StreamType
                || renderable.DrawVariants != (uint)Variants
                || renderable.DrawCmdIndex < 0
            )
                continue;

            var newDraw = CreateDrawInfo(entity);
            var drawIndex = renderable.DrawCmdIndex;
            var newMatType = GetMaterialType(ref newDraw);
            // Find the material list and local index
            if (_materialRanges.TryGetValue(newMatType, out var range))
            {
                var localIndex = drawIndex - (int)range.Start;
                var list = _drawsByMaterial[newMatType];
                if (localIndex >= 0 && localIndex < list.Count)
                {
                    list[localIndex] = newDraw;
                    _buffer!.WriteElement(newDraw, drawIndex);
                }
            }
        }

        _pendingUpdates.Clear();
        _lastBufferUploadTicks = _lastDataChangeTicks;
        return true;
    }

    #endregion

    #region GPU Upload

    /// <summary>
    /// Sorts each material group by MeshId for better GPU cache coherence during rendering.
    /// Uses parallel sort for large lists.
    /// </summary>
    private void SortByMeshId()
    {
        Parallel.ForEach(
            _drawsByMaterial,
            kv =>
            {
                if (kv.Value.Count <= 1)
                    return;
                var arr = kv.Value.GetInternalArray();
                Array.Sort(
                    arr,
                    0,
                    kv.Value.Count,
                    Comparer<MeshDraw>.Create((a, b) => a.MeshId.CompareTo(b.MeshId))
                );
            }
        );
    }

    /// <summary>
    /// Computes contiguous material ranges and writes DrawCmdIndex/DrawCategory
    /// back to each entity's <see cref="Renderable"/> component for O(1) lookup.
    /// </summary>
    private void ComputeRangesAndWriteBack()
    {
        var offset = 0u;
        foreach (var (matId, list) in _drawsByMaterial.AsValueEnumerable())
        {
            if (list.Count == 0)
                continue;

            var range = new DrawRange(offset, (uint)list.Count);
            _materialRanges[matId] = range;

            // Write back DrawCmdIndex and DrawCategory to each entity's Renderable
            for (var i = 0; i < list.Count; i++)
            {
                var entityId = (int)GetEntityId(ref list.At(i));
                var entity = _world.GetEntity(entityId);
                ref var renderable = ref _renderables[entity];
                renderable.DrawCmdIndex = (int)(offset + (uint)i);
            }

            offset += (uint)list.Count;
        }
    }

    /// <summary>
    /// Advances the ring buffer and uploads all draw commands contiguously, sorted by material.
    /// </summary>
    private void UploadToGpu()
    {
        if (_buffer is null || Count == 0)
            return;

        _buffer.Advance();
        _buffer.EnsureCapacity((int)Count);

        var byteOffset = 0;
        foreach (var (_, list) in _drawsByMaterial)
        {
            if (list.Count > 0)
            {
                _buffer.Upload(list, byteOffset);
                byteOffset += list.Count * (int)MeshDraw.SizeInBytes;
            }
        }
    }

    #endregion

    #region MeshDraw Construction

    protected abstract DRAW_TYPE CreateDrawInfo(Entity entity);

    #endregion

    #region ECS Event Handlers

    public void EntityAdded(Entity entity)
    {
        var (type, variants) = GetVariantsFromEntity(entity);
        Debug.Assert(variants.HasAllFlags(Variants));
        _entities.Add(entity);
        _renderables[entity].DrawVariants = (uint)Variants; // Pre-set category for O(1) checks during updates
        _renderables[entity].DrawType = (int)StreamType;
        MarkRebuildNeeded();
    }

    public void EntityRemoved(Entity entity)
    {
        Debug.Assert(_entities.Contains(entity));
        _entities.Remove(entity);
        _renderables[entity].DrawVariants = 0; // Clear category to indicate it's no longer in any stream
        _renderables[entity].DrawType = (int)DrawStreamType.None;
        MarkRebuildNeeded();
    }

    private (DrawStreamType, DrawStreamVariants) GetVariantsFromEntity(Entity entity)
    {
        ref var comp = ref _components[entity];
        var category = GetVariant(ref comp);
        var type = DrawStreamType.Opaque;
        if (entity.Has<TransparentComponent>())
        {
            type = DrawStreamType.Transparent;
        }
        else if (entity.Has<AlphaMaskComponent>())
        {
            type = DrawStreamType.AlphaMask;
        }
        return (type, category);
    }

    public void EntityChanged(Entity entity, in EntityChangedEvent e)
    {
        if (_needsRebuild || !_entities.Contains(entity))
            return;
        ref var meshComp = ref _components[entity];

        ref var renderable = ref _renderables[entity];

        // If material or category changed, we need a full rebuild (structural change)
        if (renderable.DrawVariants != (uint)Variants || renderable.DrawType != (int)StreamType)
        {
            MarkRebuildNeeded();
            return;
        }

        // Otherwise, queue an incremental update (e.g., transform changed → NodeInfoIndex)
        _pendingUpdates.Add(e.EntityId);
        _lastDataChangeTicks = Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkRebuildNeeded()
    {
        _needsRebuild = true;
        _lastDataChangeTicks = Stopwatch.GetTimestamp();
    }

    #endregion

    public bool Has(Entity entity)
    {
        return _entities.Contains(entity);
    }

    protected abstract bool IsValid(ref COMP_TYPE comp);

    protected abstract uint GetMaterialType(ref DRAW_TYPE draw);

    protected abstract uint GetMeshId(ref DRAW_TYPE draw);

    protected abstract uint GetEntityId(ref DRAW_TYPE draw);

    protected abstract DrawStreamVariants GetVariant(ref COMP_TYPE comp);
}
