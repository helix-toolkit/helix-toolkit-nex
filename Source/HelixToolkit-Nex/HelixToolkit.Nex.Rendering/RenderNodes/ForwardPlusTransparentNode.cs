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

    public bool UseLightCulling { set; get; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), transparent geometry is rendered using Weighted Blended
    /// Order-Independent Transparency (McGuire &amp; Bavoil 2013). A <see cref="WBOITCompositeNode"/>
    /// must be present in the render graph to resolve the result.
    /// When <see langword="false"/> the classic non-sorted alpha-blend path is used.
    /// </summary>
    public bool UseWBOIT { set; get; } = true;

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.Context;
        if (context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping forward+ transparent pass.");
            return false;
        }

        if (res.Context.Data!.MeshDrawsTransparent.Count == 0)
            return false;

        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
        if (!fpBuffer.Valid)
        {
            return false;
        }
        var fpData = new FPConstants
        {
            Enabled = UseLightCulling ? 1u : 0,
            TimeMs = res.Context.TimeMs,
            CameraPosition = context.CameraParams.Position,
            InverseViewProjection = context.CameraParams.InvViewProjection,
            ViewProjection = context.CameraParams.ViewProjection,
            View = context.CameraParams.View,
            InverseView = context.CameraParams.InvView,
            ScreenDimensions = new Vector2(context.WindowSize.Width, context.WindowSize.Height),
            DpiScale = context.DpiScale,
            MeshInfoBufferAddress = context.Data.MeshInfos.GpuAddress,
            MeshDrawBufferAddress = context.Data.MeshDrawsTransparent.GpuAddress,
            MaterialBufferAddress = context.Data.PBRPropertiesBuffer.Buffer.GpuAddress(
                context.Context
            ),
            DirectionalLightsBufferAddress =
                context.Data.DirectionalLights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferDirectionalLight]
                        .GpuAddress(context.Context)
                    : 0,
            LightBufferAddress = context.Data.Lights.GpuAddress,
            LightGridBufferAddress =
                context.Data.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightGrid].GpuAddress(context.Context)
                    : 0,
            LightIndexBufferAddress =
                context.Data.Lights.Count > 0
                    ? res.Buffers[SystemBufferNames.BufferLightIndex].GpuAddress(context.Context)
                    : 0,
            TileCountX = (uint)context.TileCountX,
            TileCountY = (uint)context.TileCountY,
            LightCount = (uint)context.Data.Lights.Count,
            TileSize = context.FPLightConfig.TileSize,
            MaxLightsPerTile = context.FPLightConfig.MaxLightsPerTile,
        };
        res.CmdBuffer.UpdateBuffer(fpBuffer, ref fpData);
        return base.BeginRender(in res);
    }

    protected override void OnRender(in RenderResources res)
    {
        res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
        res.Context.Statistics.DrawCalls += RenderHelper.RenderTransparent(in res);
    }

    protected override bool OnSetup()
    {
        return true;
    }

    protected override void OnTeardown()
    {
        base.OnTeardown();
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
            nameof(ForwardPlusTransparentNode),
            inputs:
            [
                new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer)
            ],
            outputs: [new(SystemBufferNames.TextureColorF16A, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Pass.Depth.LoadOp = LoadOp.Load;
                res.Pass.Depth.StoreOp = StoreOp.DontCare;

                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16A];
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawTransparent];
                res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
                res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferLightGrid];
                res.Deps.Buffers[3] = res.Buffers[SystemBufferNames.BufferLightIndex];
                res.Deps.Buffers[4] = res.Buffers[SystemBufferNames.BufferPBRProperties];
            },
            stage: RenderStage.Transparent
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
            nameof(ForwardPlusTransparentNode),
            inputs:
            [
                new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureWboitAccum, ResourceType.Texture),
                new(SystemBufferNames.TextureWboitRevealage, ResourceType.Texture),
            ],
            onSetup: (res) =>
            {
                // Depth: read-only from opaque prepass.
                res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Pass.Depth.LoadOp = LoadOp.Load;
                res.Pass.Depth.StoreOp = StoreOp.DontCare;

                // Color 0: WBOIT accumulation (RGBA16F).
                // Clear to (0, 0, 0, 0). Blend: ONE / ONE additive.
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureWboitAccum];
                res.Pass.Colors[0].ClearColor = new Color4(0, 0, 0, 0);
                res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;

                // Color 1: WBOIT revealage (R16F).
                // Clear to 1.0 (fully transparent). Blend: ZERO / ONE_MINUS_SRC_COLOR.
                res.Framebuf.Colors[1].Texture = res.Textures[
                    SystemBufferNames.TextureWboitRevealage
                ];
                res.Pass.Colors[1].ClearColor = new Color4(1, 1, 1, 1);
                res.Pass.Colors[1].LoadOp = LoadOp.Clear;
                res.Pass.Colors[1].StoreOp = StoreOp.Store;

                // Dependencies.
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawTransparent];
                res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
                res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferLightGrid];
                res.Deps.Buffers[3] = res.Buffers[SystemBufferNames.BufferLightIndex];
            },
            stage: RenderStage.Transparent
        );
    }
}
