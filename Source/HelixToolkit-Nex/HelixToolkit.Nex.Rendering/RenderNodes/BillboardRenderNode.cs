namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class BillboardRenderNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<BillboardRenderNode>();

    public override string Name => nameof(BillboardRenderNode);
    public override Color4 DebugColor => Color.CadetBlue;

    #region Setup / Teardown

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null)
        {
            _logger.LogError("Context or Renderer is null during BillboardRenderNode setup.");
            return false;
        }
        return true;
    }
    #endregion

    #region Render

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null. Skipping billboard rendering.");
            return false;
        }

        if (context.Data.BillboardData!.TotalBillboardCount == 0)
        {
            return false;
        }
        return base.BeginRender(res);
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        // Color 0: scene color (load existing opaque content)
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        // Color 1: entity ID (load existing mesh IDs, overwrite where billboards are closer)
        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;

        // Depth: read + write (billboards depth-test against meshes AND each other)
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.Store;

        // Dependencies
        res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Deps.Textures[1] = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];

        var billboards = res.RenderContext.Data!.BillboardData;
        foreach (var entry in billboards!.Data.Values)
        {
            if (!entry.Valid || entry.BillboardCount == 0)
            {
                continue;
            }
            res.CmdBuffer.Barrier(entry.DrawDataBuffer);
            res.CmdBuffer.Barrier(entry.DrawArgsBuffer);
        }
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.RenderContext is null || res.RenderContext.Data is null)
        {
            _logger.LogWarning("Billboard data is null. Skipping billboard rendering.");
            return;
        }
        var billboards = res.RenderContext.Data.BillboardData;
        foreach (var entry in billboards!.Data.Values)
        {
            if (!entry.Valid || entry.BillboardCount == 0)
            {
                continue;
            }
            var pipeline =
                res.RenderContext.Data.ResourceManager.BillboardMaterialManager.GetPipeline(
                    entry.MaterialId
                );
            Debug.Assert(pipeline.Valid, "Billboard render pipeline is not valid.");

            var fpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context);


            res.CmdBuffer.BindRenderPipeline(pipeline);
            res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);

            res.CmdBuffer.PushConstants(
                new BillboardRenderPC
                {
                    DrawDataAddress = entry.DrawDataBuffer,
                    FpConstAddress = fpConstAddress,
                }
            );

            res.CmdBuffer.DrawIndirect(
                entry.DrawArgsBuffer,
                0,
                1,
                BillboardDrawIndirectArgs.SizeInBytes
            );
        }
    }
    #endregion

    #region Render Graph

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Billboard,
            nameof(BillboardRenderNode),
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
