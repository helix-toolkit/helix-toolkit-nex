using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class DebugDepthBufferNode(Format targetFormat = Format.RGBA_F16)
    : SampleTextureNode(SampleTextureMode.DebugDepth, targetFormat)
{
    public override string Name => nameof(DebugDepthBufferNode);
    public override Color4 DebugColor => Color.Red;

    protected override bool BeginRender(in RenderResources res)
    {
        MinValue = res.Context.CameraParams.NearPlane;
        MaxValue = res.Context.CameraParams.FarPlane;
        return base.BeginRender(in res);
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(DebugDepthBufferNode),
            inputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16];
                res.Pass.Colors[0].ClearColor = Color.Coral;
                res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
            }
        );
    }
}

public sealed class DebugMeshIdNode(Format targetFormat = Format.RGBA_F16)
    : SampleTextureNode(SampleTextureMode.DebugMeshId, targetFormat)
{
    public override string Name => nameof(DebugDepthBufferNode);
    public override Color4 DebugColor => Color.Red;

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(DebugMeshIdNode),
            inputs: [new(SystemBufferNames.TextureEntityId, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16];
                res.Pass.Colors[0].ClearColor = Color.Black;
                res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureEntityId];
            }
        );
    }
}

public abstract class SampleTextureNode(SampleTextureMode mode, Format targetFormat) : RenderNode
{
    private readonly SampleTextureMode _mode = mode;
    private readonly Format _targetFormat = targetFormat;
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerResource _sampler = SamplerResource.Null;

    public float MinValue { set; get; } = 0.0f;
    public float MaxValue { set; get; } = 1.0f;

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Pipeline is not valid.");
        var cmdBuffer = res.CmdBuffer;
        var deps = res.Deps;
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
