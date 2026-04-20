namespace HelixToolkit.Nex.Engine;

public static class Extensions
{
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
