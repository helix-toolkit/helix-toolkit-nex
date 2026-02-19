namespace HelixToolkit.Nex.Rendering;

public static class RenderExtensions
{
    private static readonly ILogger _logger = LogManager.Create("RenderExtensions");

    /// <summary>
    /// Renders all opaque objects in the specified <see cref="RenderContext"/> using the provided command buffer.
    /// </summary>
    /// <param name="cmdBuf">The command buffer used to record rendering commands.</param>
    /// <param name="context">The rendering context that provides scene and camera information. Must not be null and must contain valid data.</param>
    /// <param name="renderStatic"><see langword="true"/> to render static opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderDynamic"><see langword="true"/> to render dynamic opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable GPU instancing for supported objects; otherwise, <see langword="false"/>.
    /// Defaults to <see langword="true"/>.</param>
    /// <returns>The total number of opaque draw calls issued. Returns 0 if the context contains no data or if no objects are
    /// rendered.</returns>
    public static int RenderOpaque(
        this ICommandBuffer cmdBuf,
        RenderContext context,
        bool renderStatic = true,
        bool renderDynamic = true,
        bool renderInstancing = true
    )
    {
        if (context.Data is null)
            return 0;
        int drawCount = 0;
        if (renderStatic)
        {
            drawCount += cmdBuf.RenderOpaqueStatic(context, renderInstancing);
        }
        if (renderDynamic)
        {
            drawCount += cmdBuf.RenderOpaqueDynamic(context, renderInstancing);
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all opaque static meshes in the specified render context using the command buffer.
    /// </summary>
    /// <remarks>This method renders only opaque static meshes. Transparent or dynamic meshes are not
    /// affected.</remarks>
    /// <param name="cmdBuf">The command buffer used to record rendering commands.</param>
    /// <param name="context">The render context containing scene and mesh data to be rendered.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported meshes; otherwise, <see langword="false"/>.</param>
    /// <returns>The number of opaque static mesh draw calls issued. Returns 0 if there are no opaque static meshes to render.</returns>
    public static int RenderOpaqueStatic(
        this ICommandBuffer cmdBuf,
        RenderContext context,
        bool renderInstancing = true
    )
    {
        if (context.Data is null)
            return 0;
        return cmdBuf.RenderStatic(context, context.Data.MeshDrawsOpaque, renderInstancing);
    }

    /// <summary>
    /// Renders static mesh draw data using the specified command buffer and render context.
    /// </summary>
    /// <remarks>This method iterates over all material types in the provided mesh draw data and issues
    /// indirect indexed draw calls for each non-empty range. The method binds the appropriate render pipeline for each
    /// material type unless the context is configured to use an external pipeline.</remarks>
    /// <param name="cmdBuf">The command buffer used to record rendering commands.</param>
    /// <param name="context">The render context that provides pipeline and buffer information for rendering.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering; <see langword="false"/> to render without instancing.
    /// Instancing can improve performance when rendering multiple copies of the same mesh.</param>
    /// <returns>The total number of draw calls issued. Returns 0 if there is no mesh data to render.</returns>
    public static int RenderStatic(
        this ICommandBuffer cmdBuf,
        RenderContext context,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true
    )
    {
        if (context.Data is null)
        {
            return 0;
        }
        int drawCount = 0;
        cmdBuf.BindIndexBuffer(context.Data.StaticMeshIndexData.Buffer, IndexFormat.UI32);
        if (meshDrawData.HasStaticMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeStaticMesh(materialType);
                if (range.Empty)
                    continue;
                if (!context.UseExternalPipeline)
                {
                    var pipeline = context.Data.GetMaterialPipeline(materialType);
                    cmdBuf.BindRenderPipeline(pipeline);
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = context.FPConstantsBuffer.GpuAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += (int)range.Count;
            }
        }

        if (renderInstancing && meshDrawData.HasStaticInstancingMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeStaticMeshInstancing(materialType);
                if (range.Empty)
                    continue;
                if (!context.UseExternalPipeline)
                {
                    var pipeline = context.Data.GetMaterialPipeline(materialType);
                    cmdBuf.BindRenderPipeline(pipeline);
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = context.FPConstantsBuffer.GpuAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += (int)range.Count;
            }
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all opaque dynamic mesh draws in the specified render context using the command buffer.
    /// </summary>
    /// <param name="cmdBuf">The command buffer used to record rendering commands.</param>
    /// <param name="context">The render context containing scene and draw data. Must not be null and must have valid mesh draw data.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported mesh draws; otherwise, <see
    /// langword="false"/> to render without instancing.</param>
    /// <returns>The number of opaque dynamic mesh draws rendered. Returns 0 if there is no mesh draw data in the context.</returns>
    public static int RenderOpaqueDynamic(
        this ICommandBuffer cmdBuf,
        RenderContext context,
        bool renderInstancing = true
    )
    {
        if (context.Data is null)
            return 0;

        return cmdBuf.RenderDynamic(context, context.Data.MeshDrawsOpaque, renderInstancing);
    }

    /// <summary>
    /// Renders all dynamic mesh draw commands in the specified context using the provided command buffer.
    /// </summary>
    /// <remarks>This method iterates over all material types in <paramref name="meshDrawData"/> and issues
    /// draw commands for each valid mesh render entry. If <paramref name="context"/> is configured to use an external
    /// pipeline, pipeline binding is skipped.</remarks>
    /// <param name="cmdBuf">The command buffer to which rendering commands are recorded. Must not be <c>null</c>.</param>
    /// <param name="context">The rendering context that provides pipeline and buffer information. Must not be <c>null</c> and must have valid
    /// <c>Data</c>.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render. Must not be <c>null</c>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering where supported; otherwise, <see langword="false"/> to
    /// render without instancing. The default is <see langword="true"/>.</param>
    /// <returns>The number of mesh draw commands rendered. Returns 0 if the context's data is <c>null</c> or if there are no
    /// drawable meshes.</returns>
    public static int RenderDynamic(
        this ICommandBuffer cmdBuf,
        RenderContext context,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true
    )
    {
        if (context.Data is null)
        {
            return 0;
        }
        int drawCount = 0;
        if (meshDrawData.HasDynamicMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeDynamicMesh(materialType);
                if (range.Empty)
                    continue;
                if (!context.UseExternalPipeline)
                {
                    var pipeline = context.Data.GetMaterialPipeline(materialType);
                    cmdBuf.BindRenderPipeline(pipeline);
                }

                for (var i = range.Start; i < range.Start + range.Count; i++)
                {
                    var meshId = meshDrawData.DrawCommands[(int)i].MeshId;
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
                            FpConstAddress = context.FPConstantsBuffer.GpuAddress,
                            DrawCommandIdxOffset = range.Start,
                            MeshDrawId = i,
                        }
                    );
                    cmdBuf.DrawIndexedIndirect(
                        meshDrawData.Buffer,
                        i * meshDrawData.Stride,
                        1,
                        meshDrawData.Stride
                    );
                    drawCount++;
                }
            }
        }

        if (renderInstancing && meshDrawData.HasDynamicInstancingMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeDynamicMeshInstancing(materialType);
                if (range.Empty)
                    continue;
                if (!context.UseExternalPipeline)
                {
                    var pipeline = context.Data.GetMaterialPipeline(materialType);
                    cmdBuf.BindRenderPipeline(pipeline);
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = context.FPConstantsBuffer.GpuAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += (int)range.Count;
            }
        }
        return drawCount;
    }
}
