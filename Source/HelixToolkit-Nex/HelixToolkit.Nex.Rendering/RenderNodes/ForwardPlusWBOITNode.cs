namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class ForwardPlusWBOITNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusWBOITNode>();
    public override string Name => nameof(ForwardPlusWBOITNode);

    public override Color4 DebugColor => Color.DarkOrange;

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
        res.Pass.Depth.StoreOp = StoreOp.None;
        // Color 0: WBOIT accumulation (RGBA16F).
        // Clear to (0, 0, 0, 0). Blend: ONE / ONE additive.
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureWboitAccum];
        res.Pass.Colors[0].ClearColor = new Color4(0, 0, 0, 0);
        res.Pass.Colors[0].LoadOp = LoadOp.Clear;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;
        // Color 1: WBOIT revealage (R16F).
        // Clear to 1.0 (fully transparent). Blend: ZERO / ONE_MINUS_SRC_COLOR.
        res.Framebuf.Colors[2].Texture = res.Textures[SystemBufferNames.TextureWboitRevealage];
        res.Pass.Colors[2].ClearColor = new Color4(1, 1, 1, 1);
        res.Pass.Colors[2].LoadOp = LoadOp.Clear;
        res.Pass.Colors[2].StoreOp = StoreOp.Store;

        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;
        // Dependencies.
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightGrid]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightIndex]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
    }

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping forward+ transparent pass.");
            return false;
        }

        if (res.RenderContext.Data!.MeshDrawsTransparent.Count == 0)
            return false;

        return base.BeginRender(in res);
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
        res.RenderContext.Statistics.DrawCalls += RenderHelper.RenderTransparent(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            false
        );
        res.RenderContext.Statistics.DrawCalls += RenderHelper.RenderTransparent(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            true
        );
    }

    protected override void EndRender(in RenderResources res)
    {
        base.EndRender(res);
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferDirectionalLight, null)
            .AddBuffer(SystemBufferNames.BufferLights, null);

        AddWboitPass(graph);
    }

    /// <summary>
    /// Registers the WBOIT transparent pass that renders into separate accumulation and
    /// revealage textures. These are later resolved by <see cref="WBOITCompositeNode"/>.
    /// </summary>
    private void AddWboitPass(RenderGraph graph)
    {
        // Register the WBOIT intermediate textures in the render graph.
        graph
            .AddTexture(
                SystemBufferNames.TextureWboitAccum,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.RGBA_F16,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureWboitAccum
                    )
            )
            .AddTexture(
                SystemBufferNames.TextureWboitRevealage,
                p =>
                    p.Context.Context.CreateTexture2D(
                        Format.R_F16,
                        (uint)p.Context.WindowSize.Width,
                        (uint)p.Context.WindowSize.Height,
                        TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureWboitRevealage
                    )
            );

        graph.AddPass(
            RenderStage.Transparent,
            nameof(ForwardPlusTransparentNode),
            inputs:
            [
                new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer),
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureWboitAccum, ResourceType.Texture),
                new(SystemBufferNames.TextureWboitRevealage, ResourceType.Texture),
            ]
        );
    }
}
