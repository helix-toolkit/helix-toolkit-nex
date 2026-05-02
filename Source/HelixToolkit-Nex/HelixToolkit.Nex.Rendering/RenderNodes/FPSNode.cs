namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class FPSNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<FPSNode>();
    private const float AspectRatio = 3f; // Max digits to show is 3, so do a 3 / 1 aspect ratio.

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public override string Name => nameof(FPSNode);
    public override Color4 DebugColor => Color.SandyBrown;

    public float Scale = 0.05f;
    public float MinSize { set; get; } = 64;

    protected override bool OnSetup()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during tone mapping initialization.");
            return false;
        }
        return CreatePipeline().CheckResult() == ResultCode.Ok;
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Tone mapping pipeline is not valid.");
        var cmdBuffer = res.CmdBuffer;

        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        var width = res.RenderContext.WindowSize.Width * Scale;
        var height = res.RenderContext.WindowSize.Height * Scale;
        var max = MathF.Max(MinSize, MathF.Max(width, height));
        cmdBuffer.BindViewport(new ViewportF(0, 0, max, max / AspectRatio));
        cmdBuffer.PushConstants((int)res.RenderContext.Statistics.FramesPerSecond);
        cmdBuffer.Draw(3); // Full-screen triangle
    }

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during tone mapping pipeline creation.");
            return ResultCode.InvalidState;
        }
        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = "FPS",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
        };
        var shaderCompiler = new ShaderCompiler();

        var toneGammaShader = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psFPS.glsl")
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
            "FPS_Fragment"
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
        pipelineDesc.Colors[0] = ColorAttachment.CreateAlphaBlend(
            RenderSettings.IntermediateTargetFormat
        );
        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        Debug.Assert(_pipeline.Valid);
        return ResultCode.Ok;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPingPongGroup(
            PingPongGroups.ColorF16,
            SystemBufferNames.TextureColorF16A,
            SystemBufferNames.TextureColorF16B
        );

        graph.AddPingPongPass(
            RenderStage.Overlay,
            nameof(FPSNode),
            PingPongGroups.ColorF16,
            extraInputs: [],
            extraOutputs: []
        );
    }
}
