using System.Numerics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Example-based unit tests for <see cref="GizmoModelBuilder"/> handle generation.
///
/// Verifies per-mode handle counts and shapes, per-axis orientation within 0.001 rad,
/// distinct per-axis colors, non-degenerate local transforms, degenerate-handle exclusion,
/// and that generation only appends to the output list (no rendering side effects).
///
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5.
/// </summary>
[TestClass]
public class GizmoModelBuilderUnitTests
{
    // Requirements 3.1/3.2/3.3 orientation tolerance.
    private const float OrientationToleranceRad = 0.001f;

    // Requirement 3.4 degeneracy threshold.
    private const float DegeneracyEpsilon = 1e-6f;

    // The three supported axes, in generation order.
    private static readonly GizmoAxis[] Axes = [GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z];

    private static List<GizmoHandle> Build(GizmoMode mode)
    {
        var handles = new List<GizmoHandle>();
        switch (mode)
        {
            case GizmoMode.Translate:
                GizmoModelBuilder.BuildTranslate(handles);
                break;
            case GizmoMode.Rotate:
                GizmoModelBuilder.BuildRotate(handles);
                break;
            case GizmoMode.Scale:
                GizmoModelBuilder.BuildScale(handles);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return handles;
    }

    // The gizmo-local axis direction each handle should align with.
    private static Vector3 ExpectedAxisDir(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    // Distinct per-axis colors documented by the builder: X=red, Y=green, Z=blue.
    private static Color4 ExpectedColor(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => new Color4(1f, 0f, 0f, 1f),
        GizmoAxis.Y => new Color4(0f, 1f, 0f, 1f),
        GizmoAxis.Z => new Color4(0f, 0f, 1f, 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    private static float AngleBetween(Vector3 a, Vector3 b)
    {
        var na = Vector3.Normalize(a);
        var nb = Vector3.Normalize(b);
        float dot = Math.Clamp(Vector3.Dot(na, nb), -1f, 1f);
        return MathF.Acos(dot);
    }

    // --- Requirements 3.1, 3.2, 3.3: per-mode handle counts and shapes ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildTranslate_ProducesThreeArrowHandlesOnePerAxis()
    {
        var handles = Build(GizmoMode.Translate);

        Assert.AreEqual(3, handles.Count, "Translate must produce one handle per supported axis.");
        for (int i = 0; i < Axes.Length; i++)
        {
            Assert.AreEqual(GizmoHandleShape.Arrow, handles[i].Shape);
            Assert.AreEqual(Axes[i], handles[i].Axis);
            Assert.AreEqual(GizmoMode.Translate, handles[i].Id.Mode);
            Assert.AreEqual(Axes[i], handles[i].Id.Axis);
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildRotate_ProducesThreeRingHandlesOnePerAxis()
    {
        var handles = Build(GizmoMode.Rotate);

        Assert.AreEqual(3, handles.Count, "Rotate must produce one ring handle per supported axis.");
        for (int i = 0; i < Axes.Length; i++)
        {
            Assert.AreEqual(GizmoHandleShape.Ring, handles[i].Shape);
            Assert.AreEqual(Axes[i], handles[i].Axis);
            Assert.AreEqual(GizmoMode.Rotate, handles[i].Id.Mode);
            Assert.AreEqual(Axes[i], handles[i].Id.Axis);
        }
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildScale_ProducesThreeBoxHandlesOnePerAxis()
    {
        var handles = Build(GizmoMode.Scale);

        Assert.AreEqual(3, handles.Count, "Scale must produce one handle per supported axis.");
        for (int i = 0; i < Axes.Length; i++)
        {
            Assert.AreEqual(GizmoHandleShape.Box, handles[i].Shape);
            Assert.AreEqual(Axes[i], handles[i].Axis);
            Assert.AreEqual(GizmoMode.Scale, handles[i].Id.Mode);
            Assert.AreEqual(Axes[i], handles[i].Id.Axis);
        }
    }

    // --- Requirements 3.1, 3.3: directional handle orientation within 0.001 rad ---
    // Arrow/Box local +X maps to the axis direction.

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildTranslate_ArrowOrientation_AlignsWithAxis_WithinTolerance()
    {
        AssertDirectionalOrientation(Build(GizmoMode.Translate));
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildScale_BoxOrientation_AlignsWithAxis_WithinTolerance()
    {
        AssertDirectionalOrientation(Build(GizmoMode.Scale));
    }

    private static void AssertDirectionalOrientation(List<GizmoHandle> handles)
    {
        foreach (var h in handles)
        {
            // Canonical directional handle points along local +X; transform maps it to the axis.
            var dir = Vector3.TransformNormal(Vector3.UnitX, h.LocalTransform);
            float angle = AngleBetween(dir, ExpectedAxisDir(h.Axis));
            Assert.IsTrue(
                angle <= OrientationToleranceRad,
                $"Handle {h.Axis} orientation off by {angle} rad (tolerance {OrientationToleranceRad}).");
        }
    }

    // --- Requirement 3.2: ring plane normal aligns with the axis within 0.001 rad ---
    // Ring canonical plane normal is local +Z.

    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuildRotate_RingNormalOrientation_AlignsWithAxis_WithinTolerance()
    {
        foreach (var h in Build(GizmoMode.Rotate))
        {
            var normal = Vector3.TransformNormal(Vector3.UnitZ, h.LocalTransform);
            float angle = AngleBetween(normal, ExpectedAxisDir(h.Axis));
            Assert.IsTrue(
                angle <= OrientationToleranceRad,
                $"Ring {h.Axis} normal off by {angle} rad (tolerance {OrientationToleranceRad}).");
        }
    }

    // --- Requirements 3.1, 3.2, 3.3: distinct per-axis colors ---

    [DataTestMethod]
    [TestCategory("Gizmo")]
    [DataRow(GizmoMode.Translate)]
    [DataRow(GizmoMode.Rotate)]
    [DataRow(GizmoMode.Scale)]
    public void Build_AssignsExpectedDistinctColorsPerAxis(GizmoMode mode)
    {
        var handles = Build(mode);

        // Each handle carries the documented per-axis color.
        foreach (var h in handles)
        {
            Assert.AreEqual(ExpectedColor(h.Axis), h.Color, $"Unexpected color for axis {h.Axis}.");
        }

        // Colors are pairwise distinct across the set.
        var colors = handles.Select(h => h.Color).ToList();
        var distinct = new HashSet<Color4>(colors);
        Assert.AreEqual(colors.Count, distinct.Count, "Each axis must have a color distinct from every other axis.");
    }

    // --- Requirement 3.4: generated handles are non-degenerate ---

    [DataTestMethod]
    [TestCategory("Gizmo")]
    [DataRow(GizmoMode.Translate)]
    [DataRow(GizmoMode.Rotate)]
    [DataRow(GizmoMode.Scale)]
    public void Build_ProducesOnlyNonDegenerateHandles(GizmoMode mode)
    {
        foreach (var h in Build(mode))
        {
            float absDet = MathF.Abs(h.LocalTransform.GetDeterminant());
            Assert.IsTrue(absDet > DegeneracyEpsilon, $"Handle {h.Axis} has degenerate determinant {absDet}.");
            Assert.IsFalse(GizmoModelBuilder.IsDegenerate(h), $"Handle {h.Axis} unexpectedly reported degenerate.");
        }
    }

    // --- Requirement 3.4: degeneracy detection (basis for exclusion) ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void IsDegenerate_NonInvertibleTransform_ReturnsTrueForEveryShape()
    {
        // Zero matrix: |det| == 0 <= 1e-6 for all shapes.
        var zero = new Matrix4x4();
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Arrow, zero));
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Box, zero));
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Ring, zero));
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Line, zero));
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void IsDegenerate_ZeroLengthDirectional_ReturnsTrue()
    {
        // Collapse the +X extent to zero: arrow/line length becomes 0 (and determinant is 0).
        var collapsedX = Matrix4x4.CreateScale(0f, 1f, 1f);
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Arrow, collapsedX));
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Line, collapsedX));
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void IsDegenerate_ZeroRadiusRing_ReturnsTrue()
    {
        // A near-zero uniform scale drives the ring radius (and determinant) below the epsilon.
        var tiny = Matrix4x4.CreateScale(1e-9f);
        Assert.IsTrue(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Ring, tiny));
    }

    [TestMethod]
    [TestCategory("Gizmo")]
    public void IsDegenerate_HealthyTransform_ReturnsFalse()
    {
        Assert.IsFalse(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Arrow, Matrix4x4.Identity));
        Assert.IsFalse(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Ring, Matrix4x4.Identity));
        Assert.IsFalse(GizmoModelBuilder.IsDegenerate(GizmoHandleShape.Box, Matrix4x4.Identity));
    }

    // --- Requirement 3.5: generation only appends to the output list (no side effects) ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void Build_OnlyAppendsToOutputList_PreservingExistingEntries()
    {
        // A pre-existing sentinel entry the builder must not touch or remove.
        var sentinel = new GizmoHandle(
            new GizmoHandleId(GizmoMode.Rotate, GizmoAxis.Screen),
            GizmoHandleShape.Plane,
            GizmoAxis.Screen,
            new Color4(0.5f, 0.5f, 0.5f, 1f),
            Matrix4x4.Identity);

        var handles = new List<GizmoHandle> { sentinel };

        GizmoModelBuilder.BuildTranslate(handles);

        Assert.AreEqual(4, handles.Count, "Build must append exactly three handles, leaving the existing entry.");
        Assert.AreEqual(sentinel, handles[0], "Existing list entries must be preserved unchanged.");

        // A second build appends again (append-only, never clears the list).
        GizmoModelBuilder.BuildTranslate(handles);
        Assert.AreEqual(7, handles.Count, "Subsequent build must append without clearing prior contents.");
        Assert.AreEqual(sentinel, handles[0], "Existing entry must remain after repeated builds.");
    }
}
