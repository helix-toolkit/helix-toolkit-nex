namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Renders an HDR environment cubemap as the scene background (skybox).
/// <para>
/// Runs at the end of <see cref="RenderStage.Opaque"/> — after opaque and alpha-mask
/// geometry — so it only fills the background pixels that no opaque geometry has
/// covered. It uses a geometry-free full-screen triangle and reconstructs the
/// world-space view ray per pixel from the inverse view-projection matrix, then
/// samples the cubemap (see <c>Frag/psEnvironmentMap.glsl</c>). Linear HDR radiance
/// is written into the F16 scene colour target so the background is tone-mapped
/// together with lit geometry by <see cref="ToneMappingNode"/>.
/// </para>
/// <para>
/// Configuration lives on <see cref="RenderContext.EnvironmentMap"/>; the node is a
/// no-op while no valid cubemap is assigned or the config is disabled, so it is safe
/// to keep in the default pipeline.
/// </para>
/// </summary>
public sealed class EnvironmentMapNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<EnvironmentMapNode>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private SamplerRef _defaultSampler = SamplerRef.Null;

    public override string Name => nameof(EnvironmentMapNode);
    public override Color4 DebugColor => Color.SkyBlue;

    public override string Description =>
        "Draws an HDR environment cubemap as the scene background.";

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError(
                "Context or ResourceManager is null during EnvironmentMapNode setup."
            );
            return false;
        }

        // Fallback sampler used when the config does not provide one. Linear + clamp with
        // mipmapping so the environment blur (mip LOD) control works out of the box.
        _defaultSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.LinearClamp.DebugName,
            SamplerStateDesc.LinearClamp
        );

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
            "EnvironmentMapNode_VS"
        );

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psEnvironmentMap.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile environment map shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return false;
        }
        using var fs = Context.CreateShaderModuleGlsl(
            fsResult.Source,
            ShaderStage.Fragment,
            "EnvironmentMapNode_FS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = nameof(EnvironmentMapNode),
            // Full-screen triangle: no back-face culling needed.
            CullMode = CullMode.None,
            VertexShader = vs,
            FragmentShader = fs,
            DepthFormat = GraphicsSettings.DepthBufferFormat,
        };
        pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
            GraphicsSettings.IntermediateTargetFormat
        );

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        return _pipeline.Valid && res.RenderContext.EnvironmentMap.ShouldRender;
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        // Read-only depth from the opaque passes: the reversed-Z GREATER_EQUAL test lets the
        // full-screen triangle (clip Z = 0, i.e. far plane) pass only where no opaque geometry
        // was drawn (background depth stays at the cleared far value).
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;

        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushTexture(res.RenderContext.EnvironmentMap.Texture.GetHandle());
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Environment map pipeline is not valid.");

        var config = res.RenderContext.EnvironmentMap;
        var camera = res.RenderContext.CameraParams;

        // Prefer the caller-supplied sampler; fall back to the node's linear-clamp sampler.
        var samplerIndex = config.Sampler is { Valid: true }
            ? config.Sampler.GetHandle().Index
            : _defaultSampler.GetHandle().Index;

        res.CmdBuffer.BindRenderPipeline(_pipeline);
        res.CmdBuffer.BindDepthState(DepthState.ReadOnlyInvZ);
        res.CmdBuffer.PushConstants(
            new EnvironmentMapPushConstants
            {
                InvViewProj = camera.InvViewProjection,
                CameraPosition = camera.Position,
                EnvTexIndex = config.Texture.GetHandle().Index,
                SamplerIndex = samplerIndex,
                Intensity = config.Intensity,
                MipLevel = config.Blur,
                RotationY = config.RotationY,
            }
        );
        res.CmdBuffer.Draw(3); // full-screen triangle
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Opaque,
            nameof(EnvironmentMapNode),
            inputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture)],
            after: [nameof(ForwardPlusOpaqueNode), nameof(ForwardPlusMaskNode)]
        );
    }
}
