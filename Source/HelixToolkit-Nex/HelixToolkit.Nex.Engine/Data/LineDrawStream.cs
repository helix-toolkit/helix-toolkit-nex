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
        ref var meshComp = ref _components[entity];
        if (!meshComp.Valid)
            return default;

        ref var renderable = ref _renderables[entity];
        return new LineDraw
        {
            MeshId = meshComp.Geometry!.Id,
            MaterialType = meshComp.LineMaterialId,
            NodeInfoIndex = (uint)renderable.GPUIndex,
            EntityId = (uint)entity.Id,
            InstanceCount = (uint)meshComp.Geometry.Vertices.Count,
            Cullable = meshComp.Cullable ? 1u : 0u,
            DrawType = (uint)meshComp.Variants,
            VertexCount = 4,
            LineColor = meshComp.LineColor.EncodeToUInt(),
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
}
