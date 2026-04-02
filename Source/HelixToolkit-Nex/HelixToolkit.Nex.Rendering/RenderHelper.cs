namespace HelixToolkit.Nex.Rendering;

public static class RenderHelper
{
    private static readonly ILogger _logger = LogManager.Create("RenderHelper");

    /// <summary>
    /// Renders all opaque objects in the specified <see cref="RenderContext"/> using the provided command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="renderStatic"><see langword="true"/> to render static opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderDynamic"><see langword="true"/> to render dynamic opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable GPU instancing for supported objects; otherwise, <see langword="false"/>.
    /// Defaults to <see langword="true"/>.</param>
    /// <returns>The total number of opaque draw calls issued. Returns 0 if the context contains no data or if no objects are
    /// rendered.</returns>
    public static uint RenderOpaque(
        in RenderResources res,
        bool renderStatic = true,
        bool renderDynamic = true,
        bool renderInstancing = true
    )
    {
        if (res.Context.Data is null)
            return 0;
        uint drawCount = 0;
        if (renderStatic)
        {
            drawCount += RenderOpaqueStatic(in res, renderInstancing);
        }
        if (renderDynamic)
        {
            drawCount += RenderOpaqueDynamic(in res, renderInstancing);
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all opaque static meshes in the specified render context using the command buffer.
    /// </summary>
    /// <remarks>This method renders only opaque static meshes. Transparent or dynamic meshes are not
    /// affected.</remarks>
    /// <param name="res">The render resources.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported meshes; otherwise, <see langword="false"/>.</param>
    /// <returns>The number of opaque static mesh draw calls issued. Returns 0 if there are no opaque static meshes to render.</returns>
    public static uint RenderOpaqueStatic(in RenderResources res, bool renderInstancing = true)
    {
        if (res.Context.Data is null)
            return 0;
        return RenderStatic(in res, res.Context.Data.MeshDrawsOpaque, renderInstancing);
    }

    /// <summary>
    /// Renders all opaque dynamic mesh draws in the specified render context using the command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported mesh draws; otherwise, <see
    /// langword="false"/> to render without instancing.</param>
    /// <returns>The number of opaque dynamic mesh draws rendered. Returns 0 if there is no mesh draw data in the context.</returns>
    public static uint RenderOpaqueDynamic(in RenderResources res, bool renderInstancing = true)
    {
        if (res.Context.Data is null)
            return 0;

        return RenderDynamic(in res, res.Context.Data.MeshDrawsOpaque, renderInstancing);
    }

    /// <summary>
    /// Renders all transparent objects in the specified <see cref="RenderContext"/> using the provided command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="renderStatic"><see langword="true"/> to render static opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderDynamic"><see langword="true"/> to render dynamic opaque objects; otherwise, <see langword="false"/>. Defaults to <see
    /// langword="true"/>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable GPU instancing for supported objects; otherwise, <see langword="false"/>.
    /// Defaults to <see langword="true"/>.</param>
    /// <returns>The total number of opaque draw calls issued. Returns 0 if the context contains no data or if no objects are
    /// rendered.</returns>
    public static uint RenderTransparent(
        in RenderResources res,
        bool renderStatic = true,
        bool renderDynamic = true,
        bool renderInstancing = true
    )
    {
        if (res.Context.Data is null)
            return 0;
        uint drawCount = 0;
        if (renderStatic)
        {
            drawCount += RenderTransparentStatic(in res, renderInstancing);
        }
        if (renderDynamic)
        {
            drawCount += RenderTransparentDynamic(in res, renderInstancing);
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all transparent static meshes in the specified render context using the command buffer.
    /// </summary>
    /// <remarks>This method renders only transparent static meshes. Transparent or dynamic meshes are not
    /// affected.</remarks>
    /// <param name="res">The render resources.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported meshes; otherwise, <see langword="false"/>.</param>
    /// <returns>The number of transparent static mesh draw calls issued. Returns 0 if there are no transparent static meshes to render.</returns>
    public static uint RenderTransparentStatic(in RenderResources res, bool renderInstancing = true)
    {
        if (res.Context.Data is null)
            return 0;
        return RenderStatic(
            in res,
            res.Context.Data.MeshDrawsTransparent,
            renderInstancing,
            MaterialPassType.Transparent
        );
    }

    /// <summary>
    /// Renders all transparent dynamic mesh draws in the specified render context using the command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable hardware instancing for supported mesh draws; otherwise, <see
    /// langword="false"/> to render without instancing.</param>
    /// <returns>The number of transparent dynamic mesh draws rendered. Returns 0 if there is no mesh draw data in the context.</returns>
    public static uint RenderTransparentDynamic(
        in RenderResources res,
        bool renderInstancing = true
    )
    {
        if (res.Context.Data is null)
            return 0;

        return RenderDynamic(
            in res,
            res.Context.Data.MeshDrawsTransparent,
            renderInstancing,
            MaterialPassType.Transparent
        );
    }

    /// <summary>
    /// Renders static mesh draw data using the specified command buffer and render context.
    /// </summary>
    /// <remarks>This method iterates over all material types in the provided mesh draw data and issues
    /// indirect indexed draw calls for each non-empty range. The method binds the appropriate render pipeline for each
    /// material type unless the context is configured to use an external pipeline.</remarks>
    /// <param name="res">The render resources.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering; <see langword="false"/> to render without instancing.
    /// Instancing can improve performance when rendering multiple copies of the same mesh.</param>
    /// <param name="passType"/> specifies the material pass type to use when binding materials. </param>
    /// <returns>The total number of draw calls issued. Returns 0 if there is no mesh data to render.</returns>
    public static uint RenderStatic(
        in RenderResources res,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true,
        MaterialPassType passType = MaterialPassType.Opaque
    )
    {
        if (res.Context.Data is null)
        {
            return 0;
        }
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.Context;
        var fpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(context.Context);
        cmdBuf.BindIndexBuffer(context.Data.StaticMeshIndexData.Buffer, IndexFormat.UI32);
        if (meshDrawData.HasStaticMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeStaticMesh(materialType);
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
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = fpConstAddress,
                        CustomMaterialBufferAddress = customMaterialBufferAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += range.Count;
            }
        }

        if (renderInstancing && meshDrawData.HasStaticInstancingMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeStaticMeshInstancing(materialType);
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
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = fpConstAddress,
                        CustomMaterialBufferAddress = customMaterialBufferAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += range.Count;
            }
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all dynamic mesh draw commands in the specified context using the provided command buffer.
    /// </summary>
    /// <remarks>This method iterates over all material types in <paramref name="meshDrawData"/> and issues
    /// draw commands for each valid mesh render entry. If <paramref name="context"/> is configured to use an external
    /// pipeline, pipeline binding is skipped.</remarks>
    /// <param name="res">The render resources.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render. Must not be <c>null</c>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering where supported; otherwise, <see langword="false"/> to
    /// render without instancing. The default is <see langword="true"/>.</param>
    /// <param name="passType"/> specifies the material pass type to use when binding materials. </param>
    /// <returns>The number of mesh draw commands rendered. Returns 0 if the context's data is <c>null</c> or if there are no
    /// drawable meshes.</returns>
    public static uint RenderDynamic(
        in RenderResources res,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true,
        MaterialPassType passType = MaterialPassType.Opaque
    )
    {
        if (res.Context.Data is null)
        {
            return 0;
        }
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.Context;
        var fpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(context.Context);
        if (meshDrawData.HasDynamicMesh)
        {
            foreach (var materialType in meshDrawData.MaterialTypes)
            {
                var range = meshDrawData.GetRangeDynamicMesh(materialType);
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
                            FpConstAddress = fpConstAddress,
                            CustomMaterialBufferAddress = customMaterialBufferAddress,
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
                }
                cmdBuf.PushConstants(
                    new MeshDrawPushConstant
                    {
                        FpConstAddress = fpConstAddress,
                        CustomMaterialBufferAddress = customMaterialBufferAddress,
                        DrawCommandIdxOffset = range.Start,
                    }
                );
                cmdBuf.DrawIndexedIndirect(
                    meshDrawData.Buffer,
                    range.Start * meshDrawData.Stride,
                    range.Count,
                    meshDrawData.Stride
                );
                drawCount += range.Count;
            }
        }
        return drawCount;
    }

    public static bool IsDynamic(this MeshDraw meshDraw)
    {
        return (meshDraw.DrawType & 0x1u) != 0u;
    }

    public static bool IsInstancing(this MeshDraw meshDraw)
    {
        return (meshDraw.DrawType & 0x2u) != 0u;
    }
}
