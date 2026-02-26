namespace HelixToolkit.Nex.Rendering.PostEffects;

public sealed class ToneMapping : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<ToneMapping>();
    private RenderPipelineResource _toneGammePipeline = RenderPipelineResource.Null;
    private SamplerResource _toneMappingSampler = SamplerResource.Null;

    public override string Name => nameof(ToneMapping);

    public override void Apply(RenderContext context, ICommandBuffer cmdBuffer, Dependencies deps)
    {
        cmdBuffer.BindRenderPipeline(_toneGammePipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new ToneGammaPushConstants()
            {
                Enabled = 1,
                Exposure = 1f,
                HdrTextureId = deps.Textures[0].Index,
                SamplerId = _toneMappingSampler.Index,
                TonemapMode = (uint)ToneMappingMode.Uncharted2,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during tone mapping initialization.");
            return ResultCode.InvalidState;
        }
        CreateToneMappingPipeline();
        var samplerDesc = SamplerStateDesc.PointRepeat;
        _toneMappingSampler = Context.CreateSampler(samplerDesc);
        if (!_toneMappingSampler.Valid || !_toneGammePipeline.Valid)
        {
            return ResultCode.RuntimeError;
        }
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _toneGammePipeline.Dispose();
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
        pipelineDesc.Colors[0].Format = Format.BGRA_SRGB8;

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
        _toneGammePipeline = Context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_toneGammePipeline.Valid);
        return ResultCode.Ok;
    }
}
