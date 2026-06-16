namespace HelixToolkit.Nex.Rendering;

public static class PointRenderHelper
{
    private const int ColorWriteIndex = 1; // The second render target (index 1) is the one used for entity ID output.

    public static uint Render(
        in RenderResources res,
        ulong fpConstAddress,
        in DrawStreamEnumerable<PointDraw> streams
    )
    {
        if (res.RenderContext.Data is null)
            return 0;
        uint drawCount = 0;
        foreach (var stream in streams)
        {
            drawCount += RenderPoints(in res, fpConstAddress, stream);
        }
        return drawCount;
    }

    public static uint RenderPoints(
        in RenderResources res,
        ulong fpConstAddress,
        IDrawStream<PointDraw> stream
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
                res.RenderContext.PickingConfig.IsPickThroughEnabled(
                    stream.StreamType,
                    stream.Variants
                )
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

        foreach (var materialType in stream.GetMaterialTypesCore())
        {
            var range = stream.GetRangeByMaterial(materialType);
            if (range.Empty)
                continue;
            if (!context.UseExternalPipeline)
            {
                var pipeline = context.Data.ResourceManager.PointMaterialManager.GetPipeline(
                    materialType
                );
                cmdBuf.BindRenderPipeline(pipeline, default);
            }
            cmdBuf.PushConstants(
                new PointRenderPC
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
}
