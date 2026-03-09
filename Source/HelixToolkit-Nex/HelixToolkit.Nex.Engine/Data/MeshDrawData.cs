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
                FullRange = new Range(FullRange.Start, end - FullRange.Start);
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
    private static readonly QueryDescription _meshDrawQuery = new QueryDescription().WithAll<
        NodeInfo,
        MeshComponent,
        Transform
    >();
    private ElementBuffer<MeshDraw>? _buffer = null;
    private readonly FastList<MeshDraw> _meshDraws = new(InitialBufferSize);
    private readonly MeshDrawSorting _meshDrawSortingStatic = new();
    private readonly MeshDrawSorting _meshDrawSortingStaticInstancing = new();
    private readonly MeshDrawSorting _meshDrawSortingDynamic = new();
    private readonly MeshDrawSorting _meshDrawSortingDynamicInstancing = new();
    private readonly HashSet<MaterialTypeId> _materialTypes = new(InitialBufferSize);
    private readonly bool _isTransparent;

    public IContext Context { get; }
    public World World { get; }
    public IReadOnlyList<MeshDraw> DrawCommands => _meshDraws;
    private long _lastBufferUpdateTicks = 0;
    private long _lastDataUpdateTicks = Stopwatch.GetTimestamp();
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
        World.SubscribeComponentSet<MeshComponent>(OnMeshRenderChanged);
        World.SubscribeComponentAdded<MeshComponent>(OnMeshRenderChanged);
        World.SubscribeComponentRemoved<MeshComponent>(OnMeshRenderChanged);
    }

    private void OnMeshRenderChanged(in Entity entity, ref MeshComponent comp)
    {
        _logger.LogDebug("MeshComponent is changed. {MESH_RENDER}", comp);
        _lastDataUpdateTicks = Stopwatch.GetTimestamp();
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
        _buffer?.Dispose();
        _buffer = null;
        return ResultCode.Ok;
    }

    public bool Update()
    {
        if (_buffer == null)
        {
            return false;
        }
        if (_lastDataUpdateTicks <= _lastBufferUpdateTicks)
        {
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        bool success = true;
        _meshDraws.Clear();
        _meshDrawSortingStatic.Clear();
        _meshDrawSortingStaticInstancing.Clear();
        _meshDrawSortingDynamic.Clear();
        _meshDrawSortingDynamicInstancing.Clear();
        var count = 0;
        var query = World.Query(in _meshDrawQuery);
        foreach (ref var chunk in query.GetChunkIterator())
        {
            foreach (var id in chunk)
            {
                ref var entity = ref chunk.Entity(id);
                ref var nodeInfo = ref chunk.Get<NodeInfo>(id);
                if (!nodeInfo.Enabled)
                {
                    continue;
                }
                ref var meshRenderComp = ref chunk.Get<MeshComponent>(id);
                if (!meshRenderComp.Valid)
                {
                    continue;
                }
                if (meshRenderComp.IsTransparent ^ _isTransparent)
                {
                    continue;
                }
                ref var transform = ref chunk.Get<WorldTransform>(id);
                var materialType = meshRenderComp.MaterialProperties!.MaterialTypeId;
                meshRenderComp.Instancing?.UpdateBuffer(Context);
                var meshDraw = new MeshDraw()
                {
                    MeshId = meshRenderComp.Geometry!.Id,
                    MaterialId = meshRenderComp.MaterialProperties!.Index,
                    MaterialType = materialType,
                    EntityId = (uint)entity.Id,
                    EntityVer = (uint)entity.Version,
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
        }
        FinalizeMeshDraws();
        _buffer.Upload(_meshDraws);
        _lastBufferUpdateTicks = _lastDataUpdateTicks;
        return success;
    }

    private void FinalizeMeshDraws()
    {
        DrawRange range = default;
        foreach (var kv in _meshDrawSortingStatic.MeshDrawByMaterialType)
        {
            _meshDrawSortingStatic.Sort();
            range = new Range(range.End, (uint)kv.Value.Count);
            _meshDrawSortingStatic.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }
        range = new Range(0, range.End);
        var start = range.End;

        foreach (var kv in _meshDrawSortingStaticInstancing.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new Range(range.End, (uint)kv.Value.Count);
            _meshDrawSortingStaticInstancing.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }

        range = new Range(start, range.End);
        start = range.End;

        foreach (var kv in _meshDrawSortingDynamic.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new Range(range.End, (uint)kv.Value.Count);
            _meshDrawSortingDynamic.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }

        range = new Range(start, range.End - start);
        start = range.End;
        foreach (var kv in _meshDrawSortingDynamicInstancing.MeshDrawByMaterialType)
        {
            _meshDrawSortingStaticInstancing.Sort();
            range = new Range(range.End, (uint)kv.Value.Count);
            _meshDrawSortingDynamicInstancing.AddRange(kv.Key, in range);
            _meshDraws.AddRange(kv.Value);
        }
    }
}
