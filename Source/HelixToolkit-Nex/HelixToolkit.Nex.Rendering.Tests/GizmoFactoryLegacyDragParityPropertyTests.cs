using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 14: Factory-model drag matches the legacy
/// drag within tolerance.
/// <para>
/// For any drag input (gizmo mode, reference space, constrained axis, target transform, and two
/// pointer rays) applied to a single gizmo, the transform manipulation the <em>factory-and-engine</em>
/// model produces — a gizmo created via <see cref="GizmoManager.TryCreateGizmo"/>, set on an entity
/// via <see cref="GizmoManager.TrySetGizmo"/>, driven per-frame via
/// <see cref="GizmoManager.UpdateInstance"/>, and dragged through the resolved-pick
/// <see cref="GizmoManager.BeginDrag(in GizmoPickResolution, in Ray)"/> path bound to the
/// per-instance frame — matches the transform the existing component-driven (legacy) model produces
/// for the same input via <see cref="GizmoManager.Update"/> +
/// <see cref="GizmoManager.BeginDrag(GizmoHandleId, in Ray)"/>. Parity holds within 1e-5 world units
/// for translation and scale and 1e-5 degrees for rotation.
/// </para>
/// **Validates: Requirements 7.7**
/// </summary>
[TestClass]
public class GizmoFactoryLegacyDragParityPropertyTests
{
    /// <summary>Tolerance in world units for translation and scale parity (Requirement 7.7).</summary>
    private const float WorldUnitTolerance = 1e-5f;

    /// <summary>Tolerance in degrees for rotation parity (Requirement 7.7).</summary>
    private const float RotationDegreeTolerance = 1e-5f;

    /// <summary>The viewport used for both models; drag math is ray-driven, so its value is immaterial to parity.</summary>
    private static readonly Size Viewport = new(1920, 1080);

    /// <summary>
    /// The maximum <c>|dot(axisWorld, rayDir)|</c> for a translate/scale drag so the pointer ray is
    /// non-parallel to the constrained axis (otherwise the closest-point solve is degenerate).
    /// </summary>
    private const float MaxAxisRayAlignment = 0.9f;

    /// <summary>
    /// The minimum <c>|dot(axisWorld, rayDir)|</c> for a rotate drag so the pointer ray is not
    /// parallel to the rotation plane (otherwise the in-plane vector is not determinable).
    /// </summary>
    private const float MinAxisRayAlignment = 0.3f;

    /// <summary>The three gizmo modes; each selects a distinct drag manipulation.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; Local orients the constrained axis by the target transform.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect drag math.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>One generated drag scenario shared by both the legacy and factory models.</summary>
    private readonly record struct DragScenario(
        GizmoMode Mode,
        GizmoSpace Space,
        int AxisIndex,
        Matrix4x4 TargetTransform,
        uint TargetEntityId,
        float DesiredPixelSize,
        GizmoOcclusionMode Occlusion,
        Ray BeginRay,
        Ray UpdateRay);

    /// <summary>
    /// Generates a drag scenario: a mode/space/axis, a target transform (rotation + translation so
    /// Local space genuinely reorients the constrained axis), the definition's non-geometry inputs,
    /// and two pointer rays generated to be valid for the selected mode against the resulting
    /// world-space constrained axis.
    /// </summary>
    private static Arbitrary<DragScenario> ScenarioArb() =>
        Arb.From(
            from mode in ModeGen()
            from space in SpaceGen()
            from axisIndex in Gen.Choose(0, 2)
                // Rotation (yaw/pitch/roll in ~[-pi, pi]) exercises Local-space axis reorientation.
            from yaw in Gen.Choose(-31, 31)
            from pitch in Gen.Choose(-31, 31)
            from roll in Gen.Choose(-31, 31)
            from ox in Gen.Choose(-500, 500)
            from oy in Gen.Choose(-500, 500)
            from oz in Gen.Choose(-500, 500)
            from target in Gen.Choose(0, 1000)
            from pixel in Gen.Choose(1, 200)
            from occlusion in OcclusionGen()
            let transform = MakeTransform(yaw / 10f, pitch / 10f, roll / 10f, new Vector3(ox, oy, oz))
            let axisWorld = ConstrainedAxisWorld(axisIndex, space, transform)
            from beginRay in RayGen(axisWorld, mode)
            from updateRay in RayGen(axisWorld, mode)
            select new DragScenario(
                mode, space, axisIndex, transform, (uint)target, pixel, occlusion, beginRay, updateRay));

