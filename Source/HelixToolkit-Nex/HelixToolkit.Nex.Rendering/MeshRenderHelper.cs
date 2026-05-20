using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.DrawStreams;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering;

public static class MeshRenderHelper
{
    private static readonly ILogger _logger = LogManager.Create("RenderHelper");
    private const int ColorWriteIndex = 1; // Assuming the second render target (index 1) is the one used for entity ID output.

    public static uint Render(
        in RenderResources res,
        ulong fpConstAddress,
        in MeshDrawStreamEnumerable streams,
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

        foreach (var materialType in stream.GetMaterialTypesCore())
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
                if (stream.Categories.HasAnyFlag(DrawStreamCategory.Transparent))
                {
                    if (stream.Categories.HasAnyFlag(DrawStreamCategory.Hitable))
                    {
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                    }
                    else
                    { // Disable entity ID output for transparent non-hitable meshes to avoid writing to the ID buffer, which is used for picking and should not be affected by transparent objects.
                        var value = res.Pass.ColorWrites[ColorWriteIndex];
                        res.Pass.ColorWrites[ColorWriteIndex] = false;
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                        res.Pass.ColorWrites[ColorWriteIndex] = value;
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

        foreach (var materialType in stream.GetMaterialTypesCore())
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
                if (stream.Categories.HasAnyFlag(DrawStreamCategory.Transparent))
                {
                    if (stream.Categories.HasAnyFlag(DrawStreamCategory.Hitable))
                    {
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                    }
                    else
                    { // Disable entity ID output for transparent non-hitable meshes to avoid writing to the ID buffer, which is used for picking and should not be affected by transparent objects.
                        var value = res.Pass.ColorWrites[ColorWriteIndex];
                        res.Pass.ColorWrites[ColorWriteIndex] = false;
                        cmdBuf.SetColorWriteEnabled(res.Pass.ColorWrites);
                        res.Pass.ColorWrites[ColorWriteIndex] = value;
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
        var dataStreams = data.DrawStreams;
        var renderables = data.World.GetComponents<Renderable>();
        uint counter = 0;
        foreach (var entity in entites.AsValueEnumerable())
        {
            var category = (DrawStreamCategory)renderables[entity].DrawCategory;
            var streams = dataStreams.GetStreamsCore(category);
            foreach (var stream in streams)
            {
                if (stream.Categories != category)
                {
                    continue;
                }
                var (meshDraw, slot) = stream.GetMeshDraw(entity);
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

                cmdBuffer.DrawIndexedIndirect(
                    stream.Buffer,
                    (uint)slot * stream.Stride,
                    1,
                    stream.Stride
                );
                ++counter;
            }
        }
        return counter;
    }
}
