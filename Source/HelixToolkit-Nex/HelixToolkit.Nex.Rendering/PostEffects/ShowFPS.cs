using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.PostEffects;

public sealed class ShowFPS : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<ShowFPS>();
    private const float AspectRatio = 3f; // Max digits to show is 3, so do a 3 / 1 aspect ratio.

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public override string Name => nameof(ShowFPS);
    public override Color DebugColor => Color.SandyBrown;

    public float Scale = 0.05f;
    public float MinSize { set; get; } = 64;
    public override uint Priority => (uint)PostEffectPriority.Other;

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_pipeline.Valid, "Tone mapping pipeline is not valid.");
        var cmdBuffer = res.CmdBuffer;
        (readSlot, writeSlot) = (writeSlot, readSlot); // Manually swap so FPS writes onto the correct texture.
        res.Deps.Textures[0] = res.Textures[writeSlot];
        res.Framebuf.Colors[0].Texture = res.Textures[writeSlot];
        cmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        var width = res.Context.WindowSize.Width * Scale;
        var height = res.Context.WindowSize.Height * Scale;
        var max = MathF.Max(MinSize, MathF.Max(width, height));
        cmdBuffer.BindViewport(new ViewportF(0, 0, max, max / AspectRatio));
        cmdBuffer.PushConstants((int)res.Context.Statistics.FramesPerSecond);
        cmdBuffer.Draw(3); // Full-screen triangle
        cmdBuffer.EndRendering();
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during tone mapping initialization.");
            return ResultCode.InvalidState;
        }
        CreatePipeline();
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _pipeline.Dispose();
        return ResultCode.Ok;
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
}
