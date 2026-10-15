using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Maths;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Regression tests for the <see cref="CameraExtensions.FocusOn(Camera, BoundingBox, float)"/> overload.
/// <para>
/// The bounding-box overload calculates a camera distance and must delegate to
/// <see cref="CameraExtensions.FocusOn(Camera, Vector3, float?)"/>. Passing the
/// calculated distance as an unnamed <see cref="float"/> previously selected the
/// bounding-sphere overload instead, causing the two overloads to call each other
/// recursively until the process terminated with a stack overflow.
/// </para>
/// <para>
/// These tests verify that perspective and orthographic cameras complete the
/// bounding-box framing operation and produce finite camera state.
/// </para>
/// </summary>
[TestClass]
public sealed class CameraExtensionsTests
{
    /// <summary>
    /// Verifies that focusing a <see cref="PerspectiveCamera"/> on a bounding box
    /// updates its target and distance without resolving to the bounding-sphere
    /// overload.
    /// <para>
    /// The camera starts on the positive Z axis looking at the origin. After framing
    /// the symmetric bounds, it must continue looking along the same direction while
    /// moving to the distance required by its field of view and the requested margin.
    /// </para>
    /// </summary>
    [TestMethod]
    public void FocusOnBoundingBox_PerspectiveCamera_UsesComputedDistanceWithoutRecursion()
    {
        const float marginFactor = 1.2f;

        var camera = new PerspectiveCamera
        {
            Position = new Vector3(0f, 0f, 10f),
            Target = Vector3.Zero,
            NearPlane = 0.01f,
            Fov = MathF.PI / 4f,
        };

        var bounds = new BoundingBox(
            new Vector3(-1f),
            new Vector3(1f)
        );

        camera.FocusOn(bounds, marginFactor);

        var boundingSphereRadius = MathF.Sqrt(3f);
        var expectedDistance =
            boundingSphereRadius
            / MathF.Sin(camera.Fov * 0.5f)
            * marginFactor;
        var actualDistance = Vector3.Distance(camera.Position, camera.Target);

        Assert.AreEqual(
            Vector3.Zero,
            camera.Target,
            "The camera target should be moved to the center of the bounding box."
        );
        AssertFinite(
            camera.Position,
            "The perspective camera position should contain only finite values."
        );
        Assert.IsTrue(
            MathF.Abs(actualDistance - expectedDistance) <= 1e-5f,
            $"Expected camera distance {expectedDistance}, but found {actualDistance}."
        );
    }

    /// <summary>
    /// Verifies that focusing an <see cref="OrthographicCamera"/> on a bounding box
    /// updates both its camera distance and orthographic width without recursion.
    /// <para>
    /// Orthographic projection size is independent of camera distance, so the framing
    /// operation must explicitly enlarge <see cref="OrthographicCamera.Width"/> to
    /// contain the bounding sphere and its requested margin.
    /// </para>
    /// </summary>
    [TestMethod]
    public void FocusOnBoundingBox_OrthographicCamera_UpdatesProjectionWithoutRecursion()
    {
        const float marginFactor = 1.2f;

        var camera = new OrthographicCamera
        {
            Position = new Vector3(0f, 0f, 10f),
            Target = Vector3.Zero,
            NearPlane = 0.01f,
        };

        var bounds = new BoundingBox(
            new Vector3(-1f),
            new Vector3(1f)
        );

        camera.FocusOn(bounds, marginFactor);

        var boundingSphereRadius = MathF.Sqrt(3f);
        var expectedWidth = boundingSphereRadius * 2f * marginFactor;
        var expectedDistance = expectedWidth;
        var actualDistance = Vector3.Distance(camera.Position, camera.Target);

        Assert.AreEqual(
            Vector3.Zero,
            camera.Target,
            "The camera target should be moved to the center of the bounding box."
        );
        AssertFinite(
            camera.Position,
            "The orthographic camera position should contain only finite values."
        );
        Assert.IsTrue(
            MathF.Abs(camera.Width - expectedWidth) <= 1e-5f,
            $"Expected orthographic width {expectedWidth}, but found {camera.Width}."
        );
        Assert.IsTrue(
            MathF.Abs(actualDistance - expectedDistance) <= 1e-5f,
            $"Expected camera distance {expectedDistance}, but found {actualDistance}."
        );
    }

    /// <summary>
    /// Verifies that every component of a vector is finite.
    /// </summary>
    /// <param name="value">The vector whose components are inspected.</param>
    /// <param name="message">The assertion message reported when a component is not finite.</param>
    private static void AssertFinite(Vector3 value, string message)
    {
        Assert.IsTrue(
            float.IsFinite(value.X)
                && float.IsFinite(value.Y)
                && float.IsFinite(value.Z),
            message
        );
    }
}
