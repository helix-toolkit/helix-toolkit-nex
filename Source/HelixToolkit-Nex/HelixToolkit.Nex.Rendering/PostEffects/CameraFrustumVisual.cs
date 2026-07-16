using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Camera-frustum debug visualization post-processing effect.
///
/// Draws the wireframe of a camera frustum directly from that camera's
/// <b>inverse view-projection</b> matrix — no meshes, vertex buffers, or
/// scene entities are required.
///
/// The vertex shader (<c>vsCameraFrustum.glsl</c>) procedurally generates the
/// 12 edges (24 vertices, line-list) of a unit NDC cube, transforms each corner
/// by the supplied <see cref="CameraFrustumVisualInfo.InversedViewProjection"/>
/// to recover the world-space frustum corners, then projects them with the
/// active render camera so the frustum is drawn from the current viewpoint.
/// This makes it trivial to visualise, for example, a shadow-casting light's
/// frustum or a secondary camera from the main camera's perspective.
/// </summary>
public sealed class CameraFrustumVisual : PostEffect
{
    /// <summary>
    /// Per-frame data driving the frustum visualization.
    /// </summary>
    public struct CameraFrustumVisualInfo
    {
        /// <summary>
        /// The inverse of the visualized camera's view-projection matrix.
        /// A unit NDC cube transformed by this matrix yields the camera's
        /// world-space frustum corners.
        /// </summary>
        public Matrix4x4 InversedViewProjection;

        public readonly bool HasValue =>
            InversedViewProjection != default && !InversedViewProjection.IsIdentity;

        public static readonly CameraFrustumVisualInfo Empty = default;
    }

    private static readonly ILogger _logger = LogManager.Create<CameraFrustumVisual>();

    private RenderPipelineResource _frustumPipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();

    public override Color4 DebugColor => Color.DeepPink;

    public override uint Priority => (uint)PostEffectPriority.Highlight;

    public override string Name => nameof(CameraFrustumVisual);

    /// <summary>
    /// The frustum to draw, expressed as the inverse view-projection of the
    /// camera being visualized. Set this every frame (or whenever the visualized
    /// camera moves) before the effect runs.
    /// </summary>
    public CameraFrustumVisualInfo Data { get; set; } = CameraFrustumVisualInfo.Empty;

    /// <summary>
    /// The wireframe colour of the frustum edges. Defaults to deep pink.
    /// </summary>
    public Color4 FrustumColor { get; set; } = Color.DeepPink;

    /// <summary>
    /// When <c>true</c>, the frustum is drawn with depth testing against the
    /// scene depth buffer so occluded edges are hidden. When <c>false</c>, all
    /// edges are drawn on top of the scene (X-ray mode). Defaults to <c>true</c>.
    /// </summary>
    public bool UseDepthTest { get; set; } = true;

    /// <summary>
    /// Optional clamp (in world units) for how far the frustum's far plane is
    /// drawn from its near plane, measured along each frustum edge.
    /// <para>
    /// Visualized cameras commonly use a very large (or effectively infinite)
    /// far plane. Reconstructing the true far corners then places them hundreds
    /// or thousands of units away, so the frustum reads as just its near plane
    /// while the rest shoots off-screen. Setting a positive value here caps the
    /// drawn far plane to a readable, bounded distance while keeping the true
    /// near plane and edge directions.
    /// </para>
    /// <para>
    /// A value of <c>0</c> (the default) draws the camera's true far plane.
    /// </para>
    /// </summary>
    public float FarPlaneDistance { get; set; }

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_frustumPipeline.Valid, "Camera frustum pipeline is not valid.");
        // Swap so we write into the current texture (same pattern as the other overlays).
        (readSlot, writeSlot) = (writeSlot, readSlot);

        DrawFrustum(in res, res.CmdBuffer, writeSlot);
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        base.OnInitializing();
        return CreatePipeline();
    }

    protected override ResultCode OnTearingDown()
    {
        _frustumPipeline.Dispose();
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Draw pass
    // -----------------------------------------------------------------------

    private void DrawFrustum(in RenderResources res, ICommandBuffer cmdBuffer, string writeSlot)
    {
        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];

        _pass.Colors[0].LoadOp = LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Depth.LoadOp = LoadOp.Load;
        _pass.Depth.StoreOp = StoreOp.Store;

        _frameBuffer.Colors[0].Texture = res.Textures[writeSlot];
        _frameBuffer.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];

        using var depScope = _deps.PushBufferScoped(fpBuffer);

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(_frustumPipeline);
        cmdBuffer.BindDepthState(UseDepthTest ? DepthState.ReadOnlyInvZ : DepthState.Disabled);

        var fpConstAddress = fpBuffer.GpuAddress(res.RenderContext.Context);
        if (Data.HasValue)
        {
            cmdBuffer.PushConstants(
                new CameraFrustumPushConstant
                {
                    Color = FrustumColor,
                    InverseViewProjection = Data.InversedViewProjection,
                    Params = new Vector4(FarPlaneDistance, 0f, 0f, 0f),
                    FpConstAddress = fpConstAddress,
                }
            );
            // 24 vertices (12 edges x 2 endpoints), single instance.
            cmdBuffer.Draw(24, 1);

            cmdBuffer.EndRendering();
        }

        var comps = res.RenderContext.Data!.World.GetComponents<CameraFrustumVisualInfo>();
        foreach (var entity in comps.GetEntities())
        {
            ref var comp = ref comps[entity];
            if (!comp.HasValue)
                continue;

            cmdBuffer.PushConstants(
                new CameraFrustumPushConstant
                {
                    Color = FrustumColor,
                    InverseViewProjection = comp.InversedViewProjection,
                    Params = new Vector4(FarPlaneDistance, 0f, 0f, 0f),
                    FpConstAddress = fpConstAddress,
                }
            );
            // 24 vertices (12 edges x 2 endpoints), single instance.
            cmdBuffer.Draw(24, 1);

            cmdBuffer.EndRendering();
        }
    }

    // -----------------------------------------------------------------------
    // Pipeline creation
    // -----------------------------------------------------------------------

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during camera frustum pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Vertex shader: procedural frustum edge generation from inverse VP.
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsCameraFrustum.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile camera frustum vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Fragment shader: flat-colour output.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psCameraFrustum.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile camera frustum fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "CameraFrustum_Vertex"
        );
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "CameraFrustum_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "CameraFrustum_Wireframe",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.Line,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);
        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

        _frustumPipeline = Context.CreateRenderPipeline(desc);

        if (!_frustumPipeline.Valid)
        {
            _logger.LogError("Camera frustum pipeline failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }
}
