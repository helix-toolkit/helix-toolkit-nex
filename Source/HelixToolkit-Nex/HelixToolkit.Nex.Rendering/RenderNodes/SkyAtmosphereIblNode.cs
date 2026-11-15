using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Bakes the sky atmosphere into a cubemap for PBR image-based lighting.
/// <para>
/// Runs in <see cref="RenderStage.Prepare"/> after <see cref="PrepareNode"/> has uploaded the
/// Forward+ constants. It renders the sky (without the sun disc, which directional light 0
/// already provides) into all six faces of <see cref="SystemBufferNames.TextureSkyAtmosphereCube"/>
/// in a single multiview pass with <c>Frag/psSkyAtmosphereBake.glsl</c>, builds the mip chain and
/// transitions the cube for sampling. <see cref="PrepareNode"/> points the PBR image-based lighting at this cube while
/// <see cref="SkyAtmosphereConfig.ShouldBakeImageBasedLighting"/> is set, so diffuse sky light
/// and reflections follow the time of day.
/// </para>
/// <para>
/// The cube is small (<see cref="FaceSize"/>² per face), so by default it is re-baked every frame,
/// which costs a tiny fraction of the sky background pass and stays correct when the sun is driven
/// by a directional light, whose values are only known on the GPU.
/// <see cref="SkyAtmosphereConfig.ImageBasedLightingUpdateInterval"/> limits how often it is
/// re-baked per <see cref="RenderContext"/>; a bake is still forced on the first frame, when the
/// cube is recreated, and whenever the CPU-side atmosphere settings change.
/// </para>
/// </summary>
public sealed class SkyAtmosphereIblNode : RenderNode
{
    /// <summary>Width and height of each baked cube face, in texels.</summary>
    public const uint FaceSize = 64;

    /// <summary>Number of mip levels in the baked cube (down to 1×1 per face).</summary>
    public static readonly uint MipLevels = (uint)BitOperations.Log2(FaceSize) + 1;

    // Ground blend angle changes smaller than this (radians, ~0.0006°) are not visible in the
    // bake and do not force a re-bake before the update interval.
    private const float GroundBlendAngleTolerance = 1e-5f;

    // Multiview mask selecting all six cube faces (one view per face).
    private const uint CubeFacesViewMask = 0b11_1111;

    // Per-RenderContext bake bookkeeping; dropped automatically with the context.
    private sealed class BakeState
    {
        public bool HasBaked;
        public ulong LastBakeTimeMs;
        public TextureHandle Target = TextureHandle.Null;
        public AtmosphereConstants Atmosphere;
        public Vector3 GroundColor;
        public float GroundBlendAngle;
    }

    private readonly ConditionalWeakTable<RenderContext, BakeState> _bakeStates = new();

    private static readonly ILogger _logger = LogManager.Create<SkyAtmosphereIblNode>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;
    private readonly RenderPass _pass = new();
    private readonly Framebuffer _fb = new();

    public override string Name => nameof(SkyAtmosphereIblNode);
    public override Color4 DebugColor => Color.LightSkyBlue;

    public override string Description =>
        "Bakes the sky atmosphere into a cubemap for PBR image-based lighting.";

