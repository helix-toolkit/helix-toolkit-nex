using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Unit tests for SDF font rendering components:
/// <see cref="SDFFontAtlasLoader"/>, <see cref="SDFFontAtlas"/>,
/// <see cref="TextLayoutHelper"/>, <see cref="SDFFontMaterialConfig"/>,
/// and <see cref="BillboardGeometry"/>.
/// </summary>
[TestClass]
[TestCategory("SDFFont")]
public class SDFFontRenderingTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal <see cref="SDFFontAtlasDescriptor"/> for testing.
    /// </summary>
    private static SDFFontAtlasDescriptor CreateTestDescriptor()
    {
        return new SDFFontAtlasDescriptor
        {
            TextureWidth = 224,
            TextureHeight = 224,
            SDFSpread = 4.0f,
            LineHeight = 1.252f,
            Glyphs =
            [
                new GlyphMetrics
                {
                    CharacterCode = 'A',
                    UVRect = new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
                    AdvanceWidth = 0.67f,
                    BearingX = -0.055f,
                    BearingY = 0.797f,
                    GlyphWidth = 0.781f,
                    GlyphHeight = 0.875f,
                },
                new GlyphMetrics
                {
                    CharacterCode = 'B',
                    UVRect = new Vector4(0.4f, 0.5f, 0.6f, 0.7f),
                    AdvanceWidth = 0.60f,
                    BearingX = 0.009f,
                    BearingY = 0.797f,
                    GlyphWidth = 0.625f,
                    GlyphHeight = 0.875f,
                },
                new GlyphMetrics
                {
                    CharacterCode = ' ',
                    UVRect = Vector4.Zero,
                    AdvanceWidth = 0.232f,
                    BearingX = 0f,
                    BearingY = 0f,
                    GlyphWidth = 0f,
                    GlyphHeight = 0f,
                },
            ],
        };
    }

    private static SDFFontAtlas CreateTestAtlas(uint textureIndex = 1, uint samplerIndex = 0)
    {
        return new SDFFontAtlas(textureIndex, samplerIndex, CreateTestDescriptor());
    }

    // -----------------------------------------------------------------------
    // SDFFontAtlasLoader — LoadFromJson
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LoadFromJson_ParsesAtlasMetadata()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        Assert.IsTrue(descriptor.TextureWidth > 0);
        Assert.IsTrue(descriptor.TextureHeight > 0);
        Assert.AreEqual(8.0f, descriptor.SDFSpread, 0.001f);
        Assert.AreEqual(1.252f, descriptor.LineHeight, 0.001f);
    }

    [TestMethod]
    public void LoadFromJson_ParsesAllPrintableAsciiGlyphs()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        // Printable ASCII: 32 (space) through 126 (~) = 95 characters
        Assert.AreEqual(
            95,
            descriptor.Glyphs.Count,
            "Expected 95 printable ASCII glyphs (0x20–0x7E)."
        );
    }

    [TestMethod]
    public void LoadFromJson_SpaceGlyphHasZeroUVRect()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        var space = descriptor.Glyphs.FirstOrDefault(g => g.CharacterCode == ' ');
        Assert.AreEqual(' ', space.CharacterCode);
        Assert.AreEqual(
            Vector4.Zero,
            space.UVRect,
            "Space glyph should have zero UV rect (no visual bounds)."
        );
        Assert.IsTrue(space.AdvanceWidth > 0, "Space glyph should have positive advance width.");
    }

    [TestMethod]
    public void LoadFromJson_LetterAHasValidUVRect()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        var glyphA = descriptor.Glyphs.FirstOrDefault(g => g.CharacterCode == 'A');
        Assert.AreEqual('A', glyphA.CharacterCode);

        // UV rect should be normalized (0–1)
        Assert.IsTrue(glyphA.UVRect.X >= 0f && glyphA.UVRect.X <= 1f, "u_min out of range");
        Assert.IsTrue(glyphA.UVRect.Y >= 0f && glyphA.UVRect.Y <= 1f, "v_min out of range");
        Assert.IsTrue(glyphA.UVRect.Z >= 0f && glyphA.UVRect.Z <= 1f, "u_max out of range");
        Assert.IsTrue(glyphA.UVRect.W >= 0f && glyphA.UVRect.W <= 1f, "v_max out of range");

        // u_min < u_max (left to right)
        Assert.IsTrue(glyphA.UVRect.X < glyphA.UVRect.Z, "u_min should be less than u_max");
        // v_min > v_max is expected: vMin maps to quad bottom (glyph bottom = large V),
        // vMax maps to quad top (glyph top = small V), so the glyph renders right-side-up
        Assert.IsTrue(
            glyphA.UVRect.Y > glyphA.UVRect.W,
            "v_min should be greater than v_max (flipped for correct orientation)"
        );
    }

    [TestMethod]
    public void LoadFromJson_AllGlyphsHavePositiveAdvanceWidth()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        foreach (var glyph in descriptor.Glyphs)
        {
            Assert.IsTrue(
                glyph.AdvanceWidth > 0,
                $"Glyph U+{(int)glyph.CharacterCode:X4} ('{glyph.CharacterCode}') has non-positive advance width: {glyph.AdvanceWidth}"
            );
        }
    }

    [TestMethod]
    public void LoadFromJson_VisibleGlyphsHavePositiveDimensions()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        foreach (var glyph in descriptor.Glyphs)
        {
            if (glyph.CharacterCode == ' ')
                continue; // Space has no visual bounds

            Assert.IsTrue(
                glyph.GlyphWidth > 0,
                $"Glyph '{glyph.CharacterCode}' has non-positive width: {glyph.GlyphWidth}"
            );
            Assert.IsTrue(
                glyph.GlyphHeight > 0,
                $"Glyph '{glyph.CharacterCode}' has non-positive height: {glyph.GlyphHeight}"
            );
        }
    }

    [TestMethod]
    public void LoadFromJson_UnicodeToCharacterCodeMapping()
    {
        var descriptor = SDFFontAtlasLoader.LoadFromEmbeddedResource("Assets.sans-regular.json");

        // Verify specific character mappings
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == '0'), "Missing digit '0'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == '9'), "Missing digit '9'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == 'a'), "Missing lowercase 'a'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == 'z'), "Missing lowercase 'z'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == 'Z'), "Missing uppercase 'Z'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == '!'), "Missing '!'");
        Assert.IsTrue(descriptor.Glyphs.Any(g => g.CharacterCode == '~'), "Missing '~'");
    }

    // -----------------------------------------------------------------------
    // SDFFontAtlas
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SDFFontAtlas_GetGlyph_ReturnsCorrectMetrics()
    {
        var atlas = CreateTestAtlas();

        var glyphA = atlas.GetGlyph('A');
        Assert.AreEqual('A', glyphA.CharacterCode);
        Assert.AreEqual(0.67f, glyphA.AdvanceWidth, 0.001f);
    }

    [TestMethod]
    public void SDFFontAtlas_GetGlyph_UnknownCharacter_ReturnsFallback()
    {
        var atlas = CreateTestAtlas();

        var fallback = atlas.GetGlyph('Z'); // Not in test descriptor
        // Fallback is the first glyph ('A')
        Assert.AreEqual('A', fallback.CharacterCode);
    }

    [TestMethod]
    public void SDFFontAtlas_StoresTextureAndSamplerIndices()
    {
        var atlas = CreateTestAtlas(textureIndex: 42, samplerIndex: 7);

        Assert.AreEqual(42u, atlas.TextureIndex);
        Assert.AreEqual(7u, atlas.SamplerIndex);
    }

    [TestMethod]
    public void SDFFontAtlas_StoresAtlasMetadata()
    {
        var atlas = CreateTestAtlas();

        Assert.AreEqual(224, atlas.TextureWidth);
        Assert.AreEqual(224, atlas.TextureHeight);
        Assert.AreEqual(4.0f, atlas.SDFSpread, 0.001f);
        Assert.AreEqual(1.252f, atlas.LineHeight, 0.001f);
    }

    [TestMethod]
    public void SDFFontAtlas_EmptyDescriptor_FallbackIsZeroSizePlaceholder()
    {
        var emptyDescriptor = new SDFFontAtlasDescriptor
        {
            TextureWidth = 64,
            TextureHeight = 64,
            SDFSpread = 2.0f,
            LineHeight = 1.0f,
            Glyphs = [],
        };
        var atlas = new SDFFontAtlas(0, 0, emptyDescriptor);

        var fallback = atlas.GetGlyph('X');
        Assert.AreEqual('\0', fallback.CharacterCode);
        Assert.AreEqual(0f, fallback.AdvanceWidth);
        Assert.AreEqual(Vector4.Zero, fallback.UVRect);
    }

    // -----------------------------------------------------------------------
    // TextLayoutHelper
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TextLayoutHelper_EmptyString_ReturnsEmptyList()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout("", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_NullString_ReturnsEmptyList()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout(null!, atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_SingleCharacter_ReturnsOneDescriptor()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout("A", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), result[0].UVRect);
        Assert.AreEqual(1u, result[0].TextureIndex);
        Assert.AreEqual(0u, result[0].SamplerIndex);
    }

    [TestMethod]
    public void TextLayoutHelper_GlyphCountMatchesNonNewlineCharacters()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout("AB A", atlas, 1.0f, Vector3.Zero);

        // 'A', 'B', ' ', 'A' = 4 characters, 4 descriptors
        Assert.AreEqual(4, result.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_HorizontalAdvanceIsIncreasing()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout("ABA", atlas, 1.0f, Vector3.Zero);

        for (int i = 1; i < result.Count; i++)
        {
            Assert.IsTrue(
                result[i].WorldPosition.X > result[i - 1].WorldPosition.X,
                $"Glyph {i} X ({result[i].WorldPosition.X}) should be greater than glyph {i - 1} X ({result[i - 1].WorldPosition.X})"
            );
        }
    }

    [TestMethod]
    public void TextLayoutHelper_MultiLine_NewlineAdvancesVertically()
    {
        var atlas = CreateTestAtlas();
        float fontSize = 2.0f;
        var result = TextLayoutHelper.Layout("A\nB", atlas, fontSize, Vector3.Zero);

        Assert.AreEqual(2, result.Count);

        float line1Y = result[0].WorldPosition.Y;
        float line2Y = result[1].WorldPosition.Y;
        Assert.IsTrue(line2Y < line1Y, $"Line 2 Y ({line2Y}) should be below line 1 Y ({line1Y})");

        // The Y gap between lines should be approximately fontSize
        // (exact values depend on bearing and glyph height centering)
        float yDelta = line1Y - line2Y;
        Assert.IsTrue(
            yDelta > fontSize * 0.5f && yDelta < fontSize * 2f,
            $"Y delta ({yDelta}) should be roughly fontSize ({fontSize})"
        );
    }

    [TestMethod]
    public void TextLayoutHelper_MultiLine_XResetsToOrigin()
    {
        var atlas = CreateTestAtlas();
        var origin = new Vector3(5.0f, 0f, 0f);
        var result = TextLayoutHelper.Layout("A\nB", atlas, 1.0f, origin);

        Assert.AreEqual(2, result.Count);

        // After newline, X should reset near origin (with bearing + centering offset)
        float xDelta = MathF.Abs(result[1].WorldPosition.X - origin.X);
        Assert.IsTrue(
            xDelta < 1.0f,
            $"After newline, X ({result[1].WorldPosition.X}) should be near origin X ({origin.X}), delta={xDelta}"
        );
    }

    [TestMethod]
    public void TextLayoutHelper_FontSizeScalesDimensions()
    {
        var atlas = CreateTestAtlas();
        float fontSize = 2.0f;
        var result = TextLayoutHelper.Layout("A", atlas, fontSize, Vector3.Zero);

        float scale = fontSize / atlas.LineHeight;
        float expectedWidth = 0.781f * scale;
        float expectedHeight = 0.875f * scale;

        Assert.AreEqual(expectedWidth, result[0].Width, 0.001f);
        Assert.AreEqual(expectedHeight, result[0].Height, 0.001f);
    }

    [TestMethod]
    public void TextLayoutHelper_PropagatesTextureIndicesFromAtlas()
    {
        var atlas = CreateTestAtlas(textureIndex: 99, samplerIndex: 3);
        var result = TextLayoutHelper.Layout("A", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(99u, result[0].TextureIndex);
        Assert.AreEqual(3u, result[0].SamplerIndex);
    }

    [TestMethod]
    public void TextLayoutHelper_OnlyNewlines_ReturnsEmptyList()
    {
        var atlas = CreateTestAtlas();
        var result = TextLayoutHelper.Layout("\n\n\n", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(0, result.Count, "Newlines produce no visible glyphs.");
    }

    // -----------------------------------------------------------------------
    // TextLayoutHelper — LayoutGeometry
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_EmptyString_ReturnsEmptyGeometry()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry("", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(0, geo.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_NullString_ReturnsEmptyGeometry()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry(null!, atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(0, geo.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_SingleCharacter_ReturnsOneEntry()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry("A", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(1, geo.Count);
        Assert.AreEqual(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), geo.UVRects[0]);
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_GlyphCountMatchesNonNewlineCharacters()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry("AB A", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(4, geo.Count);
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_MatchesLayoutDescriptors()
    {
        var atlas = CreateTestAtlas();
        float fontSize = 1.5f;
        var origin = new Vector3(1, 2, 3);

        var descriptors = TextLayoutHelper.Layout("ABA", atlas, fontSize, origin);
        using var geo = TextLayoutHelper.LayoutGeometry("ABA", atlas, fontSize, origin);

        Assert.AreEqual(descriptors.Count, geo.Count);
        for (int i = 0; i < descriptors.Count; i++)
        {
            var desc = descriptors[i];
            Assert.AreEqual(
                desc.WorldPosition.X,
                geo.Positions[i].X,
                0.001f,
                $"Position X mismatch at {i}"
            );
            Assert.AreEqual(
                desc.WorldPosition.Y,
                geo.Positions[i].Y,
                0.001f,
                $"Position Y mismatch at {i}"
            );
            Assert.AreEqual(
                desc.WorldPosition.Z,
                geo.Positions[i].Z,
                0.001f,
                $"Position Z mismatch at {i}"
            );
            Assert.AreEqual(1f, geo.Positions[i].W, 0.001f, $"Position W should be 1 at {i}");
            Assert.AreEqual(desc.Width, geo.Sizes[i].X, 0.001f, $"Width mismatch at {i}");
            Assert.AreEqual(desc.Height, geo.Sizes[i].Y, 0.001f, $"Height mismatch at {i}");
            Assert.AreEqual(desc.UVRect, geo.UVRects[i], $"UVRect mismatch at {i}");
        }
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_ColorsAreZero_WhenNoColorProvided()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry("AB", atlas, 1.0f, Vector3.Zero);

        // When no per-billboard color is provided, color alpha should be 0
        // (signals the compute shader to use the uniform color from push constants)
        for (int i = 0; i < geo.Count; i++)
        {
            Assert.AreEqual(
                0f,
                geo.Vertices[i].Color.W,
                $"Vertex {i} color alpha should be 0 when no per-billboard color is provided."
            );
        }
    }

    [TestMethod]
    public void TextLayoutHelper_LayoutGeometry_IsDynamic_Propagated()
    {
        var atlas = CreateTestAtlas();
        using var geo = TextLayoutHelper.LayoutGeometry(
            "A",
            atlas,
            1.0f,
            Vector3.Zero,
            isDynamic: true
        );

        Assert.IsTrue(geo.IsDynamic);
    }

    // -----------------------------------------------------------------------
    // BillboardGeometry
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BillboardGeometry_Add_WithColor_IncreasesCount()
    {
        using var geo = new BillboardGeometry();
        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One, Vector4.One);

        Assert.AreEqual(1, geo.Count);
        Assert.AreEqual(1, geo.Vertices.Count);
        Assert.AreEqual(Vector4.One, geo.Vertices[0].Color);
    }

    [TestMethod]
    public void BillboardGeometry_Add_WithoutColor_SetsZeroAlpha()
    {
        using var geo = new BillboardGeometry();
        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One);

        Assert.AreEqual(1, geo.Count);
        // Color alpha should be 0 (signals "use uniform color")
        Assert.AreEqual(0f, geo.Vertices[0].Color.W);
    }

    [TestMethod]
    public void BillboardGeometry_Clear_ResetsVertices()
    {
        using var geo = new BillboardGeometry();
        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One, Vector4.One);
        geo.Add(Vector3.One, 2f, 2f, Vector4.Zero, Vector4.Zero);

        Assert.AreEqual(2, geo.Count);

        geo.Clear();

        Assert.AreEqual(0, geo.Count);
        Assert.AreEqual(0, geo.Vertices.Count);
    }

    [TestMethod]
    public void BillboardGeometry_DirtyFlags_SetOnAdd()
    {
        using var geo = new BillboardGeometry();
        geo.BufferDirty = false;

        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One);

        Assert.IsTrue(geo.BufferDirty);
    }

    [TestMethod]
    public void BillboardGeometry_DirtyFlags_SetOnClear()
    {
        using var geo = new BillboardGeometry();
        geo.BufferDirty = false;

        geo.Clear();

        Assert.IsTrue(geo.BufferDirty);
    }

    [TestMethod]
    public void BillboardGeometry_MarkDirty_SetsDirtyFlag()
    {
        using var geo = new BillboardGeometry();
        geo.BufferDirty = false;

        geo.MarkDirty();

        Assert.IsTrue(geo.BufferDirty);
    }

    [TestMethod]
    public void BillboardGeometry_StoresCorrectData()
    {
        using var geo = new BillboardGeometry();
        var pos = new Vector3(1, 2, 3);
        float w = 4f;
        float h = 5f;
        var uv = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
        var col = new Vector4(0.5f, 0.6f, 0.7f, 0.8f);

        geo.Add(pos, w, h, uv, col);

        var v = geo.Vertices[0];
        Assert.AreEqual(new Vector4(pos, 1f), v.Position);
        Assert.AreEqual(new Vector2(w, h), v.Size);
        Assert.AreEqual(uv, v.UvRect);
        Assert.AreEqual(col, v.Color);
    }

    [TestMethod]
    public void BillboardGeometry_IsDynamic_DefaultFalse()
    {
        using var geo = new BillboardGeometry();
        Assert.IsFalse(geo.IsDynamic);
    }

    [TestMethod]
    public void BillboardGeometry_IsDynamic_CanBeSetTrue()
    {
        using var geo = new BillboardGeometry(isDynamic: true);
        Assert.IsTrue(geo.IsDynamic);
    }

    [TestMethod]
    public void BillboardGeometry_BufferIsNull_BeforeUpdateBuffers()
    {
        using var geo = new BillboardGeometry();
        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One);

        // Before UpdateBuffers, GPU buffer should be null/empty
        Assert.AreEqual(BufferResource.Null, geo.VertexBuffer);
    }

    // -----------------------------------------------------------------------
    // SDFFontMaterialConfig
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SDFFontMaterialConfig_DefaultValues()
    {
        var config = new SDFFontMaterialConfig();

        Assert.AreEqual(0f, config.OutlineWidth);
        Assert.AreEqual(0.5f, config.EdgeThreshold);
        Assert.AreEqual(0f, config.ShadowSoftness);
        Assert.AreEqual(Vector2.Zero, config.ShadowOffset);
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_BasicFill_ContainsSmoothstep()
    {
        var config = new SDFFontMaterialConfig();
        string glsl = SDFFontMaterialConfig.GenerateGlsl(config);

        Assert.IsTrue(glsl.Contains("smoothstep"), "Basic fill GLSL should contain smoothstep");
        Assert.IsTrue(glsl.Contains("fwidth"), "Basic fill GLSL should contain fwidth");
        Assert.IsTrue(glsl.Contains("0.5"), "Basic fill GLSL should contain edge threshold 0.5");
        Assert.IsFalse(glsl.Contains("outlineWidth"), "Basic fill should not contain outline code");
        Assert.IsFalse(glsl.Contains("shadowOffset"), "Basic fill should not contain shadow code");
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_OutlineOnly_ContainsOutlineCode()
    {
        var config = new SDFFontMaterialConfig { OutlineWidth = 0.1f };
        string glsl = SDFFontMaterialConfig.GenerateGlsl(config);

        Assert.IsTrue(glsl.Contains("outlineWidth"), "Outline GLSL should contain outlineWidth");
        Assert.IsTrue(glsl.Contains("outlineColor"), "Outline GLSL should contain outlineColor");
        Assert.IsTrue(glsl.Contains("outlineOuter"), "Outline GLSL should contain outlineOuter");
        Assert.IsFalse(
            glsl.Contains("shadowOffset"),
            "Outline-only should not contain shadow code"
        );
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_ShadowOnly_ContainsShadowCode()
    {
        var config = new SDFFontMaterialConfig { ShadowOffset = new Vector2(0.01f, -0.01f) };
        string glsl = SDFFontMaterialConfig.GenerateGlsl(config);

        Assert.IsTrue(glsl.Contains("shadowOffset"), "Shadow GLSL should contain shadowOffset");
        Assert.IsTrue(glsl.Contains("shadowColor"), "Shadow GLSL should contain shadowColor");
        Assert.IsFalse(
            glsl.Contains("outlineWidth"),
            "Shadow-only should not contain outline code"
        );
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_OutlineAndShadow_ContainsBoth()
    {
        var config = new SDFFontMaterialConfig
        {
            OutlineWidth = 0.1f,
            ShadowOffset = new Vector2(0.01f, -0.01f),
        };
        string glsl = SDFFontMaterialConfig.GenerateGlsl(config);

        Assert.IsTrue(glsl.Contains("outlineWidth"), "Combined GLSL should contain outlineWidth");
        Assert.IsTrue(glsl.Contains("shadowOffset"), "Combined GLSL should contain shadowOffset");
        Assert.IsTrue(glsl.Contains("shadowColor"), "Combined GLSL should contain shadowColor");
        Assert.IsTrue(glsl.Contains("outlineColor"), "Combined GLSL should contain outlineColor");
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_CustomEdgeThreshold()
    {
        var config = new SDFFontMaterialConfig { EdgeThreshold = 0.45f };
        string glsl = SDFFontMaterialConfig.GenerateGlsl(config);

        Assert.IsTrue(glsl.Contains("0.45"), "GLSL should contain custom edge threshold 0.45");
    }

    [TestMethod]
    public void SDFFontMaterialConfig_GenerateGlsl_ReturnsNonEmptyString()
    {
        var configs = new[]
        {
            new SDFFontMaterialConfig(),
            new SDFFontMaterialConfig { OutlineWidth = 0.1f },
            new SDFFontMaterialConfig { ShadowOffset = new Vector2(0.01f, -0.01f) },
            new SDFFontMaterialConfig
            {
                OutlineWidth = 0.1f,
                ShadowOffset = new Vector2(0.01f, -0.01f),
            },
        };

        foreach (var config in configs)
        {
            string glsl = SDFFontMaterialConfig.GenerateGlsl(config);
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(glsl),
                "Generated GLSL should not be empty or whitespace."
            );
        }
    }

    // -----------------------------------------------------------------------
    // SDFFontAtlasLoader — LoadBuiltInAtlas
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LoadBuiltInAtlas_CreatesValidAtlas()
    {
        var atlas = SDFFontAtlasLoader.LoadBuiltInAtlas(textureIndex: 5, samplerIndex: 2);

        Assert.AreEqual(5u, atlas.TextureIndex);
        Assert.AreEqual(2u, atlas.SamplerIndex);
        Assert.IsTrue(atlas.TextureWidth > 0);
        Assert.IsTrue(atlas.TextureHeight > 0);

        // Should be able to look up common characters
        var glyphA = atlas.GetGlyph('A');
        Assert.AreEqual('A', glyphA.CharacterCode);
        Assert.IsTrue(glyphA.AdvanceWidth > 0);
    }

    [TestMethod]
    public void LoadBuiltInAtlas_LayoutProducesValidDescriptors()
    {
        var atlas = SDFFontAtlasLoader.LoadBuiltInAtlas(textureIndex: 1, samplerIndex: 0);
        var result = TextLayoutHelper.Layout("Hello", atlas, 1.0f, Vector3.Zero);

        Assert.AreEqual(5, result.Count, "Expected 5 glyph descriptors for 'Hello'");

        foreach (var desc in result)
        {
            Assert.AreEqual(1u, desc.TextureIndex);
            Assert.AreEqual(0u, desc.SamplerIndex);
            Assert.IsTrue(desc.Width > 0, "Glyph width should be positive");
            Assert.IsTrue(desc.Height > 0, "Glyph height should be positive");
        }
    }

    // -----------------------------------------------------------------------
    // BillboardComponent integration
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BillboardComponent_DefaultValues()
    {
        var comp = new Components.BillboardComponent();

        Assert.AreEqual(new Color4(1f, 1f, 1f, 1f), comp.Color);
        Assert.IsFalse(comp.FixedSize);
        Assert.IsFalse(comp.AxisConstrained);
        Assert.AreEqual(new Vector3(0, 1, 0), comp.ConstraintAxis);
        Assert.IsTrue(comp.Hitable);
        Assert.AreEqual(0u, comp.TextureIndex);
        Assert.AreEqual(0u, comp.SamplerIndex);
        Assert.IsNull(comp.BillboardMaterialName);
        Assert.IsNull(comp.BillboardGeometry);
        Assert.AreEqual(0, comp.BillboardCount);
        Assert.IsFalse(comp.Valid);
    }

    [TestMethod]
    public void BillboardComponent_WithBillboardGeometry_ReportsCorrectCount()
    {
        using var geo = new BillboardGeometry();
        geo.Add(Vector3.Zero, 1f, 1f, Vector4.One);
        geo.Add(Vector3.One, 2f, 2f, Vector4.Zero);

        var comp = new Components.BillboardComponent { BillboardGeometry = geo };

        Assert.AreEqual(2, comp.BillboardCount);
        Assert.IsTrue(comp.Valid);
    }

    [TestMethod]
    public void BillboardComponent_WithEmptyBillboardGeometry_IsNotValid()
    {
        using var geo = new BillboardGeometry();

        var comp = new Components.BillboardComponent { BillboardGeometry = geo };

        Assert.AreEqual(0, comp.BillboardCount);
        Assert.IsFalse(comp.Valid);
    }
}
