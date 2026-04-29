namespace HelixToolkit.Nex.Rendering;

public static class RenderHelper
{
    private static readonly ILogger _logger = LogManager.Create("RenderHelper");

    /// <summary>
    /// Renders all opaque objects in the specified <see cref="RenderContext"/> using the provided command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="fpConstAddress">The GPU address of the forward+ constants buffer.</param>
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
        ulong fpConstAddress,
        bool renderStatic = true,
        bool renderDynamic = true,
        bool renderInstancing = true
    )
    {
        if (res.RenderContext.Data is null)
            return 0;
        uint drawCount = 0;
        if (renderStatic)
        {
            drawCount += RenderStatic(
                in res,
                fpConstAddress,
                res.RenderContext.Data.MeshDrawsOpaque,
                renderInstancing,
                MaterialPassType.Opaque
            );
        }
        if (renderDynamic)
        {
            drawCount += RenderDynamic(
                in res,
                fpConstAddress,
                res.RenderContext.Data.MeshDrawsOpaque,
                renderInstancing,
                MaterialPassType.Opaque
            );
        }
        return drawCount;
    }

    /// <summary>
    /// Renders all transparent objects in the specified <see cref="RenderContext"/> using the provided command buffer.
    /// </summary>
    /// <param name="res">The render resources.</param>
    /// <param name="fpConstAddress">The GPU address of the forward+ constants buffer.</param>
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
        ulong fpConstAddress,
        bool renderStatic = true,
        bool renderDynamic = true,
        bool renderInstancing = true
    )
    {
        if (res.RenderContext.Data is null)
            return 0;
        uint drawCount = 0;
        if (renderStatic)
        {
            drawCount += RenderStatic(
                in res,
                fpConstAddress,
                res.RenderContext.Data.MeshDrawsTransparent,
                renderInstancing,
                MaterialPassType.Transparent
            );
        }
        if (renderDynamic)
        {
            drawCount += RenderDynamic(
                in res,
                fpConstAddress,
                res.RenderContext.Data.MeshDrawsTransparent,
                renderInstancing,
                MaterialPassType.Transparent
            );
        }
        return drawCount;
    }

    /// <summary>
    /// Renders static mesh draw data using the specified command buffer and render context.
    /// </summary>
    /// <remarks>This method iterates over all material types in the provided mesh draw data and issues
    /// indirect indexed draw calls for each non-empty range. The method binds the appropriate render pipeline for each
    /// material type unless the context is configured to use an external pipeline.</remarks>
    /// <param name="res">The render resources.</param>
    /// <param name="fpConstAddress">The GPU address of the forward+ constants buffer.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering; <see langword="false"/> to render without instancing.
    /// Instancing can improve performance when rendering multiple copies of the same mesh.</param>
    /// <param name="passType"/> specifies the material pass type to use when binding materials. </param>
    /// <returns>The total number of draw calls issued. Returns 0 if there is no mesh data to render.</returns>
    public static uint RenderStatic(
        in RenderResources res,
        ulong fpConstAddress,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true,
        MaterialPassType passType = MaterialPassType.Opaque
    )
    {
        if (res.RenderContext.Data is null)
        {
            return 0;
        }
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.RenderContext;
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
                        MeshDrawBufferAddress = meshDrawData.Buffer.GpuAddress(res.RenderContext.Context),
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
                        MeshDrawBufferAddress = meshDrawData.Buffer.GpuAddress(res.RenderContext.Context),
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
    /// <param name="fpConstAddress">The GPU address of the forward+ constants buffer.</param>
    /// <param name="meshDrawData">The mesh draw data containing geometry and material information to render. Must not be <c>null</c>.</param>
    /// <param name="renderInstancing"><see langword="true"/> to enable instanced rendering where supported; otherwise, <see langword="false"/> to
    /// render without instancing. The default is <see langword="true"/>.</param>
    /// <param name="passType"/> specifies the material pass type to use when binding materials. </param>
    /// <returns>The number of mesh draw commands rendered. Returns 0 if the context's data is <c>null</c> or if there are no
    /// drawable meshes.</returns>
    public static uint RenderDynamic(
        in RenderResources res,
        ulong fpConstAddress,
        IMeshDrawData meshDrawData,
        bool renderInstancing = true,
        MaterialPassType passType = MaterialPassType.Opaque
    )
    {
        if (res.RenderContext.Data is null)
        {
            return 0;
        }
        uint drawCount = 0;
        var cmdBuf = res.CmdBuffer;
        var context = res.RenderContext;
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
                    if (meshDrawData.DrawCommands[(int)i].IndexCount == 0)
                    {
                        _logger.LogError(
                            "MeshDrawData contains a draw command with zero indices at index {Id}, skipping",
                            i
                        );
                        continue;
                    }
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
                            MeshDrawBufferAddress = meshDrawData.Buffer.GpuAddress(res.RenderContext.Context),
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
                            MeshDrawBufferAddress = meshDrawData.Buffer.GpuAddress(res.RenderContext.Context),
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
