namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class PointRenderNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<PointRenderNode>();

    public override string Name => nameof(PointRenderNode);
    public override Color4 DebugColor => Color.Cornsilk;

    #region Setup / Teardown

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && Renderer is not null);
        return true;
    }
    #endregion

    #region Render

    protected override void OnSetupRender(in RenderResources res)
    {
        // Color 0: scene color (load existing opaque content)
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        // Color 1: entity ID (load existing mesh IDs, overwrite where points are closer)
        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;

        // Depth: read + write (points depth-test against meshes AND each other)
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.Store;

        // Dependencies
        res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Deps.Textures[1] = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
    }

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null. Skipping point rendering.");
            return false;
        }
        var points = context.Data.PointCloudData;
        if (points is null || points.TotalPointCount == 0)
        {
            return false;
        }
        foreach (var entry in points!.Data.Values)
        {
            if (!entry.Valid)
            {
                continue;
            }
            res.CmdBuffer.Barrier(entry.DrawArgsBuffer);
            res.CmdBuffer.Barrier(entry.DrawDataBuffer);
        }
        return base.BeginRender(res);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.RenderContext is null || res.RenderContext.Data is null)
        {
            _logger.LogWarning("Point data is null. Skipping point culling.");
            return;
        }
        var points = res.RenderContext.Data.PointCloudData;
        foreach (var entry in points!.Data.Values)
        {
            if (entry.PointCount == 0)
            {
                continue;
            }
            var pipeline = res.RenderContext.Data.ResourceManager.PointMaterialManager.GetPipeline(
                entry.MaterialId
            );
            Debug.Assert(pipeline.Valid, "Point render pipeline is not valid.");
            res.CmdBuffer.BindRenderPipeline(pipeline);
            res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);

            res.CmdBuffer.PushConstants(
                new PointRenderPC
                {
                    DrawDataAddress = entry.DrawDataBuffer,
                    FpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                        .GpuAddress(res.RenderContext.Context),
                }
            );

            res.CmdBuffer.DrawIndirect(
                entry.DrawArgsBuffer,
                0,
                1,
                PointDrawIndirectArgs.SizeInBytes
            );
        }
    }
    #endregion

    #region Render Graph

    public override void AddToGraph(RenderGraph graph)
    {
        // Register point-specific GPU buffers
        graph.AddPass(
            RenderStage.Particle,
            nameof(PointRenderNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture)],
            after: [nameof(ForwardPlusOpaqueNode)]
        );
    }

    #endregion
}
