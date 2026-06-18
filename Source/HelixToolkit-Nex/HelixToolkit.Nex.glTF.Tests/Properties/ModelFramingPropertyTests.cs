using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based test for the model-framing behavior of the glTF importer's
/// <c>ComputeBoundsAndFrameCamera</c>, which frames a model by focusing the orbit
/// camera on the bounding center at distance <c>max(boundingRadius * 2, 1)</c>.
/// </summary>
[TestClass]
public class ModelFramingPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private const float NearPlane = 0.01f;
    private const float FarPlane = 10000f;

    /// <summary>
    /// Mirrors the importer's framing distance from
    /// <c>GltfImporterApp.ComputeBoundsAndFrameCamera</c>: a comfortable framing
    /// distance of twice the bounding radius, floored at 1.
    /// </summary>
    private static float ImporterFramingDistance(float boundingRadius) =>
        MathF.Max(boundingRadius * 2f, 1f);

    // Feature: gltf-directionallight-render-fix, Property 10: Model framing keeps the bounding volume between the near and far planes
    /// <summary>
    /// Property 10: Model framing keeps the bounding volume between the near and far planes.
    /// For any model bounding volume, the framing distance chosen by the importer places the
    /// bounding sphere fully between the camera near and far clipping planes — the nearest
    /// point of the sphere from the camera lies beyond the near plane and the farthest point
    /// lies inside the far plane.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void ModelFraming_KeepsBoundingSphere_BetweenNearAndFarPlanes()
    {
        // Generator: random bounding volumes (center + radius). Radius is kept in
        // [0.001, 2000] so the importer's framing distance (<= 4000) stays within the
        // controller's radius clamp and the resulting sphere extent (<= 6000) stays
        // comfortably inside the 10000 far plane. The center is placed arbitrarily in
        // space; framing distances are measured relative to the center, so the center
        // offset does not change the near/far containment result.
        var boundsGen =
            from rMilli in Gen.Choose(1, 2_000_000)
            from cx in Gen.Choose(-1000, 1000)
            from cy in Gen.Choose(-1000, 1000)
            from cz in Gen.Choose(-1000, 1000)
            select (radius: rMilli / 1000f, center: new Vector3(cx, cy, cz));

        Prop.ForAll(
                Arb.From(boundsGen),
                ((float radius, Vector3 center) bounds) =>
                {
                    // Construct a PerspectiveCamera + OrbitCameraController directly
                    // (no graphics context needed), matching the importer's setup.
                    var camera = new PerspectiveCamera
                    {
                        Position = new Vector3(0, 2, 5),
                        Target = Vector3.Zero,
                        NearPlane = NearPlane,
                        FarPlane = FarPlane,
                    };
                    var controller = new OrbitCameraController(camera);

                    // Frame the model exactly as the importer does.
                    float distance = ImporterFramingDistance(bounds.radius);
                    controller.FocusOn(bounds.center, distance);

                    // Actual camera-to-center distance after framing (FocusOn clamps the
                    // requested distance to the controller's radius limits).
                    float cameraToCenter = (camera.Position - bounds.center).Length();

                    // The bounding sphere occupies [cameraToCenter - radius, cameraToCenter + radius]
                    // along the view direction.
                    float nearest = cameraToCenter - bounds.radius;
                    float farthest = cameraToCenter + bounds.radius;

                    // Nearest point must lie beyond the near plane, farthest within the far plane.
                    return nearest > NearPlane && farthest < FarPlane;
                }
            )
            .Check(FsCheckConfig);
    }
}
