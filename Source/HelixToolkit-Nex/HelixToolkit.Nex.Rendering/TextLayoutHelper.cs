using System.Numerics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Describes a single glyph billboard produced by <see cref="TextLayoutHelper"/>.
/// Contains the world-space position, dimensions, UV coordinates, and texture references
/// needed to render one glyph as a camera-facing quad.
/// </summary>
public readonly struct GlyphBillboardDescriptor
{
    /// <summary>
    /// Gets the world-space position of the glyph billboard.
    /// </summary>
    public required Vector3 WorldPosition { get; init; }

    /// <summary>
    /// Gets the width of the glyph billboard in world-space units.
    /// </summary>
    public required float Width { get; init; }

    /// <summary>
    /// Gets the height of the glyph billboard in world-space units.
    /// </summary>
    public required float Height { get; init; }

    /// <summary>
    /// Gets the UV rectangle (u_min, v_min, u_max, v_max) defining the glyph's
    /// sub-region in the font atlas texture.
    /// </summary>
    public required Vector4 UVRect { get; init; }

    /// <summary>
    /// Gets the bindless texture index for the SDF font atlas texture.
    /// </summary>
    public required uint TextureIndex { get; init; }

    /// <summary>
    /// Gets the bindless sampler index for the SDF font atlas texture.
    /// </summary>
    public required uint SamplerIndex { get; init; }
}

/// <summary>
/// Converts a text string and <see cref="SDFFontAtlas"/> into a list of billboard descriptors
/// for rendering text as camera-facing quads. Supports horizontal left-to-right layout
/// with multi-line text via newline characters.
/// </summary>
public static class TextLayoutHelper
{
    /// <summary>
    /// Lays out glyphs for the given text string, producing one billboard descriptor per visible glyph.
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <param name="origin">World-space origin position for the first glyph.</param>
    /// <returns>A list of billboard descriptors, one per visible (non-newline) glyph.</returns>
    public static List<GlyphBillboardDescriptor> Layout(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Vector3 origin
    )
    {
        var descriptors = new List<GlyphBillboardDescriptor>();

        if (string.IsNullOrEmpty(text))
        {
            return descriptors;
        }

        float scale = fontSize / atlas.LineHeight;
        Vector3 cursor = origin;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                cursor.Y -= fontSize;
                cursor.X = origin.X;
                continue;
            }

            GlyphMetrics glyph = atlas.GetGlyph(c);

            float width = glyph.GlyphWidth * scale;
            float height = glyph.GlyphHeight * scale;

            // Position is the CENTER of the glyph quad (billboard expands symmetrically from center)
            // BearingX is offset from cursor to left edge, BearingY is offset from baseline to top edge
            Vector3 position = new Vector3(
                cursor.X + glyph.BearingX * scale + width * 0.5f,
                cursor.Y + (glyph.BearingY - glyph.GlyphHeight * 0.5f) * scale,
                cursor.Z
            );

            descriptors.Add(
                new GlyphBillboardDescriptor
                {
                    WorldPosition = position,
                    Width = width,
                    Height = height,
                    UVRect = glyph.UVRect,
                    TextureIndex = atlas.TextureIndex,
                    SamplerIndex = atlas.SamplerIndex,
                }
            );

            cursor.X += glyph.AdvanceWidth * scale;
        }

        return descriptors;
    }

    /// <summary>
    /// Lays out glyphs and returns a <see cref="BillboardGeometry"/> with per-glyph positions, sizes, and UV rects.
    /// This is the preferred method for SDF text rendering — one entity per text string.
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <param name="origin">World-space origin position for the first glyph.</param>
    /// <param name="isDynamic">Whether the billboard geometry should use dynamic GPU buffers.</param>
    /// <returns>A <see cref="BillboardGeometry"/> containing per-glyph billboard data.</returns>
    public static BillboardGeometry LayoutGeometry(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Vector3 origin,
        bool isDynamic = false
    )
    {
        var geo = new BillboardGeometry(isDynamic);
        if (string.IsNullOrEmpty(text))
            return geo;

        float scale = fontSize / atlas.LineHeight;
        Vector3 cursor = origin;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                cursor.Y -= fontSize;
                cursor.X = origin.X;
                continue;
            }

            GlyphMetrics glyph = atlas.GetGlyph(c);

            float width = glyph.GlyphWidth * scale;
            float height = glyph.GlyphHeight * scale;

            // Position is the CENTER of the glyph quad (billboard expands symmetrically from center)
            var position = new Vector3(
                cursor.X + glyph.BearingX * scale + width * 0.5f,
                cursor.Y + (glyph.BearingY - glyph.GlyphHeight * 0.5f) * scale,
                cursor.Z
            );

            geo.Add(position, width, height, glyph.UVRect);
            cursor.X += glyph.AdvanceWidth * scale;
        }

        return geo;
    }
}
