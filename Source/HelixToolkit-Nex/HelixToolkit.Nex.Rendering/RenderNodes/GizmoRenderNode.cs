using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Records the draw calls that render every gathered gizmo's handles into the overlay stage.
/// </summary>
/// <remarks>
/// <para>
/// The node sits in <see cref="RenderStage.Overlay"/>, guaranteed to run after tone-mapping, so it
/// draws onto the tone-mapped LDR color target while the opaque scene depth buffer is still bound
/// read-only. It writes each handle's color to <see cref="SystemBufferNames.TextureColorF16Target"/>
/// and each handle's packed pick words to <see cref="SystemBufferNames.TextureEntityId"/> so the
/// existing picking readback resolves gizmo handle picks with no new GPU code, and it never writes
/// scene depth.
/// </para>
/// </remarks>
public sealed class GizmoRenderNode : RenderNode
{
    /// <summary>The pipeline used to draw solid gizmo handles procedurally (push-constant driven).</summary>
    private RenderPipelineResource _solidPipeline = RenderPipelineResource.Null;

    /// <inheritdoc />
    public override string Name => nameof(GizmoRenderNode);

    /// <inheritdoc />
    public override Color4 DebugColor => Color.Gold;

    /// <summary>
    /// Gets or sets the occlusion mode that toggles X-ray (always-on-top) versus depth-respecting
    /// handles at runtime. Defaults to <see cref="GizmoOcclusionMode.AlwaysOnTop"/> so manipulator
    /// handles remain grabbable even when behind geometry (Requirement 8.4).
    /// </summary>
    public GizmoOcclusionMode OcclusionMode { get; set; } = GizmoOcclusionMode.AlwaysOnTop;

    /// <summary>
    /// Pure mapping from an occlusion mode to the depth state used for the gizmo pass, under the
    /// reversed-Z convention: <see cref="GizmoOcclusionMode.AlwaysOnTop"/> selects
    /// <see cref="DepthState.Disabled"/> (X-ray, no depth test, Req 8.1/8.4) and
    /// <see cref="GizmoOcclusionMode.DepthTested"/> selects <see cref="DepthState.ReadOnlyInvZ"/>
    /// (read-only reversed-Z test, Req 8.2). Both returned states are read-only
    /// (<see cref="DepthState.IsDepthWriteEnabled"/> is <see langword="false"/>), so gizmos never
    /// write scene depth (Req 8.3). Extracted so the mapping can be verified directly without a GPU
    /// context; <see cref="OnSetupRender"/> and <see cref="OnRender"/> both call it so runtime mode
    /// changes take effect on the next rendered frame (Req 8.5).
    /// </summary>
    /// <param name="mode">The occlusion mode to map.</param>
    /// <returns>The depth state to bind for the gizmo pass.</returns>
    internal static DepthState SelectDepthState(GizmoOcclusionMode mode) =>
        mode == GizmoOcclusionMode.AlwaysOnTop ? DepthState.Disabled : DepthState.ReadOnlyInvZ;

