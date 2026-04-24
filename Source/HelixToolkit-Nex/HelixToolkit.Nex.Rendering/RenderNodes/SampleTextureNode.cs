using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class RenderToFinalNode(Format outputFormat)
    : SampleTextureNode(SampleTextureMode.SAMPLE_ONLY, outputFormat)
{
    public override string Name => nameof(RenderToFinalNode);
    public override Color4 DebugColor => Color.Yellow;

    protected override bool BeginRender(in RenderResources res)
    {
        MinValue = res.Context.CameraParams.NearPlane;
        MaxValue = res.Context.CameraParams.FarPlane;
        return base.BeginRender(in res);
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(RenderToFinalNode),
            // TextureColorF16Current is the stable alias written by PostEffectsNode at the end
            // of its render loop. It always points to whichever ping-pong buffer holds the
            // final color result, regardless of how many effects ran.
            inputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.FinalOutputTexture, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.FinalOutputTexture];
                res.Pass.Colors[0].ClearColor = Color.Transparent;
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Current];
            },
            after: [nameof(PostEffectsNode)]
        );
    }
}

public sealed class DebugDepthBufferNode()
    : SampleTextureNode(SampleTextureMode.DebugDepth, RenderSettings.IntermediateTargetFormat)
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
            outputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[
                    SystemBufferNames.TextureColorF16Current
                ];
                res.Pass.Colors[0].ClearColor = Color.Transparent;
                res.Pass.Colors[0].LoadOp = LoadOp.DontCare;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureDepthF32];
            }
        );
    }
}

public sealed class DebugMeshIdNode()
    : SampleTextureNode(SampleTextureMode.DebugMeshId, RenderSettings.IntermediateTargetFormat)
{
    public override string Name => nameof(DebugDepthBufferNode);
    public override Color4 DebugColor => Color.Red;

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(DebugMeshIdNode),
            inputs: [new(SystemBufferNames.TextureEntityId, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[
                    SystemBufferNames.TextureColorF16Current
                ];
                res.Pass.Colors[0].ClearColor = Color.Black;
                res.Pass.Colors[0].LoadOp = LoadOp.DontCare;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureEntityId];
            }
        );
    }
}

public abstract class SampleTextureNode(SampleTextureMode mode, Format targetFormat) : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<SampleTextureNode>();
    private readonly SampleTextureMode _mode = mode;
    private readonly Format _targetFormat = targetFormat;
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerRef _sampler = SamplerRef.Null;

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
                SamplerId = _sampler,
                TextureId = deps.Textures[0].Index,
                MinValue = MinValue,
                MaxValue = MaxValue,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
    }

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError(
                "Context or ResourceManager is null, cannot set up SampleTextureNode."
            );
            return false;
        }
        _sampler = ResourceManager.SamplerRepository.GetOrCreate(SamplerStateDesc.PointClamp);
        return CreatePipeline();
    }

    private bool CreatePipeline()
    {
        if (Context is null || Renderer is null)
        {
            return false;
        }
        _logger.LogInformation(
            "Creating pipeline for {Mode} with target format {Format}",
            _mode,
            _targetFormat
        );
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
        using var vs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
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
        using var fs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
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
        _pipeline.Dispose();
        base.OnTeardown();
    }
}
