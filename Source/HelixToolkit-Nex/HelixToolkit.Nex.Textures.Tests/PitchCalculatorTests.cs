using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class PitchCalculatorTests
{
    // -------------------------------------------------------------------------
    // Property 4: CountMips matches logarithmic formula
    // Feature: texture-loading, Property 4: For any positive width and height,
    // CountMips(w,h) == 1 + floor(log2(max(w,h)))
    // Validates: Requirements 3.1, 3.2
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property4_CountMips_MatchesLogarithmicFormula_2D()
    {
        var gen = from w in Gen.Choose(1, 4096) from h in Gen.Choose(1, 4096) select (w, h);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h) pair) =>
                {
                    int expected = 1 + (int)Math.Floor(Math.Log2(Math.Max(pair.w, pair.h)));
                    int actual = PitchCalculator.CountMips(pair.w, pair.h);
                    return actual == expected;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property4_CountMips_MatchesLogarithmicFormula_3D()
    {
        var gen =
            from w in Gen.Choose(1, 4096)
            from h in Gen.Choose(1, 4096)
            from d in Gen.Choose(1, 4096)
            select (w, h, d);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h, int d) t) =>
                {
                    int expected =
                        1 + (int)Math.Floor(Math.Log2(Math.Max(t.w, Math.Max(t.h, t.d))));
                    int actual = PitchCalculator.CountMips(t.w, t.h, t.d);
                    return actual == expected;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 5: CalculateMipSize halves dimensions
    // Feature: texture-loading, Property 5: For any positive dimension and
    // non-negative mipLevel, CalculateMipSize(dim, level) == max(1, dim >> level)
    // Validates: Requirements 3.3
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property5_CalculateMipSize_HalvesDimensions()
    {
        var gen =
            from dim in Gen.Choose(1, 4096)
            from level in Gen.Choose(0, 12)
            select (dim, level);

        Prop.ForAll(
                Arb.From(gen),
                ((int dim, int level) pair) =>
                {
                    int expected = Math.Max(1, pair.dim >> pair.level);
                    int actual = PitchCalculator.CalculateMipSize(pair.dim, pair.level);
                    return actual == expected;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 6: BCn compressed pitch computation
    // Feature: texture-loading, Property 6: For any BCn format and positive
    // width/height, rowPitch = max(1,(w+3)/4)*blockSize and
    // slicePitch = rowPitch*max(1,(h+3)/4)
    // Validates: Requirements 4.1, 4.2
    // -------------------------------------------------------------------------

    private static readonly (Format fmt, int blockSize)[] CompressedFormatsWithBlockSize =
    [
        (Format.ETC2_RGB8, 8),
        (Format.ETC2_SRGB8, 8),
        (Format.BC7_RGBA, 16),
    ];

    [TestMethod]
    public void Property6_CompressedPitch_MatchesBlockFormula()
    {
        var gen =
            from fmtIdx in Gen.Choose(0, CompressedFormatsWithBlockSize.Length - 1)
            from w in Gen.Choose(1, 4096)
            from h in Gen.Choose(1, 4096)
            select (fmtIdx, w, h);

        Prop.ForAll(
                Arb.From(gen),
                ((int fmtIdx, int w, int h) t) =>
                {
                    var (fmt, blockSize) = CompressedFormatsWithBlockSize[t.fmtIdx];
                    int expectedWidthBlocks = Math.Max(1, (t.w + 3) / 4);
                    int expectedHeightBlocks = Math.Max(1, (t.h + 3) / 4);
                    int expectedRowPitch = expectedWidthBlocks * blockSize;
                    int expectedSlicePitch = expectedRowPitch * expectedHeightBlocks;

                    PitchCalculator.ComputePitch(
                        fmt,
                        t.w,
                        t.h,
                        out int rowPitch,
                        out int slicePitch,
                        out _,
                        out _
                    );

                    return rowPitch == expectedRowPitch && slicePitch == expectedSlicePitch;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 7: Uncompressed pitch computation
    // Feature: texture-loading, Property 7: For any uncompressed format and
    // positive width/height, rowPitch = (w*bpp+7)/8 and slicePitch = rowPitch*height
    // Validates: Requirements 4.3, 4.4
    // -------------------------------------------------------------------------

    private static readonly Format[] UncompressedFormats =
    [
        Format.R_UN8,
        Format.R_UI16,
        Format.R_UI32,
        Format.R_UN16,
        Format.R_F16,
        Format.R_F32,
        Format.RG_UN8,
        Format.RG_UI16,
        Format.RG_UI32,
        Format.RG_UN16,
        Format.RG_F16,
        Format.RG_F32,
        Format.RGBA_UN8,
        Format.RGBA_UI32,
        Format.RGBA_F16,
        Format.RGBA_F32,
        Format.RGBA_SRGB8,
        Format.BGRA_UN8,
        Format.BGRA_SRGB8,
        Format.A2B10G10R10_UN,
        Format.A2R10G10B10_UN,
    ];

    [TestMethod]
    public void Property7_UncompressedPitch_MatchesFormula()
    {
        var gen =
            from fmtIdx in Gen.Choose(0, UncompressedFormats.Length - 1)
            from w in Gen.Choose(1, 4096)
            from h in Gen.Choose(1, 4096)
            select (fmtIdx, w, h);

        Prop.ForAll(
                Arb.From(gen),
                ((int fmtIdx, int w, int h) t) =>
                {
                    Format fmt = UncompressedFormats[t.fmtIdx];
                    int bpp = PitchCalculator.GetBitsPerPixel(fmt);
                    int expectedRowPitch = (t.w * bpp + 7) / 8;
                    int expectedSlicePitch = expectedRowPitch * t.h;

                    PitchCalculator.ComputePitch(
                        fmt,
                        t.w,
                        t.h,
                        out int rowPitch,
                        out int slicePitch,
                        out _,
                        out _,
                        PitchFlags.None
                    );

                    return rowPitch == expectedRowPitch && slicePitch == expectedSlicePitch;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 8: DWORD-aligned pitch computation
    // Feature: texture-loading, Property 8: For any uncompressed format and
    // positive width, with LegacyDword flag, rowPitch = ((w*bpp+31)/32)*4
    // Validates: Requirements 4.5
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property8_LegacyDwordPitch_MatchesFormula()
    {
        var gen =
            from fmtIdx in Gen.Choose(0, UncompressedFormats.Length - 1)
            from w in Gen.Choose(1, 4096)
            from h in Gen.Choose(1, 4096)
            select (fmtIdx, w, h);

        Prop.ForAll(
                Arb.From(gen),
                ((int fmtIdx, int w, int h) t) =>
                {
                    Format fmt = UncompressedFormats[t.fmtIdx];
                    int bpp = PitchCalculator.GetBitsPerPixel(fmt);
                    int expectedRowPitch = ((t.w * bpp + 31) / 32) * 4;

                    PitchCalculator.ComputePitch(
                        fmt,
                        t.w,
                        t.h,
                        out int rowPitch,
                        out _,
                        out _,
                        out _,
                        PitchFlags.LegacyDword
                    );

                    return rowPitch == expectedRowPitch;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Unit tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CountMips_1x1_Returns1()
    {
        Assert.AreEqual(1, PitchCalculator.CountMips(1, 1));
    }

    [TestMethod]
    public void CountMips_4x4_Returns3()
    {
        Assert.AreEqual(3, PitchCalculator.CountMips(4, 4));
    }

    [TestMethod]
    public void CalculateMipSize_8_Level3_Returns1()
    {
        Assert.AreEqual(1, PitchCalculator.CalculateMipSize(8, 3));
    }

    [TestMethod]
    public void CalculateMipLevels_3D_NonPow2_WithMultipleMips_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            PitchCalculator.CalculateMipLevels(3, 4, 4, new MipMapCount(2))
        );
    }

    [TestMethod]
    public void CalculateMipLevels_3D_NonPow2_AutoMips_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            PitchCalculator.CalculateMipLevels(3, 4, 4, MipMapCount.Auto)
        );
    }

    [TestMethod]
    public void CalculateMipLevels_2D_Auto_ReturnsFullChain()
    {
        int result = PitchCalculator.CalculateMipLevels(8, 8, MipMapCount.Auto);
        Assert.AreEqual(4, result); // 8x8 → 4x4 → 2x2 → 1x1 = 4 levels
    }

    [TestMethod]
    public void CalculateMipLevels_2D_ExceedsMax_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            PitchCalculator.CalculateMipLevels(4, 4, new MipMapCount(10))
        );
    }

    [TestMethod]
    public void ComputePitch_BC7_1x1_Returns16ByteRowPitch()
    {
        PitchCalculator.ComputePitch(
            Format.BC7_RGBA,
            1,
            1,
            out int rowPitch,
            out int slicePitch,
            out _,
            out _
        );
        Assert.AreEqual(16, rowPitch);
        Assert.AreEqual(16, slicePitch);
    }

    [TestMethod]
    public void ComputePitch_ETC2_4x4_Returns8ByteRowPitch()
    {
        PitchCalculator.ComputePitch(
            Format.ETC2_RGB8,
            4,
            4,
            out int rowPitch,
            out int slicePitch,
            out _,
            out _
        );
        Assert.AreEqual(8, rowPitch);
        Assert.AreEqual(8, slicePitch);
    }

    [TestMethod]
    public void ComputePitch_RGBA_UN8_4x4_Returns16ByteRowPitch()
    {
        // 4 pixels * 32 bpp / 8 = 16 bytes per row
        PitchCalculator.ComputePitch(
            Format.RGBA_UN8,
            4,
            4,
            out int rowPitch,
            out int slicePitch,
            out _,
            out _
        );
        Assert.AreEqual(16, rowPitch);
        Assert.AreEqual(64, slicePitch);
    }

    [TestMethod]
    public void GetBitsPerPixel_RGBA_UN8_Returns32()
    {
        Assert.AreEqual(32, PitchCalculator.GetBitsPerPixel(Format.RGBA_UN8));
    }

    [TestMethod]
    public void GetBitsPerPixel_R_UN8_Returns8()
    {
        Assert.AreEqual(8, PitchCalculator.GetBitsPerPixel(Format.R_UN8));
    }

    [TestMethod]
    public void GetBitsPerPixel_CompressedFormat_Returns0()
    {
        Assert.AreEqual(0, PitchCalculator.GetBitsPerPixel(Format.BC7_RGBA));
        Assert.AreEqual(0, PitchCalculator.GetBitsPerPixel(Format.ETC2_RGB8));
    }

    [TestMethod]
    public void IsCompressed_BC7_ReturnsTrue()
    {
        Assert.IsTrue(PitchCalculator.IsCompressed(Format.BC7_RGBA));
    }

    [TestMethod]
    public void IsCompressed_RGBA_UN8_ReturnsFalse()
    {
        Assert.IsFalse(PitchCalculator.IsCompressed(Format.RGBA_UN8));
    }
}
