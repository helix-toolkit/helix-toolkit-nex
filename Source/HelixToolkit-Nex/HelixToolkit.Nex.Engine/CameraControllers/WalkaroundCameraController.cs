using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

public class WalkaroundCameraController : FirstPersonCameraController
{
    public WalkaroundCameraController(Camera camera) : base(camera)
    {
        InvertX = true;
        InvertY = true;
    }
}
