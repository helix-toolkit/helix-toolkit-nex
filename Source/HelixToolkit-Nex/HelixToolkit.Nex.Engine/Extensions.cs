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

    public static uint Add(this Engine engine, Geometry geometry)
    {
        engine.ResourceManager.Geometries.Add(geometry, out var id);
        return id;
    }

    public static uint AddAsync(this Engine engine, Geometry geometry)
    {
        engine.ResourceManager.Geometries.AddAsync(geometry, out var id);
        return id;
    }
}