    /// <summary>
    /// Creates the gizmo solid-handle pipeline for the node. Returns the resilience helper's result
    /// directly so that, on pipeline-creation failure, the node stays detached and records nothing
    /// (Requirements 10.4, 10.5). On success the pipeline is stored in <see cref="_solidPipeline"/>.
    /// </summary>
    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && Renderer is not null);
        return GizmoRenderNodeResilience.TrySetup(
            Context,
            Renderer!.ShaderRepository,
            out _solidPipeline
        );
    }

    /// <inheritdoc />
    protected override void OnTeardown()
    {
        _solidPipeline.Dispose();
        _solidPipeline = RenderPipelineResource.Null;
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        return res.RenderContext.Data?.GizmoData.Gizmos.Count > 0;
    }

    /// <summary>
    /// Binds the overlay attachments: tone-mapped color (Load/Store) and entity id (Load/Store) are
    /// preserved, and scene depth is loaded read-only with a store op that writes no depth so the
    /// scene depth buffer is left bit-identical (Requirements 2.4, 8.3).
    /// </summary>
    protected override void OnSetupRender(in RenderResources res)
    {
        // Scene depth: read-only. StoreOp.None guarantees no depth values are ever written, so the
        // opaque-pass depth buffer is preserved for later passes in both occlusion modes.
        res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
        res.Pass.Depth.LoadOp = LoadOp.Load;
        res.Pass.Depth.StoreOp = StoreOp.None;

        // Color 0: tone-mapped LDR color target — preserved (never cleared).
        res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.TextureColorF16Target];
        res.Pass.Colors[0].LoadOp = LoadOp.Load;
        res.Pass.Colors[0].StoreOp = StoreOp.Store;

        // Color 1: entity id target — preserved so uncovered pixels keep their existing ids.
        res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
        res.Pass.Colors[1].LoadOp = LoadOp.Load;
        res.Pass.Colors[1].StoreOp = StoreOp.Store;

        res.Deps.PushTexture(res.Textures[SystemBufferNames.TextureDepthF32]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferForwardPlusConstants]);

        // Select the depth state from the current OcclusionMode under the reversed-Z convention:
        // AlwaysOnTop disables the depth test so handles draw over everything (X-ray, Req 8.1/8.4),
        // while DepthTested uses a read-only reversed-Z test so handles respect scene depth (Req 8.2).
        // Because OnSetupRender runs every frame and reads the live OcclusionMode property, a runtime
        // mode change is naturally reflected on the next rendered frame (Req 8.5). Neither state writes
        // depth (StoreOp.None above), so the scene depth buffer is preserved in both modes (Req 8.3).
        res.Pass.DepthState = SelectDepthState(OcclusionMode);
    }

    /// <summary>
    /// Records the draw calls for every gathered gizmo: for each gizmo, one procedural push-constant
    /// draw per non-degenerate handle, writing color to the tone-mapped target and the packed pick
    /// words (owning entity id in R, <see cref="GizmoHandleId"/> in G) to the entity-id target at
    /// covered pixels, leaving uncovered pixels unchanged (Requirements 2.3, 2.4, 2.5, 4.1, 7.1, 7.2).
    /// Every handle — arrow / box / plane as well as ring / line — is rasterized through the single
    /// procedural solid pipeline: the vertex shader (<c>vsGizmo.glsl</c>) generates ring and line
    /// geometry for the <c>SHAPE_RING</c>/<c>SHAPE_LINE</c> ids, so every handle writes its pick and
    /// picking works uniformly. Guards against an invalid pipeline (Requirements 10.4, 10.5) before
    /// recording anything.
    /// </summary>
    protected override void OnRender(in RenderResources res)
    {
        // Resilience guard: if the pipeline is invalid, record no draw calls and leave the color,
        // entity-id, and depth targets unmodified so every other node continues for the frame.
        if (!GizmoRenderNodeResilience.CanRecord(_solidPipeline))
        {
            return;
        }

        // CanRender already ran the per-frame gather and guaranteed gathered gizmos, but guard
        // defensively so a provider cleared between CanRender and OnRender records nothing rather
        // than throwing.
        var gizmoData = res.RenderContext.Data!.GizmoData;

        var ctx = res.RenderContext;
        var fpAddr = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(ctx.Context);

        // The pipeline and depth state are bound once for every handle. The depth state matches
        // OnSetupRender's OcclusionMode selection so the draws and the render pass agree under the
        // reversed-Z convention. Only covered pixels are written (color -> attachment 0, pick words
        // -> attachment 1); the shader never writes depth, so pixels not covered by any handle keep
        // their existing color and pick words (Requirements 2.4, 4.1).
        res.CmdBuffer.BindRenderPipeline(_solidPipeline);
        res.CmdBuffer.BindDepthState(SelectDepthState(OcclusionMode));

        var camera = ctx.CameraParams;
        float viewportHeight = ctx.WindowSize.Height;

        // One draw per non-degenerate handle across every gathered gizmo. Each gizmo carries its own
        // owning entity id (Req 7.2) and origin, so picks over overlapping gizmos resolve to the
        // correct gizmo and handle.
        foreach (var gathered in gizmoData.Gizmos.AsValueEnumerable())
        {
            var info = gathered.Info;

            // World placement base: the gizmo origin. Each handle's gizmo-local transform is applied
            // relative to it.
            var originTransform = Matrix4x4.CreateTranslation(info.Origin);

            // Constant screen-size factor for this gizmo, evaluated at its origin so all of its
            // handles share the same scale, matching the previous single-gizmo behavior (Req 10.1).
            var screenScale = new GizmoScreenScale(info.DesiredPixelSize);

            var handles = info.Handles;
            for (int i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];

                // Exclude degenerate handles from the recorded draws (Requirement 2.5).
                if (GizmoModelBuilder.IsDegenerate(in handle))
                {
                    continue;
                }

                var model = handle.LocalTransform * originTransform;

                // Pack the pick for THIS gizmo + handle so a covered pixel decodes back to the
                // owning entity and handle id (Requirements 4.1, 7.2).
                GizmoPickEncoding.Pack(
                    gathered.OwningEntityId,
                    handle.Id,
                    out uint encodedR,
                    out uint encodedG
                );

                float scale = screenScale.ScreenScaleAt(model.Translation, camera, viewportHeight);

                DrawHandle(
                    in res,
                    fpAddr,
                    info.OcclusionMode,
                    in handle,
                    in model,
                    scale,
                    encodedR,
                    encodedG
                );
                ctx.Statistics.DrawCalls++;
            }
        }
    }

    /// <summary>
    /// Records a single procedural push-constant draw for one handle, populating the
    /// <see cref="GizmoPushConstant"/> (including the constant-screen-size factor, the pre-packed
    /// pick words, and the encoded shape/occlusion flags) and issuing a <see cref="ICommandBuffer.Draw"/>
    /// with the vertex count for the handle's shape. The pipeline and depth state must already be
    /// bound by the caller.
    /// </summary>
    private static void DrawHandle(
        in RenderResources res,
        ulong fpAddr,
        GizmoOcclusionMode occlusion,
        in GizmoHandle handle,
        in Matrix4x4 model,
        float screenScale,
        uint encodedR,
        uint encodedG
    )
    {
        res.CmdBuffer.PushConstants(
            new GizmoPushConstant
            {
                FpConstAddress = fpAddr,
                ModelTransform = model,
                Color = handle.Color,
                EncodedR = encodedR,
                EncodedG = encodedG,
                ScreenScale = screenScale,
                ShapeFlags = EncodeShape(handle.Shape, occlusion),
            }
        );

        res.CmdBuffer.Draw(VertexCountFor(handle.Shape), 1);
    }

    /// <summary>
    /// Returns the number of procedural vertices the vertex shader (<c>vsGizmo.glsl</c>) emits for
    /// the given <paramref name="shape"/>: arrow (translate) handles use
    /// <see cref="GizmoSolidHandlePipeline.ArrowVertexCount"/> (shaft + cone head), box (scale)
    /// handles use <see cref="GizmoSolidHandlePipeline.ScaleVertexCount"/> (shaft + end cube),
    /// planar handles use <see cref="GizmoSolidHandlePipeline.PlaneVertexCount"/> (6), ring handles
    /// use <see cref="GizmoSolidHandlePipeline.RingVertexCount"/> (annulus band), line handles use
    /// <see cref="GizmoSolidHandlePipeline.LineVertexCount"/> (thin quad), and any unrecognized
    /// shape falls back to the unit cube (<see cref="GizmoSolidHandlePipeline.BoxVertexCount"/>, 36).
    /// </summary>
    private static uint VertexCountFor(GizmoHandleShape shape) =>
        shape switch
        {
            GizmoHandleShape.Plane => GizmoSolidHandlePipeline.PlaneVertexCount,
            GizmoHandleShape.Ring => GizmoSolidHandlePipeline.RingVertexCount,
            GizmoHandleShape.Line => GizmoSolidHandlePipeline.LineVertexCount,
            GizmoHandleShape.Arrow => GizmoSolidHandlePipeline.ArrowVertexCount,
            GizmoHandleShape.Box => GizmoSolidHandlePipeline.ScaleVertexCount,
            _ => GizmoSolidHandlePipeline.BoxVertexCount,
        };

    /// <summary>
    /// Packs a handle's shape id into the low 8 bits and the occlusion mode into the higher bits,
    /// producing the <see cref="GizmoPushConstant.ShapeFlags"/> value consumed by <c>vsGizmo.glsl</c>
    /// (which reads <c>shapeFlags &amp; 0xFF</c> for the shape). The shape ids match the shader's
    /// <c>SHAPE_*</c> constants: Arrow=0, Ring=1, Box=2, Plane=3, Line=4.
    /// </summary>
    /// <param name="shape">The handle shape to encode.</param>
    /// <param name="occlusion">The occlusion mode to encode into bit 8.</param>
    /// <returns>The packed shape/occlusion flags.</returns>
    private static uint EncodeShape(GizmoHandleShape shape, GizmoOcclusionMode occlusion)
    {
        // Low 8 bits: shape id. Mapped explicitly so a future enum reordering cannot silently
        // desynchronize from the shader's SHAPE_* constants.
        uint shapeId = shape switch
        {
            GizmoHandleShape.Arrow => 0u,
            GizmoHandleShape.Ring => 1u,
            GizmoHandleShape.Box => 2u,
            GizmoHandleShape.Plane => 3u,
            GizmoHandleShape.Line => 4u,
            _ => 0u,
        };

        // Bit 8: occlusion mode (0 = always-on-top / X-ray, 1 = depth-tested). Reserved for the
        // shader; the depth test itself is applied via BindDepthState from OnSetupRender.
        uint occlusionBit = occlusion == GizmoOcclusionMode.DepthTested ? 1u << 8 : 0u;

        return (shapeId & 0xFFu) | occlusionBit;
    }

    /// <summary>
    /// Registers the node into <see cref="RenderStage.Overlay"/> with scene depth and the
    /// forward-plus constants as inputs and the tone-mapped color and entity-id targets as outputs
    /// (Requirement 7.1).
    /// </summary>
    /// <remarks>
    /// Placement in <see cref="RenderStage.Overlay"/> alone guarantees execution after the
    /// <see cref="RenderStage.ToneMap"/> stage (the graph compiler orders all passes of an earlier
    /// stage before any pass of a later stage), so no explicit <c>after</c> list is required.
    /// </remarks>
    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Overlay,
            nameof(GizmoRenderNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs:
            [
                new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
            ]
        );
    }
}
