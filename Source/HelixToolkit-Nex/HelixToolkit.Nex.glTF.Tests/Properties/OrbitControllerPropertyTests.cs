using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based tests for the <see cref="OrbitCameraController"/> orbit math relied upon
/// by the corrected +Z default viewpoint.
/// </summary>
[TestClass]
public class OrbitControllerPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Reads the current orbit azimuth (theta) from the camera by inverting the spherical
    /// mapping used by <see cref="OrbitCameraController.UpdateCameraPosition"/>:
    /// offset.X = r·sinφ·sinθ, offset.Z = r·sinφ·cosθ, so θ = Atan2(offset.X, offset.Z)
    /// (valid because sinφ &gt; 0 for φ in (0, π)).
    /// </summary>
    private static float ReadAzimuth(Camera camera)
    {
        var offset = camera.Position - camera.Target;
        return MathF.Atan2(offset.X, offset.Z);
    }

    // Feature: gltf-directionallight-render-fix, Property 7: Horizontal orbit azimuth moves monotonically with the pointer
    /// <summary>
    /// Property 7: Horizontal orbit azimuth moves monotonically with the pointer.
    /// For any continuous sequence of same-sign horizontal pointer deltas applied from a
    /// +Z-side viewpoint, the camera azimuth changes monotonically in the single horizontal
    /// direction matching the pointer movement, without reversing direction during the drag.
    /// **Validates: Requirements 3.2, 6.2**
    /// </summary>
    [TestMethod]
    public void HorizontalOrbit_Azimuth_MovesMonotonicallyWithPointer()
    {
        // Generator: a non-empty sequence of same-sign horizontal pointer-delta magnitudes.
        // Magnitudes are kept in [1, 200] pixels so each per-step azimuth change
        // (magnitude * RotationSensitivity) stays well below PI, keeping angle unwrapping
        // unambiguous. A boolean controls the common sign of every delta in the drag.
        var deltaSeqGen =
            from sign in Gen.Elements(true, false)
            from magnitudes in Gen.Choose(1, 200).Select(i => (float)i).NonEmptyListOf()
            select (sign, magnitudes: magnitudes.ToArray());

        Prop.ForAll(
                Arb.From(deltaSeqGen),
                ((bool sign, float[] magnitudes) input) =>
                {
                    float signFactor = input.sign ? 1f : -1f;

                    // Construct camera + controller directly on the +Z side (no graphics context).
                    var camera = new PerspectiveCamera
                    {
                        Position = new Vector3(0, 0, 5),
                        Target = Vector3.Zero,
                        NearPlane = 0.01f,
                        FarPlane = 10000f,
                    };
                    var controller = new OrbitCameraController(camera);

                    // Begin the drag. Keep y constant throughout so only azimuth (theta) changes.
                    const float y = 0f;
                    float x = 0f;
                    controller.OnRotateBegin(x, y);

                    // Record the unwrapped azimuth before and after each monotonic delta.
                    float prevRaw = ReadAzimuth(camera);
                    float unwrapped = prevRaw;
                    var unwrappedAzimuths = new List<float> { unwrapped };

                    foreach (var magnitude in input.magnitudes)
                    {
                        float dx = signFactor * magnitude;
                        x += dx;
                        controller.OnRotateDelta(x, y);

                        float raw = ReadAzimuth(camera);
                        float diff = raw - prevRaw;
                        // Unwrap into (-PI, PI] so cumulative motion is continuous.
                        while (diff > MathF.PI)
                            diff -= 2f * MathF.PI;
                        while (diff < -MathF.PI)
                            diff += 2f * MathF.PI;

                        unwrapped += diff;
                        unwrappedAzimuths.Add(unwrapped);
                        prevRaw = raw;
                    }

                    // Expected direction: theta -= dx * sensitivity, so a positive dx (sign>0)
                    // must strictly DECREASE the unwrapped azimuth, and a negative dx must
                    // strictly INCREASE it. The change must be strictly monotonic (no reversal,
                    // no stalling) across the whole drag.
                    for (int i = 1; i < unwrappedAzimuths.Count; i++)
                    {
                        float stepDiff = unwrappedAzimuths[i] - unwrappedAzimuths[i - 1];

                        if (input.sign)
                        {
                            // Positive dx -> azimuth strictly decreasing.
                            if (!(stepDiff < 0f))
                                return false;
                        }
                        else
                        {
                            // Negative dx -> azimuth strictly increasing.
                            if (!(stepDiff > 0f))
                                return false;
                        }
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
