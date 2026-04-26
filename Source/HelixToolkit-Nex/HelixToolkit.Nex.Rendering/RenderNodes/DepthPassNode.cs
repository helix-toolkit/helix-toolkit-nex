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

    protected override bool BeginRender(in RenderResources res)
    {
        var context = res.Context;
        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
        if (!fpBuffer.Valid)
        {
            return false;
        }
        var fpData = new FPConstants
        {
            TimeMs = res.Context.TimeMs,
            CameraPosition = context.CameraParams.Position,
            InverseViewProjection = context.CameraParams.InvViewProjection,
            ViewProjection = context.CameraParams.ViewProjection,
            ScreenDimensions = new Vector2(context.WindowSize.Width, context.WindowSize.Height),
            DpiScale = context.DpiScale,
            MeshInfoBufferAddress = context.Data?.MeshInfos.GpuAddress ?? 0,
            MeshDrawBufferAddress = context.Data?.MeshDrawsOpaque.GpuAddress ?? 0,
            MaterialBufferAddress = context.Data?.PBRPropertiesBuffer.Buffer.GpuAddress(context.Context) ?? 0,
        };
        res.CmdBuffer.UpdateBuffer(fpBuffer, ref fpData);
        return base.BeginRender(in res);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context.Data is null)
        {
            _logger.LogWarning("Render context data is null, skipping depth pass.");
            return;
        }
        Debug.Assert(_pipeline.Valid, "_pipeline is not valid.");
        if (res.Context.Data.MeshDrawsOpaque.Count > 0)
        {
            using var _ = res.Context.EnableExternalPipelineScoped();
            res.CmdBuffer.BindRenderPipeline(_pipeline);
            res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);
            res.Context.Statistics.DrawCalls += RenderHelper.RenderOpaque(in res);
        }
    }

    protected override void EndRender(in RenderResources res)
    {
        base.EndRender(res);
        res.CmdBuffer.TransitionToShaderReadOnly(res.Textures[SystemBufferNames.TextureDepthF32]);
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
        using var fs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
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
            DepthFormat = RenderSettings.DepthBufferFormat,
            Topology = Topology.Triangle,
        };
        pipelineDesc.Colors[0] = new ColorAttachment()
        {
            Format = RenderSettings.MeshIdTexFormat,
            BlendEnabled = false,
        };

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return _pipeline.Valid;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddBuffer(SystemBufferNames.BufferPBRProperties, null)
            .AddPass(
                nameof(DepthPassNode),
                inputs:
                [
                    new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                    new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
                    new(SystemBufferNames.BufferPBRProperties, ResourceType.Buffer)
                ],
                outputs:
                [
                    new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                    new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                ],
                onSetup: (res) =>
                {
                    res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
                    res.Pass.Depth.ClearDepth = 0.0f;
                    res.Pass.Depth.LoadOp = LoadOp.Clear;
                    res.Pass.Depth.StoreOp = StoreOp.Store;

                    res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureEntityId];
                    res.Pass.Colors[0].ClearColor = new(0, 0, 0, 0);
                    res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                    res.Pass.Colors[0].StoreOp = StoreOp.Store;

                    res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawOpaque];
                    res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferPBRProperties];
                    res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
                }
            );
    }
}
