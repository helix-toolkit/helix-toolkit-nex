namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Creates the render pipeline used to draw solid gizmo handles procedurally.
/// <para>
/// The pipeline pairs the gizmo vertex shader (<c>Vert/vsGizmo.glsl</c>), which applies
/// constant-screen-size scaling in gizmo-local space, with the gizmo fragment shader
/// (<c>Frag/psGizmo.glsl</c>), which writes the handle colour to color attachment 0
/// (the tone-mapped LDR target) and the handle's reserved gizmo entity id to color
/// attachment 1 (the <see cref="Format.RG_F32"/> entity-id target) for picking.
/// </para>
/// <para>
/// This helper mirrors the procedural push-constant pattern established by
/// <c>BoundingBoxPostEffect.CreatePipeline</c>. It is intentionally decoupled from the
/// (not-yet-implemented) gizmo render node so the node's <c>OnSetup</c> can call it
/// directly. For graceful degradation on failure (returning <c>false</c> from
/// <c>OnSetup</c> so the node stays detached, logging a warning, and guarding render
/// recording), the node should call <see cref="GizmoRenderNodeResilience.TrySetup"/> and
/// <see cref="GizmoRenderNodeResilience.CanRecord"/> rather than invoking
/// <see cref="Create"/> directly. The occlusion-mode depth state is chosen at draw time (via
/// <c>BindDepthState</c>), not baked into the pipeline, so a single pipeline serves both
/// the always-on-top and depth-tested occlusion modes.
/// </para>
/// </summary>
public static class GizmoSolidHandlePipeline
{
    private static readonly ILogger _logger = LogManager.Create(nameof(GizmoSolidHandlePipeline));

    /// <summary>The number of procedural vertices emitted for a solid box handle (12 triangles).</summary>
    public const uint BoxVertexCount = 36;

    /// <summary>The number of procedural vertices emitted for a solid plane handle (2 triangles).</summary>
    public const uint PlaneVertexCount = 6;

    /// <summary>
    /// The number of radial segments of an arrow-head cone. Must match <c>CONE_SIDES</c> in
    /// <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint ConeSides = 16;

    /// <summary>
    /// The number of procedural vertices emitted for an arrow (translate) handle: a 36-vertex box
    /// shaft plus a cone head of <c>ConeSides * 6</c> vertices (side triangles + base cap). Must
    /// match the arrow layout in <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint ArrowVertexCount = 36 + (ConeSides * 6);

    /// <summary>
    /// The number of procedural vertices emitted for a scale handle: a 36-vertex box shaft plus a
    /// 36-vertex end cube. Must match the scale-handle layout in <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint ScaleVertexCount = 36 + 36;

    /// <summary>
    /// The number of radial segments swept to form a ring handle's flat annulus band. Must match
    /// <c>RING_SEGMENTS</c> in <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint RingSegments = 64;

    /// <summary>
    /// The number of procedural vertices emitted for a ring handle: <see cref="RingSegments"/>
    /// segments, each a quad of two triangles (6 vertices). Must equal <c>RING_SEGMENTS * 6</c> in
    /// <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint RingVertexCount = RingSegments * 6;

    /// <summary>
    /// The number of procedural vertices emitted for a line handle (a thin quad of two triangles).
    /// Must match the line quad in <c>Vert/vsGizmo.glsl</c>.
    /// </summary>
    public const uint LineVertexCount = 6;

    /// <summary>The debug name assigned to the created pipeline.</summary>
    public const string PipelineDebugName = "Gizmo_SolidHandle";

    /// <summary>
    /// Compiles the gizmo shaders and creates the solid-handle render pipeline.
    /// </summary>
    /// <param name="context">The graphics context used to create the pipeline.</param>
    /// <param name="shaderRepository">The shader repository used to cache/create the shader modules.</param>
    /// <param name="pipeline">
    /// On success, receives the created pipeline. On failure, receives
    /// <see cref="RenderPipelineResource.Null"/>.
    /// </param>
    /// <returns>
    /// <see cref="ResultCode.Ok"/> when the pipeline is created successfully; otherwise a
    /// result code describing the failure (<see cref="ResultCode.InvalidState"/> for a null
    /// context, <see cref="ResultCode.CompileError"/> for a shader compile failure, or
    /// <see cref="ResultCode.RuntimeError"/> when pipeline creation fails).
    /// </returns>
    public static ResultCode Create(
        IContext? context,
        IShaderRepository shaderRepository,
        out RenderPipelineResource pipeline
    )
    {
        pipeline = RenderPipelineResource.Null;

        if (context is null)
        {
            _logger.LogError("Graphics context is null during gizmo pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Vertex shader: procedural handle geometry + constant-screen-size scaling.
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsGizmo.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile gizmo vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Fragment shader: colour + entity-id output.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psGizmo.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile gizmo fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = shaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "Gizmo_Vertex"
        );
        using var fs = shaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "Gizmo_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = PipelineDebugName,
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.Triangle,
        };

        // Color 0: tone-mapped LDR colour target.
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);

        // Color 1: entity id (no blend — the raw id bits are written for picking).
        desc.Colors[1] = new ColorAttachment
        {
            Format = GraphicsSettings.MeshIdTexFormat,
            BlendEnabled = false,
        };

        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

        pipeline = context.CreateRenderPipeline(desc);

        if (!pipeline.Valid)
        {
            _logger.LogError("Gizmo solid-handle pipeline failed to create.");
            pipeline = RenderPipelineResource.Null;
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }
}
