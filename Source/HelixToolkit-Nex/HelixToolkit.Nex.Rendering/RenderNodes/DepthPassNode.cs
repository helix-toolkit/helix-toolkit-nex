namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class DepthPassNode(
    Format depthFormat = Format.Z_F32,
    Format meshIdFormat = Format.RG_F32
) : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<DepthPassNode>();
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public Format DepthFormat => depthFormat;

    public Format MeshIdFormat => meshIdFormat;

    public override string Name => nameof(DepthPassNode);

    public override Color4 DebugColor => Color.Blue;

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && RenderManager is not null);
        return CreatePipeline();
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnRender(
        RenderContext renderContext,
        ICommandBuffer cmdBuffer,
        Dependencies deps
    )
    {
        if (renderContext.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping depth pass.");
            return;
        }
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        if (renderContext.Data.MeshDrawsOpaque.Count > 0)
        {
            using var _ = renderContext.EnableExternalPipelineScoped();
            cmdBuffer.BindRenderPipeline(_pipeline);
            cmdBuffer.BindDepthState(DepthState.DefaultReversedZ);
            cmdBuffer.PushConstants(renderContext.FPConstantsBuffer.GpuAddress, 0);
            cmdBuffer.RenderOpaque(renderContext);
        }
    }

    private bool CreatePipeline()
    {
        if (Context is null || RenderManager is null)
        {
            _logger.LogError(
                "Render context or render manager is null, cannot create depth pass pipeline."
            );
            return false;
        }
        var shaderCompiler = new ShaderCompiler();
        var result = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert.vsMainTemplate")
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
            [new ShaderDefine(BuildFlags.OUTPUT_DRAW_ID, BuildFlags.EXCLUDE_MESH_PROPS)],
            "DepthPass_VS"
        );
        result = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag.psEntityId")
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
            "EntityId_PS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "DepthPass",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
            DepthFormat = DepthFormat,
            Topology = Topology.Triangle,
        };
        pipelineDesc.Colors[0] = new ColorAttachment()
        {
            Format = meshIdFormat,
            BlendEnabled = false,
        };

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }
}
