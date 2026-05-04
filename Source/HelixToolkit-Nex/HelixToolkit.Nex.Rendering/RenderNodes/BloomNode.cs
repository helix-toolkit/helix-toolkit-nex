using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

public sealed class BloomNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<BloomNode>();
    private static readonly byte[] _passNameBrightness = System.Text.Encoding.UTF8.GetBytes("Bloom_BrightnessExtract");
    private static readonly byte[] _passNameBlurH = System.Text.Encoding.UTF8.GetBytes("Bloom_BlurH");
    private static readonly byte[] _passNameBlurV = System.Text.Encoding.UTF8.GetBytes("Bloom_BlurV");
    private static readonly byte[] _passNameComposite = System.Text.Encoding.UTF8.GetBytes("Bloom_Composite");

    // Four pipeline variants, one per BloomMode specialization constant.
    private RenderPipelineResource _brightnessPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurHPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurVPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _compositePipeline = RenderPipelineResource.Null;

    private SamplerRef _linearSampler = SamplerRef.Null;
    private SamplerRef _pointSampler = SamplerRef.Null;

    // -----------------------------------------------------------------------
    // Public settings
    // -----------------------------------------------------------------------

    /// <summary>Luminance threshold above which pixels contribute to bloom.</summary>
    public float Threshold { get; set; } = 0.8f;

    /// <summary>Intensity multiplier for the final composite stage.</summary>
    public float Intensity { get; set; } = 2.0f;

    /// <summary>Number of horizontal+vertical blur iterations (more = softer bloom).</summary>
    public int BlurPasses { get; set; } = 2;

    /// <summary>
    /// Resolution divisor applied to the bloom intermediate textures relative to the screen size.
    /// <list type="table">
    ///   <item><term>1</term><description>Full resolution (expensive, rarely needed)</description></item>
    ///   <item><term>2</term><description>Half resolution – good quality, ~4× cheaper than full</description></item>
    ///   <item><term>4</term><description>Quarter resolution – default, virtually indistinguishable, ~16× cheaper</description></item>
    /// </list>
    /// Must be a power-of-two value ≥ 1. Defaults to <c>4</c>.
    /// </summary>
    public int DownsampleFactor { get; set; } = 4;

    public override string Name => nameof(BloomNode);
    public override Color4 DebugColor => Color.HotPink;
    protected override bool OnSetup()
    {
        if (ResourceManager is null)
        {
            _logger.LogError("ResourceManager is null during bloom initialization.");
            return false;
        }

        _linearSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.LinearClamp
        );
        _pointSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.PointClamp
        );

        if (!_linearSampler.Valid || !_pointSampler.Valid)
        {
            return false;
        }

        return CreatePipelines().CheckResult() == ResultCode.Ok;
    }

    protected override void OnTeardown()
    {
        _brightnessPipeline.Dispose();
        _blurHPipeline.Dispose();
        _blurVPipeline.Dispose();
        _compositePipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
    }

    protected override bool BeginRender(in RenderResources res)
    {
        return true;
    }

    protected override void EndRender(in RenderResources res)
    {
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_brightnessPipeline.Valid, "Bloom pipeline is not valid.");

        var cmdBuffer = res.CmdBuffer;
        var sceneTex = res.Textures[SystemBufferNames.TextureColorF16Target];
        var bloomA = res.Textures[SystemBufferNames.TextureBloomA];
        var bloomB = res.Textures[SystemBufferNames.TextureBloomB];

        // texel size for the blur passes (dimensions come from the managed resource)
        var dims = res.RenderContext.Context.GetDimensions(bloomA);
        float texelW = dims.Width > 0 ? 1.0f / dims.Width : 0f;
        float texelH = dims.Height > 0 ? 1.0f / dims.Height : 0f;

        // ------------------------------------------------------------------
        // Stage 0: Brightness extract  scene → bloomA
        // ------------------------------------------------------------------
        // Use _linearSampler so the full-resolution scene is bilinearly
        // filtered when downsampling to the bloom texture's coarser resolution.
        // Point-sampling here creates hard aliased edges on bright pixels that
        // the blur then spreads, making bloom appear too bright on moving objects.
        RunFullScreenPass(
            _passNameBrightness,
            in res,
            _brightnessPipeline,
            inputHandle: in sceneTex,
            outputHandle: in bloomA,
            new BloomPushConstants
            {
                TextureId = sceneTex.Index,
                SamplerId = _linearSampler,
                Threshold = Threshold,
            }
        );

        // ------------------------------------------------------------------
        // Stages 1 & 2: Gaussian blur  (repeated BlurPasses times)
        //   bloomA → bloomB  (horizontal)
        //   bloomB → bloomA  (vertical)
        // ------------------------------------------------------------------
        for (int i = 0; i < BlurPasses; i++)
        {
            // Horizontal: bloomA → bloomB
            RunFullScreenPass(
                _passNameBlurH,
                 in res,
                 _blurHPipeline,
                 inputHandle: in bloomA,
                outputHandle: in bloomB,
                new BloomPushConstants
                {
                    TextureId = bloomA.Index,
                    SamplerId = _linearSampler,
                    TexelWidth = texelW,
                    TexelHeight = texelH,
                }
            );

            // Vertical: bloomB → bloomA
            RunFullScreenPass(
                _passNameBlurV,
                in res,
                _blurVPipeline,
                inputHandle: in bloomB,
                outputHandle: in bloomA,
                new BloomPushConstants
                {
                    TextureId = bloomB.Index,
                    SamplerId = _linearSampler,
                    TexelWidth = texelW,
                    TexelHeight = texelH,
                }
            );
        }

        // ------------------------------------------------------------------
        // Stage 3: Composite  (scene + bloom) → writeSlot
        // ------------------------------------------------------------------
        // Both sceneTex and bloomA are sampled — both must be in deps so that
        // BeginRendering issues the shader-read-only barrier for each of them.
        res.RenderContext.SwapIntermediateBuffers();
        RunFullScreenPass(
            _passNameComposite,
            in res,
            _compositePipeline,
            inputHandle: in sceneTex,
            outputHandle: res.Textures[SystemBufferNames.TextureColorF16Target],
            new BloomPushConstants
            {
                TextureId = sceneTex.Index,
                SamplerId = _pointSampler,
                BloomTextureId = bloomA.Index,
                BloomSamplerId = _linearSampler,
                Intensity = Intensity,
            },
            input2Handle: bloomA
        );
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddTexture(
            SystemBufferNames.TextureBloomA,
            p =>
                p.Context.Context.CreateTexture2D(
                    RenderSettings.IntermediateTargetFormat,
                    BloomTextureSize(p.Context.WindowSize.Width),
                    BloomTextureSize(p.Context.WindowSize.Height),
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureBloomA
                ),
            dependsOnScreenSize: true
        );

        graph.AddTexture(
            SystemBufferNames.TextureBloomB,
            p =>
                p.Context.Context.CreateTexture2D(
                    RenderSettings.IntermediateTargetFormat,
                    BloomTextureSize(p.Context.WindowSize.Width),
                    BloomTextureSize(p.Context.WindowSize.Height),
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureBloomB
                ),
            dependsOnScreenSize: true
        );
        graph.AddPingPongPass(
            RenderStage.Bloom,
            nameof(BloomNode),
            PingPongGroups.ColorF16,
            extraInputs: [],
            extraOutputs: []
        );
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Renders a full-screen triangle with <paramref name="pipeline"/> reading from
    /// <paramref name="inputHandle"/> and writing to <paramref name="outputHandle"/>.
    /// <para>
    /// <paramref name="inputHandle"/> is placed in <c>deps.Textures[0]</c> so that
    /// <see cref="ICommandBuffer.BeginRendering"/> issues the required
    /// shader-read-only layout transition and pipeline barrier before the draw.
    /// An optional second sampled input (<paramref name="input2Handle"/>) can be
    /// supplied for the composite stage where both the scene colour and the blurred
    /// bloom texture must be read simultaneously.
    /// </para>
    /// </summary>
    private static void RunFullScreenPass(
        ReadOnlySpan<byte> debugName,
        in RenderResources res,
        RenderPipelineResource pipeline,
        in TextureHandle inputHandle,
        in TextureHandle outputHandle,
        BloomPushConstants pc,
        in TextureHandle input2Handle = default
    )
    {
        res.Deps.Textures[0] = inputHandle;
        if (input2Handle.Valid)
        {
            res.Deps.Textures[1] = input2Handle;
        }
        var cmdBuffer = res.CmdBuffer;
        var pass = new RenderPass();
        pass.Colors[0].LoadOp = LoadOp.Load;
        pass.Colors[0].StoreOp = StoreOp.Store;

        var fb = new Framebuffer();
        fb.Colors[0].Texture = outputHandle;
        cmdBuffer.PushDebugGroupLabel(debugName, Color.AliceBlue);
        cmdBuffer.BeginRendering(pass, fb, res.Deps);
        cmdBuffer.BindRenderPipeline(pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();
        cmdBuffer.PopDebugGroupLabel();
    }

    /// <summary>
    /// Compiles the bloom GLSL shader and creates four render pipelines, one per bloom stage.
    /// </summary>
    private ResultCode CreatePipelines()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during bloom pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psBloom.glsl")
        );

        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile bloom shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
        );

        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile full-screen quad vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }
        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "FullScreenQuad_Vertex"
        );

        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "Bloom_Fragment"
        );

        _brightnessPipeline = CreateStagePipeline(
            vs,
            fs,
            BloomMode.BrightnessExtract,
            "Bloom_BrightnessExtract"
        );
        _blurHPipeline = CreateStagePipeline(vs, fs, BloomMode.BlurHorizontal, "Bloom_BlurH");
        _blurVPipeline = CreateStagePipeline(vs, fs, BloomMode.BlurVertical, "Bloom_BlurV");
        _compositePipeline = CreateStagePipeline(vs, fs, BloomMode.Composite, "Bloom_Composite");

        if (
            !_brightnessPipeline.Valid
            || !_blurHPipeline.Valid
            || !_blurVPipeline.Valid
            || !_compositePipeline.Valid
        )
        {
            _logger.LogError("One or more bloom pipelines failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    private RenderPipelineResource CreateStagePipeline(
        ShaderModuleResource vs,
        ShaderModuleResource fs,
        BloomMode stage,
        string debugName
    )
    {
        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = debugName,
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(RenderSettings.IntermediateTargetFormat);
        desc.WriteSpecInfo(0, (uint)stage);
        return Context!.CreateRenderPipeline(desc);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the bloom texture dimension for a given screen dimension,
    /// clamped to at least 1 pixel.
    /// </summary>
    private uint BloomTextureSize(int screenDim) =>
        (uint)Math.Max(1, screenDim / Math.Max(1, DownsampleFactor));
}
