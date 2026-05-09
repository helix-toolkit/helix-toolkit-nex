using HelixToolkit.Nex.ECS.Utils;
using HelixToolkit.Nex.Rendering.Components;
using DrawRange = HelixToolkit.Nex.Rendering.DrawRange;

namespace HelixToolkit.Nex.Engine.Data;

internal class MeshDrawData : Initializable, IMeshDrawData
{
    public const int InitialBufferSize = 16;

    internal sealed class MeshDrawSorting(IContext context, string? debugName) : IDisposable
    {
        public readonly Dictionary<MaterialTypeId, FastList<MeshDraw>> MeshDrawByMaterialType = new(
            InitialBufferSize
        );

        public readonly Dictionary<MaterialTypeId, DrawRange> MeshDrawRanges = new(
            InitialBufferSize
        );
        private bool _disposedValue;

        public int TotalCount { get; private set; }

        public BufferHandle Buffer => _buffer.Buffer;

        private RingElementBuffer<MeshDraw> _buffer = new(
            context,
            (int)RenderSettings.MaxFrameInFlight,
            InitialBufferSize,
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            hostVisible: true,
            debugName
        );

        public int Add(ref MeshDraw meshDraw)
        {
            var materialType = meshDraw.MaterialType;
            if (!MeshDrawByMaterialType.TryGetValue(materialType, out var list))
            {
                list = new FastList<MeshDraw>(InitialBufferSize);
                MeshDrawByMaterialType[materialType] = list;
            }
            list.Add(meshDraw);
            ++TotalCount;
            return list.Count - 1;
        }

        public DrawRange GetRange(MaterialTypeId materialType)
        {
            if (MeshDrawRanges.TryGetValue(materialType, out var range))
            {
                return range;
            }
            return DrawRange.Zero;
        }

        public (MeshDraw Draw, int Index) GetDraw(
            MaterialTypeId materialType,
            int index,
            bool relativeIndex = false
        )
        {
            if (
                MeshDrawRanges.TryGetValue(materialType, out var range)
                && MeshDrawByMaterialType.TryGetValue(materialType, out var list)
            )
            {
                if (!relativeIndex)
                {
                    index -= (int)range.Start;
                }
                if (index >= 0 && index < list.Count)
                {
                    return (list[index], relativeIndex ? index : index + (int)range.Start);
                }
            }
            return (default, -1);
        }

        public void Clear()
        {
            MeshDrawByMaterialType.Clear();
            MeshDrawRanges.Clear();
            TotalCount = 0;
        }

        public void SortByMeshId()
        {
            Parallel.ForEach(
                MeshDrawByMaterialType,
                kv =>
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
            );
        }

        public bool Update(int index, ref MeshDraw meshDraw)
        {
            var materialType = meshDraw.MaterialType;
            if (MeshDrawByMaterialType.TryGetValue(materialType, out var list))
            {
                if (list[index].EntityId != meshDraw.EntityId)
                {
                    return false;
                }
                list[index] = meshDraw;
                Upload();
                return true;
            }
            return false;
        }

