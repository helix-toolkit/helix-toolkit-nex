using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Named quality presets for the <see cref="Fxaa"/> post-processing effect.
/// Each level tunes the three underlying FXAA parameters
/// (<c>contrastThreshold</c>, <c>relativeThreshold</c>, <c>subpixelBlending</c>)
/// according to the standard Timothy Lottes quality ladder.
/// </summary>
public enum FxaaQuality
{
    /// <summary>
    /// Fastest — most aggressive early-outs, minimal sub-pixel blending.
    /// Best for very low-end hardware or when fill-rate is the primary constraint.
    /// Parameters: contrastThreshold=0.0833, relativeThreshold=0.166, subpixelBlending=0.50
    /// </summary>
    Low = 0,

    /// <summary>
    /// Balanced quality / performance trade-off (Lottes "medium" preset).
    /// Suitable for the majority of real-time use cases.
    /// Parameters: contrastThreshold=0.0312, relativeThreshold=0.125, subpixelBlending=0.75
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High quality — more sensitive edge detection and stronger sub-pixel smoothing.
    /// Parameters: contrastThreshold=0.0156, relativeThreshold=0.063, subpixelBlending=1.00
    /// </summary>
    High = 2,

    /// <summary>
    /// Maximum quality — detects the finest edges at the highest fill-rate cost.
    /// Equivalent to the Lottes "extreme" preset.
    /// Parameters: contrastThreshold=0.0078, relativeThreshold=0.031, subpixelBlending=1.00
    /// </summary>
    Ultra = 3,
}

/// <summary>
/// Shader-level debug visualisation modes for <see cref="Fxaa"/>.
/// Maps to specialization constant 0 (<c>FXAA_DEBUG_MODE</c>) in <c>psFxaa.glsl</c>.
/// </summary>
public enum FxaaDebugMode : uint
{
    /// <summary>Normal FXAA output.</summary>
    None = 0,

    /// <summary>
    /// Highlights every pixel that passed the contrast gate in red.
    /// Use this to confirm FXAA is firing and that edges are being detected.
    /// </summary>
    EdgeMask = 1,

    /// <summary>
    /// Renders the per-pixel blend amount as a blue (no blend) → red (max blend) heat map.
    /// Use this to verify the span search is producing non-trivial offsets after the
    /// <see cref="lumaLocalAvg"/> bug fix.
    /// </summary>
    BlendHeatMap = 2,
}

