using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based tests for the corrected default viewpoint the importer establishes after
/// loading a model without an authored glTF camera. The importer frames the model via
/// <see cref="OrbitCameraController.FocusOn(Vector3, float?)"/>, which sets the orbit target
/// to the bounding center and recomputes the camera position from the controller's existing
/// theta/phi. Constructing the camera on the +Z side (as <c>GltfImporterApp</c> does) means
/// the recomputed position always lands on the +Z side of the target.
/// </summary>
[TestClass]
public class DefaultViewpointPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Feature: gltf-directionallight-render-fix, Property 6: Default viewpoint is on the +Z side of the model center
    /// <summary>
    /// Property 6: Default viewpoint is on the +Z side of the model center.
    /// For any model bounding volume, after the importer establishes the default viewpoint
    /// (no authored glTF camera), the camera target equals the bounding center and the camera
    /// position has a Z coordinate strictly greater than the bounding center's Z coordinate.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [TestMethod]
    public void DefaultViewpoint_IsOnPositiveZSideOfModelCenter()
    {
        // Generator: random bounding volumes (bounding center + radius).
        // Centers are kept in [-1000, 1000] per component and radii in [0.01, 1000] so the
        // framing offset (~0.9 * distance) stays well above float resolution at the center Z,
        // keeping the strict +Z comparison unambiguous.
        var boundsGen =
            from cx in Gen.Choose(-100000, 100000).Select(i => i / 100f)
            from cy in Gen.Choose(-100000, 100000).Select(i => i / 100f)
            from cz in Gen.Choose(-100000, 100000).Select(i => i / 100f)
            from r in Gen.Choose(1, 100000).Select(i => i / 100f)
            select (center: new Vector3(cx, cy, cz), radius: r);

        Prop.ForAll(
                Arb.From(boundsGen),
                ((Vector3 center, float radius) input) =>
                {
                    // Construct the camera on the +Z side exactly as GltfImporterApp does,
                    // then wrap it in the orbit controller (no graphics context required).
                    var camera = new PerspectiveCamera
                    {
                        Position = new Vector3(0, 2, 5),
                        Target = Vector3.Zero,
                        NearPlane = 0.01f,
                        FarPlane = 10000f,
                    };
                    var controller = new OrbitCameraController(camera);

                    // Framing distance mirrors the importer: max(boundingRadius * 2, 1).
                    float distance = MathF.Max(input.radius * 2f, 1f);
                    controller.FocusOn(input.center, distance);

                    // Target must equal the bounding center (FocusOn assigns it directly).
                    if (camera.Target != input.center)
                        return false;

                    // Camera position must be strictly on the +Z side of the center.
                    return camera.Position.Z > input.center.Z;
                }
            )
            .Check(FsCheckConfig);
    }
}
