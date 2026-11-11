using HelixToolkit.Nex.ECS.Events;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects all deformed mesh entities (a <see cref="MeshDrawInfo"/> with a
/// <see cref="MeshDrawInfo.Skinning"/> and/or a <see cref="MeshDrawInfo.Morphing"/>) of a world,
/// uploads their joint matrices when world transforms change and their morph weights when the
/// weights change, and prepares the dispatch list consumed by the <c>SkinningNode</c> compute pass.
/// </summary>
/// <remarks>
/// <para>
/// Must be updated after the scene transforms (<see cref="SceneState"/>) so the joint matrices
/// reflect the current frame, and before the mesh draw streams so draw commands pick up newly
/// allocated skinned vertex buffers in the same frame.
/// </para>
/// <para>
/// Joint matrices are only re-uploaded when a world transform changed since a skinning's last
/// upload, and morph weights only when they changed (<see cref="MorphWeights.Version"/>). Each
/// upload advances a ring buffer, and the engine paces frames once per engine frame, so the rings
/// must advance at most once per frame even though every viewport rendering the world calls
/// <see cref="Update"/>.
/// </para>
/// </remarks>
internal sealed class SkinningData(IContext context, World world) : Initializable, ISkinningData
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(SkinningData));
    private static readonly ILogger _logger = LogManager.Create<SkinningData>();
    private readonly HashSet<Entity> _skinnedEntities = [];
    private readonly HashSet<Skinning> _visited = [];
    private readonly HashSet<Skinning> _sharedWarned = [];
    private readonly HashSet<Morphing> _visitedMorphings = [];
    private readonly HashSet<Morphing> _sharedMorphingWarned = [];
    private readonly FastList<SkinningDispatch> _dispatches = [];
    private readonly FastList<Entity> _reallocatedEntities = [];
    private readonly Dictionary<Skinning, ulong> _uploadedVersions = [];
    private readonly FastList<Skinning> _staleSkinnings = [];
    private Subscription? _worldTransformSubscription;

    /// <summary>
    /// Incremented whenever a world transform changes; compared with the version each skinning's
    /// joint matrices were last uploaded at.
    /// </summary>
    private ulong _transformVersion;
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
        _worldTransformSubscription = World.Register<ComponentChangedEvent<WorldTransform>>(
            OnWorldTransformChanged
        );
        foreach (var entity in _entities)
        {
            OnEntityAddedOrChanged(this, entity);
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _entities);
        _worldTransformSubscription?.Dispose();
        _worldTransformSubscription = null;
        _skinnedEntities.Clear();
        _uploadedVersions.Clear();
        _staleSkinnings.Clear();
        _visited.Clear();
        _sharedWarned.Clear();
        _visitedMorphings.Clear();
        _sharedMorphingWarned.Clear();
        _dispatches.Clear();
        _reallocatedEntities.Clear();
        _meshes = null;
        _worldTransforms = null;
        return ResultCode.Ok;
    }

    private void OnEntityAddedOrChanged(object? sender, Entity entity)
    {
        if (_meshes![entity].IsDeformed)
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

    private void OnWorldTransformChanged(World _, ComponentChangedEvent<WorldTransform> e)
    {
        ++_transformVersion;
    }

    public bool Update()
    {
        _dispatches.Clear();
        if (_skinnedEntities.Count == 0 || _meshes is null || _worldTransforms is null)
        {
            _uploadedVersions.Clear();
            return true;
        }
        using var t = _tracer.BeginScope(nameof(Update));
        _visited.Clear();
        _visitedMorphings.Clear();
        _reallocatedEntities.Clear();

        foreach (var entity in _skinnedEntities)
        {
            if (!entity.Valid || !entity.Get<NodeInfo>().Enabled)
            {
                continue;
            }
            ref var mesh = ref _meshes[entity];
            var geometry = mesh.Geometry;
            if (geometry is null || !geometry.Valid || geometry.HasPendingBufferUpdate)
            {
                continue;
            }
            // A skinning or morphing that does not apply to the geometry (no joint influences or
            // no morph targets) releases its output, so MeshDrawInfo.DeformedVertexBuffer only
            // selects the output this pass writes, or falls back to the base vertices.
            var reallocated = false;
            var skinning = mesh.Skinning;
            if (skinning is not null && !IsGeometrySkinReady(geometry))
            {
                reallocated |= !_visited.Contains(skinning) && skinning.ReleaseOutput();
                skinning = null;
            }
            else if (skinning is not null && !VisitOnce(skinning, entity))
            {
                skinning = null;
            }
            var morphing = mesh.Morphing;
            if (morphing is not null && !IsGeometryMorphReady(geometry))
            {
                reallocated |= !_visitedMorphings.Contains(morphing) && morphing.ReleaseOutput();
                morphing = null;
            }
            else if (morphing is not null && !VisitOnce(morphing, entity))
            {
                morphing = null;
            }
            if (skinning is null && morphing is null)
            {
                if (reallocated)
                {
                    _reallocatedEntities.Add(entity);
                }
                continue;
            }

            var vertexCount = geometry.Vertices.Count;
            var hasVertexProps = geometry.VertexPropsBuffer.Valid;
            if (skinning is not null && !UpdateSkinning(entity, skinning, vertexCount, hasVertexProps, ref reallocated))
            {
                skinning = null;
            }
            if (
                morphing is not null
                && !UpdateMorphing(entity, morphing, geometry, ownsOutput: skinning is null, ref reallocated)
            )
            {
                morphing = null;
            }
            if (reallocated)
            {
                _reallocatedEntities.Add(entity);
            }
            if (skinning is not null || morphing is { VertexCount: > 0 })
            {
                _dispatches.Add(new SkinningDispatch(skinning, morphing, geometry));
            }
        }

        // Forget skinnings that were not visited, so they re-upload if they come back.
        if (_uploadedVersions.Count > _visited.Count)
        {
            _staleSkinnings.Clear();
            foreach (var skinning in _uploadedVersions.Keys)
            {
                if (!_visited.Contains(skinning))
                {
                    _staleSkinnings.Add(skinning);
                }
            }
            foreach (var skinning in _staleSkinnings)
            {
                _uploadedVersions.Remove(skinning);
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

    /// <summary>
    /// Returns <see langword="true"/> the first time <paramref name="skinning"/> is visited this
    /// update; a skinning shared by several mesh entities only deforms the first one.
    /// </summary>
    private bool VisitOnce(Skinning skinning, Entity entity)
    {
        if (_visited.Add(skinning))
        {
            return true;
        }
        if (_sharedWarned.Add(skinning))
        {
            _logger.LogWarning(
                "Skinning {SKELETON} is shared by multiple mesh entities. Entity {ENTITY} is not skinned.",
                skinning.Skeleton.Name,
                entity
            );
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> the first time <paramref name="morphing"/> is visited this
    /// update; a morphing shared by several mesh entities only deforms the first one.
    /// </summary>
    private bool VisitOnce(Morphing morphing, Entity entity)
    {
        if (_visitedMorphings.Add(morphing))
        {
            return true;
        }
        if (_sharedMorphingWarned.Add(morphing))
        {
            _logger.LogWarning(
                "Morphing {MORPHING} is shared by multiple mesh entities. Entity {ENTITY} is not morphed. Share the MorphWeights instead.",
                morphing.Name,
                entity
            );
        }
        return false;
    }

    private bool UpdateSkinning(
        Entity entity,
        Skinning skinning,
        int vertexCount,
        bool hasVertexProps,
        ref bool reallocated
    )
    {
        var result = skinning.EnsureBuffers(Context, vertexCount, hasVertexProps, out var skinReallocated);
        if (result != ResultCode.Ok)
        {
            _logger.LogError(
                "Failed to create skinning buffers for entity {ENTITY}: {RESULT}",
                entity,
                result
            );
            return false;
        }
        reallocated |= skinReallocated;
        if (
            !_uploadedVersions.TryGetValue(skinning, out var uploadedVersion)
            || uploadedVersion != _transformVersion
        )
        {
            result = skinning.UpdateJointMatrices(_worldTransforms![entity].Value, _worldTransforms);
            if (result == ResultCode.Ok)
            {
                _uploadedVersions[skinning] = _transformVersion;
            }
        }
        return result == ResultCode.Ok && skinning.IsReady;
    }

    private bool UpdateMorphing(
        Entity entity,
        Morphing morphing,
        Geometry geometry,
        bool ownsOutput,
        ref bool reallocated
    )
    {
        var result = morphing.EnsureBuffers(
            Context,
            geometry.Vertices.Count,
            geometry.VertexPropsBuffer.Valid,
            ownsOutput,
            out var morphReallocated
        );
        reallocated |= morphReallocated;
        if (result == ResultCode.Ok)
        {
            result = morphing.UpdateWeights(geometry.MorphTargets.Count);
        }
        if (result != ResultCode.Ok)
        {
            _logger.LogError(
                "Failed to update morph target buffers for entity {ENTITY}: {RESULT}",
                entity,
                result
            );
            return false;
        }
        return morphing.IsReady;
    }

    private static bool IsGeometrySkinReady(Geometry geometry)
    {
        return geometry.IsSkinned
            && geometry.VertexBuffer.Valid
            && geometry.JointInfluenceBuffer.Valid
            && geometry.JointInfluences.Count == geometry.Vertices.Count;
    }

    private static bool IsGeometryMorphReady(Geometry geometry)
    {
        return geometry.HasMorphTargets
            && geometry.VertexBuffer.Valid
            && geometry.MorphDeltaBuffer.Valid;
    }
}
