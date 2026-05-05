using System.Text.Json;

namespace HelixToolkit.Nex.Rendering.SDF;

public enum BuildinFontAtlas
{
    GoogleSansRegular,
    RobotoSlabRegular,
    MichromaRegular,
}

/// <summary>
/// Loads an <see cref="SDFFontAtlasDescriptor"/> from the JSON format produced by
/// <c>msdf-atlas-gen</c> (with <c>-type sdf</c> and <c>-charset</c> flags).
/// <para>
/// The loader converts the msdf-atlas-gen coordinate system to the engine's expected format:
/// <list type="bullet">
///   <item><c>atlasBounds</c> (pixel coords) → normalized UV rect (0–1)</item>
///   <item><c>planeBounds</c> → bearing X/Y, glyph width/height (in em-space)</item>
///   <item><c>yOrigin: "bottom"</c> → V coordinates are flipped so (0,0) is top-left</item>
///   <item><c>unicode</c> → <see cref="GlyphMetrics.CharacterCode"/></item>
/// </list>
/// </para>
/// </summary>
public static class SDFFontAtlasLoader
{
    /// <summary>
    /// Parses the msdf-atlas-gen JSON string and returns an <see cref="SDFFontAtlasDescriptor"/>.
    /// </summary>
    /// <param name="json">The JSON string produced by msdf-atlas-gen.</param>
    /// <returns>A populated <see cref="SDFFontAtlasDescriptor"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required JSON fields are missing.</exception>
    public static SDFFontAtlasDescriptor LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // --- Atlas metadata ---
        var atlas = root.GetProperty("atlas");
        int atlasWidth = atlas.GetProperty("width").GetInt32();
        int atlasHeight = atlas.GetProperty("height").GetInt32();
        float distanceRange = atlas.GetProperty("distanceRange").GetSingle();
        float distanceRangeMiddle = atlas.TryGetProperty("distanceRangeMiddle", out var drmProp)
            ? drmProp.GetSingle()
            : 0f;
        float glyphCellSize = atlas.TryGetProperty("size", out var sizeProp)
            ? sizeProp.GetSingle()
            : 96f;
        bool yOriginBottom =
            atlas.TryGetProperty("yOrigin", out var yOriginProp)
            && yOriginProp.GetString() == "bottom";

        // --- Font metrics ---
        var metrics = root.GetProperty("metrics");
        float lineHeight = metrics.GetProperty("lineHeight").GetSingle();

        // --- Glyphs ---
        var glyphsArray = root.GetProperty("glyphs");
        var glyphs = new List<GlyphMetrics>();

        foreach (var glyphElement in glyphsArray.EnumerateArray())
        {
            if (!glyphElement.TryGetProperty("unicode", out var unicodeProp))
            {
                continue; // Skip glyphs without unicode mapping
            }

            int unicode = unicodeProp.GetInt32();
            float advance = glyphElement.GetProperty("advance").GetSingle();

            // Glyphs without planeBounds/atlasBounds are whitespace (e.g., space)
            float bearingX = 0f;
            float bearingY = 0f;
            float glyphWidth = 0f;
            float glyphHeight = 0f;
            var uvRect = Vector4.Zero;

            if (
                glyphElement.TryGetProperty("planeBounds", out var planeBounds)
                && glyphElement.TryGetProperty("atlasBounds", out var atlasBounds)
            )
            {
                // planeBounds: em-space coordinates
                float planeLeft = planeBounds.GetProperty("left").GetSingle();
                float planeBottom = planeBounds.GetProperty("bottom").GetSingle();
                float planeRight = planeBounds.GetProperty("right").GetSingle();
                float planeTop = planeBounds.GetProperty("top").GetSingle();

                bearingX = planeLeft;
                bearingY = planeTop;
                glyphWidth = planeRight - planeLeft;
                glyphHeight = planeTop - planeBottom;

                // atlasBounds: pixel coordinates → normalized UVs
                // msdf-atlas-gen uses texel-center coordinates (e.g., 57.5 means center of pixel 57).
                // Use texel centers directly — do NOT expand to texel edges.
                // Inset by 0.5 texels to prevent bilinear filter from sampling neighbors.
                float atlasLeft = atlasBounds.GetProperty("left").GetSingle();
                float atlasBottom = atlasBounds.GetProperty("bottom").GetSingle();
                float atlasRight = atlasBounds.GetProperty("right").GetSingle();
                float atlasTop = atlasBounds.GetProperty("top").GetSingle();

                float halfTexelU = 0.5f / atlasWidth;
                float halfTexelV = 0.5f / atlasHeight;

                float uMin = (atlasLeft / atlasWidth) + halfTexelU;
                float uMax = (atlasRight / atlasWidth) - halfTexelU;

                float vMin;
                float vMax;

                if (yOriginBottom)
                {
                    // Atlas Y=0 is at the bottom, Y increases upward.
                    // PNG in Vulkan: row 0 is at the top (V=0).
                    // Flip Y: V = (height - atlasY) / height
                    // vMin (t=0, quad bottom) → glyph bottom (atlasBottom, low Y → large V)
                    // vMax (t=1, quad top) → glyph top (atlasTop, high Y → small V)
                    float vBottom = (atlasHeight - atlasBottom) / atlasHeight;
                    float vTop = (atlasHeight - atlasTop) / atlasHeight;
                    // Inset by half texel to avoid neighbor bleeding
                    vMin = vBottom - halfTexelV; // shrink from bottom
                    vMax = vTop + halfTexelV; // shrink from top
                }
                else
                {
                    vMin = atlasTop / atlasHeight;
                    vMax = atlasBottom / atlasHeight;
                }

                uvRect = new Vector4(uMin, vMin, uMax, vMax);
            }

            glyphs.Add(
                new GlyphMetrics
                {
                    CharacterCode = (char)unicode,
                    UVRect = uvRect,
                    AdvanceWidth = advance,
                    BearingX = bearingX,
                    BearingY = bearingY,
                    GlyphWidth = glyphWidth,
                    GlyphHeight = glyphHeight,
                }
            );
        }

