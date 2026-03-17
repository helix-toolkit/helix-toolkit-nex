using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using DrawRange = HelixToolkit.Nex.Rendering.DrawRange;

namespace HelixToolkit.Nex.Engine.Data;

internal class MeshDrawData : Initializable, IMeshDrawData
{
    public const int InitialBufferSize = 64;

    internal sealed class MeshDrawSorting
    {
        public readonly Dictionary<MaterialTypeId, FastList<MeshDraw>> MeshDrawByMaterialType = new(
            InitialBufferSize
        );

        public readonly Dictionary<MaterialTypeId, DrawRange> MeshDrawRanges = new(
            InitialBufferSize
        );

        public DrawRange FullRange { private set; get; } = DrawRange.Zero;

        public void Add(ref MeshDraw meshDraw)
        {
            var materialType = meshDraw.MaterialType;
            if (!MeshDrawByMaterialType.TryGetValue(materialType, out var list))
            {
                list = new FastList<MeshDraw>(InitialBufferSize);
                MeshDrawByMaterialType[materialType] = list;
            }
            list.Add(meshDraw);
        }

        public void AddRange(MaterialTypeId materialType, in DrawRange range)
        {
            MeshDrawRanges[materialType] = range;
            if (FullRange.Empty)
            {
                FullRange = range;
            }
            else
            {
                var end = Math.Max(FullRange.End, range.End);
                FullRange = new DrawRange(FullRange.Start, end - FullRange.Start);
            }
        }

        public DrawRange GetRange(MaterialTypeId materialType)
        {
            if (MeshDrawRanges.TryGetValue(materialType, out var range))
            {
                return range;
            }
            return DrawRange.Zero;
        }

        public void Clear()
        {
            MeshDrawByMaterialType.Clear();
            MeshDrawRanges.Clear();
            FullRange = DrawRange.Zero;
        }

