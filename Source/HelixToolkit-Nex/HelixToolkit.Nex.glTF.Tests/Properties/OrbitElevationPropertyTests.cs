using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-directionallight-render-fix, Property 8: Vertical orbit elevation moves monotonically with the pointer

/// <summary>
/// Property-based test for vertical orbit elevation monotonicity (Property 8).
/// For any continuous sequence of same-sign vertical pointer deltas, the camera elevation
/// (polar angle phi, within its clamp limits) changes monotonically in a single direction
/// consistent with the pointer movement, without reversing direction during the drag.
/// </summary>
[TestClass]
public class OrbitElevationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Tolerance for phi comparisons: absorbs floating-point noise from the
    // Acos(Position->Target) reconstruction and allows clamp saturation (equal values).
    private const float PhiEpsilon = 1e-4f;

    /// <summary>
    /// Recovers the orbit polar angle (phi) from the camera state. The orbit controller
    /// sets Position = Target + (r*sinPhi*sinTheta, r*cosPhi, r*sinPhi*cosTheta), so the
    /// normalized Y component of (Position - Target) equals cos(phi), and phi in [0, PI]
    /// is recovered unambiguously via Acos.
    /// </summary>
    private static float CurrentPhi(Camera camera)
    {
        var offset = camera.Position - camera.Target;
        float r = offset.Length();
        float ny = r > 1e-6f ? offset.Y / r : 0f;
        return MathF.Acos(Math.Clamp(ny, -1f, 1f));
    }

    /// <summary>
    /// Property 8: Vertical orbit elevation moves monotonically with the pointer.
    /// **Validates: Requirements 3.3, 6.5**
    /// </summary>
    [TestMethod]
    public void VerticalOrbit_SameSignDeltas_ElevationChangesMonotonically()
    {
        // Generator: a single drag direction (sign) plus a sequence of positive pixel
        // step magnitudes. Accumulating signed steps yields monotonic same-sign dy deltas.
        var inputGen =
            from sign in Gen.Elements(new[] { -1f, 1f })
            from steps in Gen.ArrayOf(Gen.Choose(1, 100).Select(i => i / 10.0f))
            where steps.Length >= 2 // need a continuous drag of at least two steps
            select (sign, steps);

        Prop.ForAll(
                Arb.From(inputGen),
                ((float sign, float[] steps) input) =>
                {
                    // Construct camera on the +Z side with a non-degenerate elevation,
                    // then build the controller directly (no graphics context).
                    var camera = new PerspectiveCamera
                    {
                        Position = new Vector3(0f, 2f, 5f),
                        Target = Vector3.Zero,
                        NearPlane = 0.01f,
                        FarPlane = 10000f,
                    };

                    // Defaults: RotationSensitivity = 0.005, InvertY = true,
                    // MinPhi = 0.1, MaxPhi = PI - 0.1.
                    var controller = new OrbitCameraController(camera);

                    controller.OnRotateBegin(0f, 0f);

                    var phis = new List<float> { CurrentPhi(camera) };

                    // Apply same-sign vertical deltas (x held constant so azimuth is unaffected).
                    float y = 0f;
                    foreach (var step in input.steps)
                    {
                        y += input.sign * step;
                        controller.OnRotateDelta(0f, y);
                        phis.Add(CurrentPhi(camera));
                    }

                    // With InvertY = true, phi += -dy * sensitivity. So a positive-sign drag
                    // (dy > 0) drives phi non-increasing, and a negative-sign drag drives phi
                    // non-decreasing. Verify the sequence never reverses (clamp saturation,
                    // i.e. equal consecutive values, is permitted within PhiEpsilon).
                    for (int i = 1; i < phis.Count; i++)
                    {
                        float delta = phis[i] - phis[i - 1];
                        if (input.sign > 0f)
                        {
                            if (delta > PhiEpsilon)
                            {
                                return false; // reversed: phi increased on a downward-driving drag
                            }
                        }
                        else
                        {
                            if (delta < -PhiEpsilon)
                            {
                                return false; // reversed: phi decreased on an upward-driving drag
                            }
                        }
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
