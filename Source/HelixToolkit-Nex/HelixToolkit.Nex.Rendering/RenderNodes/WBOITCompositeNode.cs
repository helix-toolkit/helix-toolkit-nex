using System.Diagnostics;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Fullscreen composite pass that resolves the WBOIT accumulation and revealage textures
/// produced by <see cref="ForwardPlusTransparentNode"/> (when <c>UseWBOIT</c> is enabled)
/// and blends the result over the opaque color buffer.
/// <para>
/// The resolve formula (McGuire &amp; Bavoil 2013):
/// <code>
///   color.rgb = accum.rgb / max(accum.a, 1e-5)
///   alpha     = 1.0 - revealage
/// </code>
/// The output is alpha-blended over the destination using
/// <c>Src = SrcAlpha, Dst = OneMinusSrcAlpha</c>.
/// </para>
/// </summary>
public sealed class WBOITCompositeNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<WBOITCompositeNode>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerResource _sampler = SamplerResource.Null;

    public override string Name => nameof(WBOITCompositeNode);
    public override Color4 DebugColor => new(0.2f, 0.8f, 0.4f, 1.0f);

    protected override bool BeginRender(in RenderResources res)
    {
        res.CmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        return true;
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "WBOIT composite pipeline is not valid.");
        var cmdBuffer = res.CmdBuffer;
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new WBOITCompositePushConstants
            {
                AccumTextureId = res.Deps.Textures[0].Index,
                RevealTextureId = res.Deps.Textures[1].Index,
                SamplerId = _sampler.Index,
            }
        );
        cmdBuffer.Draw(3); // Full-screen triangle
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.EndRendering();
    }

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null)
        {
            return false;
        }
        _sampler = Context.CreateSampler(SamplerStateDesc.PointClamp);

        var shaderCompiler = new ShaderCompiler();

        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert.vsFullScreenQuad")
        );
        if (!vsResult.Success)
        {
            _logger.LogError(
                "Failed to compile WBOIT composite vertex shader: {Errors}",
                vsResult.Errors
            );
            return false;
        }
        using var vs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source!,
            [],
            "WBOITComposite_VS"
        );

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag.psWBOITComposite")
        );
        if (!fsResult.Success)
        {
            _logger.LogError(
                "Failed to compile WBOIT composite fragment shader: {Errors}",
                fsResult.Errors
            );
            return false;
        }
        using var fs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source!,
            [],
            "WBOITComposite_PS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "WBOITComposite",
            FrontFaceWinding = WindingMode.CCW,
            CullMode = CullMode.None,
        };
        // Output onto the main color buffer with standard alpha blending so the resolved
        // transparent color composites correctly over whatever opaque content is already there.
        pipelineDesc.Colors[0] = ColorAttachment.CreateAlphaBlend(
            RenderSettings.IntermediateTargetFormat
        );

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        if (!_pipeline.Valid)
        {
            _logger.LogError("Failed to create WBOIT composite render pipeline.");
            return false;
        }
        return true;
    }

    protected override void OnTeardown()
    {
        _sampler.Dispose();
        _pipeline.Dispose();
        base.OnTeardown();
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(WBOITCompositeNode),
            inputs:
            [
                new(SystemBufferNames.TextureWboitAccum, ResourceType.Texture),
                new(SystemBufferNames.TextureWboitRevealage, ResourceType.Texture),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16A, ResourceType.Texture)],
            onSetup: (res) =>
            {
                // Render onto the main color target (which already has the opaque scene).
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16A];
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;

                // Bind the WBOIT textures as sampled inputs (no depth needed).
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureWboitAccum];
                res.Deps.Textures[1] = res.Textures[SystemBufferNames.TextureWboitRevealage];
            },
            after: [nameof(ForwardPlusTransparentNode)]
        );
    }
}
