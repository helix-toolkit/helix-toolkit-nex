using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering;

public sealed class SampleTextureRenderer(SampleTextureMode mode, Format targetFormat) : Renderer
{
    private readonly SampleTextureMode _mode = mode;
    private readonly Format _targetFormat = targetFormat;
    private readonly Framebuffer _framebuffer = new();
    private readonly Dependencies _dependencies = new();
    private readonly RenderPass _renderPass = new();
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerResource _sampler = SamplerResource.Null;

    public override RenderStages Stage => RenderStages.End;

    public override string Name => nameof(SampleTextureRenderer);

    public override IEnumerable<string> GetInputs()
    {
        yield return RenderGraphBufferNames.TextureMeshId;
        yield return RenderGraphBufferNames.TextureDepth;
    }

    public override IEnumerable<string> GetOutputs()
    {
        yield return RenderGraphBufferNames.TextureOutput;
    }

    public float MinValue { set; get; } = 0.0f;
    public float MaxValue { set; get; } = 1.0f;

    protected override void OnRender(RenderContext context, ICommandBuffer cmdBuffer)
    {
        _framebuffer.Colors[0].Texture = context.SharedBuffers.TextureOutput;
        switch (_mode)
        {
            case SampleTextureMode.DebugMeshId:
                _dependencies.Textures[0] = context.SharedBuffers.TextureMeshId;
                break;
            case SampleTextureMode.DebugDepth:
                _dependencies.Textures[0] = context.SharedBuffers.TextureDepth;
                break;
        }

        cmdBuffer.BeginRendering(_renderPass, _framebuffer, _dependencies);
        cmdBuffer.PushDebugGroupLabel("DebugTexture", Color.Red);
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new SampleTexturePushConstants()
            {
                SamplerId = _sampler.Index,
                TextureId = _dependencies.Textures[0].Index,
                MinValue = MinValue,
                MaxValue = MaxValue,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
        cmdBuffer.PopDebugGroupLabel();
        cmdBuffer.EndRendering();
    }

    protected override bool OnSetup()
    {
        _renderPass.Colors[0].ClearColor = Color.Black;
        _renderPass.Colors[0].LoadOp = LoadOp.Clear;
        _renderPass.Colors[0].StoreOp = StoreOp.Store;
        _sampler = Context!.CreateSampler(SamplerStateDesc.PointClamp);
        return CreatePipeline();
    }

    private bool CreatePipeline()
    {
        if (Context is null || RenderManager is null)
        {
            return false;
        }
        var shaderCompiler = new ShaderCompiler();
        var result = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert.vsFullScreenQuad")
        );
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to compile vertex shader: {result.Errors}"
            );
        }
        using var vs = RenderManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            result.Source!,
            [],
            "FullScreenQuad_VS"
        );
        result = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag.psSampleTexture")
        );
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to compile fragment shader: {result.Errors}"
            );
        }
        using var fs = RenderManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            result.Source!,
            [],
            "SampleTexture_PS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragementShader = fs,
            DebugName = "TextureCopy",
            FrontFaceWinding = WindingMode.CCW,
        };
        pipelineDesc.Colors[0].Format = _targetFormat;
        pipelineDesc.Colors[0].BlendEnabled = false;
        pipelineDesc.WriteSpecInfo(0, (uint)_mode);

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    protected override void OnTearDown()
    {
        _sampler.Dispose();
        _pipeline.Dispose();
        base.OnTearDown();
    }
}
