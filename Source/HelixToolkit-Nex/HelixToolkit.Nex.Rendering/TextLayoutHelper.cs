using System.Numerics;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// The total width and height of a laid-out text string.
/// </summary>
public readonly struct TextBounds
{
    /// <summary>Total width: max horizontal extent across all lines.</summary>
    public float Width { get; init; }

    /// <summary>Total height: top ascender of first line to bottom descender of last line.</summary>
    public float Height { get; init; }
}

/// <summary>
/// Specifies the anchor point within a text bounding rectangle.
/// The anchor point becomes the local origin (0,0) for glyph offsets.
/// </summary>
public enum BillboardAnchor
{
    BottomLeft = 0,
    BottomCenter,
    BottomRight,
    CenterLeft,
    Center,
    CenterRight,
    TopLeft,
    TopCenter,
    TopRight,
}

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
    /// Computes the total bounding dimensions of the laid-out text.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <returns>A <see cref="TextBounds"/> containing the total width and height of the text.</returns>
    public static TextBounds ComputeBounds(string? text, SDFFontAtlas atlas, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TextBounds { Width = 0, Height = 0 };
        }

        float scale = fontSize / atlas.LineHeight;
        float maxLineWidth = 0;
        float currentLineWidth = 0;
        int lineCount = 1;
        float maxAscender = 0;
        float maxDescender = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                maxLineWidth = MathF.Max(maxLineWidth, currentLineWidth);
                currentLineWidth = 0;
                lineCount++;
                maxDescender = 0; // Reset for the new (last) line
                continue;
            }

            GlyphMetrics glyph = atlas.GetGlyph(c);

            float glyphRightEdge = currentLineWidth + (glyph.BearingX + glyph.GlyphWidth) * scale;
            maxLineWidth = MathF.Max(maxLineWidth, glyphRightEdge);

            if (lineCount == 1)
            {
                maxAscender = MathF.Max(maxAscender, glyph.BearingY * scale);
            }

            // Track descender for the last line only (reset on newline above)
            maxDescender = MathF.Min(maxDescender, (glyph.BearingY - glyph.GlyphHeight) * scale);

            currentLineWidth += glyph.AdvanceWidth * scale;
        }

        // Final line width (no trailing newline needed)
        maxLineWidth = MathF.Max(maxLineWidth, currentLineWidth);

        float width = maxLineWidth;
        float height = maxAscender - maxDescender + (lineCount - 1) * fontSize;

        return new TextBounds { Width = width, Height = height };
    }

    /// <summary>
    /// Computes the offset from BottomLeft to the named anchor point within the text bounds.
    /// </summary>
    /// <param name="anchor">The anchor point to compute the offset for.</param>
    /// <param name="bounds">The text bounds to compute the offset within.</param>
    /// <returns>A <see cref="Vector3"/> offset from BottomLeft to the named anchor point.</returns>
    private static Vector3 ComputeAnchorOffset(BillboardAnchor anchor, TextBounds bounds)
    {
        float x = anchor switch
        {
            BillboardAnchor.BottomLeft or BillboardAnchor.CenterLeft or BillboardAnchor.TopLeft =>
                0,
            BillboardAnchor.BottomCenter or BillboardAnchor.Center or BillboardAnchor.TopCenter =>
                bounds.Width / 2,
            BillboardAnchor.BottomRight
            or BillboardAnchor.CenterRight
            or BillboardAnchor.TopRight => bounds.Width,
            _ => 0,
        };

        float y = anchor switch
        {
            BillboardAnchor.BottomLeft
            or BillboardAnchor.BottomCenter
            or BillboardAnchor.BottomRight => 0,
            BillboardAnchor.CenterLeft or BillboardAnchor.Center or BillboardAnchor.CenterRight =>
                bounds.Height / 2,
            BillboardAnchor.TopLeft or BillboardAnchor.TopCenter or BillboardAnchor.TopRight =>
                bounds.Height,
            _ => 0,
        };

        return new Vector3(x, y, 0);
    }

    /// <summary>
    /// Lays out glyphs for the given text string, producing one billboard descriptor per visible glyph.
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <param name="origin">World-space origin position for the first glyph.</param>
    /// <param name="anchor">The anchor point within the text bounding rectangle. Defaults to BottomLeft (preserves legacy behavior).</param>
    /// <returns>A list of billboard descriptors, one per visible (non-newline) glyph.</returns>
    public static List<GlyphBillboardDescriptor> Layout(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Vector3 origin,
        BillboardAnchor anchor = BillboardAnchor.BottomLeft
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

        // Compute bounds and anchor offset, then subtract from all glyph positions
        TextBounds bounds = ComputeBounds(text, atlas, fontSize);
        Vector3 anchorOffset = ComputeAnchorOffset(anchor, bounds);

        if (anchorOffset != Vector3.Zero)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                var desc = descriptors[i];
                descriptors[i] = new GlyphBillboardDescriptor
                {
                    WorldPosition = desc.WorldPosition - anchorOffset,
                    Width = desc.Width,
                    Height = desc.Height,
                    UVRect = desc.UVRect,
                    TextureIndex = desc.TextureIndex,
                    SamplerIndex = desc.SamplerIndex,
                };
            }
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
    /// <param name="anchor">The anchor point within the text bounding rectangle. Defaults to BottomLeft (preserves legacy behavior).</param>
    /// <param name="isDynamic">Whether the billboard geometry should use dynamic GPU buffers.</param>
    /// <returns>A <see cref="BillboardGeometry"/> containing per-glyph billboard data.</returns>
    public static BillboardGeometry LayoutGeometry(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Vector3 origin,
        BillboardAnchor anchor = BillboardAnchor.BottomLeft,
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

        // Compute bounds and anchor offset, then subtract from all glyph positions
        TextBounds bounds = ComputeBounds(text, atlas, fontSize);
        Vector3 anchorOffset = ComputeAnchorOffset(anchor, bounds);

        if (anchorOffset != Vector3.Zero)
        {
            geo.ApplyPositionOffset(-anchorOffset);
        }

        return geo;
    }

    /// <summary>
    /// Creates a <see cref="BillboardComponent"/> with text layout and all SDF atlas parameters set
    /// from the given <see cref="SDFFontAtlas"/>.
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics and atlas parameters.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <param name="origin">World-space origin position for the first glyph.</param>
    /// <param name="color">The text color.</param>
    /// <param name="anchor">The anchor point within the text bounding rectangle. Defaults to BottomLeft (preserves legacy behavior).</param>
    /// <param name="materialName">The billboard material name (e.g., "SDFFont").</param>
    /// <param name="fixedSize">Whether billboard sizes are fixed screen-space pixels.</param>
    /// <param name="isDynamic">Whether the billboard geometry should use dynamic GPU buffers.</param>
    /// <returns>A fully-configured <see cref="BillboardComponent"/>.</returns>
    public static BillboardComponent CreateTextBillboard(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Vector3 origin,
        Color4 color,
        BillboardAnchor anchor = BillboardAnchor.BottomLeft,
        string? materialName = "SDFFont",
        bool fixedSize = false,
        bool isDynamic = false
    )
    {
        var geo = LayoutGeometry(text, atlas, fontSize, origin, anchor, isDynamic);
        return new BillboardComponent
        {
            BillboardGeometry = geo,
            Color = color,
            TextureIndex = atlas.TextureIndex,
            SamplerIndex = atlas.SamplerIndex,
            BillboardMaterialName = materialName,
            Hitable = true,
            FixedSize = fixedSize,
            SdfDistanceRange = atlas.SDFSpread,
            SdfDistanceRangeMiddle = atlas.DistanceRangeMiddle,
            SdfGlyphCellSize = atlas.GlyphCellSize,
            SdfAtlasWidth = (float)atlas.TextureWidth,
            SdfAtlasHeight = (float)atlas.TextureHeight,
            Anchor = anchor,
        };
    }
}