        return new SDFFontAtlasDescriptor
        {
            TextureWidth = atlasWidth,
            TextureHeight = atlasHeight,
            SDFSpread = distanceRange,
            DistanceRangeMiddle = distanceRangeMiddle,
            GlyphCellSize = glyphCellSize,
            LineHeight = lineHeight,
            Glyphs = glyphs,
        };
    }

    /// <summary>
    /// Loads an <see cref="SDFFontAtlasDescriptor"/> from a JSON stream.
    /// </summary>
    /// <param name="stream">A stream containing the msdf-atlas-gen JSON data.</param>
    /// <returns>A populated <see cref="SDFFontAtlasDescriptor"/>.</returns>
    public static SDFFontAtlasDescriptor LoadFromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads an <see cref="SDFFontAtlasDescriptor"/> from an embedded resource in the
    /// <c>HelixToolkit.Nex.Rendering</c> assembly.
    /// </summary>
    /// <param name="resourceName">
    /// The embedded resource name (e.g., <c>"Assets.sans-regular.json"</c>).
    /// The assembly namespace prefix is added automatically.
    /// </param>
    /// <returns>A populated <see cref="SDFFontAtlasDescriptor"/>.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the embedded resource is not found.</exception>
    public static SDFFontAtlasDescriptor LoadFromEmbeddedResource(string resourceName)
    {
        var assembly = typeof(SDFFontAtlasLoader).Assembly;
        string fullName = $"{assembly.GetName().Name}.{resourceName}";

        using var stream =
            assembly.GetManifestResourceStream(fullName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{fullName}' not found. Available resources: "
                    + string.Join(", ", assembly.GetManifestResourceNames())
            );

        return LoadFromStream(stream);
    }

    /// <summary>
    /// Convenience method that loads the built-in <c>sans-regular.json</c> embedded resource
    /// and creates an <see cref="SDFFontAtlas"/> from it.
    /// </summary>
    /// <param name="texture">The bindless texture index for the loaded SDF atlas PNG.</param>
    /// <param name="sampler">The bindless sampler index.</param>
    /// <returns>A ready-to-use <see cref="SDFFontAtlas"/>.</returns>
    public static SDFFontAtlas LoadBuiltInAtlas(
        BuildinFontAtlas altasType,
        TextureRef texture,
        SamplerRef sampler
    )
    {
        var name = altasType switch
        {
            BuildinFontAtlas.GoogleSansRegular => "Assets.google-sans-regular.json",
            BuildinFontAtlas.RobotoSlabRegular => "Assets.robotoslab-sans-regular.json",
            BuildinFontAtlas.MichromaRegular => "Assets.michroma-regular.json",
            _ => throw new ArgumentOutOfRangeException(
                nameof(altasType),
                $"Unsupported atlas type: {altasType}"
            ),
        };
        var descriptor = LoadFromEmbeddedResource(name);
        return new SDFFontAtlas(texture, sampler, descriptor);
    }
}
