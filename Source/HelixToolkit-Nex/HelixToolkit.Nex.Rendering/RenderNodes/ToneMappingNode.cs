namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Standalone render node that performs HDR-to-LDR tone mapping and optional gamma correction.
/// <para>
/// Runs at <see cref="RenderStage.ToneMap"/> — after all HDR post-processing effects
/// (<see cref="RenderStage.PostProcess"/>) and before LDR overlay passes
/// (<see cref="RenderStage.Overlay"/>).  This makes the HDR/LDR boundary explicit in the
/// render graph so that future nodes (gizmos, editor widgets, etc.) can safely assume the
/// surface is already in LDR/sRGB space.
/// </para>
/// </summary>
public sealed class ToneMappingNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ToneMappingNode>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerRef _sampler = SamplerRef.Null;

    public override string Name => nameof(ToneMappingNode);
    public override Color4 DebugColor => Color.Aquamarine;

    /// <summary>Gets or sets the tone-mapping operator to apply.</summary>
    public ToneMappingMode Mode { get; set; } = ToneMappingMode.ACESFilm;

    /// <summary>Gets or sets the scene exposure multiplier.</summary>
    public float Exposure { get; set; } = 1f;

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError("Context or ResourceManager is null during ToneMappingNode setup.");
            return false;
        }

        _sampler = ResourceManager.SamplerRepository.GetOrCreate(SamplerStateDesc.PointRepeat);

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
            "ToneMappingNode_VS"
        );

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psToneGamma.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile tone mapping shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return false;
        }
        using var fs = Context.CreateShaderModuleGlsl(
            fsResult.Source,
            ShaderStage.Fragment,
            "ToneMappingNode_FS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = nameof(ToneMappingNode),
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
            VertexShader = vs,
            FragmentShader = fs,
        };
        pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
            RenderSettings.IntermediateTargetFormat
        );

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
    }

    protected override bool BeginRender(in RenderResources res)
    {
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;
        res.Deps.Textures[0] = res.RenderContext.TextureColorF16Current;
        res.RenderContext.SwapIntermediateBuffers();
        res.Framebuf.Colors[0].Texture = res.RenderContext.TextureColorF16Current;
        res.CmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        return true;
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Tone mapping pipeline is not valid.");
        res.CmdBuffer.BindRenderPipeline(_pipeline);
        res.CmdBuffer.BindDepthState(DepthState.Disabled);
        res.CmdBuffer.PushConstants(
            new ToneGammaPushConstants
            {
                Enabled = 1,
                Exposure = Exposure,
                HdrTextureId = res.Deps.Textures[0].Index,
                SamplerId = _sampler.GetHandle().Index,
                TonemapMode = (uint)Mode,
                GammaEnabled = res.RenderContext.RenderParams.EnableGammaCorrection ? 1u : 0,
            }
        );
        res.CmdBuffer.Draw(3); // full-screen triangle
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPingPongGroup(
            PingPongGroups.ColorF16,
            SystemBufferNames.TextureColorF16A,
            SystemBufferNames.TextureColorF16B
        );

        graph.AddPingPongPass(
            nameof(ToneMappingNode),
            PingPongGroups.ColorF16,
            extraInputs: [],
            extraOutputs: [],
            stage: RenderStage.ToneMap
        );
    }
}
