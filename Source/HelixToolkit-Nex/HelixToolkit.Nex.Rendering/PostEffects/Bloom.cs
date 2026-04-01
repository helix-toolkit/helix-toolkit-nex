using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Bloom post-processing effect.
/// Performs a three-stage pipeline:
///   1. Brightness extract – isolates pixels brighter than <see cref="Threshold"/>.
///   2. Two-pass Gaussian blur – horizontal then vertical, applied <see cref="BlurPasses"/> times.
///   3. Composite – additively blends the blurred result onto the scene colour.
///
/// The two intermediate blur textures (<see cref="SystemBufferNames.TextureBloomA"/> and
/// <see cref="SystemBufferNames.TextureBloomB"/>) are registered into the shared
/// <see cref="RenderGraph"/> resource set via <see cref="RegisterResources"/>, so they are
/// allocated and automatically resized by the resource set alongside all other render targets.
/// </summary>
public sealed class Bloom : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<Bloom>();

    // Four pipeline variants, one per BloomMode specialization constant.
    private RenderPipelineResource _brightnessPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurHPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurVPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _compositePipeline = RenderPipelineResource.Null;

    private SamplerResource _linearSampler = SamplerResource.Null;
    private SamplerResource _pointSampler = SamplerResource.Null;

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

    public override string Name => nameof(Bloom);
    public override Color DebugColor => Color.HotPink;
    public override uint Priority => (uint)PostEffectPriority.Bloom;

    // -----------------------------------------------------------------------
    // Resource registration (graph-time)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Registers <see cref="SystemBufferNames.TextureBloomA"/> and
    /// <see cref="SystemBufferNames.TextureBloomB"/> into the render graph so they are
    /// created and resized by the shared resource set, exactly like every other render target.
    /// The textures are allocated at <c>1 / <see cref="DownsampleFactor"/></c> of the screen
    /// resolution, which is sufficient for the low-frequency bloom signal and significantly
    /// reduces fill-rate cost.
    /// </remarks>
    public override void RegisterResources(RenderGraph graph)
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
    }

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_brightnessPipeline.Valid, "Bloom pipeline is not valid.");

        var cmdBuffer = res.CmdBuffer;
        var sceneTex = res.Textures[readSlot];
        var bloomA = res.Textures[SystemBufferNames.TextureBloomA];
        var bloomB = res.Textures[SystemBufferNames.TextureBloomB];

        // texel size for the blur passes (dimensions come from the managed resource)
        var dims = res.Context.Context.GetDimensions(bloomA);
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
            cmdBuffer,
            _brightnessPipeline,
            inputHandle: sceneTex,
            outputHandle: bloomA,
            new BloomPushConstants
            {
                TextureId = sceneTex.Index,
                SamplerId = _linearSampler.Index,
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
                cmdBuffer,
                _blurHPipeline,
                inputHandle: bloomA,
                outputHandle: bloomB,
                new BloomPushConstants
                {
                    TextureId = bloomA.Index,
                    SamplerId = _linearSampler.Index,
                    TexelWidth = texelW,
                    TexelHeight = texelH,
                }
            );

            // Vertical: bloomB → bloomA
            RunFullScreenPass(
                cmdBuffer,
                _blurVPipeline,
                inputHandle: bloomB,
                outputHandle: bloomA,
                new BloomPushConstants
                {
                    TextureId = bloomB.Index,
                    SamplerId = _linearSampler.Index,
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
        RunFullScreenPass(
            cmdBuffer,
            _compositePipeline,
            inputHandle: sceneTex,
            outputHandle: res.Textures[writeSlot],
            new BloomPushConstants
            {
                TextureId = sceneTex.Index,
                SamplerId = _pointSampler.Index,
                BloomTextureId = bloomA.Index,
                BloomSamplerId = _linearSampler.Index,
                Intensity = Intensity,
            },
            input2Handle: bloomA
        );
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during bloom initialization.");
            return ResultCode.InvalidState;
        }

        _linearSampler = Context.CreateSampler(SamplerStateDesc.LinearClamp);
        _pointSampler = Context.CreateSampler(SamplerStateDesc.PointClamp);

        if (!_linearSampler.Valid || !_pointSampler.Valid)
        {
            return ResultCode.RuntimeError;
        }

        return CreatePipelines();
    }

    protected override ResultCode OnTearingDown()
    {
        _brightnessPipeline.Dispose();
        _blurHPipeline.Dispose();
        _blurVPipeline.Dispose();
        _compositePipeline.Dispose();
        _linearSampler.Dispose();
        _pointSampler.Dispose();
        // Note: BloomA / BloomB are owned by the shared RenderGraphResourceSet — not disposed here.
        return ResultCode.Ok;
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
        ICommandBuffer cmdBuffer,
        RenderPipelineResource pipeline,
        TextureHandle inputHandle,
        TextureHandle outputHandle,
        BloomPushConstants pc,
        TextureHandle input2Handle = default
    )
    {
        var deps = new Dependencies();
        deps.Textures[0] = inputHandle;
        if (input2Handle.Valid)
        {
            deps.Textures[1] = input2Handle;
        }

        var pass = new RenderPass();
        pass.Colors[0].LoadOp = LoadOp.DontCare;
        pass.Colors[0].StoreOp = StoreOp.Store;

        var fb = new Framebuffer();
        fb.Colors[0].Texture = outputHandle;

        cmdBuffer.BeginRendering(pass, fb, deps);
        cmdBuffer.BindRenderPipeline(pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();
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
