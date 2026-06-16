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

    protected override bool CanRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            return false;
        }
        return context.Data.PointDrawStreams.GetStreams(DrawStreamType.Point).HasAny();
    }

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
        res.Pass.DepthState = DepthState.DefaultReversedZ;

        // Dependencies
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureColorF16Target]);
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);

        var streams = res.RenderContext.Data!.PointDrawStreams.GetStreams(DrawStreamType.Point);
        foreach (var stream in streams)
        {
            if (stream.Count == 0)
            {
                continue;
            }
            stream.Barrier(res.CmdBuffer);
        }
    }

    protected override void OnRender(in RenderResources res)
    {
        var streams = res.RenderContext.Data!.PointDrawStreams.GetStreams(DrawStreamType.Point);
        res.RenderContext.Statistics.DrawCalls += PointRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            streams
        );
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
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
            ],
            after: [nameof(ForwardPlusOpaqueNode)]
        );
    }

    #endregion
}
