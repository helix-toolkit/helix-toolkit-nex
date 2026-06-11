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
    public static BillboardDrawInfo CreateBillboard(
        this Engine engine,
        BuildinFontAtlas fontType,
        string text,
        float fontSize,
        Color4 color,
        Color4? background = null,
        BillboardAnchor anchor = BillboardAnchor.Center,
        string materialName = "SDFFont",
        bool fixedSize = true,
        float cullDistance = 0
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
            color,
            background,
            anchor,
            materialName: materialName,
            fixedSize: fixedSize,
            cullDistance: cullDistance
        );
    }

    public static BillboardDrawInfo? CreateBillboard(
        this Engine engine,
        string fontType,
        string text,
        float fontSize,
        Color4 color,
        Color4? background = null,
        BillboardAnchor anchor = BillboardAnchor.Center,
        string materialName = "SDFFont",
        bool fixedSize = true,
        float cullDistance = 0
    )
    {
        var fontAtlasRepo = engine.ResourceManager.FontAtlasRepository;
        if (fontAtlasRepo.TryGet(fontType, out var atlas))
        {
            return TextLayoutHelper.CreateTextBillboard(
                text,
                atlas!,
                fontSize,
                color,
                background,
                anchor,
                materialName: materialName,
                fixedSize: fixedSize,
                cullDistance: cullDistance
            );
        }
        return null;
    }
}
