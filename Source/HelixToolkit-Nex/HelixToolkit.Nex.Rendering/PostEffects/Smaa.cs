using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// SMAA (Subpixel Morphological Anti-Aliasing) post-processing effect.
///
/// Implements a three-stage morphological anti-aliasing pipeline:
/// <list type="number">
///   <item>
///     <term>Edge detection</term>
///     <description>
///       Detects luminance-contrast edges in the scene colour buffer and writes a
///       two-channel (RG) edge mask into <see cref="SystemBufferNames.TextureSmaaEdges"/>.
///       Edges with a contrast below <see cref="EdgeThreshold"/> are suppressed.
///     </description>
///   </item>
///   <item>
///     <term>Blending-weight computation</term>
///     <description>
///       Searches along detected edges for crossing patterns and computes per-pixel
///       MLAA blending weights (left/right/top/bottom), stored as RGBA in
///       <see cref="SystemBufferNames.TextureSmaaWeights"/>.
///     </description>
///   </item>
///   <item>
///     <term>Neighbourhood blending</term>
///     <description>
///       Reads the original colour and the blending weights, then blends each pixel
///       with its four direct neighbours to produce the final anti-aliased output.
///     </description>
///   </item>
/// </list>
///
/// The two intermediate textures are registered into the shared <see cref="RenderGraph"/>
/// resource set via <see cref="RegisterResources"/> so they are created and resized
/// automatically alongside all other render targets.
/// </summary>
public sealed class Smaa : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<Smaa>();

    // One pipeline per SmaaMode specialization constant.
    private RenderPipelineResource _edgePipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _weightPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blendPipeline = RenderPipelineResource.Null;

    private SamplerResource _linearSampler = SamplerResource.Null;
    private SamplerResource _pointSampler = SamplerResource.Null;

    // Reusable per-frame objects (avoids per-pass allocations).
    private readonly Dependencies _deps = new();
    private readonly Framebuffer _fb = new();
    private readonly RenderPass _pass = new();

    // -----------------------------------------------------------------------
    // Public settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Luminance-contrast threshold above which a pixel is classified as an edge.
    /// Lower values detect more edges (sharper AA, more fill-rate cost).
    /// Defaults to <c>0.1</c> which is the standard SMAA "medium" quality preset.
    /// </summary>
    public float EdgeThreshold { get; set; } = 0.1f;

    public override string Name => nameof(Smaa);
    public override Color DebugColor => Color.LightGreen;

    // -----------------------------------------------------------------------
    // Resource registration (graph-time)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Registers <see cref="SystemBufferNames.TextureSmaaEdges"/> (RG8, edge mask) and
    /// <see cref="SystemBufferNames.TextureSmaaWeights"/> (RGBA8, blending weights) into the
    /// render graph at full screen resolution so they are allocated and resized by the shared
    /// resource set.
    /// </remarks>
    public override void RegisterResources(RenderGraph graph)
    {
        graph.AddTexture(
            SystemBufferNames.TextureSmaaEdges,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.RG_UN8,
                    (uint)p.Context.WindowSize.Width,
                    (uint)p.Context.WindowSize.Height,
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureSmaaEdges
                ),
            dependsOnScreenSize: true
        );

        graph.AddTexture(
            SystemBufferNames.TextureSmaaWeights,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.RGBA_UN8,
                    (uint)p.Context.WindowSize.Width,
                    (uint)p.Context.WindowSize.Height,
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureSmaaWeights
                ),
            dependsOnScreenSize: true
        );
    }

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_edgePipeline.Valid, "SMAA edge pipeline is not valid.");
        Debug.Assert(_weightPipeline.Valid, "SMAA weight pipeline is not valid.");
        Debug.Assert(_blendPipeline.Valid, "SMAA blend pipeline is not valid.");

        var cmdBuffer = res.CmdBuffer;
        var sceneTex = res.Textures[readSlot];
        var edgeTex = res.Textures[SystemBufferNames.TextureSmaaEdges];
        var weightTex = res.Textures[SystemBufferNames.TextureSmaaWeights];

        // Texel size – taken from the scene (full-resolution) texture.
        var dims = res.Context.Context.GetDimensions(sceneTex);
        float tw = dims.Width > 0 ? 1.0f / dims.Width : 0f;
        float th = dims.Height > 0 ? 1.0f / dims.Height : 0f;

        // ------------------------------------------------------------------
        // Pass 0: Edge detection  scene → edgeTex
        // ------------------------------------------------------------------
        RunSmaaPass(
            cmdBuffer,
            _edgePipeline,
            outputHandle: edgeTex,
            clearOutput: true,
            new SmaaPushConstants
            {
                ColorTextureId = sceneTex.Index,
                ColorSamplerId = _linearSampler.Index,
                TexelWidth = tw,
                TexelHeight = th,
                EdgeThreshold = EdgeThreshold,
            },
            dep0: sceneTex
        );

        // ------------------------------------------------------------------
        // Pass 1: Blending-weight computation  edgeTex → weightTex
        // ------------------------------------------------------------------
        RunSmaaPass(
            cmdBuffer,
            _weightPipeline,
            outputHandle: weightTex,
            clearOutput: true,
            new SmaaPushConstants
            {
                EdgeTextureId = edgeTex.Index,
                EdgeSamplerId = _linearSampler.Index,
                TexelWidth = tw,
                TexelHeight = th,
                EdgeThreshold = EdgeThreshold,
            },
            dep0: edgeTex
        );

        // ------------------------------------------------------------------
        // Pass 2: Neighbourhood blending  (scene + weights) → writeSlot
        // ------------------------------------------------------------------
        RunSmaaPass(
            cmdBuffer,
            _blendPipeline,
            outputHandle: res.Textures[writeSlot],
            clearOutput: false,
            new SmaaPushConstants
            {
                ColorTextureId = sceneTex.Index,
                ColorSamplerId = _pointSampler.Index,
                WeightTextureId = weightTex.Index,
                WeightSamplerId = _linearSampler.Index,
                TexelWidth = tw,
                TexelHeight = th,
                EdgeThreshold = EdgeThreshold,
            },
            dep0: sceneTex,
            dep1: weightTex
        );

        return true;
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during SMAA initialization.");
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
        _edgePipeline.Dispose();
        _weightPipeline.Dispose();
        _blendPipeline.Dispose();
        _linearSampler.Dispose();
        _pointSampler.Dispose();
        // TextureSmaaEdges / TextureSmaaWeights are owned by the shared RenderGraphResourceSet.
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes one full-screen SMAA pass.
    /// </summary>
    /// <param name="clearOutput">
    /// When <see langword="true"/> the output attachment is cleared to zero before drawing
    /// (required for the edge and weight textures which must start clean each frame).
    /// When <see langword="false"/> a <c>DontCare</c> load op is used (the neighbourhood-blend
    /// output overwrites every pixel unconditionally).
    /// </param>
    private void RunSmaaPass(
        ICommandBuffer cmdBuffer,
        RenderPipelineResource pipeline,
        TextureHandle outputHandle,
        bool clearOutput,
        SmaaPushConstants pc,
        TextureHandle dep0 = default,
        TextureHandle dep1 = default
    )
    {
        _deps.Textures[0] = dep0;
        _deps.Textures[1] = dep1;

        _pass.Colors[0].LoadOp = clearOutput ? LoadOp.Clear : LoadOp.DontCare;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Colors[0].ClearColor = new Color4(0f, 0f, 0f, 0f);

        _fb.Colors[0].Texture = outputHandle;

        cmdBuffer.BeginRendering(_pass, _fb, _deps);
        cmdBuffer.BindRenderPipeline(pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();
    }

    /// <summary>
    /// Compiles the SMAA GLSL shader and creates three render pipelines,
    /// one per <see cref="SmaaMode"/> specialization constant value.
    /// </summary>
    private ResultCode CreatePipelines()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during SMAA pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Compile the shared fragment shader.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psSmaa.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile SMAA fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Compile the shared full-screen-quad vertex shader.
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
            "Smaa_Fragment"
        );

        // Edge-detection pipeline writes to an RG8 intermediate texture.
        _edgePipeline = CreateStagePipeline(
            vs,
            fs,
            SmaaMode.EdgeDetection,
            Format.RG_UN8,
            "Smaa_EdgeDetection"
        );

        // Weight-computation pipeline writes to an RGBA8 intermediate texture.
        _weightPipeline = CreateStagePipeline(
            vs,
            fs,
            SmaaMode.BlendingWeights,
            Format.RGBA_UN8,
            "Smaa_BlendingWeights"
        );

        // Neighbourhood-blending pipeline writes to the same HDR format as all
        // other post-processing effects (the ping-pong colour buffer).
        _blendPipeline = CreateStagePipeline(
            vs,
            fs,
            SmaaMode.NeighbourhoodBlending,
            RenderSettings.IntermediateTargetFormat,
            "Smaa_NeighbourhoodBlending"
        );

        if (!_edgePipeline.Valid || !_weightPipeline.Valid || !_blendPipeline.Valid)
        {
            _logger.LogError("One or more SMAA pipelines failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    private RenderPipelineResource CreateStagePipeline(
        ShaderModuleResource vs,
        ShaderModuleResource fs,
        SmaaMode stage,
        Format outputFormat,
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
        desc.Colors[0] = ColorAttachment.CreateOpaque(outputFormat);
        desc.WriteSpecInfo(0, (uint)stage);
        return Context!.CreateRenderPipeline(desc);
    }
}