        public void Sort()
        {
            foreach (var kv in MeshDrawByMaterialType)
            {
                var list = kv.Value.GetInternalArray();
                Array.Sort(
                    list,
                    0,
                    kv.Value.Count,
                    Comparer<MeshDraw>.Create(
                        (a, b) =>
                        {
                            return a.MeshId.CompareTo(b.MeshId);
                        }
                    )
                );
            }
        }
    }

    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(MeshDrawData));
    private static readonly ILogger _logger = LogManager.Create<MeshDrawData>();

    private ElementBuffer<MeshDraw>? _buffer = null;
    private readonly FastList<MeshDraw> _meshDraws = new(InitialBufferSize);
    private readonly MeshDrawSorting _meshDrawSortingStatic = new();
    private readonly MeshDrawSorting _meshDrawSortingStaticInstancing = new();
    private readonly MeshDrawSorting _meshDrawSortingDynamic = new();
    private readonly MeshDrawSorting _meshDrawSortingDynamicInstancing = new();
    private readonly HashSet<MaterialTypeId> _materialTypes = new(InitialBufferSize);
    private readonly Dictionary<uint, int> _entityToDrawIdx = new(1024 * 10);
    private readonly bool _isTransparent;
    private EntityCollection? _entities;
    private readonly HashSet<int> _updatedEntities = [];
    private readonly FastList<int> _updatedIndices = [];

    public IContext Context { get; }
    public World World { get; }
    public IReadOnlyList<MeshDraw> DrawCommands => _meshDraws;
    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private bool _needRebuilt = true;
    public IEnumerable<MaterialTypeId> MaterialTypes => _materialTypes;

    public BufferHandle Buffer => _buffer is not null ? _buffer.Buffer : BufferHandle.Null;
    public ulong GpuAddress => _buffer is null ? 0 : _buffer.Buffer.GpuAddress;
    public uint Stride => MeshDraw.SizeInBytes;

    public uint Count => _buffer is not null ? (uint)_buffer.Count : 0;

    public override string Name { get; }

    public bool HasDynamicMesh => _meshDrawSortingDynamic.MeshDrawRanges.Count > 0;

    public bool HasDynamicInstancingMesh =>
        _meshDrawSortingDynamicInstancing.MeshDrawRanges.Count > 0;

    public bool HasStaticMesh => _meshDrawSortingStatic.MeshDrawRanges.Count > 0;

    public bool HasStaticInstancingMesh =>
        _meshDrawSortingStaticInstancing.MeshDrawRanges.Count > 0;

    public DrawRange RangeStaticMesh => _meshDrawSortingStatic.FullRange;

    public DrawRange RangeStaticMeshInstancing => _meshDrawSortingStaticInstancing.FullRange;

    public DrawRange RangeDynamicMesh => _meshDrawSortingDynamic.FullRange;

    public DrawRange RangeDynamicMeshInstancing => _meshDrawSortingDynamicInstancing.FullRange;

    public MeshDrawData(IContext context, World world, bool isTransparent)
    {
        Context = context;
        _isTransparent = isTransparent;
        World = world;
        Name = $"{nameof(MeshDrawData)}_{(isTransparent ? "Transparent" : "Opaque")}_{World.Id}";
    }

    public DrawRange GetRangeDynamicMesh(MaterialTypeId materialType)
    {
        return _meshDrawSortingDynamic.GetRange(materialType);
    }

    public DrawRange GetRangeDynamicMeshInstancing(MaterialTypeId materialType)
    {
        return _meshDrawSortingDynamicInstancing.GetRange(materialType);
    }

    public DrawRange GetRangeStaticMesh(MaterialTypeId materialType)
    {
        return _meshDrawSortingStatic.GetRange(materialType);
    }

    public DrawRange GetRangeStaticMeshInstancing(MaterialTypeId materialType)
    {
        return _meshDrawSortingStaticInstancing.GetRange(materialType);
    }

    protected override ResultCode OnInitializing()
    {
        _needRebuilt = true;
        var filter = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<MeshComponent>()
            .Has<WorldTransform>();
        if (_isTransparent)
        {
            filter.Has<TransparentComponent>();
        }
        else
        {
            filter.NotHas<TransparentComponent>();
        }
        _entities = filter.Build();
        _entities.EntityChanged += OnEntityChanged;
        _entities.EntityAdded += OnAddOrRemovedChanged;
        _entities.EntityRemoved += OnAddOrRemovedChanged;
        _buffer = new ElementBuffer<MeshDraw>(
            Context,
            InitialBufferSize,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            true,
            Name
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _entities);
        Disposer.DisposeAndRemove(ref _buffer);
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (_buffer == null)
        {
            return false;
        }
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks && !_needRebuilt)
        {
            return true;
        }

        using var t = _tracer.BeginScope(nameof(Update));
        var success = true;
        if (_needRebuilt)
        {
            _updatedEntities.Clear();
            success = Rebuild();
        }
        else
        {
            success = UpdateChanges();
        }
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return success;
    }

    private bool Rebuild()
    {
        if (_entities is null)
        {
            return false;
        }
        _logger.LogTrace(
            "{NAME}: Rebuilding MeshDrawData with entity count: {COUNT}",
            Name,
            _entities.Count
        );
        _updatedEntities.Clear();
        _meshDraws.Clear();
        _meshDrawSortingStatic.Clear();
        _meshDrawSortingStaticInstancing.Clear();
        _meshDrawSortingDynamic.Clear();
        _meshDrawSortingDynamicInstancing.Clear();
        var count = 0;
        foreach (var entity in _entities)
        {
            Debug.Assert(entity.Id != 0);
            ref var nodeInfo = ref entity.Get<NodeInfo>();
            if (!nodeInfo.Enabled)
            {
                continue;
            }
            ref var meshRenderComp = ref entity.Get<MeshComponent>();
            if (!meshRenderComp.Valid)
            {
                continue;
            }
            ref var transform = ref entity.Get<WorldTransform>();
            var materialType = meshRenderComp.MaterialProperties!.MaterialTypeId;
            meshRenderComp.Instancing?.UpdateBuffer(Context);
            var meshDraw = new MeshDraw()
            {
                MeshId = meshRenderComp.Geometry!.Id,
                MaterialId = meshRenderComp.MaterialProperties!.Index,
                MaterialType = materialType,
                EntityId = meshRenderComp.Hitable ? (uint)entity.Id : 0u,
                EntityVer = meshRenderComp.Hitable ? entity.Gen : 0u,
                Transform = transform.Value,
                InstancingBufferAddress = meshRenderComp.Instancing is not null
                    ? meshRenderComp.Instancing.Buffer!.Buffer.GpuAddress
                    : 0,
                InstancingIndexBufferAddress =
                    meshRenderComp.Cullable && meshRenderComp.Instancing is not null
                        ? meshRenderComp.Instancing.CulledIndicesBuffer!.Buffer.GpuAddress
                        : 0,
                FirstIndex = meshRenderComp.Geometry!.IndexOffset,
                IndexCount = meshRenderComp.Geometry!.IndexCount,
                InstanceCount = meshRenderComp.Instancing is not null
                    ? (uint)meshRenderComp.Instancing.Transforms.Count
                    : 1u,
                Cullable = meshRenderComp.Cullable ? 1u : 0u,
                DrawType = meshRenderComp.GetDrawType(),
            };
            _materialTypes.Add(materialType);
            if (meshRenderComp.Instancing is not null)
            {
                if (meshRenderComp.Geometry.IsDynamic)
                {
                    _meshDrawSortingDynamicInstancing.Add(ref meshDraw);
                }
                else
                {
                    _meshDrawSortingStaticInstancing.Add(ref meshDraw);
                }
            }
            else
            {
                if (meshRenderComp.Geometry.IsDynamic)
                {
                    _meshDrawSortingDynamic.Add(ref meshDraw);
                }
                else
                {
                    _meshDrawSortingStatic.Add(ref meshDraw);
                }
            }
            ++count;
        }
        FinalizeMeshDraws();
        _buffer?.Upload(_meshDraws);
        _needRebuilt = false;
        return true;
    }

    private void FinalizeMeshDraws()
    {
        _entityToDrawIdx.Clear();
        DrawRange range = default;
        foreach (var kv in _meshDrawSortingStatic.MeshDrawByMaterialType)
        {
            _meshDrawSortingStatic.Sort();
            range = new DrawRange(range.End, (uint)kv.Value.Count);
            _meshDrawSortingStatic.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }
        range = new DrawRange(0, range.End);
        var start = range.End;

        foreach (var kv in _meshDrawSortingStaticInstancing.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new DrawRange(range.End, (uint)kv.Value.Count);
            _meshDrawSortingStaticInstancing.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }

        range = new DrawRange(start, range.End);
        start = range.End;

        foreach (var kv in _meshDrawSortingDynamic.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new DrawRange(range.End, (uint)kv.Value.Count);
            _meshDrawSortingDynamic.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }

        range = new DrawRange(start, range.End - start);
        start = range.End;
        foreach (var kv in _meshDrawSortingDynamicInstancing.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new DrawRange(range.End, (uint)kv.Value.Count);
            _meshDrawSortingDynamicInstancing.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }

        for (int i = 0; i < _meshDraws.Count; ++i)
        {
            ref var draw = ref _meshDraws.At(i);
            var entity = World.GetEntity((int)draw.EntityId, (ushort)draw.EntityVer);
            ref var comp = ref entity.Get<MeshComponent>();
            comp.Index = i;
        }
    }

    private bool UpdateChanges()
    {
        _logger.LogTrace(
            "{NAME}: Updating MeshDrawData with changed entity count: {COUNT}",
            Name,
            _updatedEntities.Count
        );
        _updatedIndices.Clear();
        foreach (var entityId in _updatedEntities)
        {
            Debug.Assert(entityId != 0);
            var entity = World.GetEntity(entityId);
            ref var meshRenderComp = ref entity.Get<MeshComponent>();
            if (!meshRenderComp.Valid || meshRenderComp.Index < 0)
            {
                return false;
            }
            ref var transform = ref entity.Get<WorldTransform>();
            var materialType = meshRenderComp.MaterialProperties!.MaterialTypeId;
            _meshDraws.At(meshRenderComp.Index) = new MeshDraw()
            {
                MeshId = meshRenderComp.Geometry!.Id,
                MaterialId = meshRenderComp.MaterialProperties!.Index,
                MaterialType = materialType,
                EntityId = meshRenderComp.Hitable ? (uint)entity.Id : 0u,
                EntityVer = meshRenderComp.Hitable ? entity.Gen : 0u,
                Transform = transform.Value,
                InstancingBufferAddress = meshRenderComp.Instancing is not null
                    ? meshRenderComp.Instancing.Buffer!.Buffer.GpuAddress
                    : 0,
                InstancingIndexBufferAddress =
                    meshRenderComp.Cullable && meshRenderComp.Instancing is not null
                        ? meshRenderComp.Instancing.CulledIndicesBuffer!.Buffer.GpuAddress
                        : 0,
                FirstIndex = meshRenderComp.Geometry!.IndexOffset,
                IndexCount = meshRenderComp.Geometry!.IndexCount,
                InstanceCount = meshRenderComp.Instancing is not null
                    ? (uint)meshRenderComp.Instancing.Transforms.Count
                    : 1u,
                Cullable = meshRenderComp.Cullable ? 1u : 0u,
                DrawType = meshRenderComp.GetDrawType(),
            };
            _updatedIndices.Add(meshRenderComp.Index);
        }
        _updatedEntities.Clear();
        _buffer?.WriteDynamic(
            _meshDraws.Count,
            ctx =>
            {
                foreach (var idx in _updatedIndices)
                {
                    ref var draw = ref _meshDraws.At(idx);
                    ctx.WriteElement(ref draw, idx);
                }
            }
        );
        return true;
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        if (!_needRebuilt)
        {
            var entity = World.GetEntity(e.EntityId);
            if (e.Type == World.GetComponentTypeId<WorldTransform>())
            {
                _updatedEntities.Add(e.EntityId);
                return;
            }
            else if (e.Type == World.GetComponentTypeId<MeshComponent>())
            {
                ref var comp = ref entity.Get<MeshComponent>();
                if (CanUpdate(ref comp))
                {
                    _updatedEntities.Add(e.EntityId);
                }
                else
                {
                    comp.Index = -1;
                    _needRebuilt = true;
                }
                return;
            }
            else if (e.Type == World.GetComponentTypeId<NodeInfo>())
            {
                _needRebuilt = true;
            }
        }
    }

    private bool CanUpdate(ref MeshComponent comp)
    {
        if (_meshDraws.Count <= comp.Index)
        {
            return false;
        }
        if (!comp.Valid || comp.Index < 0)
        {
            return false;
        }
        ref var draw = ref _meshDraws.At(comp.Index);
        if (
            comp.MaterialProperties is null
            || draw.MaterialType != comp.MaterialProperties.MaterialTypeId
        )
        {
            return false;
        }
        var drawType = comp.GetDrawType();
        if (drawType != draw.DrawType)
        {
            return false;
        }
        return true;
    }

    private void OnAddOrRemovedChanged(object? sender, int e)
    {
        _needRebuilt = true;
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    }
}
