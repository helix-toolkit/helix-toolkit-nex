using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Rendering;

public static class RenderHelper
{
    private static readonly ILogger _logger = LogManager.Create("RenderHelper");
    private static readonly bool[] _colorWriteNoIdOuput = [true, false, true, true];

    public static uint Render(
        in RenderResources res,
        ulong fpConstAddress,
        IEnumerable<IDrawStream> streams,
        MaterialPassType passType
    )
    {
        if (res.RenderContext.Data is null)
            return 0;
        uint drawCount = 0;
        foreach (var stream in streams)
        {
            switch (stream.IndexBufferStrategy)
            {
                case IndexBufferStrategy.Shared:
                    drawCount += RenderStatic(in res, fpConstAddress, stream, passType);
                    break;
                case IndexBufferStrategy.PerDraw:
                    drawCount += RenderDynamic(in res, fpConstAddress, stream, passType);
                    break;
            }
        }
        return drawCount;
    }

    public static uint RenderStatic(
        in RenderResources res,
        ulong fpConstAddress,
        IDrawStream stream,
        MaterialPassType passType
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
        Debug.Assert(
            stream.IndexBufferStrategy == IndexBufferStrategy.Shared,
            "Static draw streams must use shared index buffer strategy."
        );
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.RenderContext;

        cmdBuf.BindIndexBuffer(context.Data.StaticMeshIndexData.Buffer, IndexFormat.UI32);

        foreach (var materialType in stream.GetMaterialTypes())
        {
            var range = stream.GetRangeByMaterial(materialType);
            if (range.Empty)
                continue;
            ulong customMaterialBufferAddress = 0;
            if (!context.UseExternalPipeline)
            {
                var mat = context.Data.GetMaterial(materialType);
                if (mat == null || !mat.Bind(cmdBuf, passType))
                {
                    _logger.LogError(
                        "Failed to bind material of type {MaterialType} for rendering",
                        materialType
                    );
                    continue;
                }
                customMaterialBufferAddress = mat.CustomBufferAddress;
                if (stream.Categories.HasFlag(DrawStreamCategory.Transparent))
                {
                    if (stream.Categories.HasFlag(DrawStreamCategory.Hitable))
                    {
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                    }
                    else
                    {// Disable entity ID output for transparent non-hitable meshes to avoid writing to the ID buffer, which is used for picking and should not be affected by transparent objects.
                        cmdBuf.SetColorWriteEnabled(_colorWriteNoIdOuput);
                    }
                }
            }
            cmdBuf.PushConstants(
                new MeshDrawPushConstant
                {
                    FpConstAddress = fpConstAddress,
                    CustomMaterialBufferAddress = customMaterialBufferAddress,
                    DrawCommandIdxOffset = range.Start,
                    MeshDrawBufferAddress = stream.Buffer.GpuAddress(res.RenderContext.Context),
                }
            );
            cmdBuf.DrawIndexedIndirect(
                stream.Buffer,
                range.Start * stream.Stride,
                range.Count,
                stream.Stride
            );
            drawCount += range.Count;
        }
        return drawCount;
    }

    public static uint RenderDynamic(
        in RenderResources res,
        ulong fpConstAddress,
        IDrawStream stream,
        MaterialPassType passType
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

        foreach (var materialType in stream.GetMaterialTypes())
        {
            var range = stream.GetRangeByMaterial(materialType);
            if (range.Empty)
                continue;
            ulong customMaterialBufferAddress = 0;
            if (!context.UseExternalPipeline)
            {
                var mat = context.Data.GetMaterial(materialType);
                if (mat == null || !mat.Bind(cmdBuf, passType))
                {
                    _logger.LogError(
                        "Failed to bind material of type {MaterialType} for rendering",
                        materialType
                    );
                    continue;
                }
                customMaterialBufferAddress = mat.CustomBufferAddress;
                if (stream.Categories.HasFlag(DrawStreamCategory.Transparent))
                {
                    if (stream.Categories.HasFlag(DrawStreamCategory.Hitable))
                    {
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                    }
                    else
                    {// Disable entity ID output for transparent non-hitable meshes to avoid writing to the ID buffer, which is used for picking and should not be affected by transparent objects.
                        cmdBuf.SetColorWriteEnabled(_colorWriteNoIdOuput);
                    }
                }
            }

            for (var i = range.Start; i < range.Start + range.Count; ++i)
            {
                if (!stream.TryGetMeshDraw((int)i, out var meshDraw))
                {
                    continue;
                }
                if (meshDraw.IndexCount == 0)
                {
                    _logger.LogError(
                        "MeshDrawData contains a draw command with zero indices at index {Id}, skipping",
                        i
                    );
                    continue;
                }
                var meshId = meshDraw.MeshId;
                var geometry = context.Data.GetGeometry(meshId);
                if (geometry is null)
                {
                    _logger.LogTrace(
                        "MeshDrawData contains invalid MeshId {MeshId} at index {Id}",
                        meshId,
                        i
                    );
                    continue;
                }

                cmdBuf.BindIndexBuffer(geometry.IndexBuffer, IndexFormat.UI32);
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = fpConstAddress,
                        CustomMaterialBufferAddress = customMaterialBufferAddress,
                        DrawCommandIdxOffset = i,
                        MeshDrawBufferAddress = stream.Buffer.GpuAddress(res.RenderContext.Context),
                    }
                );
                cmdBuf.DrawIndexedIndirect(stream.Buffer, i * stream.Stride, 1, stream.Stride);
                drawCount++;
            }
        }
        return drawCount;
    }
}
