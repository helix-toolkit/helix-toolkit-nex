namespace HelixToolkit.Nex.Rendering;

public sealed class DepthPassRenderer(
    Format depthFormat = Format.Z_F32,
    Format meshIdFormat = Format.RG_F32
) : Renderer
{
    private static readonly ILogger _logger = LogManager.Create<DepthPassRenderer>();
    private readonly RenderPass _renderPass = new();
    private readonly Framebuffer _framebuffer = new();
    private readonly Dependencies _dependencies = new();

    public RenderPipelineResource Pipeline { private set; get; } = RenderPipelineResource.Null;

    public Format DepthFormat => depthFormat;

    public Format MeshIdFormat => meshIdFormat;

    public override RenderStages Stage => RenderStages.Begin;

    public override string Name => nameof(DepthPassRenderer);

    public override IEnumerable<string> GetOutputs()
    {
        yield return RenderGraphBufferNames.TextureDepth;
        yield return RenderGraphBufferNames.TextureMeshId;
    }

    public override IEnumerable<string> GetInputs()
    {
        yield return RenderGraphBufferNames.MeshDrawsOpaque;
    }

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && RenderManager is not null);
        return CreatePipeline();
    }

    protected override void OnTearDown()
    {
        Pipeline.Dispose();
        base.OnTearDown();
    }

    protected override void OnRender(RenderContext renderContext, ICommandBuffer cmdBuffer)
    {
        if (renderContext.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping depth pass.");
            return;
        }
        if (renderContext.Data.MeshDrawsOpaque.Count > 0)
        {
            HxDebug.Assert(renderContext.SharedBuffers.TextureDepth, "Missing Depth Buffer.");
            HxDebug.Assert(
                renderContext.SharedBuffers.TextureMeshId,
                "Missing Mesh Id Texture Buffer."
            );
            _framebuffer.DepthStencil.Texture = renderContext.SharedBuffers.TextureDepth;
            _renderPass.Depth.ClearDepth = 0.0f;
            _renderPass.Depth.LoadOp = LoadOp.Clear;
            _renderPass.Depth.StoreOp = StoreOp.Store;

            _framebuffer.Colors[0].Texture = renderContext.SharedBuffers.TextureMeshId;
            _renderPass.Colors[0].ClearColor = new(0, 0, 0, 0);
            _renderPass.Colors[0].LoadOp = LoadOp.Clear;
            _renderPass.Colors[0].StoreOp = StoreOp.Store;
            cmdBuffer.PushDebugGroupLabel("Depth Pass", Color.Blue);
            _dependencies.Buffers[0] = renderContext.Data.MeshDrawsOpaque.Buffer;
            using var pipelineScope = renderContext.EnableExternalPipelineScoped();
            cmdBuffer.BeginRendering(_renderPass, _framebuffer, _dependencies);
            cmdBuffer.BindRenderPipeline(Pipeline);
            cmdBuffer.BindDepthState(DepthState.DefaultReversedZ);
            cmdBuffer.PushConstants(renderContext.FPConstantsBuffer.GpuAddress, 0);
            cmdBuffer.RenderOpaque(renderContext);
            cmdBuffer.EndRendering();
            cmdBuffer.PopDebugGroupLabel();
        }
    }

    private bool CreatePipeline()
    {
        if (Context is null || RenderManager is null)
        {
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
            FragementShader = fs,
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

        Pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return Pipeline.Valid;
    }
}
