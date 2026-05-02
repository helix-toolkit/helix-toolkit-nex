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

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.Store;

        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;
        res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawOpaque];
        res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferLightGrid];
        res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferLightIndex];
        res.Deps.Buffers[3] = res.Buffers[SystemBufferNames.BufferPBRProperties];
        res.Deps.Buffers[4] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
    }

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping forward+ opaque pass.");
            return false;
        }

        if (res.RenderContext.Data!.MeshDrawsOpaque.Count == 0)
            return false;

        return base.BeginRender(in res);
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
        res.RenderContext.Statistics.DrawCalls += RenderHelper.RenderOpaque(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context)
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
                    new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                    new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                    new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                    new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer),
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                ],
                outputs: [new(SystemBufferNames.TextureColorF16A, ResourceType.Texture)],
                after: [nameof(ForwardPlusLightCulling)]
            );
    }
}
