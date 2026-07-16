namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Pipeline-failure resilience helpers for the gizmo render node.
/// <para>
/// The gizmo render node draws into shared overlay targets (the tone-mapped color target,
/// the entity-id target, and the read-only scene depth buffer). If the gizmo shader pipeline
/// cannot be created during setup, the node must degrade gracefully: it must stay
/// <em>detached</em> so the render graph skips it entirely, and — as a defensive second
/// line — it must refuse to record any draw calls, leaving all three shared targets
/// bit-identical so every other render node continues executing for the frame.
/// </para>
/// <para>
/// This helper centralizes that policy so the (task&#160;10.x) <c>GizmoRenderNode</c> stays a
/// thin consumer and the resilience behavior is unit-testable on its own. It is intentionally
/// decoupled from the node type, which does not exist yet.
/// </para>
/// <para>
/// <b>Integration contract for the gizmo render node (task&#160;10.1 / 10.3):</b>
/// <list type="number">
///   <item>
///     <description>
///     In <c>OnSetup</c>, call <see cref="TrySetup"/> and <b>return its result directly</b>.
///     When it returns <see langword="false"/> the base <c>RenderNode.Setup</c> leaves
///     <c>IsAttached == false</c>, so <c>RenderNode.Render</c> early-returns before
///     <c>OnSetupRender</c>/<c>OnRender</c> and no target is ever bound (Requirement&#160;10.4).
///     </description>
///   </item>
///   <item>
///     <description>
///     At the very start of <c>OnRender</c> (and, if the node overrides it, the start of
///     <c>OnSetupRender</c>), call <see cref="CanRecord"/> with the stored pipeline and
///     early-return when it is <see langword="false"/>. This guards against a pipeline that
///     became invalid after setup and guarantees no draw calls touch the color, entity-id,
///     or depth targets (Requirements&#160;10.4, 10.5).
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public static class GizmoRenderNodeResilience
{
    private static readonly ILogger _logger = LogManager.Create(nameof(GizmoRenderNodeResilience));

    /// <summary>
    /// Attempts to create the gizmo solid-handle pipeline for a render node's <c>OnSetup</c>.
    /// <para>
    /// On success, <paramref name="pipeline"/> receives the created pipeline and this returns
    /// <see langword="true"/> so the node attaches. On <em>any</em> failure, this logs a warning
    /// indicating pipeline creation failure (Requirement&#160;10.5), leaves
    /// <paramref name="pipeline"/> set to <see cref="RenderPipelineResource.Null"/>, and returns
    /// <see langword="false"/> so the node stays detached and records nothing
    /// (Requirement&#160;10.4).
    /// </para>
    /// </summary>
    /// <param name="context">The graphics context used to create the pipeline. May be <see langword="null"/>.</param>
    /// <param name="shaderRepository">The shader repository used to cache/create the shader modules.</param>
    /// <param name="pipeline">
    /// On success, receives the created pipeline; on failure, receives
    /// <see cref="RenderPipelineResource.Null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pipeline was created and is valid; otherwise
    /// <see langword="false"/>. The return value is intended to be used directly as the result
    /// of the node's <c>OnSetup</c>.
    /// </returns>
    public static bool TrySetup(
        IContext? context,
        IShaderRepository shaderRepository,
        out RenderPipelineResource pipeline
    )
    {
        var result = GizmoSolidHandlePipeline.Create(context, shaderRepository, out pipeline);

        if (result != ResultCode.Ok || !pipeline.Valid)
        {
            // Requirement 10.5: log a warning indicating pipeline creation failure.
            _logger.LogWarning(
                "Gizmo shader pipeline creation failed ({RESULT}); the gizmo render node will "
                    + "stay detached and record no draw calls. The color, entity-id, and depth "
                    + "targets are left unmodified so other render nodes continue for the frame.",
                result
            );

            // Requirement 10.4: ensure no partially-created pipeline is retained. Only dispose
            // a genuinely valid handle; on the normal failure paths Create returns the shared
            // RenderPipelineResource.Null sentinel, which must never be disposed.
            if (pipeline.Valid)
            {
                pipeline.Dispose();
            }
            pipeline = RenderPipelineResource.Null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Guard for the start of the gizmo render node's recording (<c>OnRender</c> /
    /// <c>OnSetupRender</c>). Returns <see langword="true"/> only when the supplied pipeline is
    /// valid and safe to bind.
    /// <para>
    /// When this returns <see langword="false"/> the caller MUST early-return immediately,
    /// before binding or clearing any attachment, so that the tone-mapped color target, the
    /// entity-id target, and the scene depth buffer are all left unmodified
    /// (Requirements&#160;10.4, 10.5).
    /// </para>
    /// </summary>
    /// <param name="pipeline">The pipeline the node intends to bind for this frame.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="pipeline"/> is valid and recording may
    /// proceed; otherwise <see langword="false"/>.
    /// </returns>
    public static bool CanRecord(in RenderPipelineResource pipeline)
    {
        return pipeline.Valid;
    }
}
