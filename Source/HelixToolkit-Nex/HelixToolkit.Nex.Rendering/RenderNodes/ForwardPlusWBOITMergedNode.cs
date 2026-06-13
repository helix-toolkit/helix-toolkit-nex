namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Merged render node that combines WBOIT transparent rendering (accumulation) and compositing
/// into a single render pass using Vulkan dynamic rendering local read.
/// <para>
/// Uses <c>VK_KHR_dynamic_rendering_local_read</c> to read the accumulation and revealage
/// color attachments as input attachments within the same dynamic rendering instance,
/// avoiding the need to end and restart rendering between passes.
/// </para>
/// </summary>
public sealed class ForwardPlusWBOITMergedNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<ForwardPlusWBOITMergedNode>();

    private RenderPipelineResource _compositePipeline = RenderPipelineResource.Null;
    private SamplerRef _sampler = SamplerRef.Null;
    private readonly byte[] _accumPass = System.Text.Encoding.UTF8.GetBytes("Accumulation");
    private readonly byte[] _compositePass = System.Text.Encoding.UTF8.GetBytes("Composition");

    public override string Name => nameof(ForwardPlusWBOITMergedNode);
    public override Color4 DebugColor => new(0.9f, 0.5f, 0.1f, 1.0f);
    public override string Description =>
        "Merged WBOIT node: accumulation + composite in a single render pass via subpasses.";

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddTexture(
            SystemBufferNames.TextureWboitRevealage,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.R_F16,
                    (uint)p.Context.WindowSize.Width,
                    (uint)p.Context.WindowSize.Height,
                    TextureUsageBits.InputAttachment
                        | TextureUsageBits.Attachment
                        | TextureUsageBits.Sampled,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureWboitRevealage
                )
        );

        // Register a single pass at RenderStage.Transparent.
        // The accumulation/revealage textures are NOT in the outputs list —
        // they are consumed internally within the node's subpasses.
        graph.AddPass(
            RenderStage.Transparent,
            nameof(ForwardPlusWBOITMergedNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
                new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
            ]
        );
    }

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && Renderer is not null);

        if (ResourceManager is null)
        {
            return false;
        }

        var shaderCompiler = new ShaderCompiler();

        // Compile the full-screen quad vertex shader.
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
        using var vs = ResourceManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source!,
            [],
            "WBOITMergedComposite_VS"
        );

        var defines = new Dictionary<string, string>();
        if (Context.SupportsSubpass)
        {
            defines["WBOIT_SUBPASS"] = "1";
        }
        // Compile the subpass composite fragment shader (uses subpassLoad).
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag.psWBOITCompositeSubpass"),
            new ShaderBuildOptions() { Defines = defines }
        );

        if (!fsResult.Success)
        {
            _logger.LogError(
                "Failed to compile WBOIT subpass composite fragment shader: {Errors}",
                fsResult.Errors
            );
            return false;
        }
        using var fs = ResourceManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source!,
            [],
            "WBOITComposite"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "WBOITComposite",
            FrontFaceWinding = WindingMode.CCW,
            CullMode = CullMode.None,
        };
        pipelineDesc.Colors[0] = ColorAttachment.CreateAlphaBlend(
            GraphicsSettings.IntermediateTargetFormat
        );
        if (Context.SupportsSubpass)
        {
            pipelineDesc.Colors[1] = new ColorAttachment()
            {
                Format = GraphicsSettings.MeshIdTexFormat,
                BlendEnabled = false,
            };
            pipelineDesc.Colors[2] = new ColorAttachment()
            {
                Format = GraphicsSettings.IntermediateTargetFormat,
                BlendEnabled = false,
            };
            pipelineDesc.Colors[3] = new ColorAttachment()
            {
                Format = Format.R_F16,
                BlendEnabled = false,
            };
            pipelineDesc.DepthFormat = GraphicsSettings.DepthBufferFormat;
        }

        _compositePipeline = Context.CreateRenderPipeline(pipelineDesc);
        if (!_compositePipeline.Valid)
        {
            _logger.LogError("Failed to create WBOIT subpass composite pipeline.");
            return false;
        }
        _sampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.PointClamp.DebugName,
            SamplerStateDesc.PointClamp
        );
        return true;
    }

    protected override void OnTeardown()
    {
        _compositePipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        var context = res.RenderContext;
        if (context.Data is null)
        {
            return false;
        }

        return context.Data is not null
            && context.Data.MeshDrawStreams.GetStreams(DrawStreamType.Transparent).HasAny();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        // Depth attachment: read-only inverse-Z (written by opaque pass).
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;
        res.Pass.DepthState = res.RenderContext.RenderParams.EnableGlobalWireframe
            ? DepthState.ReadOnlyInvZBias
            : DepthState.ReadOnlyInvZ;

        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;
        // Color 1: Entity ID (LoadOp.Load / StoreOp.Store to preserve picking).
        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;

        if (!res.RenderContext.RenderParams.EnableGlobalWireframe)
        {
            // Color 2: WBOIT accumulation (RGBA16F).
            // Clear to (0, 0, 0, 0). Blend: ONE / ONE additive.
            // Use the unused color texture as color accumulate texture.
            var accumTex =
                res.RenderContext.TextureColorF16Current
                == res.Textures[SystemBufferNames.TextureColorF16A]
                    ? res.Textures[SystemBufferNames.TextureColorF16B]
                    : res.Textures[SystemBufferNames.TextureColorF16A];
            res.Framebuf.Colors[2].Texture = accumTex;
            res.Pass.Colors[2].ClearColor = new Color4(0, 0, 0, 0);
            res.Pass.Colors[2].LoadOp = LoadOp.Clear;
            res.Pass.Colors[2].StoreOp = StoreOp.Store;

            // Color 3: WBOIT revealage (R16F).
            // Clear to 1.0 (fully transparent). Blend: ZERO / ONE_MINUS_SRC_COLOR.
            res.Framebuf.Colors[3].Texture = res.Textures[SystemBufferNames.TextureWboitRevealage];
            res.Pass.Colors[3].ClearColor = new Color4(1, 1, 1, 1);
            res.Pass.Colors[3].LoadOp = LoadOp.Clear;
            res.Pass.Colors[3].StoreOp = StoreOp.Store;

            if (Context!.SupportsSubpass)
            { // If subpass load is supported, we can read the accumulation/revealage attachments directly as input attachments in the composite subpass.
                res.Pass.Colors[2].StoreOp = StoreOp.DontCare;
                res.Pass.Colors[3].StoreOp = StoreOp.DontCare;

                res.Deps.PushInputAttachment(accumTex);
                res.Deps.PushInputAttachment(res.Textures[SystemBufferNames.TextureWboitRevealage]);
            }
            res.Pass.ColorWrites[0] = false;
        }
        else
        {
            res.Framebuf.Colors[2].Texture = TextureHandle.Null;
            res.Framebuf.Colors[3].Texture = TextureHandle.Null;
            res.Pass.Colors[2].LoadOp = LoadOp.Invalid;
            res.Pass.Colors[3].LoadOp = LoadOp.Invalid;
            res.Pass.ColorWrites[0] = true;
        }

        // Resource dependencies for the accumulation subpass.
        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightGrid]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferLightIndex]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferPBRProperties]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);
    }

    protected override void OnRender(in RenderResources res)
    {
        var cmdBuffer = res.CmdBuffer;
        cmdBuffer.PushDebugGroupLabel(_accumPass, Color.Chartreuse);
        if (res.RenderContext.RenderParams.EnableGlobalWireframe)
        {
            var streams = res.RenderContext.Data!.MeshDrawStreams.GetStreams(
                DrawStreamType.Transparent
            );
            res.RenderContext.Statistics.DrawCalls += MeshRenderHelper.Render(
                in res,
                res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                    .GpuAddress(res.RenderContext.Context),
                streams,
                MaterialPassType.Wireframe
            );
        }
        else
        {
            RenderWBOIT(in res);
        }
        cmdBuffer.PopDebugGroupLabel();
    }

    private void RenderWBOIT(in RenderResources res)
    {
        var cmdBuffer = res.CmdBuffer;
        // Disable color buffer 0 output in first pass since second pass outputs the final color to the buffer 0.
        // cmdBuffer.SetColorWriteEnabled(false);
        // ── Subpass 1: Render transparent geometry into accumulation/revealage ──
        var streams = res.RenderContext.Data!.MeshDrawStreams.GetStreams(
            DrawStreamType.Transparent
        );
        res.RenderContext.Statistics.DrawCalls += MeshRenderHelper.Render(
            in res,
            res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
                .GpuAddress(res.RenderContext.Context),
            streams,
            MaterialPassType.WBOIT
        );
        cmdBuffer.PopDebugGroupLabel();
        if (Context!.SupportsSubpass)
        {
            cmdBuffer.PushDebugGroupLabel(_compositePass, Color.OrangeRed);
            cmdBuffer.NextSubpass();
            cmdBuffer.BindRenderPipeline(_compositePipeline);
        }
        else
        {
            cmdBuffer.EndRendering();
            cmdBuffer.PushDebugGroupLabel(_compositePass, Color.BlueViolet);
            var accumTexId = res.Framebuf.Colors[2].Texture.Index;
            var revealageTexId = res.Framebuf.Colors[3].Texture.Index;
            res.Deps.Clear(); // Clear previous dependencies since we're starting a new render pass instance for the composite pass.
            res.Deps.PushTexture(res.Framebuf.Colors[2].Texture);
            res.Deps.PushTexture(res.Framebuf.Colors[3].Texture);
            res.Framebuf.Colors[1].Texture = TextureHandle.Null;
            res.Framebuf.Colors[2].Texture = TextureHandle.Null;
            res.Framebuf.Colors[3].Texture = TextureHandle.Null;
            res.Framebuf.DepthStencil.Texture = TextureHandle.Null;
            res.Pass.Colors[1].LoadOp = LoadOp.Invalid;
            res.Pass.Colors[2].LoadOp = LoadOp.Invalid;
            res.Pass.Colors[3].LoadOp = LoadOp.Invalid;
            cmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
            // ── Subpass 2: Composite resolve (full-screen triangle) ──
            cmdBuffer.BindRenderPipeline(_compositePipeline);
            cmdBuffer.PushConstants(
                new WBOITCompositePushConstants
                {
                    AccumTextureId = accumTexId,
                    RevealTextureId = revealageTexId,
                    SamplerId = _sampler,
                }
            );
        }

        // Now disable other color buffers except 0 for output.
        cmdBuffer.SetColorWriteEnabled(true, false, false, false);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.Draw(3); // Full-screen triangle (3 vertices, no index buffer)
    }
}
