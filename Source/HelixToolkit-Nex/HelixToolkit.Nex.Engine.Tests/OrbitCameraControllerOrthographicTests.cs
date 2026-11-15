using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Tests for <see cref="OrbitCameraController"/> zoom with an <see cref="OrthographicCamera"/>,
/// whose view size is set by <see cref="OrthographicCamera.Width"/> rather than the distance to
/// the target.
/// </summary>
[TestClass]
public class OrbitCameraControllerOrthographicTests
{
    private static OrthographicCamera CreateOrthoCamera() =>
        new()
        {
            Position = new Vector3(0, 0, 10),
            Target = Vector3.Zero,
            Width = 20f,
        };

    [TestMethod]
    [TestCategory("CameraController")]
    public void ZoomIn_Orthographic_ShrinksWidthAndKeepsPosition()
    {
        var camera = CreateOrthoCamera();
        var controller = new OrbitCameraController(camera);
        var position = camera.Position;

        controller.OnZoomDelta(1f);

        Assert.IsTrue(camera.Width < 20f, $"Width should shrink on zoom in, was {camera.Width}");
        Assert.AreEqual(position, camera.Position);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    public void ZoomOut_Orthographic_GrowsWidth()
    {
        var camera = CreateOrthoCamera();
        var controller = new OrbitCameraController(camera);

        controller.OnZoomDelta(-1f);

        Assert.IsTrue(camera.Width > 20f, $"Width should grow on zoom out, was {camera.Width}");
    }

    [TestMethod]
    [TestCategory("CameraController")]
    public void Zoom_Orthographic_ClampsToWidthLimits()
    {
        var camera = CreateOrthoCamera();
        var controller = new OrbitCameraController(camera) { MinOrthoWidth = 5f, MaxOrthoWidth = 30f };

        for (int i = 0; i < 100; i++)
        {
            controller.OnZoomDelta(1f);
        }
        Assert.AreEqual(5f, camera.Width, 1e-5f);

        for (int i = 0; i < 100; i++)
        {
            controller.OnZoomDelta(-1f);
        }
        Assert.AreEqual(30f, camera.Width, 1e-5f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    public void Zoom_OrthographicWithPick_MovesTargetTowardPick()
    {
        var camera = CreateOrthoCamera();
        var controller = new OrbitCameraController(camera);
        var pick = new Vector3(4, 0, 0);

        controller.OnZoomDelta(1f, pick);

        Assert.IsTrue(camera.Target.X > 0f && camera.Target.X < pick.X);
        // The camera keeps looking at the shifted target from the same orbit distance.
        Assert.AreEqual(10f, Vector3.Distance(camera.Position, camera.Target), 1e-4f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    public void Reset_Orthographic_RestoresInitialWidth()
    {
        var camera = CreateOrthoCamera();
        var controller = new OrbitCameraController(camera);

        controller.OnZoomDelta(1f);
        controller.Reset();

        Assert.AreEqual(20f, camera.Width, 1e-5f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    public void ZoomIn_Perspective_StillMovesCameraCloser()
    {
        var camera = new PerspectiveCamera { Position = new Vector3(0, 0, 10), Target = Vector3.Zero };
        var controller = new OrbitCameraController(camera);

        controller.OnZoomDelta(1f);

        Assert.IsTrue(Vector3.Distance(camera.Position, camera.Target) < 10f);
    }
}
