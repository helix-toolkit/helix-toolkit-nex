using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Engine.Data;

internal sealed class LineDrawStream : DrawStreamBase<LineDraw, LineDrawInfo>
{
    private static readonly ILogger _logger = LogManager.Create<LineDrawStream>();

    public override uint Stride => LineDraw.SizeInBytes;

    public LineDrawStream(IContext context, World world, DrawStreamType type, DrawStreamName name)
        : base(context, world, type, name, _logger) { }

    protected override LineDraw CreateDrawInfo(Entity entity)
    {
        ref var lineComp = ref _components[entity];
        if (!lineComp.Valid)
            return default;

        ref var renderable = ref _renderables[entity];
        lineComp.LineMaterialId = LineMaterialRegistry.TryGetById(
            lineComp.LineMaterialId,
            out var matReg
        )
            ? matReg!.TypeId
            : 0; // Fallback to a default material if the specified one is not found.
        return new LineDraw
        {
            MeshId = lineComp.Geometry!.Id,
            MaterialType = lineComp.LineMaterialId,
            NodeInfoIndex = (uint)renderable.GPUIndex,
            EntityId = (uint)entity.Id,
            InstanceCount = (uint)(lineComp.Geometry.Vertices.Count / 2),
            Cullable = lineComp.Cullable ? 1u : 0u,
            VertexCount = 4,
            LineCount = (uint)(lineComp.Geometry.Vertices.Count / 2),
            LineColor = lineComp.LineColor.EncodeToUInt(),
            // Screen-space line width in pixels, sourced from the per-entity
            // LineDrawInfo.LineThickness. The vertex shader clamps it to [1,64] (Req 2.3).
            LineWidth = lineComp.LineThickness,
            DrawType = (uint)lineComp.Variants,
        };
    }

    protected override uint GetEntityId(ref LineDraw draw)
    {
        return draw.EntityId;
    }

    protected override uint GetMaterialType(ref LineDraw draw)
    {
        return draw.MaterialType;
    }

    protected override uint GetMeshId(ref LineDraw draw)
    {
        return draw.MeshId;
    }

    protected override DrawStreamVariants GetVariant(ref LineDrawInfo comp)
    {
        return comp.Variants;
    }

    protected override bool IsValid(ref LineDrawInfo comp)
    {
        return comp.Valid;
    }

    protected override uint GetDrawCommandSize()
    {
        return LineDraw.SizeInBytes;
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
                    Comparer<LineDraw>.Create((a, b) => a.MeshId.CompareTo(b.MeshId))
                );
            }
        );
    }
}