/// <summary>
/// FXAA (Fast Approximate Anti-Aliasing) post-processing effect.
///
/// Implements Timothy Lottes' FXAA 3.11 algorithm as a single full-screen pass.
/// The algorithm detects edges by measuring local luma contrast, then blends each
/// edge pixel with its neighbour across the edge by an amount proportional to
/// how far the pixel sits from the midpoint of the detected edge span.
///
/// FXAA is significantly cheaper than <see cref="Smaa"/> (one pass vs. three) at
/// the cost of slightly lower quality and some blurring of fine detail.  It is a
/// good choice when fill-rate budget is tight.
///
///// Use the <see cref="Quality"/> property to select a named preset, or set
///// <see cref="Quality"/> to <see langword="null"/> and adjust
///// <see cref="ContrastThreshold"/>, <see cref="RelativeThreshold"/>, and
///// <see cref="SubpixelBlending"/> directly for fine-grained control.
/// </summary>
public sealed class Fxaa : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<Fxaa>();

    // Preset parameter table  [contrastThreshold, relativeThreshold, subpixelBlending]
    private static readonly (float Contrast, float Relative, float Subpixel)[] _presets =
    [
        (0.0833f, 0.166f, 0.50f), // Low
        (0.0312f, 0.125f, 0.75f), // Medium
        (0.0156f, 0.063f, 1.00f), // High
        (0.0078f, 0.031f, 1.00f), // Ultra
    ];

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _debugEdgePipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _debugBlendPipeline = RenderPipelineResource.Null;
    private SamplerResource _linearSampler = SamplerResource.Null;
    private readonly RenderPass _pass = new();
    private readonly Framebuffer _framebuffer = new();
    private readonly Dependencies _deps = new();

    // Backing fields for the custom-override properties.
    private float _contrastThreshold = _presets[(int)FxaaQuality.Medium].Contrast;
    private float _relativeThreshold = _presets[(int)FxaaQuality.Medium].Relative;
    private float _subpixelBlending = _presets[(int)FxaaQuality.Medium].Subpixel;

    // -----------------------------------------------------------------------
    // Public settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Selects a named quality preset that simultaneously sets
    /// <see cref="ContrastThreshold"/>, <see cref="RelativeThreshold"/>, and
    /// <see cref="SubpixelBlending"/> to their recommended values for that level.
    /// Assigning a new preset overwrites any previous manual overrides.
    /// Defaults to <see cref="FxaaQuality.Medium"/>.
    /// </summary>
    public FxaaQuality Quality
    {
        get => _quality;
        set
        {
            _quality = value;
            var (contrast, relative, subpixel) = _presets[(int)value];
            _contrastThreshold = contrast;
            _relativeThreshold = relative;
            _subpixelBlending = subpixel;
        }
    }
    private FxaaQuality _quality = FxaaQuality.Medium;

    /// <summary>
    /// Minimum local luma contrast required to trigger anti-aliasing on a pixel.
    /// Pixels whose neighbourhood contrast falls below this value are left untouched.
    /// Lower values detect more edges (higher quality, more fill-rate cost).
    /// Setting this property does not change <see cref="Quality"/>; it acts as a
    /// fine-grained override on top of the last applied preset.
    /// </summary>
    public float ContrastThreshold
    {
        get => _contrastThreshold;
        set => _contrastThreshold = value;
    }

    /// <summary>
    /// Contrast threshold expressed relative to the brightest neighbour.
    /// Prevents AA from firing in very dark regions where absolute contrast
    /// differences are negligible.
    /// Setting this property does not change <see cref="Quality"/>; it acts as a
    /// fine-grained override on top of the last applied preset.
    /// </summary>
    public float RelativeThreshold
    {
        get => _relativeThreshold;
        set => _relativeThreshold = value;
    }

    /// <summary>
    /// Controls the strength of sub-pixel anti-aliasing blending.
    /// <c>1.0</c> maximises blending (softest result); <c>0.0</c> disables it.
    /// Setting this property does not change <see cref="Quality"/>; it acts as a
    /// fine-grained override on top of the last applied preset.
    /// </summary>
    public float SubpixelBlending
    {
        get => _subpixelBlending;
        set => _subpixelBlending = value;
    }

    /// <summary>
    /// Selects a shader-level debug visualisation mode.
    /// Defaults to <see cref="FxaaDebugMode.None"/> (normal output).
    /// Changing this property does not require re-initialisation; the
    /// appropriate pre-compiled pipeline variant is selected each frame.
    /// </summary>
    public FxaaDebugMode DebugMode { get; set; } = FxaaDebugMode.None;

    public override string Name => nameof(Fxaa);
    public override Color DebugColor => Color.Cyan;

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_pipeline.Valid, "FXAA pipeline is not valid.");

        var cmdBuffer = res.CmdBuffer;
        var inputTex = res.Textures[readSlot];

        var dims = res.Context.Context.GetDimensions(inputTex);
        float tw = dims.Width > 0 ? 1.0f / dims.Width : 0f;
        float th = dims.Height > 0 ? 1.0f / dims.Height : 0f;

        _deps.Textures[0] = inputTex;

        _pass.Colors[0].LoadOp = LoadOp.DontCare;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Colors[0].ClearColor = new Color4(0f, 0f, 0f, 1f);

        _framebuffer.Colors[0].Texture = res.Textures[writeSlot];

        var activePipeline = DebugMode switch
        {
            FxaaDebugMode.EdgeMask => _debugEdgePipeline,
            FxaaDebugMode.BlendHeatMap => _debugBlendPipeline,
            _ => _pipeline,
        };

        cmdBuffer.BeginRendering(_pass, _framebuffer, _deps);
        cmdBuffer.BindRenderPipeline(activePipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(
            new FxaaPushConstants
            {
                ColorTextureId = inputTex.Index,
                SamplerId = _linearSampler.Index,
                TexelWidth = tw,
                TexelHeight = th,
                ContrastThreshold = _contrastThreshold,
                RelativeThreshold = _relativeThreshold,
                SubpixelBlending = _subpixelBlending,
            }
        );
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();

        return true;
    }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during FXAA initialization.");
            return ResultCode.InvalidState;
        }

        _linearSampler = Context.CreateSampler(SamplerStateDesc.LinearClamp);
        if (!_linearSampler.Valid)
        {
            return ResultCode.RuntimeError;
        }

        return CreatePipeline();
    }

    protected override ResultCode OnTearingDown()
    {
        _pipeline.Dispose();
        _debugEdgePipeline.Dispose();
        _debugBlendPipeline.Dispose();
        _linearSampler.Dispose();
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during FXAA pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psFxaa.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile FXAA fragment shader: {ERRORS}",
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
            "Fxaa_Fragment"
        );

        _pipeline = CreateVariantPipeline(vs, fs, FxaaDebugMode.None, "Fxaa");
        _debugEdgePipeline = CreateVariantPipeline(
            vs,
            fs,
            FxaaDebugMode.EdgeMask,
            "Fxaa_DebugEdge"
        );
        _debugBlendPipeline = CreateVariantPipeline(
            vs,
            fs,
            FxaaDebugMode.BlendHeatMap,
            "Fxaa_DebugBlend"
        );

        if (!_pipeline.Valid || !_debugEdgePipeline.Valid || !_debugBlendPipeline.Valid)
        {
            _logger.LogError("One or more FXAA pipelines failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    private RenderPipelineResource CreateVariantPipeline(
        ShaderModuleResource vs,
        ShaderModuleResource fs,
        FxaaDebugMode mode,
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
        desc.WriteSpecInfo(0, (uint)mode);
        return Context!.CreateRenderPipeline(desc);
    }
}
