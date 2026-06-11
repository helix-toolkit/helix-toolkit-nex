using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// The total width and height of a laid-out text string.
/// </summary>
public readonly struct TextBounds
{
    /// <summary>Total width: max horizontal extent across all lines.</summary>
    public readonly float Width { get; init; }

    /// <summary>Total height: top ascender of first line to bottom descender of last line.</summary>
    public readonly float Height { get; init; }

    /// <summary>
    /// Y offset from the baseline (origin) to the bottom edge of the bounds.
    /// This is zero or negative (e.g. -0.2 for glyphs with descenders such as 'y' or 'g').
    /// Use this to correctly position a background quad so it covers descenders.
    /// </summary>
    public readonly float DescenderOffset { get; init; }

    /// <summary>
    /// Y offset from the baseline (origin) to the top edge of the bounds (ascender of the first line).
    /// Always positive. Use together with <see cref="Height"/> to correctly centre a background quad
    /// over multi-line text: <c>centerY = Ascender - Height / 2</c>.
    /// </summary>
    public readonly float Ascender { get; init; }
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

        return new TextBounds
        {
            Width = width,
            Height = height,
            DescenderOffset = maxDescender,
            Ascender = maxAscender,
        };
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
    /// Convenience overload of <see cref="LayoutGeometry"/> with default <see cref="BillboardAnchor.BottomLeft"/> anchor.
    /// </summary>
    public static BillboardGeometry LayoutGeometry(
        string text,
        SDFFontAtlas atlas,
        float fontSize
    )
    {
        return LayoutGeometry(
            text,
            atlas,
            fontSize,
            BillboardAnchor.BottomLeft,
            out _,
            out _
        );
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
    /// <param name="bounds">Output parameter receiving the total bounding dimensions of the laid-out text.</param>
    /// <param name="anchorOffset">Output parameter receiving the offset from BottomLeft to the named anchor point.</param>
    /// <returns>A <see cref="BillboardGeometry"/> containing per-glyph billboard data.</returns>
    public static BillboardGeometry LayoutGeometry(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        BillboardAnchor anchor,
        out TextBounds bounds,
        out Vector3 anchorOffset
    )
    {
        var geo = new BillboardGeometry();
        if (string.IsNullOrEmpty(text))
        {
            bounds = default;
            anchorOffset = default;
            return geo;
        }

        float scale = fontSize / atlas.LineHeight;
        Vector3 cursor = Vector3.Zero;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                cursor.Y -= fontSize;
                cursor.X = 0;
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
        bounds = ComputeBounds(text, atlas, fontSize);
        anchorOffset = ComputeAnchorOffset(anchor, bounds);

        if (anchorOffset != Vector3.Zero)
        {
            geo.ApplyPositionOffset(-anchorOffset);
        }

        return geo;
    }

    /// <summary>
    /// Creates a <see cref="BillboardDrawInfo"/> with text layout and all SDF atlas parameters set
    /// from the given <see cref="SDFFontAtlas"/>.
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="atlas">The SDF font atlas containing glyph metrics and atlas parameters.</param>
    /// <param name="fontSize">Desired font size in world-space units.</param>
    /// <param name="color">The text color.</param>
    /// <param name="background">The optional background color (null for no background quad).</param>
    /// <param name="anchor">The anchor point within the text bounding rectangle. Defaults to BottomLeft (preserves legacy behavior).</param>
    /// <param name="materialName">The billboard material name (e.g., "SDFFont").</param>
    /// <param name="fixedSize">Whether billboard sizes are fixed screen-space pixels.</param>
    /// <param name="cullDistance">The distance beyond which the billboard should be culled (0 for no culling).</param>
    /// <returns>A fully-configured <see cref="BillboardDrawInfo"/>.</returns>
    public static BillboardDrawInfo CreateTextBillboard(
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Color4 color,
        Color4? background = null,
        BillboardAnchor anchor = BillboardAnchor.BottomLeft,
        Vector4? padding = null,
        string? materialName = "SDFFont",
        bool fixedSize = false,
        float cullDistance = 0
    )
    {
        var geo = LayoutGeometry(
            text,
            atlas,
            fontSize,
            anchor,
            out var bounds,
            out var anchorOffset
        );
        if (background.HasValue)
        {
            // Padding: X = left, Y = bottom, Z = right, W = top
            float padLeft = padding?.X ?? 0;
            float padBottom = padding?.Y ?? 0;
            float padRight = padding?.Z ?? 0;
            float padTop = padding?.W ?? 0;

            float bgWidth = bounds.Width + padLeft + padRight;
            float bgHeight = bounds.Height + padBottom + padTop;

            // Center of the text box relative to origin (first-line baseline), accounting for
            // descenders and multiple lines. The top edge is at +Ascender and the bottom edge is at
            // Ascender - Height, so the centre is Ascender - Height/2.
            float textCenterX = bounds.Width / 2f;
            float textCenterY = bounds.Ascender - bounds.Height / 2f;

            // Shift center by asymmetric padding.
            float bgCenterX = textCenterX + (padRight - padLeft) / 2f;
            float bgCenterY = textCenterY + (padTop - padBottom) / 2f;

            Vector3 bgPosition = new Vector3(bgCenterX, bgCenterY, 0) - anchorOffset;
            geo.Insert(0, bgPosition, bgWidth, bgHeight, background.Value);
        }
        return new BillboardDrawInfo
        {
            BillboardGeometry = geo,
            Color = color,
            Texture = atlas.Texture,
            Sampler = atlas.Sampler,
            BillboardMaterialName = materialName,
            Hitable = true,
            FixedSize = fixedSize,
            SdfDistanceRange = atlas.SDFSpread,
            SdfDistanceRangeMiddle = atlas.DistanceRangeMiddle,
            SdfGlyphCellSize = atlas.GlyphCellSize,
            SdfAtlasWidth = (float)atlas.TextureWidth,
            SdfAtlasHeight = (float)atlas.TextureHeight,
            Anchor = anchor,
            CullDistance = cullDistance,
        };
    }
}
