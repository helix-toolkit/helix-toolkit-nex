namespace HelixToolkit.Nex.Rendering.SDF;

/// <summary>
/// Per-glyph metrics loaded from the font atlas descriptor.
/// Contains character code, UV coordinates, advance width, bearings, and glyph dimensions.
/// </summary>
public readonly struct GlyphMetrics
{
    /// <summary>
    /// Gets the character code this glyph represents.
    /// </summary>
    public required char CharacterCode { get; init; }

    /// <summary>
    /// Gets the UV rectangle in the atlas texture (u_min, v_min, u_max, v_max).
    /// </summary>
    public required Vector4 UVRect { get; init; }

    /// <summary>
    /// Gets the horizontal advance width used to position the next glyph.
    /// </summary>
    public required float AdvanceWidth { get; init; }

    /// <summary>
    /// Gets the horizontal bearing (offset from cursor to left edge of glyph).
    /// </summary>
    public required float BearingX { get; init; }

    /// <summary>
    /// Gets the vertical bearing (offset from baseline to top edge of glyph).
    /// </summary>
    public required float BearingY { get; init; }

    /// <summary>
    /// Gets the width of the glyph in atlas units.
    /// </summary>
    public required float GlyphWidth { get; init; }

    /// <summary>
    /// Gets the height of the glyph in atlas units.
    /// </summary>
    public required float GlyphHeight { get; init; }
}

/// <summary>
/// Descriptor used to load font atlas data from an external source (e.g., JSON or binary).
/// Contains atlas metadata and the list of glyph metrics entries.
/// </summary>
public sealed class SDFFontAtlasDescriptor
{
    /// <summary>
    /// Gets the width of the atlas texture in pixels.
    /// </summary>
    public required int TextureWidth { get; init; }

    /// <summary>
    /// Gets the height of the atlas texture in pixels.
    /// </summary>
    public required int TextureHeight { get; init; }

    /// <summary>
    /// Gets the SDF spread (distance range in texels) over which the signed distance field
    /// transitions from inside to outside a glyph outline.
    /// </summary>
    public required float SDFSpread { get; init; }

    /// <summary>
    /// Gets the distance range middle value from msdf-atlas-gen.
    /// This determines where the glyph edge sits in the distance field encoding.
    /// </summary>
    public required float DistanceRangeMiddle { get; init; }

    /// <summary>
    /// Gets the glyph cell size in atlas pixels from msdf-atlas-gen.
    /// </summary>
    public required float GlyphCellSize { get; init; }

    /// <summary>
    /// Gets the line height value for vertical text advancement.
    /// </summary>
    public required float LineHeight { get; init; }

    /// <summary>
    /// Gets the list of glyph metrics entries in this atlas.
    /// </summary>
    public required IReadOnlyList<GlyphMetrics> Glyphs { get; init; }
}

/// <summary>
/// Holds an SDF font atlas texture, glyph metrics, and atlas metadata
/// for SDF-based text rendering via the billboard pipeline.
/// </summary>
public class SDFFontAtlas
{
    /// <summary>
    /// Gets the bindless texture index for the SDF atlas texture.
    /// </summary>
    public uint TextureIndex { get; }

    /// <summary>
    /// Gets the bindless sampler index for the SDF atlas texture.
    /// </summary>
    public uint SamplerIndex { get; }

    /// <summary>
    /// Gets the width of the atlas texture in pixels.
    /// </summary>
    public int TextureWidth { get; }

    /// <summary>
    /// Gets the height of the atlas texture in pixels.
    /// </summary>
    public int TextureHeight { get; }

    /// <summary>
    /// Gets the SDF spread (distance range in texels) over which the signed distance field
    /// transitions from inside to outside a glyph outline.
    /// </summary>
    public float SDFSpread { get; }

    /// <summary>
    /// Gets the distance range middle value from msdf-atlas-gen.
    /// </summary>
    public float DistanceRangeMiddle { get; }

    /// <summary>
    /// Gets the glyph cell size in atlas pixels from msdf-atlas-gen.
    /// </summary>
    public float GlyphCellSize { get; }

    /// <summary>
    /// Gets the line height value for vertical text advancement.
    /// </summary>
    public float LineHeight { get; }

    private readonly Dictionary<char, GlyphMetrics> _glyphs;
    private readonly GlyphMetrics _fallbackGlyph;

    /// <summary>
    /// Creates a new <see cref="SDFFontAtlas"/> from the given texture handles and descriptor.
    /// </summary>
    /// <param name="textureIndex">The bindless texture index for the SDF atlas texture.</param>
    /// <param name="samplerIndex">The bindless sampler index for the SDF atlas texture.</param>
    /// <param name="descriptor">The descriptor containing atlas metadata and glyph metrics.</param>
    public SDFFontAtlas(uint textureIndex, uint samplerIndex, SDFFontAtlasDescriptor descriptor)
    {
        TextureIndex = textureIndex;
        SamplerIndex = samplerIndex;
        TextureWidth = descriptor.TextureWidth;
        TextureHeight = descriptor.TextureHeight;
        SDFSpread = descriptor.SDFSpread;
        DistanceRangeMiddle = descriptor.DistanceRangeMiddle;
        GlyphCellSize = descriptor.GlyphCellSize;
        LineHeight = descriptor.LineHeight;

        _glyphs = new Dictionary<char, GlyphMetrics>(descriptor.Glyphs.Count);
        foreach (var glyph in descriptor.Glyphs)
        {
            _glyphs[glyph.CharacterCode] = glyph;
        }

        // Resolve fallback glyph: use the first glyph in the descriptor if available,
        // otherwise create a zero-size placeholder.
        _fallbackGlyph =
            descriptor.Glyphs.Count > 0
                ? descriptor.Glyphs[0]
                : new GlyphMetrics
                {
                    CharacterCode = '\0',
                    UVRect = Vector4.Zero,
                    AdvanceWidth = 0f,
                    BearingX = 0f,
                    BearingY = 0f,
                    GlyphWidth = 0f,
                    GlyphHeight = 0f,
                };
    }

    /// <summary>
    /// Returns glyph metrics for the given character, or the fallback glyph if not found.
    /// </summary>
    /// <param name="character">The character to look up.</param>
    /// <returns>The <see cref="GlyphMetrics"/> for the character, or the fallback glyph.</returns>
    public GlyphMetrics GetGlyph(char character)
    {
        return _glyphs.TryGetValue(character, out var metrics) ? metrics : _fallbackGlyph;
    }
}
