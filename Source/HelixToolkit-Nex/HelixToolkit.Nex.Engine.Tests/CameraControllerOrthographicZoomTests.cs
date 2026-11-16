using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Tests for zooming an <see cref="OrthographicCamera"/>, whose view size is set by
/// <see cref="OrthographicCamera.Width"/> rather than the distance to the target, with the
/// controllers that zoom by width (<see cref="OrbitCameraController"/>,
/// <see cref="TurntableCameraController"/>, <see cref="PanZoomCameraController"/>).
/// </summary>
[TestClass]
public class CameraControllerOrthographicZoomTests
{
    private const string Orbit = nameof(OrbitCameraController);
    private const string Turntable = nameof(TurntableCameraController);
    private const string PanZoom = nameof(PanZoomCameraController);

    private static OrthographicCamera CreateOrthoCamera() =>
        new()
        {
            Position = new Vector3(0, 0, 10),
            Target = Vector3.Zero,
            Width = 20f,
        };

    private static ICameraController CreateController(
        string controller,
        Camera camera,
        float minOrthoWidth = 0.1f,
        float maxOrthoWidth = 100000f
    ) =>
        controller switch
        {
            Orbit => new OrbitCameraController(camera)
            {
                MinOrthoWidth = minOrthoWidth,
                MaxOrthoWidth = maxOrthoWidth,
            },
            Turntable => new TurntableCameraController(camera)
            {
                MinOrthoWidth = minOrthoWidth,
                MaxOrthoWidth = maxOrthoWidth,
            },
            PanZoom => new PanZoomCameraController(camera)
            {
                MinOrthoWidth = minOrthoWidth,
                MaxOrthoWidth = maxOrthoWidth,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(controller), controller, null),
        };

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void ZoomIn_Orthographic_ShrinksWidthAndKeepsPosition(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);
        var position = camera.Position;

        controller.OnZoomDelta(1f);

        Assert.IsTrue(camera.Width < 20f, $"Width should shrink on zoom in, was {camera.Width}");
        Assert.AreEqual(position, camera.Position);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void ZoomOut_Orthographic_GrowsWidth(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);

        controller.OnZoomDelta(-1f);

        Assert.IsTrue(camera.Width > 20f, $"Width should grow on zoom out, was {camera.Width}");
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void Zoom_Orthographic_ClampsToWidthLimits(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera, minOrthoWidth: 5f, maxOrthoWidth: 30f);

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
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void Zoom_OrthographicWithPick_MovesTargetTowardPick(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);
        var pick = new Vector3(4, 0, 0);

        controller.OnZoomDelta(1f, pick);

        Assert.IsTrue(camera.Target.X > 0f && camera.Target.X < pick.X);
        // The camera keeps looking at the shifted target from the same distance.
        Assert.AreEqual(10f, Vector3.Distance(camera.Position, camera.Target), 1e-4f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void ZoomOut_OrthographicWithPickNearCamera_KeepsCameraDepth(string controllerName)
    {
        // A picked point just in front of the camera: shifting the target along the view
        // direction would drag the camera past the point and clip it.
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);
        var pick = new Vector3(0, 0, 9.5f);

        controller.OnZoomDelta(-1f, pick);

        Assert.AreEqual(10f, camera.Position.Z, 1e-4f);
        Assert.AreEqual(0f, camera.Target.Z, 1e-4f);
        Assert.IsTrue(camera.Position.Z > pick.Z, "The picked point must stay in front of the camera.");
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void Zoom_OrthographicWithPick_ShiftsTargetWithinViewPlaneOnly(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);
        var pick = new Vector3(4, 2, 5); // off-axis and at a different depth than the target

        controller.OnZoomDelta(1f, pick);

        // The camera looks down -Z, so only X and Y may change.
        Assert.IsTrue(camera.Target.X > 0f && camera.Target.X < pick.X);
        Assert.IsTrue(camera.Target.Y > 0f && camera.Target.Y < pick.Y);
        Assert.AreEqual(0f, camera.Target.Z, 1e-4f);
        Assert.AreEqual(10f, camera.Position.Z, 1e-4f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void Reset_Orthographic_RestoresInitialWidth(string controllerName)
    {
        var camera = CreateOrthoCamera();
        var controller = CreateController(controllerName, camera);

        controller.OnZoomDelta(1f);
        controller.Reset();

        Assert.AreEqual(20f, camera.Width, 1e-5f);
    }

    [TestMethod]
    [TestCategory("CameraController")]
    [DataRow(Orbit)]
    [DataRow(Turntable)]
    [DataRow(PanZoom)]
    public void ZoomIn_Perspective_StillMovesCameraCloser(string controllerName)
    {
        var camera = new PerspectiveCamera { Position = new Vector3(0, 0, 10), Target = Vector3.Zero };
        var controller = CreateController(controllerName, camera);

        controller.OnZoomDelta(1f);

        Assert.IsTrue(Vector3.Distance(camera.Position, camera.Target) < 10f);
    }
}
