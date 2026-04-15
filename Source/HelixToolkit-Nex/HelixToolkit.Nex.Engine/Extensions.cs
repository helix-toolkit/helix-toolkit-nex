using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine;

public static class Extensions
{
    public static void Update(this RenderContext context, Camera camera)
    {
        context.CameraParams = camera.ToCameraParams(
            context.WindowSize.Width / (float)context.WindowSize.Height
        );
    }
}
