using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Integration tests for ImageSharpDecoder — round-trip load/save for each supported format.
/// </summary>
[TestClass]
public class ImageSharpDecoderTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Creates a small 4x4 RGBA_UN8 image with a known checkerboard pattern.</summary>
    private static Image CreateTestImage(int width = 4, int height = 4)
    {
        var img = Image.New2D(width, height, 1, Format.RGBA_UN8);
        unsafe
        {
            var pb = img.GetPixelBuffer(0, 0);
            var ptr = (byte*)pb.DataPointer;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool checker = ((x + y) & 1) == 0;
                    int offset = (y * pb.RowStride) + (x * 4);
                    ptr[offset + 0] = checker ? (byte)255 : (byte)0; // R
                    ptr[offset + 1] = checker ? (byte)0 : (byte)255; // G
                    ptr[offset + 2] = (byte)128; // B
                    ptr[offset + 3] = (byte)255; // A
                }
            }
        }
        return img;
    }

    private static void AssertRoundTrip(ImageFileType fileType, bool lossless = true)
    {
        using var original = CreateTestImage();
        using var ms = new MemoryStream();

        original.Save(ms, fileType);
        Assert.IsTrue(ms.Length > 0, $"{fileType}: saved stream should not be empty");

        ms.Position = 0;
        using var loaded = Image.Load(ms);

        Assert.IsNotNull(loaded, $"{fileType}: Load should return a non-null image");
        Assert.AreEqual(
            TextureDimension.Texture2D,
            loaded!.Description.Dimension,
            $"{fileType}: dimension"
        );
        Assert.AreEqual(4, loaded.Description.Width, $"{fileType}: width");
        Assert.AreEqual(4, loaded.Description.Height, $"{fileType}: height");
        Assert.AreEqual(Format.RGBA_UN8, loaded.Description.Format, $"{fileType}: format");

        if (lossless)
        {
            // Verify pixel data is preserved exactly
            var origPb = original.GetPixelBuffer(0, 0);
            var loadedPb = loaded.GetPixelBuffer(0, 0);
            unsafe
            {
                var origPtr = (byte*)origPb.DataPointer;
                var loadedPtr = (byte*)loadedPb.DataPointer;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int offset = y * origPb.RowStride + x * 4;
                        for (int c = 0; c < 4; c++)
                        {
                            Assert.AreEqual(
                                origPtr[offset + c],
                                loadedPtr[offset + c],
                                $"{fileType}: pixel ({x},{y}) channel {c} mismatch"
                            );
                        }
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Round-trip tests per format
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RoundTrip_Png_LosslessPreservesPixels() =>
        AssertRoundTrip(ImageFileType.Png, lossless: true);

    [TestMethod]
    public void RoundTrip_Bmp_LosslessPreservesPixels() =>
        AssertRoundTrip(ImageFileType.Bmp, lossless: true);

    [TestMethod]
    public void RoundTrip_Tiff_LosslessPreservesPixels() =>
        AssertRoundTrip(ImageFileType.Tiff, lossless: true);

    [TestMethod]
    public void RoundTrip_Gif_LosslessPreservesPixels() =>
        AssertRoundTrip(ImageFileType.Gif, lossless: true);

    [TestMethod]
    public void RoundTrip_Tga_LosslessPreservesPixels() =>
        AssertRoundTrip(ImageFileType.Tga, lossless: true);

    [TestMethod]
    public void RoundTrip_Jpg_LoadsWithCorrectDimensions()
        // JPEG is lossy — only check dimensions and format, not exact pixels
        =>
        AssertRoundTrip(ImageFileType.Jpg, lossless: false);

    [TestMethod]
    public void RoundTrip_Webp_LoadsWithCorrectDimensions()
        // WebP — lossless by default in ImageSharp
        =>
        AssertRoundTrip(ImageFileType.Webp, lossless: false);

    // -------------------------------------------------------------------------
    // Load-only: verify ImageSharp decoder is tried when DDS fails
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_PngBytes_ReturnsValidImage()
    {
        // Create a PNG in memory via ImageSharp and load it back through Image.Load
        using var original = CreateTestImage(8, 8);
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
        using var original = CreateTestImage(16, 16);
        using var ms = new MemoryStream();
        original.Save(ms, ImageFileType.Jpg);

        using var loaded = Image.Load(ms.ToArray());
        Assert.IsNotNull(loaded);
        Assert.AreEqual(16, loaded!.Description.Width);
        Assert.AreEqual(16, loaded.Description.Height);
    }

    // -------------------------------------------------------------------------
    // DDS still works after ImageSharp registration
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_DdsStillWorksAfterImageSharpRegistration()
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