    protected override bool OnSetup()
    {
        if (Context is null || ResourceManager is null)
        {
            _logger.LogError(
                "Context or ResourceManager is null during SkyAtmosphereIblNode setup."
            );
            return false;
        }

        var shaderCompiler = new ShaderCompiler();

        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile full-screen quad vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return false;
        }
        using var vs = Context.CreateShaderModuleGlsl(
            vsResult.Source,
            ShaderStage.Vertex,
            "SkyAtmosphereIblNode_VS"
        );

        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psSkyAtmosphereBake.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile sky atmosphere bake shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return false;
        }
        using var fs = Context.CreateShaderModuleGlsl(
            fsResult.Source,
            ShaderStage.Fragment,
            "SkyAtmosphereIblNode_FS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            DebugName = nameof(SkyAtmosphereIblNode),
            // Full-screen triangle per face: no back-face culling needed.
            CullMode = CullMode.None,
            VertexShader = vs,
            FragmentShader = fs,
        };
        pipelineDesc.Colors[0] = ColorAttachment.CreateOpaque(
            GraphicsSettings.IntermediateTargetFormat
        );

        _pipeline = Context.CreateRenderPipeline(pipelineDesc);

        // Every face is fully covered by the full-screen triangle, so the previous contents
        // never need to be loaded. The view mask broadcasts the draw to all six face layers.
        _pass.Colors[0].LoadOp = LoadOp.DontCare;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Colors[0].Layer = 0;
        _pass.Colors[0].Level = 0;
        _pass.ViewMask = CubeFacesViewMask;
        return _pipeline.Valid;
    }

    protected override void OnTeardown()
    {
        _pipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        if (!_pipeline.Valid || !res.RenderContext.SkyAtmosphere.ShouldBakeImageBasedLighting)
        {
            // Forget the last bake so re-enabling bakes immediately instead of waiting for the
            // update interval with a stale cube.
            _bakeStates.Remove(res.RenderContext);
            return false;
        }
        return res.Textures.TryGetValue(SystemBufferNames.TextureSkyAtmosphereCube, out var cube)
            && cube.Valid
            && IsBakeDue(res.RenderContext, cube);
    }

    /// <summary>
    /// Decides whether the cube must be re-baked this frame and, if so, records the bake. A bake is
    /// due on the first frame, when the cube was recreated, when the CPU-side atmosphere or ground
    /// settings changed, or once <see cref="SkyAtmosphereConfig.ImageBasedLightingUpdateInterval"/>
    /// has elapsed since the last bake (every frame when the interval is zero).
    /// </summary>
    private bool IsBakeDue(RenderContext context, in TextureHandle cube)
    {
        var sky = context.SkyAtmosphere;
        var state = _bakeStates.GetOrCreateValue(context);
        var now = context.TimeMs;
        var atmosphere = sky.ToConstants();

        var intervalMs = sky.ImageBasedLightingUpdateInterval.TotalMilliseconds;
        var due =
            !state.HasBaked
            || state.Target != cube
            || intervalMs <= 0
            || now - state.LastBakeTimeMs >= intervalMs
            || !BytesEqual(state.Atmosphere, atmosphere)
            || state.GroundColor != sky.GroundColor
            || MathF.Abs(state.GroundBlendAngle - sky.GroundBlendAngle) > GroundBlendAngleTolerance;
        if (!due)
        {
            return false;
        }

        state.HasBaked = true;
        state.LastBakeTimeMs = now;
        state.Target = cube;
        state.Atmosphere = atmosphere;
        state.GroundColor = sky.GroundColor;
        state.GroundBlendAngle = sky.GroundBlendAngle;
        return true;
    }

    private static bool BytesEqual(in AtmosphereConstants a, in AtmosphereConstants b)
    {
        return MemoryMarshal
            .AsBytes(MemoryMarshal.CreateReadOnlySpan(in a, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in b, 1)));
    }

    protected override void OnSetupRender(in RenderResources res) { }

    // The bake uses its own multiview render pass, begun in OnRender.
    protected override bool BeginRender(in RenderResources res)
    {
        return true;
    }

    protected override void EndRender(in RenderResources res) { }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_pipeline.Valid, "Sky atmosphere bake pipeline is not valid.");

        var cmdBuffer = res.CmdBuffer;
        var cube = res.Textures[SystemBufferNames.TextureSkyAtmosphereCube];
        var fpConstAddress = res
            .Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(res.RenderContext.Context);

        var sky = res.RenderContext.SkyAtmosphere;
        var groundBlend = Math.Clamp(sky.GroundBlendAngle, 0f, MathF.PI / 2);
        var pc = new SkyAtmosphereBakePushConstants
        {
            FpConstAddress = fpConstAddress,
            FaceSize = FaceSize,
            GroundColor = sky.GroundColor,
            GroundBlendSin = MathF.Sin(groundBlend),
        };

        // One multiview pass: the full-screen triangle is broadcast to all six faces and the
        // shader picks the face from gl_ViewIndex.
        _fb.Colors[0].Texture = cube;
        res.Deps.Clear();
        cmdBuffer.BeginRendering(_pass, _fb, res.Deps);
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3); // full-screen triangle
        cmdBuffer.EndRendering();

        // Box-filtered mips: the coarsest level approximates diffuse irradiance and the
        // intermediate levels serve rough reflections (see environmentIBL in psPBRTemplate.glsl).
        cmdBuffer.GenerateMipmap(cube);
        cmdBuffer.TransitionToShaderReadOnly(cube, ShaderStage.Fragment);
        sky.BakedImageBasedLightingCube = cube;
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddTexture(
                SystemBufferNames.TextureSkyAtmosphereCube,
                p =>
                    p.Context.Context.CreateTexture(
                        new TextureDesc
                        {
                            Type = TextureType.TextureCube,
                            Format = GraphicsSettings.IntermediateTargetFormat,
                            Dimensions = new Dimensions(FaceSize, FaceSize, 1),
                            NumMipLevels = MipLevels,
                            Usage = TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                            Storage = StorageType.Device,
                        },
                        SystemBufferNames.TextureSkyAtmosphereCube
                    ),
                dependsOnScreenSize: false
            )
            .AddPass(
                RenderStage.Prepare,
                nameof(SkyAtmosphereIblNode),
                inputs: [new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer)],
                outputs: [new(SystemBufferNames.TextureSkyAtmosphereCube, ResourceType.Texture)],
                after: [nameof(PrepareNode)]
            );
    }
}
