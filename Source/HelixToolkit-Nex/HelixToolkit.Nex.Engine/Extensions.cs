using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;

namespace HelixToolkit.Nex.Engine;

public static class Extensions
{
    public static Handle<GeometryResourceType> Add(this Engine engine, Geometry geometry)
    {
        return engine.ResourceManager.Geometries.Add(geometry);
    }

    public static async Task<Handle<GeometryResourceType>> AddAsync(
        this Engine engine,
        Geometry geometry
    )
    {
        var (_, handle) = await engine.ResourceManager.Geometries.AddAsync(geometry);
        return handle;
    }

    public static PBRMaterialProperties CreatePBRProperties(this Engine engine, string materialName)
    {
        return engine.ResourceManager.PBRPropertyManager.Create(materialName);
    }
}

public static class BillboardExtensions
{
    public static BillboardComponent CreateBillboard(
        this Engine engine,
        BuildinFontAtlas fontType,
        string text,
        float fontSize,
        Vector3 origin,
        Color4 color,
        string materialName,
        bool fixedSize = true,
        bool isDynamic = false
    )
    {
        var fontAtlasRepo = engine.ResourceManager.FontAtlasRepository;
        var atlas = fontAtlasRepo.GetOrCreateBuiltIn(
            fontType,
            engine.ResourceManager.TextureRepository,
            engine.ResourceManager.SamplerRepository
        );
        return TextLayoutHelper.CreateTextBillboard(
            text,
            atlas,
            fontSize,
            origin,
            color,
            materialName: materialName,
            fixedSize: fixedSize,
            isDynamic: isDynamic
        );
    }

    public static BillboardComponent? CreateBillboard(
        this Engine engine,
        string fontType,
        string text,
        float fontSize,
        Vector3 origin,
        Color4 color,
        string materialName,
        bool fixedSize = true,
        bool isDynamic = false
    )
    {
        var fontAtlasRepo = engine.ResourceManager.FontAtlasRepository;
        if (fontAtlasRepo.TryGet(fontType, out var atlas))
        {
            return TextLayoutHelper.CreateTextBillboard(
                text,
                atlas!,
                fontSize,
                origin,
                color,
                materialName: materialName,
                fixedSize: fixedSize,
                isDynamic: isDynamic
            );
        }
        return null;
    }
}
