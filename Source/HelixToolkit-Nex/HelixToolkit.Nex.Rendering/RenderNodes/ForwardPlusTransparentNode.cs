namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Renders transparent geometry using Forward+ tile-based light culling.
/// </summary>
public class ForwardPlusTransparentNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusTransparentNode>();
    public override string Name => nameof(ForwardPlusTransparentNode);

    public override Color4 DebugColor => Color.Orange;

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
        return context.Data.MeshDrawStreams.GetStreams(DrawStreamType.Transparent).HasAny();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;

        // Color 0: Main color target (RGBA16F).
        // Clear: Load existing opaque result. Blend: SRC_ALPHA / ONE_MINUS_SRC_ALPHA.
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        // Dependencies.
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightGrid]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightIndex]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
        res.Pass.DepthState = res.RenderContext.RenderParams.EnableGlobalWireframe
            ? DepthState.ReadOnlyInvZBias
            : DepthState.ReadOnlyInvZ;
        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;

        var streams = res.RenderContext.Data!.MeshDrawStreams.GetStreams(
            DrawStreamType.Transparent
        );
        foreach (var stream in streams)
        { stream.Barrier(res.CmdBuffer); }
    }

    protected override void OnRender(in RenderResources res)
    {
        var streams = res.RenderContext.Data!.MeshDrawStreams.GetStreams(
            DrawStreamType.Transparent
        );
        res.RenderContext.Statistics.DrawCalls += MeshRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            streams,
            res.RenderContext.RenderParams.EnableGlobalWireframe
                ? MaterialPassType.Wireframe
                : MaterialPassType.Transparent
        );
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Transparent,
            nameof(ForwardPlusTransparentNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
            ]
        );
    }
}
