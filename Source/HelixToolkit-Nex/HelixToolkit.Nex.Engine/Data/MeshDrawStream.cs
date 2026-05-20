using System.Runtime.CompilerServices;
using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;
using DrawRange = HelixToolkit.Nex.Rendering.DrawRange;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// A single named draw stream that owns a GPU buffer of <see cref="MeshDraw"/> commands
/// sharing the same rendering characteristics. Draws are grouped by material type for
/// efficient batched rendering.
/// <para>
/// Uses <see cref="Renderable.DrawCmdIndex"/> and <see cref="Renderable.DrawCategory"/>
/// on each entity for O(1) entity-to-slot lookup without maintaining a separate dictionary.
/// </para>
/// <para>
/// Follows the same rebuild-on-structural-change, incremental-update-on-property-change
/// pattern as <see cref="MeshDrawData"/>, but scoped to a single stream category.
/// </para>
/// </summary>
internal sealed class MeshDrawStream : Initializable, IDrawStream
{
    private const int InitialCapacity = 32;

    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(MeshDrawStream));
    private static readonly ILogger _logger = LogManager.Create<MeshDrawStream>();

    private readonly IContext _context;
    private readonly World _world;
    private readonly bool _isTransparent;

    // GPU buffer (ring buffer for multi-frame-in-flight)
    private RingElementBuffer<MeshDraw>? _buffer;
    private readonly HashSet<Entity> _entities = [];

    // CPU-side draw storage grouped by material type for sorted upload
    private readonly Dictionary<MaterialTypeId, FastList<MeshDraw>> _drawsByMaterial = new(
        InitialCapacity
    );
    private readonly Dictionary<MaterialTypeId, DrawRange> _materialRanges = new(InitialCapacity);

    // ECS references
    private Components<MeshComponent> _meshComponents;
    private Components<Renderable> _renderables;

    // Change tracking
    private readonly HashSet<int> _pendingUpdates = [];
    private long _lastDataChangeTicks = Stopwatch.GetTimestamp();
    private long _lastBufferUploadTicks = 0;
    private bool _needsRebuild = true;

    #region IDrawStream Properties

    public DrawStreamName StreamName { get; }
    public DrawStreamCategory Categories { get; }
    public bool IsInstancing { get; }
    public IndexBufferStrategy IndexBufferStrategy { get; }
    public float FragmentationThreshold { get; set; } = 0.5f;
    public float Fragmentation => 0f; // No fragmentation — full rebuild on structural changes
    #endregion

    #region IRenderData Properties

    public BufferHandle Buffer => _buffer?.Buffer ?? BufferHandle.Null;
    public ulong GpuAddress => _buffer?.GpuAddress ?? 0;
    public uint Stride => MeshDraw.SizeInBytes;
    public uint Count { get; private set; }
    public override string Name { get; }

    #endregion

    public MeshDrawStream(IContext context, World world, DrawStreamName name)
    {
        _context = context;
        _world = world;
        StreamName = name;
        Categories = name.GetCategory();
        IsInstancing = Categories.HasAnyFlag(DrawStreamCategory.Instancing);
        IndexBufferStrategy = Categories.HasAnyFlag(DrawStreamCategory.Dynamic)
            ? IndexBufferStrategy.PerDraw
            : IndexBufferStrategy.Shared;
        _isTransparent = Categories.HasAnyFlag(DrawStreamCategory.Transparent);
        Name = name.ToString();

        _meshComponents = world.GetComponents<MeshComponent>();
        _renderables = world.GetComponents<Renderable>();
    }

    #region IDrawStream Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<MaterialTypeId> GetMaterialTypes() => _materialRanges.Keys;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DrawRange GetRangeByMaterial(MaterialTypeId materialType)
    {
        return _materialRanges.TryGetValue(materialType, out var range) ? range : DrawRange.Zero;
    }

    public bool TryGetMeshDraw(int drawIndex, out MeshDraw meshDraw)
    {
        if (drawIndex < 0 || drawIndex >= (int)Count)
        {
            meshDraw = default;
            return false;
        }
        // Walk material ranges to find which list contains this index
        foreach (var (matId, range) in _materialRanges.AsValueEnumerable())
        {
            if (drawIndex >= (int)range.Start && drawIndex < (int)range.End)
            {
                var list = _drawsByMaterial[matId];
                meshDraw = list[drawIndex - (int)range.Start];
                return true;
            }
        }
        meshDraw = default;
        return false;
    }

    public (MeshDraw Draw, int SlotIndex) GetMeshDraw(Entity entity)
    {
        if (!_entities.Contains(entity) || !entity.Has<Renderable>())
        {
            return (default, -1);
        }
        ref var renderable = ref _renderables[entity];
        if (renderable.DrawCmdIndex < 0 || renderable.DrawCategory != (uint)Categories)
        {
            return (default, -1);
        }
        var drawIndex = renderable.DrawCmdIndex;
        if (TryGetMeshDraw(drawIndex, out var draw))
        {
            Debug.Assert(draw.EntityId == entity.Id, "Entity Id in MeshDraw does not equal to the requested entity");
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
        _buffer = new RingElementBuffer<MeshDraw>(
            _context,
            (int)GraphicsSettings.MaxFrameInFlight,
            InitialCapacity,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            hostVisible: true,
            debugName: $"MeshStream_{StreamName}"
        );

        _needsRebuild = true;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _buffer);
        _drawsByMaterial.Clear();
        _materialRanges.Clear();
        _pendingUpdates.Clear();
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
        using var t = _tracer.BeginScope($"{Name}.Rebuild");

        // Clear previous state
        foreach (var list in _drawsByMaterial.Values.AsValueEnumerable())
            list.Clear();
        _materialRanges.Clear();
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
            ref var meshComp = ref _meshComponents[entity];
            if (!meshComp.Valid)
                continue;

            var meshDraw = CreateMeshDraw(entity);
            if (meshDraw.EntityId == 0)
                continue;

            var matType = meshDraw.MaterialType;
            if (!_drawsByMaterial.TryGetValue(matType, out var list))
            {
                list = new FastList<MeshDraw>(InitialCapacity);
                _drawsByMaterial[matType] = list;
            }
            list.Add(meshDraw);
            Count++;
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

        using var t = _tracer.BeginScope($"{Name}.IncrementalUpdate");

        foreach (var entityId in _pendingUpdates.AsValueEnumerable())
        {
            var entity = _world.GetEntity(entityId);
            ref var renderable = ref _renderables[entity];

            // Only process if this entity belongs to this stream
            if (renderable.DrawCategory != (uint)Categories || renderable.DrawCmdIndex < 0)
                continue;

            var newDraw = CreateMeshDraw(entity);
            var drawIndex = renderable.DrawCmdIndex;

            // Find the material list and local index
            if (_materialRanges.TryGetValue(newDraw.MaterialType, out var range))
            {
                var localIndex = drawIndex - (int)range.Start;
                var list = _drawsByMaterial[newDraw.MaterialType];
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
                var entityId = (int)list[i].EntityId;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MeshDraw CreateMeshDraw(Entity entity)
    {
        ref var meshComp = ref _meshComponents[entity];
        if (!meshComp.Valid)
            return default;

        ref var renderable = ref _renderables[entity];
        return new MeshDraw
        {
            MeshId = meshComp.Geometry!.Id,
            MaterialId = meshComp.MaterialProperties!.Index,
            MaterialType = meshComp.MaterialProperties!.MaterialTypeId,
            NodeInfoIndex = (uint)renderable.GPUIndex,
            EntityId = (uint)entity.Id,
            InstancingBufferAddress = meshComp.Instancing is not null
                ? meshComp.Instancing.Buffer!.Buffer.GpuAddress
                : 0,
            InstancingIndexBufferAddress =
                meshComp.Cullable && meshComp.Instancing is not null
                    ? meshComp.Instancing.CulledIndicesBuffer!.Buffer.GpuAddress
                    : 0,
            FirstIndex = meshComp.Geometry!.IndexOffset,
            IndexCount = meshComp.Geometry!.IndexCount,
            InstanceCount = meshComp.Instancing is not null
                ? (uint)meshComp.Instancing.Transforms.Count
                : 1u,
            Cullable = meshComp.Cullable ? 1u : 0u,
            DrawType = (uint)meshComp.Category,
        };
    }

    #endregion

    #region ECS Event Handlers

    public void EntityAdded(Entity entity)
    {
        Debug.Assert(GetCategoryFromEntity(entity).HasAnyFlag(Categories));
        _entities.Add(entity);
        _renderables[entity].DrawCategory = (uint)Categories; // Pre-set category for O(1) checks during updates
        MarkRebuildNeeded();
    }

    public void EntityRemoved(Entity entity)
    {
        Debug.Assert(_entities.Contains(entity));
        _entities.Remove(entity);
        _renderables[entity].DrawCategory = 0; // Clear category to indicate it's no longer in any stream
        MarkRebuildNeeded();
    }

    private DrawStreamCategory GetCategoryFromEntity(Entity entity)
    {
        ref var comp = ref _meshComponents[entity];
        var category = comp.Category;
        if (entity.Has<TransparentComponent>())
        {
            category |= DrawStreamCategory.Transparent;
        }
        else
        {
            category |= DrawStreamCategory.Opaque;
        }
        return category;
    }

    public void EntityChanged(Entity entity, in EntityChangedEvent e)
    {
        if (_needsRebuild || !_entities.Contains(entity))
            return;
        ref var meshComp = ref _meshComponents[entity];

        // If the entity doesn't match this stream, ignore
        if (!meshComp.Valid)
            return;

        ref var renderable = ref _renderables[entity];

        // If material or category changed, we need a full rebuild (structural change)
        if (renderable.DrawCategory != (uint)Categories)
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
}
