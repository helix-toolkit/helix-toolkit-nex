using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering;

public sealed class DebugDepthBufferNode(Format targetFormat = Format.RGBA_F16)
    : SampleTextureNode(SampleTextureMode.DebugDepth, targetFormat)
{
    public override string Name => nameof(DebugDepthBufferNode);
    public override Color4 DebugColor => Color.Red;

    protected override bool BeginRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        RenderPass pass,
        Framebuffer framebuf,
        Dependencies deps
    )
    {
        MinValue = context.CameraParams.NearPlane;
        MaxValue = context.CameraParams.FarPlane;
        return base.BeginRender(context, cmdBuffer, pass, framebuf, deps);
    }
}

public sealed class DebugMeshIdNode(Format targetFormat = Format.RGBA_F16)
    : SampleTextureNode(SampleTextureMode.DebugMeshId, targetFormat)
{
    public override string Name => nameof(DebugDepthBufferNode);
    public override Color4 DebugColor => Color.Red;
}

public abstract class SampleTextureNode(SampleTextureMode mode, Format targetFormat) : RenderNode
{
    private readonly SampleTextureMode _mode = mode;
    private readonly Format _targetFormat = targetFormat;
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerResource _sampler = SamplerResource.Null;

    public float MinValue { set; get; } = 0.0f;
    public float MaxValue { set; get; } = 1.0f;

    protected override void OnRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        Dependencies deps
    )
    {
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new SampleTexturePushConstants()
            {
                SamplerId = _sampler.Index,
                TextureId = deps.Textures[0].Index,
                MinValue = MinValue,
                MaxValue = MaxValue,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
    }

    protected override bool OnSetup()
    {
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
            FragmentShader = fs,
            DebugName = "TextureCopy",
            FrontFaceWinding = WindingMode.CCW,
        };
        pipelineDesc.Colors[0].Format = _targetFormat;
        pipelineDesc.Colors[0].BlendEnabled = false;
        pipelineDesc.WriteSpecInfo(0, (uint)_mode);

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    protected override void OnTeardown()
    {
        _sampler.Dispose();
        _pipeline.Dispose();
        base.OnTeardown();
    }
}
