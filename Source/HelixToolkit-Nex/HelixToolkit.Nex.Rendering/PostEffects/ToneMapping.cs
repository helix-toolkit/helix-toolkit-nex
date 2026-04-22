using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.PostEffects;

public sealed class ToneMapping : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<ToneMapping>();
    private RenderPipelineResource _toneGammaPipeline = RenderPipelineResource.Null;
    private SamplerResource _toneMappingSampler = SamplerResource.Null;

    public override string Name => nameof(ToneMapping);

    public ToneMappingMode Mode { set; get; } = ToneMappingMode.ACESFilm;

    public bool EnableGammaCorrection { set; get; } = false;

    public override Color DebugColor => Color.Aquamarine;
    public override uint Priority => (uint)PostEffectPriority.ToneMapping;

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_toneGammaPipeline.Valid, "Tone mapping pipeline is not valid.");
        var cmdBuffer = res.CmdBuffer;
        res.Deps.Textures[0] = res.Textures[readSlot];
        res.Framebuf.Colors[0].Texture = res.Textures[writeSlot];
        cmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        cmdBuffer.BindRenderPipeline(_toneGammaPipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new ToneGammaPushConstants()
            {
                Enabled = 1,
                Exposure = 1f,
                HdrTextureId = res.Textures[readSlot].Index,
                SamplerId = _toneMappingSampler.Index,
                TonemapMode = (uint)Mode,
                GammaEnabled = EnableGammaCorrection ? 1u : 0,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
        cmdBuffer.EndRendering();
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        if (ResourceManager is null)
        {
            _logger.LogError("ResourceManager is null during tone mapping initialization.");
            return ResultCode.InvalidState;
        }
        CreateToneMappingPipeline();
        var samplerDesc = SamplerStateDesc.PointRepeat;
        _toneMappingSampler = ResourceManager.SamplerRepository.GetOrCreate(samplerDesc);
        if (!_toneMappingSampler.Valid || !_toneGammaPipeline.Valid)
        {
            return ResultCode.RuntimeError;
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _toneGammaPipeline.Dispose();
        _toneMappingSampler.Dispose();
        return ResultCode.Ok;
    }

    private ResultCode CreateToneMappingPipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during tone mapping pipeline creation.");
            return ResultCode.InvalidState;
        }
        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = "ToneMapping",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
        };
        var shaderCompiler = new ShaderCompiler();
        pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
            RenderSettings.IntermediateTargetFormat
        );

        var toneGammaShader = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psToneGamma.glsl")
        );
        if (!toneGammaShader.Success || toneGammaShader.Source == null)
        {
            _logger.LogError(
                "Failed to compile tone mapping shader: "
                    + string.Join("\n", toneGammaShader.Errors)
            );
            return ResultCode.CompileError;
        }
        using var fragmentShader = Context.CreateShaderModuleGlsl(
            toneGammaShader.Source,
            ShaderStage.Fragment,
            "ToneMapping_Fragment"
        );

        var vsQuad = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
        );

        if (!vsQuad.Success || vsQuad.Source == null)
        {
            _logger.LogError(
                "Failed to compile full-screen quad vertex shader: "
                    + string.Join("\n", vsQuad.Errors)
            );
            return ResultCode.CompileError;
        }
        using var vertexShader = Context.CreateShaderModuleGlsl(
            vsQuad.Source,
            ShaderStage.Vertex,
            "FullScreenQuad_Vertex"
        );
        pipelineDesc.VertexShader = vertexShader;
        pipelineDesc.FragmentShader = fragmentShader;
        _toneGammaPipeline = Context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_toneGammaPipeline.Valid);
        return ResultCode.Ok;
    }
}
