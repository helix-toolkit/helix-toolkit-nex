namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class DepthPassNode() : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<DepthPassNode>();
    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    public override string Name => nameof(DepthPassNode);

    public override Color4 DebugColor => Color.Blue;

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && Renderer is not null);
        return CreatePipeline();
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
    }

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data!.MeshDrawsOpaque.Count == 0)
            return false;
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.ClearDepth = 0.0f;
        res.Pass.Depth.LoadOp = LoadOp.Clear; // Clear depth to 0 (far plane) for reversed Z.
        res.Pass.Depth.StoreOp = StoreOp.Store;
        return base.BeginRender(res);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.RenderContext.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping depth pass.");
            return;
        }
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        using var _ = res.RenderContext.EnableExternalPipelineScoped();
        res.CmdBuffer.BindRenderPipeline(_pipeline);

        res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);
        res.RenderContext.Statistics.DrawCalls += RenderHelper.RenderOpaque(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            false
        );

        res.RenderContext.Statistics.DrawCalls += RenderHelper.RenderOpaque(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            true
        );
    }

    private bool CreatePipeline()
    {
        if (Context is null || Renderer is null)
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
        using var vs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            result.Source!,
            [
                new ShaderDefine(BuildFlags.EXCLUDE_MESH_PROPS),
                new ShaderDefine(BuildFlags.DEPTH_PREPASS),
            ],
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

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            DebugName = "DepthPass",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
            DepthFormat = RenderSettings.DepthBufferFormat,
            Topology = Topology.Triangle,
        };
        //pipelineDesc.Colors[0] = new ColorAttachment()
        //{
        //    Format = RenderSettings.MeshIdTexFormat,
        //    BlendEnabled = false,
        //};

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferPBRProperties, null)
            .AddPass(
                RenderStage.Prepare,
                nameof(DepthPassNode),
                inputs:
                [
                    new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer),
                    new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer),
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                ],
                outputs:
                [
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                ]
            );
    }
}
