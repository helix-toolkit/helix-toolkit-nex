using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// A single named draw stream that owns a GPU buffer of <see cref="MeshDraw"/> commands
/// sharing the same rendering characteristics. Draws are grouped by material type for
/// efficient batched rendering.
/// <para>
/// Uses <see cref="Renderable.DrawCmdIndex"/>, <see cref="Renderable.DrawType"/> and <see cref="Renderable.DrawVariants"/>
/// on each entity for O(1) entity-to-slot lookup without maintaining a separate dictionary.
/// </para>
/// <para>
/// Follows the same rebuild-on-structural-change, incremental-update-on-property-change
/// pattern as <see cref="MeshDrawData"/>, but scoped to a single stream category.
/// </para>
/// </summary>
internal sealed class MeshDrawStream : DrawStreamBase<MeshDraw, MeshDrawInfo>
{
    private static readonly ILogger _logger = LogManager.Create<MeshDrawStream>();
    private static readonly EventBus _eventBus = EventBus.Instance;
    public override uint Stride => MeshDraw.SizeInBytes;
    private IEventSubscription? _sub;

    public MeshDrawStream(IContext context, World world, DrawStreamType type, DrawStreamName name)
        : base(context, world, type, name, _logger) { }

    protected override ResultCode OnInitializing()
    {
        _sub = _eventBus.Subscribe<MaterialPropsUpdatedEvent>(
            (e) =>
            {
                _logger.LogTrace(
                    "Material Props is changed. Index: {INDEX}; Op: {OP};",
                    e.Index,
                    e.Operation
                );

                if (e.Operation == MaterialPropertyOp.TypeChange)
                {
                    MarkRebuildNeeded();
                }
            }
        );
        return base.OnInitializing();
    }

    protected override ResultCode OnTearingDown()
    {
        Disposer.DisposeAndRemove(ref _sub);
        return base.OnTearingDown();
    }

    protected override MeshDraw CreateDrawInfo(Entity entity)
    {
        ref var meshComp = ref _components[entity];
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
            DrawType = (uint)meshComp.Variants,
        };
    }

    protected override bool IsValid(ref MeshDrawInfo comp)
    {
        return comp.Valid;
    }

    protected override uint GetCurrentMaterialTypeId(ref MeshDraw draw)
    {
        return draw.MaterialType;
    }

    protected override uint GetActualMaterialTypeId(ref MeshDrawInfo info)
    {
        if (info.MaterialProperties == null)
        {
            return 0;
        }
        return info.MaterialProperties.MaterialTypeId;
    }

    protected override uint GetMeshId(ref MeshDraw draw)
    {
        return draw.MeshId;
    }

    protected override uint GetEntityId(ref MeshDraw draw)
    {
        return draw.EntityId;
    }

    protected override DrawStreamVariants GetVariant(ref MeshDrawInfo comp)
    {
        return comp.Variants;
    }

    protected override uint GetDrawCommandSize()
    {
        return MeshDraw.SizeInBytes;
    }

    protected override void SortByMeshId()
    {
        Parallel.ForEach(
            DrawsByMaterial,
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
}
