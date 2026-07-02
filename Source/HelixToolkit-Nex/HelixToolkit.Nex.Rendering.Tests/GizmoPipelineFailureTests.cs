using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-rendering, Requirement 10: pipeline-failure resilience for the gizmo render node.
/// <para>
/// The gizmo render node delegates its pipeline-creation and record-guard policy to the static
/// <see cref="GizmoRenderNodeResilience"/> helper. Constructing a genuine GPU pipeline-creation
/// failure in a headless test is impractical, so these tests exercise the resilience contract that
/// governs the node's behavior instead: a null graphics context makes
/// <see cref="GizmoSolidHandlePipeline.Create"/> short-circuit (simulating creation failure), and
/// the node's <c>OnSetup</c>/<c>OnRender</c> guards are thin wrappers over the helper's
/// <see cref="GizmoRenderNodeResilience.TrySetup"/> and
/// <see cref="GizmoRenderNodeResilience.CanRecord"/> methods verified here.
/// </para>
/// **Validates: Requirements 10.4, 10.5**
/// </summary>
[TestClass]
public class GizmoPipelineFailureTests
{
    /// <summary>
    /// Requirement 10.4: when pipeline creation fails during setup, <c>TrySetup</c> returns
    /// <see langword="false"/> (so the base <c>RenderNode.Setup</c> leaves the node detached) and
    /// emits <see cref="RenderPipelineResource.Null"/> — never a partially-created handle — without
    /// throwing. A null graphics context deterministically drives the creation-failure path.
    /// **Validates: Requirements 10.4**
    /// </summary>
    [TestMethod]
    public void TrySetup_WhenPipelineCreationFails_ReturnsFalseAndOutputsNullPipeline()
    {
        // A null context makes GizmoSolidHandlePipeline.Create short-circuit before touching the
        // shader repository, so a null repository is safe here and models a headless setup failure.
        bool attached = GizmoRenderNodeResilience.TrySetup(
            context: null,
            shaderRepository: null!,
            out RenderPipelineResource pipeline
        );

        Assert.IsFalse(
            attached,
            "TrySetup must return false on pipeline-creation failure so the node stays detached (Req 10.4)."
        );
        Assert.AreSame(
            RenderPipelineResource.Null,
            pipeline,
            "On failure the node must retain the shared Null sentinel, not a partially-created pipeline (Req 10.4)."
        );
        Assert.IsFalse(pipeline.Valid, "The output pipeline must be invalid on failure.");
    }

    /// <summary>
    /// Requirement 10.4/10.5: the record guard early-returns for an invalid pipeline. Because the
    /// node's <c>OnRender</c> begins with <c>if (!CanRecord(_solidPipeline)) return;</c>, a
    /// <see langword="false"/> result here proves the node records no draw calls and binds/clears no
    /// attachment — leaving the color, entity-id, and depth targets unmodified when setup failed.
    /// **Validates: Requirements 10.4, 10.5**
    /// </summary>
    [TestMethod]
    public void CanRecord_WithNullPipeline_ReturnsFalseSoNoDrawCallsAreRecorded()
    {
        bool canRecord = GizmoRenderNodeResilience.CanRecord(RenderPipelineResource.Null);

        Assert.IsFalse(
            canRecord,
            "CanRecord must be false for an invalid pipeline so OnRender early-returns, recording no "
                + "draw calls and leaving all three shared targets unmodified (Req 10.4, 10.5)."
        );
    }

    /// <summary>
    /// Requirement 10.5: a pipeline-creation failure during setup records a warning (rather than
    /// throwing) indicating pipeline creation failure. Warnings are captured by the module-installed
    /// <see cref="GizmoLoggerCapture"/> factory; a monotonic count check is concurrency-safe because
    /// warnings are only ever appended.
    /// **Validates: Requirements 10.5**
    /// </summary>
    [TestMethod]
    public void TrySetup_WhenPipelineCreationFails_RecordsWarningWithoutThrowing()
    {
        int before = GizmoLoggerCapture.Factory.WarningCount;

        bool attached = GizmoRenderNodeResilience.TrySetup(
            context: null,
            shaderRepository: null!,
            out RenderPipelineResource pipeline
        );

        Assert.IsFalse(attached, "Setup must fail for a null context.");
        Assert.IsFalse(pipeline.Valid, "The output pipeline must be invalid on failure.");
        Assert.IsTrue(
            GizmoLoggerCapture.Factory.WarningCount > before,
            "A pipeline-creation failure during setup must record a warning (Req 10.5)."
        );
        Assert.IsTrue(
            GizmoLoggerCapture
                .Factory.Snapshot()
                .Any(w => w.Contains("pipeline", StringComparison.OrdinalIgnoreCase)),
            "Expected a warning indicating gizmo pipeline creation failure (Req 10.5)."
        );
    }
}