        public void Upload()
        {
            if (TotalCount == 0)
            {
                return;
            }
            _buffer.Advance();
            var offset = 0;
            var range = new DrawRange();
            _buffer.EnsureCapacity(TotalCount);
            foreach (var kv in MeshDrawByMaterialType)
            {
                if (kv.Value.Count > 0)
                {
                    _buffer.Upload(kv.Value, offset);
                    offset += kv.Value.Count * (int)MeshDraw.SizeInBytes;
                }
                range = new DrawRange(range.End, (uint)kv.Value.Count);
                MeshDrawRanges[kv.Key] = range;
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _buffer.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~MeshDrawSorting()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(MeshDrawData));
    private static readonly ILogger _logger = LogManager.Create<MeshDrawData>();

    private readonly MeshDrawSorting?[] _meshDrawSortings = new MeshDrawSorting?[
        (int)MeshVariant.All + 1
    ];

    private readonly HashSet<MaterialTypeId> _materialTypes = new(InitialBufferSize);
    private readonly Dictionary<
        int,
        (int Index, uint MaterialPropId, MeshVariant CompFeature)
    > _entityInfos = new(1024 * 10);
    private readonly bool _isTransparent;
    private EntityCollection? _entities;
    private readonly HashSet<int> _updatedEntities = [];
    private readonly Components<MeshComponent> _meshComponents;
    private readonly Components<NodeInfo> _nodeInfos;
    private readonly Components<Renderable> _renderables;

    public IContext Context { get; }
    public World World { get; }
    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    private bool _needRebuilt = true;
    public IEnumerable<MaterialTypeId> MaterialTypes => _materialTypes;

    public BufferHandle Buffer =>
        throw new InvalidOperationException(
            "MeshDrawData does not have a single buffer. "
                + "Use GetBuffer or GetBufferByMaterial to get the appropriate buffer for a given mesh variant and material type."
        );
    public ulong GpuAddress =>
        throw new InvalidOperationException(
            "MeshDrawData does not have a single buffer. "
                + "Use GetBuffer or GetBufferByMaterial to get the appropriate buffer for a given mesh variant and material type."
        );
    public uint Stride => MeshDraw.SizeInBytes;

    public uint Count { private set; get; } = 0;
    public override string Name { get; }

    public MeshDrawData(IContext context, World world, bool isTransparent)
    {
        Context = context;
        _isTransparent = isTransparent;
        World = world;
        Name = $"{(isTransparent ? "Transparent" : "Opaque")}_{World.Id}";
        _meshComponents = world.GetComponents<MeshComponent>();
        _nodeInfos = world.GetComponents<NodeInfo>();
        _renderables = world.GetComponents<Renderable>();
    }

    public bool HasAny(MeshVariant variant)
    {
        var meshDraw = GetMeshDrawSorting(variant);
        return meshDraw?.TotalCount > 0;
    }

    public (BufferHandle, DrawRange) GetBufferByMaterial(MaterialTypeId id, MeshVariant variant)
    {
        var meshDraw = GetMeshDrawSorting(variant);
        if (meshDraw is null)
        {
            return (BufferHandle.Null, DrawRange.Zero);
        }
        return (meshDraw.Buffer, meshDraw.GetRange(id));
    }

    public (BufferHandle, DrawRange) GetBuffer(MeshVariant variant)
    {
        var meshDraw = GetMeshDrawSorting(variant);
        if (meshDraw is null)
        {
            return (BufferHandle.Null, DrawRange.Zero);
        }
        return (meshDraw.Buffer, new DrawRange(0, (uint)meshDraw.TotalCount));
    }

    private MeshDrawSorting? GetMeshDrawSorting(MeshVariant variant)
    {
        return _meshDrawSortings[(int)variant];
    }

    public MeshDraw GetMeshDraw(MeshVariant variant, MaterialTypeId id, int drawIndex)
    {
        var meshDraw = GetMeshDrawSorting(variant);
        if (meshDraw is null)
        {
            return default;
        }
        return meshDraw.GetDraw(id, drawIndex).Draw;
    }

    public DrawRange GetRangeByMaterial(MeshVariant variant, MaterialTypeId id)
    {
        var meshDraw = GetMeshDrawSorting(variant);
        if (meshDraw is null)
        {
            return default;
        }
        return meshDraw.MeshDrawRanges.TryGetValue(id, out var drawRange) ? drawRange : default;
    }

    public (BufferHandle, MeshDraw, int DrawIndex) GetMeshDraw(Entity entity)
    {
        if (!_entityInfos.TryGetValue(entity.Id, out var info))
        {
            return (BufferHandle.Null, default, 0);
        }
        var meshDrawSorting = GetMeshDrawSorting(info.CompFeature);
        var (meshDraw, drawIndex) = meshDrawSorting!.GetDraw(info.MaterialPropId, info.Index, true);
        return (meshDrawSorting?.Buffer ?? BufferHandle.Null, meshDraw, drawIndex);
    }

    public IEnumerable<MaterialTypeId> GetMaterialTypes(MeshVariant variant)
    {
        var meshDrawSorting = GetMeshDrawSorting(variant);
        return meshDrawSorting?.MeshDrawByMaterialType.Keys ?? Enumerable.Empty<MaterialTypeId>();
    }

    protected override ResultCode OnInitializing()
    {
        _needRebuilt = true;
        var filter = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<MeshComponent>()
            .Has<WorldTransform>()
            .Has<Renderable>();
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
        _entities.EntityAdded += OnAddedChanged;
        _entities.EntityRemoved += OnRemovedChanged;
        _entities.World.Register<SceneChangedEvents>(OnSceneChanged);

        for (var i = 0; i < _meshDrawSortings.Length; ++i)
        {
            _meshDrawSortings[i] = new MeshDrawSorting(
                Context,
                $"{Name}_{((MeshVariant)i).ToLiteralShort()}"
            );
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _entities!.EntityChanged -= OnEntityChanged;
        _entities.EntityAdded -= OnAddedChanged;
        _entities.EntityRemoved -= OnRemovedChanged;
        _entities.World.Unregister<SceneChangedEvents>(OnSceneChanged);
        Disposer.DisposeAndRemove(ref _entities);

        for (var i = 0; i < _meshDrawSortings.Length; ++i)
        {
            Disposer.DisposeAndRemove(ref _meshDrawSortings[i]);
        }
        return ResultCode.Ok;
    }

    public bool Update()
    {
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
        _entityInfos.Clear();
        Count = 0;

        foreach (var meshDrawSorting in _meshDrawSortings)
        {
            meshDrawSorting?.Clear();
        }

        foreach (var entity in _entities)
        {
            Debug.Assert(entity.Id != 0);
            var meshDraw = CreateMeshDraw(entity);
            if (meshDraw.EntityId != entity.Id)
            {
                continue;
            }
            _materialTypes.Add(meshDraw.MaterialType);
            var meshDrawSorting = GetMeshDrawSorting((MeshVariant)meshDraw.DrawType);
            var index = meshDrawSorting!.Add(ref meshDraw);
            _entityInfos[(int)meshDraw.EntityId] = (
                index,
                meshDraw.MaterialId,
                (MeshVariant)meshDraw.DrawType
            );
            ++Count;
        }
        FinalizeMeshDraws();
        _needRebuilt = false;
        return true;
    }

    private MeshDraw CreateMeshDraw(Entity entity)
    {
        ref var nodeInfo = ref _nodeInfos[entity];
        ref var meshRenderComp = ref _meshComponents[entity];
        if (!meshRenderComp.Valid)
        {
            return default;
        }
        var materialType = meshRenderComp.MaterialProperties!.MaterialTypeId;
        ref var renderable = ref _renderables[entity];
        var meshDraw = new MeshDraw()
        {
            MeshId = meshRenderComp.Geometry!.Id,
            MaterialId = meshRenderComp.MaterialProperties!.Index,
            MaterialType = materialType,
            NodeInfoIndex = (uint)renderable.GPUIndex,
            EntityId = (uint)entity.Id,
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
            DrawType = (uint)meshRenderComp.Variant,
        };
        return meshDraw;
    }

    private void FinalizeMeshDraws()
    {
        foreach (var meshDrawSorting in _meshDrawSortings)
        {
            meshDrawSorting?.SortByMeshId();
            meshDrawSorting?.Upload();
        }
    }

    private bool UpdateChanges()
    {
        _logger.LogTrace(
            "{NAME}: Updating MeshDrawData with changed entity count: {COUNT}",
            Name,
            _updatedEntities.Count
        );
        foreach (var entityId in _updatedEntities)
        {
            var entity = World.GetEntity(entityId);
            var draw = CreateMeshDraw(entity);
            var meshDrawSorting = GetMeshDrawSorting((MeshVariant)draw.DrawType);
            meshDrawSorting?.Update(_entityInfos[entityId].Index, ref draw);
        }
        _updatedEntities.Clear();
        return true;
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
        if (!_needRebuilt)
        {
            var entity = World.GetEntity(e.EntityId);
            if (e.Type == World.GetComponentTypeId<MeshComponent>())
            {
                if (CanUpdate(e.EntityId, ref _meshComponents[entity]))
                {
                    _updatedEntities.Add(e.EntityId);
                    return;
                }
                RebuildNeeded();
            }
            else if (e.Type == World.GetComponentTypeId<NodeInfo>())
            {
                RebuildNeeded();
            }
        }
    }

    private bool CanUpdate(int entity, ref MeshComponent comp)
    {
        if (!_entityInfos.ContainsKey(entity))
        {
            return false;
        }
        var (idx, materialPropId, compFeature) = _entityInfos[entity];
        if (materialPropId == comp.MaterialProperties?.Index && comp.Variant == compFeature)
        {
            return true;
        }
        return false;
    }

    private void OnAddedChanged(object? sender, int e)
    {
        RebuildNeeded();
    }

    private void OnRemovedChanged(object? sender, int e)
    {
        RebuildNeeded();
    }

    private void OnSceneChanged(int worldId, SceneChangedEvents message)
    {
        RebuildNeeded();
    }

    private void RebuildNeeded()
    {
        _needRebuilt = true;
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
    }
}