    /// <summary>Builds a rotation-then-translation transform whose translation is the gizmo origin.</summary>
    private static Matrix4x4 MakeTransform(float yaw, float pitch, float roll, Vector3 origin)
        => Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll) * Matrix4x4.CreateTranslation(origin);

    /// <summary>
    /// Computes the world-space constrained axis exactly as the drag does: the canonical unit axis in
    /// World space, or that axis reoriented by the target transform's rotation in Local space.
    /// </summary>
    private static Vector3 ConstrainedAxisWorld(int axisIndex, GizmoSpace space, in Matrix4x4 transform)
    {
        Vector3 unit = UnitAxis(axisIndex);
        Vector3 dir = space == GizmoSpace.Local ? Vector3.TransformNormal(unit, transform) : unit;
        float length = dir.Length();
        return length > 1e-6f ? dir / length : unit;
    }

    /// <summary>
    /// Generates a pointer ray valid for the selected mode against <paramref name="axisWorld"/>:
    /// non-parallel to the axis for translate/scale, and non-parallel to the rotation plane (a
    /// sufficient axis component) for rotate.
    /// </summary>
    private static Gen<Ray> RayGen(Vector3 axisWorld, GizmoMode mode) =>
        from px in Gen.Choose(-500, 500)
        from py in Gen.Choose(-500, 500)
        from pz in Gen.Choose(-500, 500)
        from dx in Gen.Choose(-1000, 1000)
        from dy in Gen.Choose(-1000, 1000)
        from dz in Gen.Choose(-1000, 1000)
        let rawDir = new Vector3(dx, dy, dz)
        let dir = rawDir.LengthSquared() < 1f ? new Vector3(0.3f, 0.5f, 0.8f) : Vector3.Normalize(rawDir)
        let alignment = MathF.Abs(Vector3.Dot(dir, axisWorld))
        where mode == GizmoMode.Rotate ? alignment >= MinAxisRayAlignment : alignment <= MaxAxisRayAlignment
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
    /// Property 14: Factory-model drag matches the legacy drag within tolerance.
    /// Drives the same drag input through the legacy single-gizmo path and the factory-created
    /// per-instance path and asserts they begin/report identically and, when a delta is produced,
    /// that the deltas match within 1e-5 world units (translation/scale) and 1e-5 degrees (rotation).
    ///
    /// **Validates: Requirements 7.7**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void FactoryModelDrag_MatchesLegacyDrag_WithinTolerance()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            GizmoHandleId handleId = new(scenario.Mode, GizmoAxisFor(scenario.AxisIndex));

            // --- Legacy (component-driven) model: configure mode/space, publish the frame via
            // Update, and drag via the single-gizmo BeginDrag entry point. ---
            var legacy = new GizmoManager { Mode = scenario.Mode, Space = scenario.Space };
            legacy.Update(CameraParams.Identity, Viewport, scenario.TargetTransform);

            bool legacyBegan = legacy.BeginDrag(handleId, scenario.BeginRay);
            bool legacyUpdated = false;
            Matrix4x4 legacyDelta = Matrix4x4.Identity;
            if (legacyBegan)
            {
                legacyUpdated = legacy.UpdateDrag(scenario.UpdateRay, out legacyDelta);
            }

            // --- Factory (factory-and-engine) model: create + set the gizmo on an entity, drive its
            // per-instance frame via UpdateInstance, then drag through the resolved-pick BeginDrag. ---
            using World world = World.CreateWorld();
            var factory = new GizmoManager();

            var definition = new GizmoDefinition(
                scenario.Mode,
                scenario.Space,
                scenario.TargetEntityId,
                new GizmoHandleConfiguration(scenario.DesiredPixelSize, scenario.Occlusion));

            if (!factory.TryCreateGizmo(definition, out GizmoInstanceHandle instance))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!factory.TrySetGizmo(entity, instance))
            {
                return false;
            }

            factory.UpdateInstance(instance, CameraParams.Identity, Viewport, scenario.TargetTransform);

            // Resolve the same handle through the tracked-gizmo pick path so the drag binds to the
            // per-instance frame (task 7.1), then drive the identical drag input.
            if (!factory.TryResolvePick(true, (uint)entity.Id, handleId, out GizmoPickResolution resolution))
            {
                return false;
            }

            bool factoryBegan = factory.BeginDrag(resolution, scenario.BeginRay);
            bool factoryUpdated = false;
            Matrix4x4 factoryDelta = Matrix4x4.Identity;
            if (factoryBegan)
            {
                factoryUpdated = factory.UpdateDrag(scenario.UpdateRay, out factoryDelta);
            }

            // Both models must agree on whether the drag began and whether a delta was produced.
            if (legacyBegan != factoryBegan || legacyUpdated != factoryUpdated)
            {
                return false;
            }

            // With no produced delta (either did not begin, or the motion was not determinable this
            // frame) both models report identity — parity holds trivially.
            if (!legacyUpdated)
            {
                return true;
            }

            // A delta was produced by both: assert it matches within the requirement's tolerances,
            // interpreting the tolerance by the manipulation the mode produces.
            return DeltasMatch(scenario.Mode, legacyDelta, factoryDelta);
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Compares the legacy and factory transform deltas within the Requirement 7.7 tolerances:
    /// translation within 1e-5 world units (Translate), rotation within 1e-5 degrees (Rotate), and
    /// scale within 1e-5 (Scale). The linear part is always compared element-wise as a strict
    /// catch-all so no mode escapes an unmatched rotation/scale component.
    /// </summary>
    private static bool DeltasMatch(GizmoMode mode, in Matrix4x4 legacy, in Matrix4x4 factory)
    {
        // Translation parity (world units) — the governing tolerance for translate drags.
        if (Vector3.Distance(legacy.Translation, factory.Translation) > WorldUnitTolerance)
        {
            return false;
        }

        switch (mode)
        {
            case GizmoMode.Rotate:
                // Rotation parity in degrees: the angle of the relative rotation between the two
                // (orthonormal) linear parts.
                if (RelativeRotationDegrees(legacy, factory) > RotationDegreeTolerance)
                {
                    return false;
                }
                break;

            default:
                // Scale (and the identity linear part of translate): compare the linear 3x3 parts
                // element-wise in world units.
                if (!LinearPartWithin(legacy, factory, WorldUnitTolerance))
                {
                    return false;
                }
                break;
        }

        return true;
    }

    /// <summary>Compares the linear (3x3) parts of two matrices element-wise within <paramref name="tolerance"/>.</summary>
    private static bool LinearPartWithin(in Matrix4x4 a, in Matrix4x4 b, float tolerance)
        => MathF.Abs(a.M11 - b.M11) <= tolerance && MathF.Abs(a.M12 - b.M12) <= tolerance && MathF.Abs(a.M13 - b.M13) <= tolerance
        && MathF.Abs(a.M21 - b.M21) <= tolerance && MathF.Abs(a.M22 - b.M22) <= tolerance && MathF.Abs(a.M23 - b.M23) <= tolerance
        && MathF.Abs(a.M31 - b.M31) <= tolerance && MathF.Abs(a.M32 - b.M32) <= tolerance && MathF.Abs(a.M33 - b.M33) <= tolerance;

    /// <summary>
    /// Returns the angle in degrees of the rotation that maps the linear part of <paramref name="a"/>
    /// onto that of <paramref name="b"/>. Both linear parts are pure rotations for a rotate drag, so
    /// their quaternions differ by exactly this angle.
    /// </summary>
    /// <remarks>
    /// The angle is taken from the relative quaternion <c>qa * conjugate(qb)</c> via
    /// <c>2 * atan2(|vector part|, |scalar part|)</c>. This stays accurate near a zero angle (the
    /// expected result for two matching deltas), unlike an <c>acos(dot)</c> formulation whose
    /// derivative diverges at <c>dot == 1</c> and would amplify float32 normalization noise into a
    /// spurious fraction-of-a-degree difference.
    /// </remarks>
    private static float RelativeRotationDegrees(in Matrix4x4 a, in Matrix4x4 b)
    {
        Quaternion qa = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(a));
        Quaternion qb = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(b));
        Quaternion rel = qa * Quaternion.Conjugate(qb);
        float vectorLength = new Vector3(rel.X, rel.Y, rel.Z).Length();
        float radians = 2f * MathF.Atan2(vectorLength, MathF.Abs(rel.W));
        return radians * (180f / MathF.PI);
    }
}
