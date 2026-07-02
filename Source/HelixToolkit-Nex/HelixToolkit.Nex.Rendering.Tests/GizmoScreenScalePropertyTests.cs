using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="GizmoScreenScale"/>.
/// </summary>
[TestClass]
public class GizmoScreenScalePropertyTests
{
    private const float Aspect = 16f / 9f;
    private const float Near = 0.05f;
    private const float Far = 100_000f;

    /// <summary>
    /// Random scenario: a camera distance <c>d &gt; 0</c> with the gizmo origin (world zero) in front of
    /// the camera, a vertical field of view, a viewport height, a configured desired pixel size, and a
    /// camera view direction.
    /// </summary>
    private static Arbitrary<(float D, float FovY, float ViewportHeight, float DesiredPixels, Vector3 Dir)> ScenarioArb() =>
        Arb.From(
            from dMilli in Gen.Choose(1, 20_000_000)      // d in [0.001, 20000] world units, all > 0
            from fovDeg in Gen.Choose(15, 150)            // vertical fov, degrees
            from vpH in Gen.Choose(16, 4096)              // viewport height, pixels
            from desired in Gen.Choose(1, 1000)           // desired handle pixel size (Requirement 2.2 range)
            from ax in Gen.Choose(-1000, 1000)
            from ay in Gen.Choose(-1000, 1000)
            from az in Gen.Choose(-1000, 1000)
            let d = dMilli / 1000f
            let fovY = fovDeg * MathF.PI / 180f
            let raw = new Vector3(ax, ay, az)
            let dir = raw.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(raw)
            select (d, fovY, (float)vpH, (float)desired, dir));

    /// <summary>
    /// Property 4: Constant screen size.
    /// For any camera distance <c>d &gt; 0</c> with the origin in front of the camera, a gizmo handle
    /// sized by <see cref="GizmoScreenScale.ScreenScaleAt(Vector3, in Matrix4x4, float, float)"/> projects
    /// to a pixel size within ±2% of the configured desired pixel size, independent of distance.
    ///
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void ScreenScaleAt_ProjectedHandlePixelSize_StaysWithinTwoPercentOfDesired()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            var (d, fovY, viewportHeight, desiredPixels, dir) = scenario;

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, Aspect, Near, Far);

            // Camera at distance d along dir, looking at the world origin -> origin is in front of the camera.
            var eye = dir * d;
            var up = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, up);
            var viewProjection = view * projection;

            // Camera basis (view axes expressed in world space), matching CreateLookAt's Gram-Schmidt.
            var zAxis = Vector3.Normalize(eye - Vector3.Zero);       // forward (points away from target)
            var xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis)); // right
            var yAxis = Vector3.Cross(zAxis, xAxis);                 // true camera up

            var origin = Vector3.Zero;
            var scale = new GizmoScreenScale(desiredPixels);

            // World size that should map to the desired pixel size at the origin.
            float s = scale.ScreenScaleAt(origin, viewProjection, viewportHeight, projection.M22);

            // Independently project a world-space handle segment (centered at the origin, spanning ±s
            // along the true camera-up axis) through the full view-projection and perspective divide,
            // then convert clip space to pixels via the standard NDC mapping.
            float top = ProjectPixelY(origin + yAxis * s, viewProjection, viewportHeight);
            float bottom = ProjectPixelY(origin - yAxis * s, viewProjection, viewportHeight);
            float projectedPixels = MathF.Abs(top - bottom);

            float tolerance = 0.02f * desiredPixels;
            return MathF.Abs(projectedPixels - desiredPixels) <= tolerance;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Constant screen size implies distance independence: the projected handle pixel size at a near
    /// distance matches the projected size at a far distance for the same camera configuration.
    ///
    /// **Validates: Requirements 2.1**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void ScreenScaleAt_ProjectedPixelSize_IsIndependentOfDistance()
    {
        var gen =
            from nearMilli in Gen.Choose(100, 5_000)          // near distance in [0.1, 5]
            from farFactor in Gen.Choose(2, 5_000)            // far = near * factor
            from fovDeg in Gen.Choose(15, 150)
            from vpH in Gen.Choose(16, 4096)
            from desired in Gen.Choose(1, 1000)
            let dNear = nearMilli / 1000f
            let dFar = dNear * farFactor
            let fovY = fovDeg * MathF.PI / 180f
            select (dNear, dFar, fovY, (float)vpH, (float)desired);

        Prop.ForAll(Arb.From(gen), t =>
        {
            var (dNear, dFar, fovY, viewportHeight, desiredPixels) = t;
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, Aspect, Near, Far);
            var scale = new GizmoScreenScale(desiredPixels);

            float nearPixels = MeasureProjectedPixels(scale, projection, viewportHeight, dNear);
            float farPixels = MeasureProjectedPixels(scale, projection, viewportHeight, dFar);

            // Both must be within ±2% of the desired size, hence within ~4% of each other.
            return MathF.Abs(nearPixels - desiredPixels) <= 0.02f * desiredPixels
                && MathF.Abs(farPixels - desiredPixels) <= 0.02f * desiredPixels;
        }).QuickCheckThrowOnFailure();
    }

    private static float MeasureProjectedPixels(GizmoScreenScale scale, in Matrix4x4 projection, float viewportHeight, float distance)
    {
        var eye = new Vector3(0f, 0f, distance);
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        var viewProjection = view * projection;

        float s = scale.ScreenScaleAt(Vector3.Zero, viewProjection, viewportHeight, projection.M22);
        float top = ProjectPixelY(Vector3.UnitY * s, viewProjection, viewportHeight);
        float bottom = ProjectPixelY(-Vector3.UnitY * s, viewProjection, viewportHeight);
        return MathF.Abs(top - bottom);
    }

    private static float ProjectPixelY(Vector3 world, in Matrix4x4 viewProjection, float viewportHeight)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        float w = MathF.Max(clip.W, GizmoScreenScale.WEpsilon);
        float ndcY = clip.Y / w;
        return ndcY * viewportHeight * 0.5f;
    }
}
