using HelixToolkit.Nex.Rendering.DrawStreams;

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

    protected override bool CanRender(in RenderResources res)
    {
        var context = res.RenderContext;
        return context.Data is not null
            && context.Data.DrawStreams.GetStreams(DrawStreamCategory.Opaque).AsValueEnumerable().Any(x => x.Count > 0);
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.Store;
        res.Pass.DepthState = DepthState.DefaultReversedZ;

        res.RenderContext.Data!.DrawStreams.GetStreams(DrawStreamCategory.Opaque).Barrier(res.CmdBuffer);
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

        res.RenderContext.Statistics.DrawCalls += MeshRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            res.RenderContext.Data.DrawStreams.GetStreams(DrawStreamCategory.Opaque),
            MaterialPassType.Opaque
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
            DepthFormat = GraphicsSettings.DepthBufferFormat,
            Topology = Topology.Triangle,
        };

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
                outputs: [new(SystemBufferNames.TextureDepthF32, ResourceType.Texture)]
            );
    }
}
