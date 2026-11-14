namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Renders a dynamic, raymarched sky atmosphere as the scene background.
/// <para>
/// Takes the same slot as <see cref="EnvironmentMapNode"/>: it runs at the end of
/// <see cref="RenderStage.Opaque"/> with a geometry-free full-screen triangle and a reversed-Z
/// read-only depth test, so only pixels not covered by opaque geometry are shaded. Each pixel's
/// world-space view ray is raymarched through the atmosphere (see
/// <c>Frag/psSkyAtmosphere.glsl</c> and <c>HxHeaders/Atmosphere.glsl</c>) and the linear HDR
/// result is written into the F16 scene colour target for <see cref="ToneMappingNode"/>.
/// </para>
/// <para>
/// Configuration lives on <see cref="RenderContext.SkyAtmosphere"/>; the node is a no-op while
/// the atmosphere is disabled, and <see cref="EnvironmentMapNode"/> skips itself while it is
/// enabled, so both nodes can stay in the pipeline.
/// </para>
/// </summary>
public sealed class SkyAtmosphereNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<SkyAtmosphereNode>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public override string Name => nameof(SkyAtmosphereNode);
    public override Color4 DebugColor => Color.DeepSkyBlue;

    public override string Description =>
        "Draws a raymarched sky atmosphere as the scene background.";

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError("Context or ResourceManager is null during SkyAtmosphereNode setup.");
            return false;
        }

        var shaderCompiler = new ShaderCompiler();

        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile full-screen quad vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return false;
        }
        using var vs = Context.CreateShaderModuleGlsl(
            vsResult.Source,
            ShaderStage.Vertex,
            "SkyAtmosphereNode_VS"
        );

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psSkyAtmosphere.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile sky atmosphere shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return false;
        }
        using var fs = Context.CreateShaderModuleGlsl(
            fsResult.Source,
            ShaderStage.Fragment,
            "SkyAtmosphereNode_FS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = nameof(SkyAtmosphereNode),
            // Full-screen triangle: no back-face culling needed.
            CullMode = CullMode.None,
            VertexShader = vs,
            FragmentShader = fs,
            DepthFormat = GraphicsSettings.DepthBufferFormat,
        };
        pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
            GraphicsSettings.IntermediateTargetFormat
        );

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        // Skipped in global wireframe mode: the depth pre-pass is not run, so the background
        // would be drawn over the wireframe lines.
        return _pipeline.Valid
            && !res.RenderContext.RenderParams.EnableGlobalWireframe
            && res.RenderContext.SkyAtmosphere.Enabled;
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        // Read-only depth from the opaque passes: the reversed-Z GREATER_EQUAL test lets the
        // full-screen triangle (clip Z = 0, i.e. far plane) pass only where no opaque geometry
        // was drawn.
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;

        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        if (ShouldDrawSpaceBackdrop(res.RenderContext))
        {
            res.Deps.PushTexture(res.RenderContext.EnvironmentMap.Texture.GetHandle());
        }
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Sky atmosphere pipeline is not valid.");

        // Atmosphere parameters and camera matrices live in the shared Forward+ constants
        // buffer, populated by PrepareNode.
        var fpConstAddress = res
            .Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(res.RenderContext.Context);

        var sky = res.RenderContext.SkyAtmosphere;
        var groundBlend = Math.Clamp(sky.GroundBlendAngle, 0f, MathF.PI / 2);

        res.CmdBuffer.BindRenderPipeline(_pipeline);
        res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
        res.CmdBuffer.PushConstants(
            new SkyAtmospherePushConstants
            {
                FpConstAddress = fpConstAddress,
                SpaceBackdrop = ShouldDrawSpaceBackdrop(res.RenderContext) ? 1u : 0u,
                GroundColor = sky.GroundColor,
                GroundBlendSin = MathF.Sin(groundBlend),
            }
        );
        res.CmdBuffer.Draw(3); // full-screen triangle
    }

    private static bool ShouldDrawSpaceBackdrop(RenderContext context)
    {
        return context.SkyAtmosphere.ShowEnvironmentMapAsSpace
            && context.EnvironmentMap.ShouldRender;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Opaque,
            nameof(SkyAtmosphereNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture)],
            after: [nameof(ForwardPlusOpaqueNode), nameof(ForwardPlusMaskNode)]
        );
    }
}
