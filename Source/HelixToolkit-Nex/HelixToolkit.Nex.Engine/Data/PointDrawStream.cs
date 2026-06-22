using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class PointDrawStream : DrawStreamBase<PointDraw, PointDrawInfo>
{
    private static readonly ILogger _logger = LogManager.Create<PointDrawStream>();

    public override uint Stride => PointDraw.SizeInBytes;

    public PointDrawStream(IContext context, World world, DrawStreamType type, DrawStreamName name)
        : base(context, world, type, name, _logger) { }

    protected override PointDraw CreateDrawInfo(Entity entity)
    {
        ref var pointComp = ref _components[entity];
        if (!pointComp.Valid)
            return default;

        ref var renderable = ref _renderables[entity];
        var pointCount = (uint)pointComp.Geometry!.Vertices.Count;
        return new PointDraw
        {
            MeshId = pointComp.Geometry!.Id,
            MaterialType = GetActualMaterialTypeId(ref pointComp),
            NodeInfoIndex = (uint)renderable.GPUIndex,
            EntityId = (uint)entity.Id,
            // A single Point_Quad emits 4 vertices (triangle-strip quad).
            VertexCount = 4,
            // Plain vertex list: point s = vertex s, so pointCount = Vertices.Count.
            PointCount = pointCount,
            // One Point_Quad instance per point; PointFrustumCull rewrites this to 0 when culled.
            InstanceCount = pointCount,
            Cullable = pointComp.Cullable ? 1u : 0u,
            PointColor = pointComp.PointColor.EncodeToUInt(),
            PointSize = pointComp.PointSize,
            FixedSize = pointComp.FixedSize ? 1u : 0u,
            DrawType = (uint)pointComp.Variants,
            TextureId = pointComp.TextureIndex,
            SamplerId = pointComp.SamplerIndex,
        };
    }

    protected override uint GetEntityId(ref PointDraw draw)
    {
        return draw.EntityId;
    }

    protected override uint GetCurrentMaterialTypeId(ref PointDraw draw)
    {
        return draw.MaterialType;
    }

    protected override uint GetActualMaterialTypeId(ref PointDrawInfo comp)
    {
        return PointMaterialRegistry.TryGetByName(comp.PointMaterialName, out var matReg)
            ? matReg!.TypeId
            : 0; // Fallback to a default material if the specified one is not found.
    }

    protected override uint GetMeshId(ref PointDraw draw)
    {
        return draw.MeshId;
    }

    protected override DrawStreamVariants GetVariant(ref PointDrawInfo comp)
    {
        return comp.Variants;
    }

    protected override bool IsValid(ref PointDrawInfo comp)
    {
        return comp.Valid;
    }

    protected override uint GetDrawCommandSize()
    {
        return PointDraw.SizeInBytes;
    }

    protected override void SortByMeshId()
    {
        Parallel.ForEach(
            DrawsByMaterial.Where(x => x.Value.Count > 1),
            kv =>
            {
                if (kv.Value.Count <= 1)
                    return;
                var arr = kv.Value.GetInternalArray();
                Array.Sort(
                    arr,
                    0,
                    kv.Value.Count,
                    Comparer<PointDraw>.Create((a, b) => a.MeshId.CompareTo(b.MeshId))
                );
            }
        );
    }
}
