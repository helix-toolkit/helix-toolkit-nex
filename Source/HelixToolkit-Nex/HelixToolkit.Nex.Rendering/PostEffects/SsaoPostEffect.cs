using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Screen-Space Ambient Occlusion (SSAO) post-processing effect.
///
/// <para>
/// Darkens creases, corners, and contact regions by estimating how much of a pixel's
/// surrounding hemisphere is occluded by nearby geometry, using only the depth buffer that
/// the engine already produces (<see cref="SystemBufferNames.TextureDepthF32"/>). Surface
/// normals are reconstructed from depth in the shader, so no additional geometry pass or
/// normals G-buffer is required.
/// </para>
///
/// <para>
/// The effect derives from <see cref="PostEffect"/> and is owned and sequenced by
/// <see cref="PostEffectsNode"/> inside the HDR ping-pong chain. It runs before any
/// higher-priority color-grading / anti-aliasing post-effect via
/// <see cref="PostEffectPriority.AmbientOcclusion"/>. Its intermediate AO render targets are
/// registered with the shared <see cref="RenderGraph"/> via
/// <see cref="RegisterResources"/> so they are allocated and resized with the viewport.
/// </para>
///
/// <para>
/// The pure, GPU-free math this effect uploads and mirrors in the shader lives in
/// <see cref="SsaoMath"/>. Tunable parameters are clamped on assignment through the
/// <c>SsaoMath.Clamp*</c> helpers and applied on the next frame without any pipeline rebuild.
/// </para>
/// </summary>
public sealed class SsaoPostEffect : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<SsaoPostEffect>();

    // -----------------------------------------------------------------------
    // Owned GPU state (pipelines only; textures are graph-managed).
    // These are created in OnInitializing and disposed in OnTearingDown (tasks 6.1–6.3).
    // -----------------------------------------------------------------------

    private RenderPipelineResource _occlusionPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurHPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _blurVPipeline = RenderPipelineResource.Null;
    private RenderPipelineResource _compositePipeline = RenderPipelineResource.Null; // Normal
    private RenderPipelineResource _compositeDebugPipeline = RenderPipelineResource.Null; // RawAO

    private SamplerRef _pointSampler = SamplerRef.Null; // depth + AO (nearest)
    private SamplerRef _linearSampler = SamplerRef.Null; // AO upsample in half-res composite

    // Captured in RegisterResources so the HalfResolution setter can invalidate the graph.
    private RenderGraph? _graph;

    // Reusable per-pass GPU state (avoids per-frame allocations), mirroring
    // BorderHighlightPostEffect's private pass/framebuffer/dependency instances.
    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();

    // -----------------------------------------------------------------------
    // Backing fields for tunable parameters (clamped on assignment).
    // -----------------------------------------------------------------------

    private SsaoQuality _quality;
    private float _radius = 0.5f;
    private float _intensity = 1.0f;
    private float _bias = 0.025f;
    private float _power = 1.0f;
    private int _sampleCount = 16;
    private float _blurDepthThreshold = 0.1f;
    private bool _halfResolution;

    /// <summary>
    /// Constructs the SSAO effect, defaulting to the <see cref="SsaoQuality.Medium"/> preset
    /// (Sample_Count 16), blur enabled, full resolution, and normal (non-debug) output.
    /// </summary>
    /// <param name="quality">The initial quality preset. Defaults to <see cref="SsaoQuality.Medium"/>.</param>
    public SsaoPostEffect(SsaoQuality quality = SsaoQuality.Medium)
    {
        // Assigning through the property applies the preset's authoritative sample count.
        Quality = quality;
    }

    /// <inheritdoc/>
    public override string Name => nameof(SsaoPostEffect);

    /// <inheritdoc/>
    public override Color4 DebugColor => Color.SlateGray;

    /// <inheritdoc/>
    public override uint Priority => (uint)PostEffectPriority.AmbientOcclusion;

    /// <summary>
    /// Gets or sets the quality preset. Assigning a preset authoritatively overwrites
    /// <see cref="SampleCount"/> with the preset's value from
    /// <see cref="SsaoPresets.SampleCounts"/> (Req 4.2, 4.6).
    /// </summary>
    public SsaoQuality Quality
    {
        get => _quality;
        set
        {
            _quality = value;
            _sampleCount = SsaoPresets.SampleCounts[(int)value];
        }
    }

    /// <summary>
    /// Gets or sets the view-space sampling radius in world units. Clamped to
    /// <c>[0.01, 100.0]</c> on assignment; default <c>0.5</c> (Req 5.1, 5.6).
    /// </summary>
    public float Radius
    {
        get => _radius;
        set => _radius = SsaoMath.ClampRadius(value);
    }

    /// <summary>
    /// Gets or sets the occlusion darkening multiplier. Clamped to <c>[0.0, 10.0]</c> on
    /// assignment; default <c>1.0</c> (Req 5.2, 5.7).
    /// </summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = SsaoMath.ClampIntensity(value);
    }

    /// <summary>
    /// Gets or sets the self-occlusion bias in view-space units. Clamped to <c>[0.0, 1.0]</c>
    /// on assignment; default <c>0.025</c> (Req 5.3, 5.9).
    /// </summary>
    public float Bias
    {
        get => _bias;
        set => _bias = SsaoMath.ClampBias(value);
    }

    /// <summary>
    /// Gets or sets the falloff-contrast exponent applied to the occlusion factor. Clamped to
    /// <c>[0.1, 16.0]</c> on assignment; default <c>1.0</c> (Req 5.4, 5.10).
    /// </summary>
    public float Power
    {
        get => _power;
        set => _power = SsaoMath.ClampPower(value);
    }

    /// <summary>
    /// Gets or sets the number of hemisphere samples per pixel. Clamped to <c>[1, 256]</c> on
    /// assignment; preset-driven (default <c>16</c> for <see cref="SsaoQuality.Medium"/>).
    /// Note: assigning <see cref="Quality"/> overwrites this value (Req 5.5, 5.8, 4.6).
    /// </summary>
    public int SampleCount
    {
        get => _sampleCount;
        set => _sampleCount = SsaoMath.ClampSampleCount(value);
    }

    /// <summary>
    /// Gets or sets whether the edge-aware blur pass is applied to the AO buffer before
    /// compositing. Defaults to <see langword="true"/> (Req 6.8).
    /// </summary>
    public bool BlurEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum view-space depth difference (in world units) beyond which a
    /// neighbor is excluded from the blur. Any value ≤ 0 is clamped to a fixed positive
    /// minimum on assignment; default <c>0.1</c> (Req 6.6, 6.7).
    /// </summary>
    public float BlurDepthThreshold
    {
        get => _blurDepthThreshold;
        set => _blurDepthThreshold = SsaoMath.ClampBlurDepthThreshold(value);
    }

    /// <summary>
    /// Gets or sets whether the AO buffer is computed at half the viewport resolution.
    /// Defaults to <see langword="false"/>. Toggling invalidates the render graph so the AO
    /// buffers are reallocated at the new size before the next computation (Req 8.1, 8.5).
    /// </summary>
    public bool HalfResolution
    {
        get => _halfResolution;
        set
        {
            if (_halfResolution == value)
            {
                return;
            }
            _halfResolution = value;
            _graph?.Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the debug visualization mode. Defaults to <see cref="SsaoDebugMode.Normal"/>.
    /// Changing the mode selects a pre-created pipeline variant without reinitialization
    /// (Req 9.5, 9.6).
    /// </summary>
    public SsaoDebugMode DebugMode { get; set; } = SsaoDebugMode.Normal;

    // Enabled is inherited from PostEffect (defaults true) (Req 10.1).

    /// <inheritdoc/>
    /// <remarks>
    /// Captures the render graph so <see cref="HalfResolution"/> changes can invalidate it,
    /// then registers exactly one AO buffer (<see cref="SystemBufferNames.TextureSsaoAO"/>)
    /// plus the horizontal-blur scratch target (<see cref="SystemBufferNames.TextureSsaoBlur"/>)
    /// as screen-size-dependent single-channel (<see cref="Format.R_F16"/>) render targets
    /// (Req 11.1, 11.2). Both build functions read the live <see cref="HalfResolution"/> flag
    /// through <see cref="SsaoMath.AoDim"/>, so when the graph is rebuilt — whether from a
    /// viewport resize or a <see cref="HalfResolution"/> toggle — the textures are reallocated
    /// at the resolution derived from the new viewport and the current mode (Req 8.2, 8.4).
    /// </remarks>
    public override void RegisterResources(RenderGraph graph)
    {
        // Capture the graph so the HalfResolution setter can invalidate it (Req 8.5).
        _graph = graph;

        // AO_Buffer — the single AO buffer holding the raw factor after the occlusion pass
        // and the blurred factor after the blur pass (Req 11.1).
        graph.AddTexture(
            SystemBufferNames.TextureSsaoAO,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.R_F16,
                    SsaoMath.AoDim(p.Context.WindowSize.Width, _halfResolution),
                    SsaoMath.AoDim(p.Context.WindowSize.Height, _halfResolution),
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureSsaoAO
                ),
            dependsOnScreenSize: true
        );

        // Horizontal-blur scratch target, only written when the blur pass runs. A separate
        // resource, so "exactly one AO_Buffer" is still satisfied.
        graph.AddTexture(
            SystemBufferNames.TextureSsaoBlur,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.R_F16,
                    SsaoMath.AoDim(p.Context.WindowSize.Width, _halfResolution),
                    SsaoMath.AoDim(p.Context.WindowSize.Height, _halfResolution),
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureSsaoBlur
                ),
            dependsOnScreenSize: true
        );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs up to four full-screen-triangle passes each frame:
    /// <list type="number">
    ///   <item>Occlusion (<c>TextureDepthF32</c> → <c>TextureSsaoAO</c>).</item>
    ///   <item>Optional edge-aware separable blur when <see cref="BlurEnabled"/>
    ///     (<c>TexSsaoAO</c> → <c>TexSsaoBlur</c> horizontal, then
    ///     <c>TexSsaoBlur</c> → <c>TexSsaoAO</c> vertical).</item>
    ///   <item>Composite (scene <paramref name="readSlot"/> + AO → <paramref name="writeSlot"/>),
    ///     selecting the <see cref="SsaoDebugMode.Normal"/> or <see cref="SsaoDebugMode.RawAO"/>
    ///     pipeline variant per <see cref="DebugMode"/>.</item>
    /// </list>
    /// The guard sequence returns <see langword="false"/> without touching Scene_Color when the
    /// effect is uninitialized (Req 12.5), the viewport is zero (Req 11.3), the depth buffer is
    /// missing (Req 2.7), the camera constants are missing (Req 12.4), or the ping-pong write
    /// slot is unavailable (Req 7.6). On success the composite writes the freshly darkened scene
    /// to <paramref name="writeSlot"/> and returns <see langword="true"/>; the hosting
    /// <see cref="PostEffectsNode"/> then swaps the slots so that texture feeds the next effect
    /// (Req 7.1, 7.5, 9.7).
    /// </remarks>
    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        // --- Guard sequence (Scene_Color is left untouched on any failure) ---

        // 1. Not initialized / failed initialization (Req 12.5).
        if (!IsInitialized)
        {
            return false;
        }

        // 2. Zero viewport width or height (Req 11.3).
        var windowSize = res.RenderContext.WindowSize;
        if (windowSize.Width <= 0 || windowSize.Height <= 0)
        {
            return false;
        }

        // 3. Depth buffer missing this frame (Req 2.7).
        if (
            !res.Textures.TryGetValue(SystemBufferNames.TextureDepthF32, out var depthTex)
            || !depthTex.Valid
        )
        {
            return false;
        }

        // 4. Camera constants missing this frame (Req 12.4).
        if (
            !res.Buffers.TryGetValue(
                SystemBufferNames.BufferForwardPlusConstants,
                out var fpBuffer
            )
            || !fpBuffer.Valid
        )
        {
            return false;
        }

        // 5. Ping-pong write slot texture unavailable (Req 7.6).
        if (!res.Textures.TryGetValue(writeSlot, out var writeTex) || !writeTex.Valid)
        {
            return false;
        }

        // Scene color read slot must also be available to composite against.
        if (!res.Textures.TryGetValue(readSlot, out var sceneTex) || !sceneTex.Valid)
        {
            return false;
        }

        var context = res.RenderContext;
        var cmdBuffer = res.CmdBuffer;
        var fpConstAddress = fpBuffer.GpuAddress(context.Context);

        var aoTex = res.Textures[SystemBufferNames.TextureSsaoAO];
        var aoBlurTex = res.Textures[SystemBufferNames.TextureSsaoBlur];

        // Texel size of the AO buffer (which may be half-resolution). The blur pass steps by
        // this along each axis and the occlusion pass distributes samples in AO-buffer space.
        var aoDims = context.Context.GetDimensions(aoTex);
        var invAoSize = new Vector2(
            aoDims.Width > 0 ? 1.0f / aoDims.Width : 0f,
            aoDims.Height > 0 ? 1.0f / aoDims.Height : 0f
        );

        // Full-resolution texel size for the composite target.
        var fullTexelSize = new Vector2(1.0f / windowSize.Width, 1.0f / windowSize.Height);

        var sampleCount = (uint)_sampleCount;

        // ------------------------------------------------------------------
        // Pass 1 — Occlusion: TextureDepthF32 -> TextureSsaoAO
        // ------------------------------------------------------------------
        RunFullScreenPass(
            cmdBuffer,
            _occlusionPipeline,
            outputHandle: aoTex,
            clearOutput: true,
            new SsaoPushConstants
            {
                DepthTextureId = depthTex.Index,
                DepthSamplerId = _pointSampler,
                FpConstAddress = fpConstAddress,
                TexelSize = invAoSize,
                InvAoSize = invAoSize,
                Radius = _radius,
                Intensity = _intensity,
                Bias = _bias,
                Power = _power,
                SampleCount = sampleCount,
                BlurDepthThreshold = _blurDepthThreshold,
            },
            dep0: depthTex,
            fpDependency: fpBuffer
        );

        // The AO texture the composite reads from: raw after occlusion, blurred after Pass 2.
        var compositeAoTex = aoTex;

        // ------------------------------------------------------------------
        // Pass 2 — Edge-aware separable blur (only when enabled, Req 6.2/6.3)
        // ------------------------------------------------------------------
        if (BlurEnabled)
        {
            // Pass 2a — horizontal blur: TexSsaoAO -> TexSsaoBlur.
            RunFullScreenPass(
                cmdBuffer,
                _blurHPipeline,
                outputHandle: aoBlurTex,
                clearOutput: true,
                new SsaoPushConstants
                {
                    DepthTextureId = depthTex.Index,
                    DepthSamplerId = _pointSampler,
                    AoTextureId = aoTex.Index,
                    AoSamplerId = _pointSampler,
                    FpConstAddress = fpConstAddress,
                    TexelSize = invAoSize,
                    InvAoSize = invAoSize,
                    Radius = _radius,
                    Intensity = _intensity,
                    Bias = _bias,
                    Power = _power,
                    SampleCount = sampleCount,
                    BlurDepthThreshold = _blurDepthThreshold,
                },
                dep0: aoTex,
                dep1: depthTex,
                fpDependency: fpBuffer
            );

            // Pass 2b — vertical blur: TexSsaoBlur -> TexSsaoAO (smoothed factor back in AO).
            RunFullScreenPass(
                cmdBuffer,
                _blurVPipeline,
                outputHandle: aoTex,
                clearOutput: true,
                new SsaoPushConstants
                {
                    DepthTextureId = depthTex.Index,
                    DepthSamplerId = _pointSampler,
                    AoTextureId = aoBlurTex.Index,
                    AoSamplerId = _pointSampler,
                    FpConstAddress = fpConstAddress,
                    TexelSize = invAoSize,
                    InvAoSize = invAoSize,
                    Radius = _radius,
                    Intensity = _intensity,
                    Bias = _bias,
                    Power = _power,
                    SampleCount = sampleCount,
                    BlurDepthThreshold = _blurDepthThreshold,
                },
                dep0: aoBlurTex,
                dep1: depthTex,
                fpDependency: fpBuffer
            );

            compositeAoTex = aoTex;
        }

        // ------------------------------------------------------------------
        // Pass 3 — Composite: scene (readSlot) + AO -> writeSlot
        // ------------------------------------------------------------------
        // In half-resolution mode the AO fetch upsamples, so use the linear sampler; otherwise
        // a nearest fetch of the full-resolution AO buffer (Req 8.3).
        var aoSampler = _halfResolution ? _linearSampler : _pointSampler;

        // Select the pre-created Normal or RawAO composite variant per DebugMode (Req 9.2, 9.6).
        var compositePipeline =
            DebugMode == SsaoDebugMode.RawAO ? _compositeDebugPipeline : _compositePipeline;

        RunFullScreenPass(
            cmdBuffer,
            compositePipeline,
            outputHandle: writeTex,
            clearOutput: false,
            new SsaoPushConstants
            {
                AoTextureId = compositeAoTex.Index,
                AoSamplerId = aoSampler,
                SceneTextureId = sceneTex.Index,
                SceneSamplerId = _pointSampler,
                FpConstAddress = fpConstAddress,
                TexelSize = fullTexelSize,
                InvAoSize = invAoSize,
                Radius = _radius,
                Intensity = _intensity,
                Bias = _bias,
                Power = _power,
                SampleCount = sampleCount,
                BlurDepthThreshold = _blurDepthThreshold,
            },
            dep0: sceneTex,
            dep1: compositeAoTex
        );

        // The composite wrote the freshly darkened scene to writeSlot. Returning true lets the
        // hosting PostEffectsNode swap the slots so that texture becomes the next effect's input
        // (Req 7.1, 7.5, 9.7); readSlot/writeSlot are left untouched here for that swap.
        return true;
    }

    /// <summary>
    /// Executes one full-screen-triangle SSAO pass into <paramref name="outputHandle"/>, binding
    /// up to two sampled-texture dependencies and an optional camera-constants buffer dependency,
    /// then uploading <paramref name="pc"/> as push constants.
    /// </summary>
    /// <param name="clearOutput">
    /// When <see langword="true"/> the output attachment is cleared to white (fully lit) before
    /// drawing; the occlusion and blur passes overwrite every pixel, so this only guards against
    /// undefined initial contents. When <see langword="false"/> the existing content is loaded
    /// (the composite pass writes every pixel unconditionally).
    /// </param>
    private void RunFullScreenPass(
        ICommandBuffer cmdBuffer,
        RenderPipelineResource pipeline,
        TextureHandle outputHandle,
        bool clearOutput,
        SsaoPushConstants pc,
        TextureHandle dep0 = default,
        TextureHandle dep1 = default,
        BufferHandle fpDependency = default
    )
    {
        _deps.Clear();
        if (dep0.Valid)
        {
            _deps.PushTexture(dep0);
        }
        if (dep1.Valid)
        {
            _deps.PushTexture(dep1);
        }
        if (fpDependency.Valid)
        {
            _deps.PushBuffer(fpDependency);
        }

        _pass.Colors[0].LoadOp = clearOutput ? LoadOp.Clear : LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Colors[0].ClearColor = new Color4(1f, 1f, 1f, 1f);

        _frameBuffer.Colors[0].Texture = outputHandle;

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();
    }

    // -----------------------------------------------------------------------
    // SSAO_STAGE specialization-constant values (constant_id 0 in psSsao.glsl).
    // -----------------------------------------------------------------------
    private const uint StageOcclusion = 0;
    private const uint StageBlur = 1;
    private const uint StageComposite = 2;

    // SSAO_BLUR_AXIS specialization-constant values (constant_id 1 in psSsao.glsl).
    private const uint BlurAxisH = 0;
    private const uint BlurAxisV = 1;

    /// <inheritdoc/>
    /// <remarks>
    /// Acquires the point/linear clamp samplers, compiles the SSAO fragment shader and the
    /// shared full-screen-quad vertex shader, and creates the five pipeline variants
    /// (occlusion, blur-H, blur-V, composite, composite-debug) selected by the
    /// <c>SSAO_STAGE</c>, <c>SSAO_BLUR_AXIS</c>, and <c>SSAO_DEBUG</c> specialization
    /// constants. On a missing resource manager returns <see cref="ResultCode.InvalidState"/>;
    /// on shader compile failure returns <see cref="ResultCode.CompileError"/> (before any
    /// pipeline is created); on pipeline creation failure returns
    /// <see cref="ResultCode.RuntimeError"/> after disposing any already-created pipelines. In
    /// every failure case the effect stays uninitialized and the error is logged (Req 12.1–12.3).
    /// </remarks>
    protected override ResultCode OnInitializing()
    {
        var result = base.OnInitializing();
        if (result != ResultCode.Ok)
        {
            return result;
        }

        if (ResourceManager is null)
        {
            _logger.LogError("ResourceManager is null during SSAO initialization.");
            return ResultCode.InvalidState;
        }

        // Point/nearest clamp sampler for depth + AO fetches; linear clamp for the half-res
        // AO upsample in the composite pass.
        _pointSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.PointClamp.DebugName,
            SamplerStateDesc.PointClamp
        );
        _linearSampler = ResourceManager.SamplerRepository.GetOrCreate(
            SamplerStateDesc.LinearClamp.DebugName,
            SamplerStateDesc.LinearClamp
        );

        if (!_pointSampler.Valid || !_linearSampler.Valid)
        {
            _logger.LogError("Failed to create SSAO samplers.");
            return ResultCode.RuntimeError;
        }

        return CreatePipelines();
    }

    /// <summary>
    /// Compiles <c>psSsao.glsl</c> and <c>vsFullScreenQuad.glsl</c> and creates the five
    /// pipeline variants. Aborts before creating any pipeline on a shader compile failure
    /// (<see cref="ResultCode.CompileError"/>) and disposes any already-created pipelines on a
    /// pipeline-creation failure (<see cref="ResultCode.RuntimeError"/>).
    /// </summary>
    private ResultCode CreatePipelines()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during SSAO pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Compile the shared SSAO fragment shader (all five variants share this module and
        // differ only by specialization constants).
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psSsao.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile SSAO fragment shader: {ERRORS}",
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
                "Failed to compile SSAO full-screen quad vertex shader: {ERRORS}",
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
            "Ssao_Fragment"
        );

        // Pass 1 — occlusion → AO buffer (single-channel R_F16).
        _occlusionPipeline = CreateStagePipeline(
            vs,
            fs,
            StageOcclusion,
            BlurAxisH,
            SsaoDebugMode.Normal,
            Format.R_F16,
            "Ssao_Occlusion"
        );

        // Pass 2a — horizontal blur → AO_Blur (single-channel R_F16).
        _blurHPipeline = CreateStagePipeline(
            vs,
            fs,
            StageBlur,
            BlurAxisH,
            SsaoDebugMode.Normal,
            Format.R_F16,
            "Ssao_Blur_H"
        );

        // Pass 2b — vertical blur → AO buffer (single-channel R_F16).
        _blurVPipeline = CreateStagePipeline(
            vs,
            fs,
            StageBlur,
            BlurAxisV,
            SsaoDebugMode.Normal,
            Format.R_F16,
            "Ssao_Blur_V"
        );

        // Pass 3 — composite (normal darkening) → HDR ping-pong slot.
        _compositePipeline = CreateStagePipeline(
            vs,
            fs,
            StageComposite,
            BlurAxisH,
            SsaoDebugMode.Normal,
            GraphicsSettings.IntermediateTargetFormat,
            "Ssao_Composite"
        );

        // Pass 3 (debug) — composite (RawAO grayscale) → HDR ping-pong slot.
        _compositeDebugPipeline = CreateStagePipeline(
            vs,
            fs,
            StageComposite,
            BlurAxisH,
            SsaoDebugMode.RawAO,
            GraphicsSettings.IntermediateTargetFormat,
            "Ssao_Composite_Debug"
        );

        if (
            !_occlusionPipeline.Valid
            || !_blurHPipeline.Valid
            || !_blurVPipeline.Valid
            || !_compositePipeline.Valid
            || !_compositeDebugPipeline.Valid
        )
        {
            _logger.LogError("One or more SSAO pipelines failed to create.");
            // Dispose any already-created pipelines and stay uninitialized (Req 12.3).
            _occlusionPipeline.Dispose();
            _blurHPipeline.Dispose();
            _blurVPipeline.Dispose();
            _compositePipeline.Dispose();
            _compositeDebugPipeline.Dispose();
            _occlusionPipeline = RenderPipelineResource.Null;
            _blurHPipeline = RenderPipelineResource.Null;
            _blurVPipeline = RenderPipelineResource.Null;
            _compositePipeline = RenderPipelineResource.Null;
            _compositeDebugPipeline = RenderPipelineResource.Null;
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    /// <summary>
    /// Creates a single SSAO pipeline variant from the shared vertex/fragment modules,
    /// selecting the pass, blur axis, and debug mode through specialization constants
    /// (<c>SSAO_STAGE</c> = 0, <c>SSAO_BLUR_AXIS</c> = 1, <c>SSAO_DEBUG</c> = 2).
    /// </summary>
    private RenderPipelineResource CreateStagePipeline(
        ShaderModuleResource vs,
        ShaderModuleResource fs,
        uint stage,
        uint blurAxis,
        SsaoDebugMode debugMode,
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
        desc.WriteSpecInfo(0, stage);
        desc.WriteSpecInfo(1, blurAxis);
        desc.WriteSpecInfo(2, (uint)debugMode);
        return Context!.CreateRenderPipeline(desc);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Disposes exactly the five owned pipelines. Graph-managed textures are released by the
    /// resource set. Disposing an already-disposed pipeline is a no-op, so repeated teardown
    /// is safe (Req 11.4–11.6). Pipeline creation itself is implemented in task 6.2.
    /// </remarks>
    protected override ResultCode OnTearingDown()
    {
        // Dispose exactly the five owned pipelines. Disposing an already-disposed
        // RenderPipelineResource is a no-op, and resetting to Null makes a repeated teardown
        // dispose nothing, so double teardown neither double-releases nor throws (Req 11.6).
        _occlusionPipeline.Dispose();
        _blurHPipeline.Dispose();
        _blurVPipeline.Dispose();
        _compositePipeline.Dispose();
        _compositeDebugPipeline.Dispose();

        _occlusionPipeline = RenderPipelineResource.Null;
        _blurHPipeline = RenderPipelineResource.Null;
        _blurVPipeline = RenderPipelineResource.Null;
        _compositePipeline = RenderPipelineResource.Null;
        _compositeDebugPipeline = RenderPipelineResource.Null;

        // TextureSsaoAO / TextureSsaoBlur are owned by the shared resource set — not disposed
        // here (Req 11.5). The repository-owned samplers are likewise left alone.
        return ResultCode.Ok;
    }
}
