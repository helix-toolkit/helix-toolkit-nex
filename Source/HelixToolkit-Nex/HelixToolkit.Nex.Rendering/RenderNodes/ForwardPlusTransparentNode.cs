namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Renders transparent geometry using Forward+ tile-based light culling.
/// <para>
/// Supports two transparency modes controlled by <see cref="UseWBOIT"/>:
/// <list type="bullet">
/// <item><b>Classic (default)</b> — renders directly into the main color target with standard
/// alpha blending. No sorting; simple and fast, but order-dependent artifacts may appear.</item>
/// <item><b>WBOIT</b> — renders into dedicated accumulation (<see cref="SystemBufferNames.TextureWboitAccum"/>)
/// and revealage (<see cref="SystemBufferNames.TextureWboitRevealage"/>) textures using the
/// Weighted Blended OIT blend states. A subsequent <see cref="WBOITCompositeNode"/> resolves
/// and composites the result over the opaque color buffer. Order-independent.</item>
/// </list>
/// </para>
/// </summary>
public class ForwardPlusTransparentNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusTransparentNode>();
    public override string Name => nameof(ForwardPlusTransparentNode);

    public override Color4 DebugColor => Color.Green;

    /// <summary>
    /// When <see langword="true"/> (default), transparent geometry is rendered using Weighted Blended
    /// Order-Independent Transparency (McGuire &amp; Bavoil 2013). A <see cref="WBOITCompositeNode"/>
    /// must be present in the render graph to resolve the result.
    /// When <see langword="false"/> the classic non-sorted alpha-blend path is used.
    /// </summary>
    public bool UseWBOIT { set; get; } = true;

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

        if (UseWBOIT)
        {
            // Color 0: WBOIT accumulation (RGBA16F).
            // Clear to (0, 0, 0, 0). Blend: ONE / ONE additive.
            res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureWboitAccum];
            res.Pass.Colors[0].ClearColor = new Color4(0, 0, 0, 0);
            res.Pass.Colors[0].LoadOp = LoadOp.Clear;
            res.Pass.Colors[0].StoreOp = StoreOp.Store;
            // Color 1: WBOIT revealage (R16F).
            // Clear to 1.0 (fully transparent). Blend: ZERO / ONE_MINUS_SRC_COLOR.
            res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureWboitRevealage];
            res.Pass.Colors[1].ClearColor = new Color4(1, 1, 1, 1);
            res.Pass.Colors[1].LoadOp = LoadOp.Clear;
            res.Pass.Colors[1].StoreOp = StoreOp.Store;
        }
        else
        {
            // Color 0: Main color target (RGBA16F).
            // Clear: Load existing opaque result. Blend: SRC_ALPHA / ONE_MINUS_SRC_ALPHA.
            res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Current];
            res.Pass.Colors[0].LoadOp = LoadOp.Load;
            res.Pass.Colors[0].StoreOp = StoreOp.Store;
        }

        // Dependencies.
        res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawTransparent];
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
                .GpuAddress(res.RenderContext.Context)
        );
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferDirectionalLight, null)
            .AddBuffer(SystemBufferNames.BufferLights, null);

        if (UseWBOIT)
        {
            AddWboitPass(graph);
        }
        else
        {
            AddClassicPass(graph);
        }
    }

    /// <summary>
    /// Registers the classic (non-sorted) transparent pass that renders directly into the
    /// main color target with standard alpha blending.
    /// </summary>
    private void AddClassicPass(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Transparent,
            nameof(ForwardPlusTransparentNode),
            inputs:
            [
                new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16A, ResourceType.Texture)]
        );
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
                new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
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
