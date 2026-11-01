using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Integration tests for image decoder/encoder classes — round-trip load/save for each supported format.
/// </summary>
[TestClass]
public class ImageDecoderTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AssertRoundTrip(ImageFileType fileType, Format format, bool lossless = true, bool opaque = false)
    {
        var (inputImage, expectedImage) = TestImage.CreateEncodeDecodePair(format: format, opaque: opaque);
        using var input = inputImage;
        using var expected = expectedImage;

        using var ms = new MemoryStream();

        input.Save(ms, fileType);
        Assert.IsTrue(ms.Length > 0, $"{fileType}: saved stream should not be empty");

        ms.Position = 0;
        using var loaded = Image.Load(ms);

        Assert.IsNotNull(loaded, $"{fileType}: Load should return a non-null image");
        Assert.AreEqual(
            TextureDimension.Texture2D,
            loaded!.Description.Dimension,
            $"{fileType}: dimension"
        );
        Assert.AreEqual(expected.Description.Width, loaded.Description.Width, $"{fileType}: width");
        Assert.AreEqual(expected.Description.Height, loaded.Description.Height, $"{fileType}: height");
        Assert.AreEqual(Format.RGBA_UN8, loaded.Description.Format, $"{fileType}: format");

        if (lossless)
        {
            // Verify decoded pixel data matches the expected data exactly
            var expectedPb = expected.GetPixelBuffer(0, 0);
            var loadedPb = loaded.GetPixelBuffer(0, 0);
            var pixelCount = expectedPb.Width * expectedPb.Height;
            unsafe
            {
                var expectedPixels = new ReadOnlySpan<Rgba8Pixel>((void*)expectedPb.DataPointer, pixelCount);
                var loadedPixels = new ReadOnlySpan<Rgba8Pixel>((void*)loadedPb.DataPointer, pixelCount);

                for (var i = 0; i < expectedPixels.Length; i++)
                {
                    var x = i % expectedPb.Width;
                    var y = i / expectedPb.Width;

                    Assert.AreEqual(
                        expectedPixels[i],
                        loadedPixels[i],
                        $"{fileType}: pixel ({x},{y}) mismatch"
                    );
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Round-trip tests per format
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> DefaultFormats =>
    [
        [Format.RGBA_UN8],
        [Format.RGBA_SRGB8],
        [Format.BGRA_UN8],
        [Format.BGRA_SRGB8],
    ];

    public static IEnumerable<object[]> SkiaFormats =>
    [
        [Format.R_UN8],
        [Format.RG_UN8],
        [Format.RG_F16],
        [Format.RGBA_UN8],
        [Format.RGBA_F16],
        [Format.RGBA_F32],
        [Format.RGBA_SRGB8],
        [Format.BGRA_UN8],
        [Format.BGRA_SRGB8],
    ];

    public static IEnumerable<object[]> TiffFormats =>
    [
        [Format.R_UN8],
        [Format.R_UI16],
        [Format.R_UN16],
        [Format.R_F16],
        [Format.RG_UN8],
        [Format.RG_UI16],
        [Format.RG_UN16],
        [Format.RG_F16],
        [Format.RGBA_UN8],
        [Format.RGBA_F16],
        [Format.RGBA_SRGB8],
        [Format.BGRA_UN8],
        [Format.BGRA_SRGB8],
    ];

    [TestMethod]
    [DynamicData(nameof(SkiaFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Png_LosslessPreservesPixels(Format format) =>
        AssertRoundTrip(ImageFileType.Png, format, lossless: true);

    [TestMethod]
    [DynamicData(nameof(DefaultFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Bmp_LosslessPreservesPixels(Format format) =>
        AssertRoundTrip(ImageFileType.Bmp, format, lossless: true);

    [TestMethod]
    [DynamicData(nameof(TiffFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Tiff_LosslessPreservesPixels(Format format) =>
        AssertRoundTrip(ImageFileType.Tiff, format, lossless: true);

    // Gif encode is not currently supported
    //[TestMethod]
    //public void RoundTrip_Gif_LosslessPreservesPixels() =>
    //    AssertRoundTrip(ImageFileType.Gif, lossless: true);

    [TestMethod]
    [DynamicData(nameof(DefaultFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Tga_LosslessPreservesPixels(Format format) =>
        AssertRoundTrip(ImageFileType.Tga, format, lossless: true);

    // JPEG is lossy — only check dimensions and format, not exact pixels
    // The test images are small enough that JPEG may not lose any data, but Skia's
    // JPEG encoder also does not currently preserve the original colors exactly.
    // See https://github.com/mono/SkiaSharp/issues/931
    [TestMethod]
    [DynamicData(nameof(SkiaFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Jpg_LoadsWithCorrectDimensions(Format format) =>
        AssertRoundTrip(ImageFileType.Jpg, format, lossless: false, opaque: true);

    // WebP — SkiaSharp encoder is lossless when quality is set to 100
    [TestMethod]
    [DynamicData(nameof(SkiaFormats), DynamicDataSourceType.Property)]
    public void RoundTrip_Webp_LosslessPreservesPixels(Format format) =>
        AssertRoundTrip(ImageFileType.Webp, format, lossless: true);

    // -------------------------------------------------------------------------
    // Load-only: verify other decoders are tried when DDS fails
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_PngBytes_ReturnsValidImage()
    {
        // Create a PNG in memory and load it back through Image.Load
        using var original = TestImage.Create(8, 8);
        using var ms = new MemoryStream();
        original.Save(ms, ImageFileType.Png);

        using var loaded = Image.Load(ms.ToArray());
        Assert.IsNotNull(loaded);
        Assert.AreEqual(8, loaded!.Description.Width);
        Assert.AreEqual(8, loaded.Description.Height);
        Assert.AreEqual(Format.RGBA_UN8, loaded.Description.Format);
    }

    [TestMethod]
    public void Load_JpgBytes_ReturnsValidImage()
    {
        using var original = TestImage.Create(16, 16);
        using var ms = new MemoryStream();
        original.Save(ms, ImageFileType.Jpg);

        using var loaded = Image.Load(ms.ToArray());
        Assert.IsNotNull(loaded);
        Assert.AreEqual(16, loaded!.Description.Width);
        Assert.AreEqual(16, loaded.Description.Height);
    }

    // -------------------------------------------------------------------------
    // DDS still works after other decoder registration
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_DdsStillWorksAfterImageDecoderRegistration()
    {
        using var original = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        using var ms = new MemoryStream();
        original.Save(ms, ImageFileType.Dds);
        ms.Position = 0;

        using var loaded = Image.Load(ms);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(Format.RGBA_UN8, loaded!.Description.Format);
    }
}
