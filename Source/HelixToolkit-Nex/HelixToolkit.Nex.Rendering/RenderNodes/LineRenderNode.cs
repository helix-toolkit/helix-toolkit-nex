namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class LineRenderNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<LineRenderNode>();
    public override string Name => nameof(LineRenderNode);

    public override Color4 DebugColor => Color.DarkKhaki;

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && Renderer is not null);
        return true;
    }

    protected override void OnTeardown()
    {
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            return false;
        }
        return context.Data.LineDrawStreams.GetStreams(DrawStreamType.Line).HasAny();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;

        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightGrid]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightIndex]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
        res.Pass.DepthState = DepthState.DefaultReversedZ;

        var streams = res.RenderContext.Data!.LineDrawStreams.GetStreams(DrawStreamType.Line);
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
        var streams = res.RenderContext.Data!.LineDrawStreams.GetStreams(DrawStreamType.Line);
        res.RenderContext.Statistics.DrawCalls += LineRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            streams
        );
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Particle,
            nameof(LineRenderNode),
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
            after: [nameof(ForwardPlusLightCulling)]
        );
    }
}
