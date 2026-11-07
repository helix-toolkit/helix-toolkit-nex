namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects all skinned mesh entities (a <see cref="MeshDrawInfo"/> with a
/// <see cref="MeshDrawInfo.Skinning"/>) of a world, uploads their joint matrices every frame and
/// prepares the dispatch list consumed by the <c>SkinningNode</c> compute pass.
/// </summary>
/// <remarks>
/// Must be updated after the scene transforms (<see cref="SceneState"/>) so the joint matrices
/// reflect the current frame, and before the mesh draw streams so draw commands pick up newly
/// allocated skinned vertex buffers in the same frame.
/// </remarks>
internal sealed class SkinningData(IContext context, World world) : Initializable, ISkinningData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(SkinningData));
    private static readonly ILogger _logger = LogManager.Create<SkinningData>();
    private readonly HashSet<Entity> _skinnedEntities = [];
    private readonly HashSet<Skinning> _visited = [];
    private readonly HashSet<Skinning> _sharedWarned = [];
    private readonly FastList<SkinningDispatch> _dispatches = [];
    private readonly FastList<Entity> _reallocatedEntities = [];
    private EntityCollection? _entities;
    private IComponents<MeshDrawInfo>? _meshes;
    private IComponents<WorldTransform>? _worldTransforms;

    public IContext Context { get; } = context;
    public World World { get; } = world;

    public override string Name { get; } = nameof(SkinningData);

    public ReadOnlySpan<SkinningDispatch> Dispatches =>
        _dispatches.GetInternalArray().AsSpan(0, _dispatches.Count);

    public BufferHandle Buffer => BufferHandle.Null;

    public ulong GpuAddress => 0;

    public uint Stride => 0;

    public uint Count => (uint)_dispatches.Count;

    protected override ResultCode OnInitializing()
    {
        _entities = World
            .CreateCollection()
            .Has<NodeInfo>()
            .Has<MeshDrawInfo>()
            .Has<WorldTransform>()
            .Has<Renderable>()
            .Build();
        _meshes = World.GetComponents<MeshDrawInfo>();
        _worldTransforms = World.GetComponents<WorldTransform>();
        _entities.EntityAdded += OnEntityAddedOrChanged;
        _entities.EntityRemoved += OnEntityRemoved;
        _entities.EntityChanged += OnEntityChanged;
        foreach (var entity in _entities)
        {
            OnEntityAddedOrChanged(this, entity);
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _entities);
        _skinnedEntities.Clear();
        _visited.Clear();
        _sharedWarned.Clear();
        _dispatches.Clear();
        _reallocatedEntities.Clear();
        _meshes = null;
        _worldTransforms = null;
        return ResultCode.Ok;
    }

    private void OnEntityAddedOrChanged(object? sender, Entity entity)
    {
        if (_meshes![entity].IsSkinned)
        {
            _skinnedEntities.Add(entity);
        }
        else
        {
            _skinnedEntities.Remove(entity);
        }
    }

    private void OnEntityChanged(object? sender, EntityChangedEvent e)
    {
        OnEntityAddedOrChanged(sender, e.Entity);
    }

    private void OnEntityRemoved(object? sender, Entity entity)
    {
        _skinnedEntities.Remove(entity);
    }

    public bool Update()
    {
        _dispatches.Clear();
        if (_skinnedEntities.Count == 0 || _meshes is null || _worldTransforms is null)
        {
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        _visited.Clear();
        _reallocatedEntities.Clear();

        foreach (var entity in _skinnedEntities)
        {
            if (!entity.Valid || !entity.Get<NodeInfo>().Enabled)
            {
                continue;
            }
            ref var mesh = ref _meshes[entity];
            var skinning = mesh.Skinning;
            var geometry = mesh.Geometry;
            if (skinning is null || geometry is null || !geometry.Valid)
            {
                continue;
            }
            if (!_visited.Add(skinning))
            {
                if (_sharedWarned.Add(skinning))
                {
                    _logger.LogWarning(
                        "Skinning {SKELETON} is shared by multiple mesh entities. Entity {ENTITY} is not skinned.",
                        skinning.Skeleton.Name,
                        entity
                    );
                }
                continue;
            }
            if (!IsGeometryReady(geometry))
            {
                continue;
            }

            var result = skinning.EnsureBuffers(
                Context,
                geometry.Vertices.Count,
                geometry.VertexPropsBuffer.Valid,
                out var reallocated
            );
            if (result != ResultCode.Ok)
            {
                _logger.LogError(
                    "Failed to create skinning buffers for entity {ENTITY}: {RESULT}",
                    entity,
                    result
                );
                continue;
            }
            if (reallocated)
            {
                _reallocatedEntities.Add(entity);
            }
            result = skinning.UpdateJointMatrices(
                _worldTransforms[entity].Value,
                _worldTransforms
            );
            if (result == ResultCode.Ok && skinning.IsReady)
            {
                _dispatches.Add(new SkinningDispatch(skinning, geometry));
            }
        }

        // Refresh the draw commands of entities whose skinned buffer addresses changed.
        // Notified after the loop since the change event updates the skinned entity set.
        foreach (var entity in _reallocatedEntities)
        {
            entity.NotifyComponentChanged<MeshDrawInfo>();
        }
        return true;
    }

    private static bool IsGeometryReady(Geometry geometry)
    {
        return geometry.IsSkinned
            && !geometry.HasPendingBufferUpdate
            && geometry.VertexBuffer.Valid
            && geometry.JointInfluenceBuffer.Valid
            && geometry.JointInfluences.Count == geometry.Vertices.Count;
    }
}
