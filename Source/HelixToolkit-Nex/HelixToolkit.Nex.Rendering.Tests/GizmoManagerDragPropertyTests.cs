using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-rendering, Property 6: Drag consistency.
/// <para>
/// For a translate drag constrained to an axis <c>a</c>, every <see cref="Matrix4x4"/> that
/// <see cref="GizmoManager.UpdateDrag"/> produces is a <em>pure translation</em> parallel to
/// <c>a</c>: its linear (3x3) part is the identity (no rotation or scale) and its translation
/// component is parallel to the constrained world axis.
/// </para>
/// **Validates: Requirements 7.2, 7.3**
/// </summary>
[TestClass]
public class GizmoManagerDragPropertyTests
{
    /// <summary>Tolerance for the linear-part identity comparison (no rotation / no scale).</summary>
    private const float LinearTolerance = 1e-4f;

    /// <summary>
    /// Cross-product magnitude below which the translation is treated as parallel to the axis
    /// (Requirement 7.2 uses a 1e-5 cross-product tolerance; the delta is exact by construction so
    /// this bound is generous relative to floating-point noise scaled by the translation length).
    /// </summary>
    private const float ParallelTolerance = 1e-5f;

    /// <summary>
    /// The maximum <c>|dot(axis, rayDir)|</c> permitted so the pointer ray is guaranteed to be
    /// non-parallel to the constrained axis (otherwise the closest-point solve is degenerate and
    /// <see cref="GizmoManager.UpdateDrag"/> legitimately reports no update).
    /// </summary>
    private const float MaxAxisRayAlignment = 0.95f;

    /// <summary>
    /// Random scenario: a constrained axis in {X, Y, Z}, a gizmo origin, and two pointer rays (one
    /// for <see cref="GizmoManager.BeginDrag"/> and one for <see cref="GizmoManager.UpdateDrag"/>),
    /// each guaranteed to be non-parallel to the constrained axis.
    /// </summary>
    private static Arbitrary<(int AxisIndex, Vector3 Origin, Ray BeginRay, Ray UpdateRay)> ScenarioArb() =>
        Arb.From(
            from axisIndex in Gen.Choose(0, 2)
            from ox in Gen.Choose(-500, 500)
            from oy in Gen.Choose(-500, 500)
            from oz in Gen.Choose(-500, 500)
            from beginRay in RayGen(axisIndex)
            from updateRay in RayGen(axisIndex)
            select (axisIndex, new Vector3(ox, oy, oz), beginRay, updateRay));

    /// <summary>
    /// Generates a pointer ray whose direction is non-parallel to the axis selected by
    /// <paramref name="axisIndex"/>, by rejecting directions too closely aligned with the axis.
    /// </summary>
    private static Gen<Ray> RayGen(int axisIndex) =>
        from px in Gen.Choose(-500, 500)
        from py in Gen.Choose(-500, 500)
        from pz in Gen.Choose(-500, 500)
        from dx in Gen.Choose(-1000, 1000)
        from dy in Gen.Choose(-1000, 1000)
        from dz in Gen.Choose(-1000, 1000)
        let rawDir = new Vector3(dx, dy, dz)
        // Fall back to a direction well off every principal axis when the sample is (near) zero.
        let dir = rawDir.LengthSquared() < 1f ? new Vector3(0.3f, 0.5f, 0.8f) : Vector3.Normalize(rawDir)
        where MathF.Abs(Vector3.Dot(dir, UnitAxis(axisIndex))) <= MaxAxisRayAlignment
        select new Ray(new Vector3(px, py, pz), dir);

    private static Vector3 UnitAxis(int axisIndex) => axisIndex switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    private static GizmoAxis GizmoAxisFor(int axisIndex) => axisIndex switch
    {
        0 => GizmoAxis.X,
        1 => GizmoAxis.Y,
        _ => GizmoAxis.Z,
    };

    /// <summary>
    /// Property 6: Drag consistency.
    /// For random constrained axes and non-parallel pointer rays, when
    /// <see cref="GizmoManager.UpdateDrag"/> produces a delta it is a pure translation
    /// (identity linear part) parallel to the constrained world axis.
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void UpdateDrag_ConstrainedToAxis_ProducesPureTranslationParallelToAxis()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            var (axisIndex, origin, beginRay, updateRay) = scenario;

            // Space=World so the constrained world axis equals the canonical unit axis.
            var manager = new GizmoManager { Mode = GizmoMode.Translate, Space = GizmoSpace.World };
            manager.Update(CameraParams.Identity, new Size(1920, 1080), Matrix4x4.CreateTranslation(origin));

            var handle = new GizmoHandleId(GizmoMode.Translate, GizmoAxisFor(axisIndex));

            // The rays are non-parallel to the axis, so the drag must start.
            if (!manager.BeginDrag(handle, beginRay))
            {
                return false;
            }

            bool updated = manager.UpdateDrag(updateRay, out Matrix4x4 delta);

            // Requirement 7.5: if no valid translation was determinable the delta is identity; that is
            // consistent (a pure, axis-parallel zero translation) so the property still holds.
            if (!updated)
            {
                return IsIdentity(delta);
            }

            return IsPureTranslation(delta)
                && IsTranslationParallelToAxis(delta, UnitAxis(axisIndex));
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>Verifies the linear (3x3) part is the identity and the homogeneous row is canonical.</summary>
    private static bool IsPureTranslation(in Matrix4x4 m)
        => Approx(m.M11, 1f) && Approx(m.M22, 1f) && Approx(m.M33, 1f) && Approx(m.M44, 1f)
        && Approx(m.M12, 0f) && Approx(m.M13, 0f) && Approx(m.M14, 0f)
        && Approx(m.M21, 0f) && Approx(m.M23, 0f) && Approx(m.M24, 0f)
        && Approx(m.M31, 0f) && Approx(m.M32, 0f) && Approx(m.M34, 0f);

    private static bool IsIdentity(in Matrix4x4 m)
        => IsPureTranslation(m) && Approx(m.M41, 0f) && Approx(m.M42, 0f) && Approx(m.M43, 0f);

    /// <summary>
    /// Verifies the translation component is parallel to <paramref name="axis"/> by requiring the
    /// cross-product magnitude to be negligible relative to the translation length.
    /// </summary>
    private static bool IsTranslationParallelToAxis(in Matrix4x4 m, Vector3 axis)
    {
        Vector3 translation = m.Translation;
        Vector3 cross = Vector3.Cross(translation, axis);
        // Scale the absolute tolerance by the translation magnitude so large drags stay in bounds.
        float bound = ParallelTolerance * MathF.Max(1f, translation.Length());
        return cross.Length() <= bound;
    }

    private static bool Approx(float a, float b) => MathF.Abs(a - b) <= LinearTolerance;
}
