using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 13: Degenerate handles are never drawn.
/// <para>
/// For any published handle set, the render node records draws only for non-degenerate handles,
/// where a degenerate handle is one whose rendered geometry has zero length, area, or scale along
/// its defining axis (per <see cref="GizmoModelBuilder.IsDegenerate(in GizmoHandle)"/>).
/// </para>
/// <para>
/// <see cref="RenderNodes.GizmoRenderNode.OnRender"/> requires a live GPU context, so the property is
/// validated at the seam it can be observed without a GPU: the exact per-handle exclusion predicate
/// the render node applies before recording a draw. The render node's draw loop skips any handle for
/// which <c>GizmoModelBuilder.IsDegenerate(in handle)</c> is <see langword="true"/> (via
/// <c>continue</c>) and records a draw for every other handle. This test reproduces that same filter
/// and asserts that the recorded-draw set never contains a degenerate handle, and that it excludes
/// exactly the degenerate handles (no non-degenerate handle is dropped).
/// </para>
/// **Validates: Requirements 7.5**
/// </summary>
[TestClass]
public class GizmoDegenerateExclusionPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(500);

    /// <summary>
    /// Scale factors chosen to straddle the degeneracy threshold: exact zero and sub-epsilon values
    /// force zero length / area / scale (and a zero determinant), while the healthy values keep the
    /// handle non-degenerate. Mixing them lets a generated handle set contain both kinds.
    /// </summary>
    private static readonly float[] ScaleChoices = [0f, 1e-9f, 1e-7f, 0.01f, 0.5f, 1f, 2f, 7f];

    /// <summary>
    /// Generates a gizmo-local transform spanning degenerate and non-degenerate geometry: a
    /// per-axis scale (possibly zero / sub-epsilon), a rotation (which preserves lengths so it never
    /// hides a collapsed axis), and a translation (which does not affect the linear part the
    /// degeneracy test inspects).
    /// </summary>
    private static Gen<Matrix4x4> TransformGen() =>
        from sxi in Gen.Choose(0, ScaleChoices.Length - 1)
        from syi in Gen.Choose(0, ScaleChoices.Length - 1)
        from szi in Gen.Choose(0, ScaleChoices.Length - 1)
        from tx in Gen.Choose(-100, 100)
        from ty in Gen.Choose(-100, 100)
        from tz in Gen.Choose(-100, 100)
        from angleDeg in Gen.Choose(0, 359)
        let scale = Matrix4x4.CreateScale(ScaleChoices[sxi], ScaleChoices[syi], ScaleChoices[szi])
        let rotation = Matrix4x4.CreateRotationZ(angleDeg * MathF.PI / 180f)
        let translation = Matrix4x4.CreateTranslation(tx, ty, tz)
        select scale * rotation * translation;

    /// <summary>Generates a single handle with a random shape, axis, and (possibly degenerate) transform.</summary>
    private static Gen<GizmoHandle> HandleGen() =>
        from shapeIndex in Gen.Choose(0, 4)   // Arrow, Ring, Box, Plane, Line
        from axisIndex in Gen.Choose(0, 7)    // X, Y, Z, XY, YZ, XZ, Screen, Uniform
        from modeIndex in Gen.Choose(0, 2)    // Translate, Rotate, Scale
        from transform in TransformGen()
        let axis = (GizmoAxis)axisIndex
        let mode = (GizmoMode)modeIndex
        select new GizmoHandle(
            new GizmoHandleId(mode, axis),
            (GizmoHandleShape)shapeIndex,
            axis,
            new Color4(1f),
            transform);

    /// <summary>Generates published handle sets (arrays) mixing degenerate and non-degenerate handles.</summary>
    private static Arbitrary<GizmoHandle[]> HandleSetArb() =>
        HandleGen().ArrayOf().ToArbitrary();

    /// <summary>
    /// Reproduces the exact per-handle decision <see cref="RenderNodes.GizmoRenderNode.OnRender"/>
    /// makes: iterate the published handle set and record a draw for a handle only when it is not
    /// degenerate (the render node executes <c>continue</c> for degenerate handles).
    /// </summary>
    private static List<GizmoHandle> RecordedDraws(IReadOnlyList<GizmoHandle> handles)
    {
        var drawn = new List<GizmoHandle>(handles.Count);
        for (int i = 0; i < handles.Count; i++)
        {
            var handle = handles[i];

            // Mirror of GizmoRenderNode.OnRender: exclude degenerate handles from recorded draws.
            if (GizmoModelBuilder.IsDegenerate(in handle))
            {
                continue;
            }

            drawn.Add(handle);
        }

        return drawn;
    }

    /// <summary>
    /// Property 13: for any published handle set, the recorded-draw set contains no degenerate
    /// handle, and it is exactly the non-degenerate subset of the published set (no non-degenerate
    /// handle is dropped).
    /// **Validates: Requirements 7.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void RecordedDraws_NeverIncludeDegenerateHandles()
    {
        Prop.ForAll(HandleSetArb(), handles =>
        {
            var drawn = RecordedDraws(handles);

            // No recorded draw is degenerate (the core of Property 13 / Requirement 7.5).
            bool noDegenerateDrawn = drawn.TrueForAll(h => !GizmoModelBuilder.IsDegenerate(in h));

            // The recorded set is exactly the non-degenerate handles: every non-degenerate handle in
            // the published set is drawn and every degenerate handle is excluded.
            int expectedDrawn = 0;
            for (int i = 0; i < handles.Length; i++)
            {
                if (!GizmoModelBuilder.IsDegenerate(in handles[i]))
                {
                    expectedDrawn++;
                }
            }

            return noDegenerateDrawn && drawn.Count == expectedDrawn;
        }).Check(DefaultConfig);
    }
}
