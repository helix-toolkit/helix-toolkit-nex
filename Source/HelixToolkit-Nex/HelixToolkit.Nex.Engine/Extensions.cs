namespace HelixToolkit.Nex.Engine;

public static class Extensions
{
    public static Handle<GeometryResourceType> Add(this Engine engine, Geometry geometry)
    {
        return engine.ResourceManager.Geometries.Add(geometry);
    }

    public static async Task<Handle<GeometryResourceType>> AddAsync(this Engine engine, Geometry geometry)
    {
        var (_, handle) = await engine.ResourceManager.Geometries.AddAsync(geometry);
        return handle;
    }

    public static PBRMaterialProperties CreatePBRProperties(this Engine engine, string materialName)
    {
        return engine.ResourceManager.PBRPropertyManager.Create(materialName);
    }
}
