using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering;

public static class LineRenderHelper
{
    private static readonly ILogger _logger = LogManager.Create("RenderHelper");
    private const int ColorWriteIndex = 1; // Assuming the second render target (index 1) is the one used for entity ID output.

    public static uint Render(
        in RenderResources res,
        ulong fpConstAddress,
        in DrawStreamEnumerable<LineDraw> streams
    )
    {
        if (res.RenderContext.Data is null)
            return 0;
        uint drawCount = 0;
        foreach (var stream in streams)
        {
            drawCount += RenderLines(in res, fpConstAddress, stream);
        }
        return drawCount;
    }

    public static uint RenderLines(
        in RenderResources res,
        ulong fpConstAddress,
        IDrawStream<LineDraw> stream
    )
    {
        if (res.RenderContext.Data is null)
        {
            return 0;
        }
        if (stream.Count == 0)
        {
            return 0;
        }
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.RenderContext;
        if (!context.UseExternalPipeline)
        {
            if (
                res.RenderContext.PickingConfig.IsPickThroughEnabled(stream.StreamType, stream.Variants)
            )
            {
                // If pick-through is enabled for this stream type and variant, disable color writes to the entity ID buffer to allow picking through this object.
                var value = res.Pass.ColorWrites[ColorWriteIndex];
                res.Pass.ColorWrites[ColorWriteIndex] = false;
                cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                res.Pass.ColorWrites[ColorWriteIndex] = value;
            }
            else
            {
                cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
            }
        }

        foreach (var materialType in stream.GetMaterialTypes())
        {
            var range = stream.GetRangeByMaterial(materialType);
            if (range.Empty)
                continue;
            if (!context.UseExternalPipeline)
            {
                var pipeline = context.Data.ResourceManager.LineMaterialManager.GetPipeline(
                    materialType
                );
                cmdBuf.BindRenderPipeline(pipeline, default);
            }
            cmdBuf.PushConstants(
                new LineRenderPC
                {
                    FpConstAddress = fpConstAddress,
                    MeshDrawBufferAddress = stream.Buffer.GpuAddress(res.RenderContext.Context),
                    DrawCommandIdxOffset = range.Start,
                }
            );
            cmdBuf.DrawIndirect(
                stream.Buffer,
                range.Start * stream.Stride,
                range.Count,
                stream.Stride
            );
            drawCount += range.Count;
        }
        return drawCount;
    }

    public static uint RenderEntities(
        this IEnumerable<Entity> entites,
        in RenderResources res,
        BufferHandle fpConst
    )
    {
        if (res.RenderContext.Data is null)
        {
            return 0;
        }
        var data = res.RenderContext.Data;
        var cmdBuffer = res.CmdBuffer;
        var dataStreams = data.LineDrawStreams;
        var renderables = data.World.GetComponents<Renderable>();
        uint counter = 0;
        foreach (var entity in entites.AsValueEnumerable())
        {
            var category = (DrawStreamVariants)renderables[entity].DrawVariants;
            var type = (DrawStreamType)renderables[entity].DrawType;
            var streams = dataStreams.GetStreams(type, category);
            foreach (var stream in streams)
            {
                if (stream.StreamType != type || stream.Variants != category)
                {
                    continue;
                }
                var (meshDraw, slot) = stream.GetDraw(entity);
                if (slot < 0)
                {
                    continue;
                }

                if (stream.IndexBufferStrategy == IndexBufferStrategy.Shared)
                {
                    cmdBuffer.BindIndexBuffer(data.StaticMeshIndexData.Buffer, IndexFormat.UI32);
                }
                else
                {
                    // Dynamic mesh — bind its own index buffer.
                    var geom = data.GetGeometry(meshDraw.MeshId);
                    if (geom is null)
                    {
                        continue;
                    }
                    cmdBuffer.BindIndexBuffer(geom.IndexBuffer, IndexFormat.UI32);
                }

                cmdBuffer.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = fpConst.GpuAddress(res.RenderContext.Context),
                        DrawCommandIdxOffset = (uint)slot,
                        MeshDrawBufferAddress = stream.Buffer.GpuAddress(res.RenderContext.Context),
                    }
                );

                cmdBuffer.DrawIndirect(stream.Buffer, (uint)slot * stream.Stride, 1, stream.Stride);
                ++counter;
            }
        }
        return counter;
    }
}
