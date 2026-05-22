using HelixToolkit.Nex.Rendering.DrawStreams;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class ForwardPlusOpaqueNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusOpaqueNode>();
    public override string Name => nameof(ForwardPlusOpaqueNode);

    public override Color4 DebugColor => Color.Green;

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
        return context.Data.DrawStreams.GetStreamsCore(DrawStreamCategory.Opaque).HasAny();
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
        res.Pass.DepthState = res.RenderContext.RenderParams.EnableGlobalWireframe
            ? DepthState.ReadOnlyInvZBias
            : DepthState.ReadOnlyInvZ;

        var streams = res.RenderContext.Data!.DrawStreams.GetStreamsCore(DrawStreamCategory.Opaque);
        foreach (var stream in streams)
            stream.Barrier(res.CmdBuffer);
    }

    protected override void OnRender(in RenderResources res)
    {
        var streams = res.RenderContext.Data!.DrawStreams.GetStreamsCore(DrawStreamCategory.Opaque);
        res.RenderContext.Statistics.DrawCalls += MeshRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            streams,
            res.RenderContext.RenderParams.EnableGlobalWireframe
                ? MaterialPassType.Wireframe
                : MaterialPassType.Opaque
        );
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferDirectionalLight, null)
            .AddBuffer(SystemBufferNames.BufferLights, null)
            .AddPass(
                RenderStage.Opaque,
                nameof(ForwardPlusOpaqueNode),
                inputs:
                [
                    new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                    new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer),
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
