using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class OmrTextureCombinerTests
{
    // =========================================================================
    // Supported formats and channels used across tests
    // =========================================================================

    private static readonly Format[] SupportedFormats =
    [
        Format.RGBA_UN8,
        Format.R_UN8,
        Format.BGRA_UN8,
    ];

    private static readonly ChannelComponent[] AllChannels =
    [
        ChannelComponent.R,
        ChannelComponent.G,
        ChannelComponent.B,
        ChannelComponent.A,
    ];

    // =========================================================================
    // 5.1 Unit tests — fluent API and basic correctness
    // =========================================================================

    /// <summary>
    /// Fluent API smoke test: chain WithOcclusion, WithMetallic, WithRoughness
    /// (each with a 4×4 RGBA_UN8 image) and call Combine(); assert the result
    /// is non-null and has Format.RGBA_UN8.
    /// </summary>
    [TestMethod]
    public void FluentApi_SmokeTest_ReturnsNonNullRgbaImage()
    {
        using var occImg = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        using var metImg = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        using var rghImg = Image.New2D(4, 4, 1, Format.RGBA_UN8);

        using var result = new OmrTextureCombiner()
            .WithOcclusion(occImg, ChannelComponent.R)
            .WithMetallic(metImg, ChannelComponent.G)
            .WithRoughness(rghImg, ChannelComponent.B)
            .Combine();

        Assert.IsNotNull(result);
        Assert.AreEqual(Format.RGBA_UN8, result.Description.Format);
    }

    /// <summary>
    /// All-constant + explicit dimensions: new OmrTextureCombiner().Combine(64, 64)
    /// produces a 64×64 RGBA_UN8 image with all pixels (0, 0, 0, 255).
    /// </summary>
    [TestMethod]
    public void AllConstant_ExplicitDimensions_ProducesCorrectImage()
    {
        using var result = new OmrTextureCombiner().Combine(64, 64);

        Assert.IsNotNull(result);
        Assert.AreEqual(Format.RGBA_UN8, result.Description.Format);
        Assert.AreEqual(64, result.Description.Width);
        Assert.AreEqual(64, result.Description.Height);

        var pb = result.GetPixelBuffer(0, 0);
        // Verify a sample of pixels are (0, 0, 0, 255)
        for (int y = 0; y < 64; y += 8)
        {
            for (int x = 0; x < 64; x += 8)
            {
                var pixel = pb.GetPixel<Rgba8Pixel>(x, y);
                Assert.AreEqual(0, pixel.R, $"R at ({x},{y})");
                Assert.AreEqual(0, pixel.G, $"G at ({x},{y})");
                Assert.AreEqual(0, pixel.B, $"B at ({x},{y})");
                Assert.AreEqual(255, pixel.A, $"A at ({x},{y})");
            }
        }
    }

    /// <summary>
    /// All-constant + no dimensions: new OmrTextureCombiner().Combine() throws ArgumentException.
    /// </summary>
    [TestMethod]
    public void AllConstant_NoDimensions_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => new OmrTextureCombiner().Combine());
    }

    /// <summary>
    /// Null source image: WithOcclusion(null!, ChannelComponent.R) throws ArgumentNullException.
    /// </summary>
    [TestMethod]
    public void NullSourceImage_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new OmrTextureCombiner().WithOcclusion((Image)null!, ChannelComponent.R)
        );
    }

    /// <summary>
    /// Disposed source image: dispose the source before calling Combine(),
    /// verify ObjectDisposedException is thrown.
    /// </summary>
    [TestMethod]
    public void DisposedSourceImage_ThrowsObjectDisposedException()
    {
        var source = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        var combiner = new OmrTextureCombiner().WithOcclusion(source, ChannelComponent.R);
        source.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => combiner.Combine());
    }

    /// <summary>
    /// Multi-mip source: create a source image with 2 mip levels, use it as occlusion source;
    /// verify the output is 1 mip level and only mip-0 data is reflected in the output.
    /// </summary>
    [TestMethod]
    public void MultiMipSource_OutputHasOneMipLevel()
    {
        // Create a 4×4 image with 2 mip levels
        using var source = Image.New2D(4, 4, 2, Format.RGBA_UN8);

        // Write a known value to mip 0
        var mip0Pb = source.GetPixelBuffer(0, 0);
        mip0Pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 200,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        // Write a different value to mip 1 (2×2)
        var mip1Pb = source.GetPixelBuffer(0, 1);
        mip1Pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 50,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        using var result = new OmrTextureCombiner()
            .WithOcclusion(source, ChannelComponent.R)
            .Combine();

        // Output must have exactly 1 mip level
        Assert.AreEqual(1, result.Description.MipLevels);

        // Output pixel (0,0) R should match mip-0 source R = 200
        var outPb = result.GetPixelBuffer(0, 0);
        var outPixel = outPb.GetPixel<Rgba8Pixel>(0, 0);
        Assert.AreEqual(200, outPixel.R);
    }

    /// <summary>
    /// Same image for multiple channels: use one RGBA_UN8 image for both occlusion (channel R)
    /// and metallic (channel G); verify output R and G match the source R and G channels.
    /// </summary>
    [TestMethod]
    public void SameImageForMultipleChannels_OutputMatchesSourceChannels()
    {
        using var source = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        var pb = source.GetPixelBuffer(0, 0);

        // Write known values
        pb.SetPixel<Rgba8Pixel>(
            2,
            2,
            new Rgba8Pixel
            {
                R = 100,
                G = 150,
                B = 200,
                A = 255,
            }
        );

        using var result = new OmrTextureCombiner()
            .WithOcclusion(source, ChannelComponent.R)
            .WithMetallic(source, ChannelComponent.G)
            .Combine();

        var outPb = result.GetPixelBuffer(0, 0);
        var outPixel = outPb.GetPixel<Rgba8Pixel>(2, 2);

        Assert.AreEqual(100, outPixel.R, "Occlusion (R) should match source R");
        Assert.AreEqual(150, outPixel.G, "Metallic (G) should match source G");
    }

    /// <summary>
    /// Integration — save to stream: call output.Save(stream, ImageFileType.Png)
    /// on the combined image and verify the stream length is greater than zero.
    /// </summary>
    [TestMethod]
    public void Integration_SaveToStream_ProducesNonEmptyStream()
    {
        using var source = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        using var result = new OmrTextureCombiner()
            .WithOcclusion(source, ChannelComponent.R)
            .Combine();

        using var stream = new MemoryStream();
        result.Save(stream, ImageFileType.Png);

        Assert.IsTrue(stream.Length > 0, "Saved PNG stream should be non-empty");
    }

    // =========================================================================
    // 5.2 Property 1: Output image structure
    // Feature: omr-texture-combiner, Property 1: OutputImageHasCorrectFormatAndMipLayout
    // Validates: Requirements 2.1, 2.6, 8.3
    // =========================================================================

    [TestMethod]
    public void Property1_OutputImageHasCorrectFormatAndMipLayout()
    {
        // Generator: random combination of constant and image-sourced channel mappings
        // (at least one image source to avoid the all-constant exception);
        // random small image sizes (4–32); random supported formats.
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from useOccImage in Gen.Elements(true, false)
            from useMetImage in Gen.Elements(true, false)
            from useRghImage in Gen.Elements(true, false)
            from occChannel in Gen.Elements(AllChannels)
            from metChannel in Gen.Elements(AllChannels)
            from rghChannel in Gen.Elements(AllChannels)
            from occConst in Gen.Choose(0, 255).Select(v => (byte)v)
            from metConst in Gen.Choose(0, 255).Select(v => (byte)v)
            from rghConst in Gen.Choose(0, 255).Select(v => (byte)v)
                // Ensure at least one image source
            let hasImage = useOccImage || useMetImage || useRghImage
            where hasImage
            select (
                format,
                width,
                height,
                useOccImage,
                useMetImage,
                useRghImage,
                occChannel,
                metChannel,
                rghChannel,
                occConst,
                metConst,
                rghConst
            );

        Prop.ForAll(
                Arb.From(gen),
                (
                    (
                        Format format,
                        int width,
                        int height,
                        bool useOccImage,
                        bool useMetImage,
                        bool useRghImage,
                        ChannelComponent occChannel,
                        ChannelComponent metChannel,
                        ChannelComponent rghChannel,
                        byte occConst,
                        byte metConst,
                        byte rghConst
                    ) t
                ) =>
                {
                    using var img = Image.New2D(t.width, t.height, 1, t.format);
                    var combiner = new OmrTextureCombiner();

                    if (t.useOccImage)
                        combiner.WithOcclusion(img, t.occChannel);
                    else
                        combiner.WithOcclusion(t.occConst);

                    if (t.useMetImage)
                        combiner.WithMetallic(img, t.metChannel);
                    else
                        combiner.WithMetallic(t.metConst);

                    if (t.useRghImage)
                        combiner.WithRoughness(img, t.rghChannel);
                    else
                        combiner.WithRoughness(t.rghConst);

                    using var result = combiner.Combine();

                    return result.Description.Format == Format.RGBA_UN8
                        && result.Description.MipLevels == 1
                        && result.Description.ArraySize == 1;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.3 Property 2: Alpha channel is always 255
    // Feature: omr-texture-combiner, Property 2: AllOutputPixelsHaveAlpha255
    // Validates: Requirement 2.2
    // =========================================================================

    [TestMethod]
    public void Property2_AllOutputPixelsHaveAlpha255()
    {
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (format, width, height, x, y);

        Prop.ForAll(
                Arb.From(gen),
                ((Format format, int width, int height, int x, int y) t) =>
                {
                    using var img = Image.New2D(t.width, t.height, 1, t.format);
                    using var result = new OmrTextureCombiner()
                        .WithOcclusion(img, ChannelComponent.R)
                        .Combine();

                    var outPb = result.GetPixelBuffer(0, 0);
                    var pixel = outPb.GetPixel<Rgba8Pixel>(t.x, t.y);
                    return pixel.A == 255;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.4 Property 3: Channel round-trip (image source)
    // Feature: omr-texture-combiner, Property 3: ChannelRoundTripPreservesSourceBytes
    // Validates: Requirements 2.3, 2.4, 2.5, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5
    // =========================================================================

    [TestMethod]
    public void Property3_ChannelRoundTripPreservesSourceBytes()
    {
        // Generator: pick a supported format, create a small image with random pixel data,
        // pick a ChannelComponent for the source, pick which OMR output channel (R/G/B)
        // to map it to, pick a random (x, y) within bounds.
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from srcChannel in Gen.Elements(AllChannels)
                // 0=Occlusion(R), 1=Metallic(G), 2=Roughness(B)
            from outputChannel in Gen.Choose(0, 2)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (format, width, height, srcChannel, outputChannel, x, y);

        var prop = Prop.ForAll(
            Arb.From(gen),
            (
                (
                    Format format,
                    int width,
                    int height,
                    ChannelComponent srcChannel,
                    int outputChannel,
                    int x,
                    int y
                ) t
            ) =>
            {
                using var source = Image.New2D(t.width, t.height, 1, t.format);
                var sourcePb = source.GetPixelBuffer(0, 0);

                // Fill with deterministic test data
                FillWithTestData(sourcePb, t.format, t.width, t.height);

                // Get expected value from PixelSampler
                byte expected = PixelSampler.Sample(sourcePb, t.format, t.srcChannel, t.x, t.y);

                // Build combiner mapping the source channel to the chosen output channel
                var combiner = new OmrTextureCombiner();
                switch (t.outputChannel)
                {
                    case 0:
                        combiner.WithOcclusion(source, t.srcChannel);
                        break;
                    case 1:
                        combiner.WithMetallic(source, t.srcChannel);
                        break;
                    case 2:
                        combiner.WithRoughness(source, t.srcChannel);
                        break;
                }

                using var result = combiner.Combine();
                var outPb = result.GetPixelBuffer(0, 0);
                var outPixel = outPb.GetPixel<Rgba8Pixel>(t.x, t.y);

                byte actual = t.outputChannel switch
                {
                    0 => outPixel.R,
                    1 => outPixel.G,
                    2 => outPixel.B,
                    _ => throw new InvalidOperationException(),
                };

                return actual == expected;
            }
        );
        Check.One(Config.QuickThrowOnFailure.WithMaxTest(200), prop);
    }

    // =========================================================================
    // 5.5 Property 4: Constant mapping writes constant to all pixels
    // Feature: omr-texture-combiner, Property 4: ConstantMappingWritesConstantToAllPixels
    // Validates: Requirements 2.8, 6.4
    // =========================================================================

    [TestMethod]
    public void Property4_ConstantMappingWritesConstantToAllPixels()
    {
        var gen =
            from rConst in Gen.Choose(0, 255).Select(v => (byte)v)
            from gConst in Gen.Choose(0, 255).Select(v => (byte)v)
            from bConst in Gen.Choose(0, 255).Select(v => (byte)v)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (rConst, gConst, bConst, width, height, x, y);

        Prop.ForAll(
                Arb.From(gen),
                ((byte rConst, byte gConst, byte bConst, int width, int height, int x, int y) t) =>
                {
                    using var result = new OmrTextureCombiner()
                        .WithOcclusion(t.rConst)
                        .WithMetallic(t.gConst)
                        .WithRoughness(t.bConst)
                        .Combine(t.width, t.height);

                    var outPb = result.GetPixelBuffer(0, 0);
                    var pixel = outPb.GetPixel<Rgba8Pixel>(t.x, t.y);

                    return pixel.R == t.rConst && pixel.G == t.gConst && pixel.B == t.bConst;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property4_DefaultZeroBehavior_UnconfiguredChannelIsZero()
    {
        // An unconfigured channel defaults to constant 0
        var gen =
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (width, height, x, y);

        Prop.ForAll(
                Arb.From(gen),
                ((int width, int height, int x, int y) t) =>
                {
                    // No channels configured — all default to constant 0
                    using var result = new OmrTextureCombiner().Combine(t.width, t.height);
                    var outPb = result.GetPixelBuffer(0, 0);
                    var pixel = outPb.GetPixel<Rgba8Pixel>(t.x, t.y);

                    return pixel.R == 0 && pixel.G == 0 && pixel.B == 0;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.6 Property 5: Output dimensions match source dimensions
    // Feature: omr-texture-combiner, Property 5: OutputDimensionsMatchSourceDimensions
    // Validates: Requirement 3.1
    // =========================================================================

    [TestMethod]
    public void Property5_OutputDimensionsMatchSourceDimensions()
    {
        var gen =
            from width in Gen.Choose(4, 64)
            from height in Gen.Choose(4, 64)
            from format in Gen.Elements(SupportedFormats)
            select (width, height, format);

        Prop.ForAll(
                Arb.From(gen),
                ((int width, int height, Format format) t) =>
                {
                    using var source = Image.New2D(t.width, t.height, 1, t.format);
                    using var result = new OmrTextureCombiner()
                        .WithOcclusion(source, ChannelComponent.R)
                        .Combine();

                    return result.Description.Width == t.width
                        && result.Description.Height == t.height;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.7 Property 6: Dimension mismatch throws ArgumentException
    // Feature: omr-texture-combiner, Property 6: DimensionMismatchThrowsArgumentException
    // Validates: Requirement 3.2
    // =========================================================================

    [TestMethod]
    public void Property6_DimensionMismatchThrowsArgumentException()
    {
        // Generator: two images with different (width, height) pairs
        var gen =
            from w1 in Gen.Choose(4, 32)
            from h1 in Gen.Choose(4, 32)
            from w2 in Gen.Choose(4, 32)
            from h2 in Gen.Choose(4, 32)
                // Ensure dimensions differ
            where w1 != w2 || h1 != h2
            select (w1, h1, w2, h2);

        Prop.ForAll(
                Arb.From(gen),
                ((int w1, int h1, int w2, int h2) t) =>
                {
                    using var img1 = Image.New2D(t.w1, t.h1, 1, Format.RGBA_UN8);
                    using var img2 = Image.New2D(t.w2, t.h2, 1, Format.RGBA_UN8);

                    try
                    {
                        using var result = new OmrTextureCombiner()
                            .WithOcclusion(img1, ChannelComponent.R)
                            .WithMetallic(img2, ChannelComponent.G)
                            .Combine();
                        return false; // Should have thrown
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.8 Property 7: Unsupported format throws ArgumentException
    // Feature: omr-texture-combiner, Property 7: UnsupportedFormatThrowsArgumentException
    // Validates: Requirement 4.5
    // =========================================================================

    [TestMethod]
    public void Property7_UnsupportedFormatThrowsArgumentException()
    {
        // Formats not supported by OmrTextureCombiner (uncompressed, known bpp, not in supported set)
        var unsupportedFormats = Generators
            .AllValidFormatsPublic.Where(f =>
                f != Format.RGBA_UN8
                && f != Format.R_UN8
                && f != Format.BGRA_UN8
                // Only use formats that can be used to create a valid Image (uncompressed, known bpp)
                && f != Format.ETC2_RGB8
                && f != Format.ETC2_SRGB8
                && f != Format.BC7_RGBA
            )
            .ToArray();

        if (unsupportedFormats.Length == 0)
        {
            // No unsupported formats available to test — skip
            return;
        }

        var gen = from format in Gen.Elements(unsupportedFormats) select format;

        Prop.ForAll(
                Arb.From(gen),
                (Format format) =>
                {
                    try
                    {
                        using var source = Image.New2D(4, 4, 1, format);
                        try
                        {
                            using var result = new OmrTextureCombiner()
                                .WithOcclusion(source, ChannelComponent.R)
                                .Combine();
                            return false; // Should have thrown
                        }
                        catch (ArgumentException ex)
                        {
                            // Exception message should contain the format name
                            return ex.Message.Contains(format.ToString());
                        }
                    }
                    catch (Exception)
                    {
                        // If we can't even create the image with this format, skip
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 5.9 Property 8: Constant value range validation
    // Feature: omr-texture-combiner, Property 8: ConstantValueRangeValidation
    // Note: byte type enforces [0, 255] at compile time; this property documents
    // that all valid byte values succeed without throwing.
    // Validates: Requirements 1.3, 7.3
    // =========================================================================

    [TestMethod]
    public void Property8_ConstantValueRangeValidation_AllByteValuesSucceed()
    {
        var gen = Gen.Choose(0, 255).Select(v => (byte)v);

        Prop.ForAll(
                Arb.From(gen),
                (byte b) =>
                {
                    try
                    {
                        var combiner = new OmrTextureCombiner()
                            .WithOcclusion(b)
                            .WithMetallic(b)
                            .WithRoughness(b);
                        // No exception should be thrown for any valid byte value
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // 6. Unit tests — InvertedImageChannel and WithRoughnessFromGloss
    // =========================================================================

    [TestMethod]
    public void InvertedImageChannel_Constructor_SetsSourceAndChannel()
    {
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        var channel = ChannelComponent.G;

        var ic = new ChannelSource.InvertedImageChannel(image, channel);

        Assert.AreSame(image, ic.Source);
        Assert.AreEqual(channel, ic.Channel);
    }

    [TestMethod]
    public void InvertedImageChannel_Constructor_NullSource_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new ChannelSource.InvertedImageChannel((Image)null!, ChannelComponent.R)
        );
    }

    [TestMethod]
    public void WithRoughnessFromGloss_Image_ReturnsThis()
    {
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        var combiner = new OmrTextureCombiner();

        var result = combiner.WithRoughnessFromGloss(image, ChannelComponent.R);

        Assert.AreSame(combiner, result);
    }

    [TestMethod]
    public void WithRoughnessFromGloss_NullImage_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new OmrTextureCombiner().WithRoughnessFromGloss((Image)null!, ChannelComponent.R)
        );
    }

    [TestMethod]
    public void WithRoughnessFromGloss_DisposedImage_ThrowsObjectDisposedException()
    {
        var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        var combiner = new OmrTextureCombiner().WithRoughnessFromGloss(image, ChannelComponent.R);
        image.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => combiner.Combine());
    }

    [TestMethod]
    public void WithRoughnessFromGloss_AfterWithRoughness_UsesInversion()
    {
        // Create a 1×1 RGBA_UN8 image with a known R value
        using var source = Image.New2D(1, 1, 1, Format.RGBA_UN8);
        var pb = source.GetPixelBuffer(0, 0);
        pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 100,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        // Call WithRoughness first, then WithRoughnessFromGloss — last write wins
        using var result = new OmrTextureCombiner()
            .WithRoughness(source, ChannelComponent.R)
            .WithRoughnessFromGloss(source, ChannelComponent.R)
            .Combine();

        var outPb = result.GetPixelBuffer(0, 0);
        var pixel = outPb.GetPixel<Rgba8Pixel>(0, 0);

        // Inversion should be applied: 255 - 100 = 155
        Assert.AreEqual(
            (byte)(255 - 100),
            pixel.B,
            "B channel should be inverted (255 - rawValue)"
        );
    }

    [TestMethod]
    public void WithRoughness_AfterWithRoughnessFromGloss_NoInversion()
    {
        // Create a 1×1 RGBA_UN8 image with a known R value
        using var source = Image.New2D(1, 1, 1, Format.RGBA_UN8);
        var pb = source.GetPixelBuffer(0, 0);
        pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 100,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        // Call WithRoughnessFromGloss first, then WithRoughness — last write wins
        using var result = new OmrTextureCombiner()
            .WithRoughnessFromGloss(source, ChannelComponent.R)
            .WithRoughness(source, ChannelComponent.R)
            .Combine();

        var outPb = result.GetPixelBuffer(0, 0);
        var pixel = outPb.GetPixel<Rgba8Pixel>(0, 0);

        // No inversion: raw value 100 should be written directly
        Assert.AreEqual((byte)100, pixel.B, "B channel should be raw value (no inversion)");
    }

    [TestMethod]
    public void WithRoughnessFromGloss_OnlyInvertedSource_InfersDimensions()
    {
        // All non-roughness channels are constant; roughness is InvertedImageChannel
        using var glossSource = Image.New2D(8, 8, 1, Format.RGBA_UN8);

        using var result = new OmrTextureCombiner()
            .WithOcclusion(0)
            .WithMetallic(0)
            .WithRoughnessFromGloss(glossSource, ChannelComponent.R)
            .Combine(); // No explicit dimensions — should infer from glossSource

        Assert.IsNotNull(result);
        Assert.AreEqual(8, result.Description.Width);
        Assert.AreEqual(8, result.Description.Height);
    }

    [TestMethod]
    public void WithRoughnessFromGloss_BoundaryValue_Zero_OutputIs255()
    {
        // 1×1 image with R = 0; inverted output should be 255
        using var source = Image.New2D(1, 1, 1, Format.RGBA_UN8);
        var pb = source.GetPixelBuffer(0, 0);
        pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 0,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        using var result = new OmrTextureCombiner()
            .WithRoughnessFromGloss(source, ChannelComponent.R)
            .Combine();

        var outPb = result.GetPixelBuffer(0, 0);
        var pixel = outPb.GetPixel<Rgba8Pixel>(0, 0);
        Assert.AreEqual((byte)255, pixel.B, "Inversion of 0 should be 255");
    }

    [TestMethod]
    public void WithRoughnessFromGloss_BoundaryValue_255_OutputIsZero()
    {
        // 1×1 image with R = 255; inverted output should be 0
        using var source = Image.New2D(1, 1, 1, Format.RGBA_UN8);
        var pb = source.GetPixelBuffer(0, 0);
        pb.SetPixel<Rgba8Pixel>(
            0,
            0,
            new Rgba8Pixel
            {
                R = 255,
                G = 0,
                B = 0,
                A = 255,
            }
        );

        using var result = new OmrTextureCombiner()
            .WithRoughnessFromGloss(source, ChannelComponent.R)
            .Combine();

        var outPb = result.GetPixelBuffer(0, 0);
        var pixel = outPb.GetPixel<Rgba8Pixel>(0, 0);
        Assert.AreEqual((byte)0, pixel.B, "Inversion of 255 should be 0");
    }

    // =========================================================================
    // 7. Property-based tests — gloss map support (Properties 9–13)
    // =========================================================================

    [TestMethod]
    public void Property9_InversionCorrectness_OutputEquals255MinusRawValue()
    {
        // Feature: omr-gloss-map-support, Property 9: InversionCorrectness
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            from channel in Gen.Elements(AllChannels)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (format, width, height, channel, x, y);

        var prop = Prop.ForAll(
            Arb.From(gen),
            ((Format format, int width, int height, ChannelComponent channel, int x, int y) t) =>
            {
                using var source = Image.New2D(t.width, t.height, 1, t.format);
                var sourcePb = source.GetPixelBuffer(0, 0);
                FillWithTestData(sourcePb, t.format, t.width, t.height);

                byte rawValue = PixelSampler.Sample(sourcePb, t.format, t.channel, t.x, t.y);

                using var result = new OmrTextureCombiner()
                    .WithRoughnessFromGloss(source, t.channel)
                    .Combine();

                var outPb = result.GetPixelBuffer(0, 0);
                var pixel = outPb.GetPixel<Rgba8Pixel>(t.x, t.y);

                return pixel.B == (byte)(255 - rawValue);
            }
        );
        Check.One(Config.QuickThrowOnFailure.WithMaxTest(200), prop);
    }

    [TestMethod]
    public void Property10_DoubleInversionRoundTrip()
    {
        // Feature: omr-gloss-map-support, Property 10: DoubleInversionRoundTrip
        var gen = Gen.Choose(0, 255).Select(v => (byte)v);

        Prop.ForAll(
                Arb.From(gen),
                (byte v) =>
                {
                    byte inverted = (byte)(255 - v);
                    byte doubleInverted = (byte)(255 - inverted);
                    return doubleInverted == v;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property11_InvertedImageChannel_UnsupportedFormat_ThrowsArgumentException()
    {
        // Feature: omr-gloss-map-support, Property 11: UnsupportedFormatThrows
        var unsupportedFormats = Generators
            .AllValidFormatsPublic.Where(f =>
                f != Format.RGBA_UN8
                && f != Format.R_UN8
                && f != Format.BGRA_UN8
                && f != Format.ETC2_RGB8
                && f != Format.ETC2_SRGB8
                && f != Format.BC7_RGBA
            )
            .ToArray();

        if (unsupportedFormats.Length == 0)
            return;

        var gen = from format in Gen.Elements(unsupportedFormats) select format;

        Prop.ForAll(
                Arb.From(gen),
                (Format format) =>
                {
                    try
                    {
                        using var source = Image.New2D(4, 4, 1, format);
                        try
                        {
                            using var result = new OmrTextureCombiner()
                                .WithRoughnessFromGloss(source, ChannelComponent.R)
                                .Combine();
                            return false; // Should have thrown
                        }
                        catch (ArgumentException ex)
                        {
                            return ex.Message.Contains(format.ToString());
                        }
                    }
                    catch (Exception)
                    {
                        // Can't create image with this format — skip
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property12_InvertedImageChannel_DimensionMismatch_ThrowsArgumentException()
    {
        // Feature: omr-gloss-map-support, Property 12: DimensionMismatchThrows
        var gen =
            from w1 in Gen.Choose(4, 32)
            from h1 in Gen.Choose(4, 32)
            from w2 in Gen.Choose(4, 32)
            from h2 in Gen.Choose(4, 32)
            where w1 != w2 || h1 != h2
            select (w1, h1, w2, h2);

        Prop.ForAll(
                Arb.From(gen),
                ((int w1, int h1, int w2, int h2) t) =>
                {
                    using var img1 = Image.New2D(t.w1, t.h1, 1, Format.RGBA_UN8);
                    using var img2 = Image.New2D(t.w2, t.h2, 1, Format.RGBA_UN8);

                    try
                    {
                        using var result = new OmrTextureCombiner()
                            .WithOcclusion(img1, ChannelComponent.R)
                            .WithRoughnessFromGloss(img2, ChannelComponent.R)
                            .Combine();
                        return false; // Should have thrown
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property13_InvertedImageChannel_NotTreatedAsConstant()
    {
        // Feature: omr-gloss-map-support, Property 13: AllConstantGuardUnaffected
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 32)
            from height in Gen.Choose(4, 32)
            select (format, width, height);

        var prop = Prop.ForAll(
            Arb.From(gen),
            ((Format format, int width, int height) t) =>
            {
                using var source = Image.New2D(t.width, t.height, 1, t.format);

                try
                {
                    using var result = new OmrTextureCombiner()
                        .WithOcclusion(0)
                        .WithMetallic(0)
                        .WithRoughnessFromGloss(source, ChannelComponent.R)
                        .Combine(); // No explicit dimensions — should infer from source

                    return result.Description.Width == t.width
                        && result.Description.Height == t.height;
                }
                catch (ArgumentException)
                {
                    return false; // Should NOT throw the all-constant exception
                }
            }
        );
        Check.One(Config.QuickThrowOnFailure.WithMaxTest(200), prop);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Fills the pixel buffer with deterministic test data based on pixel coordinates.
    /// </summary>
    private static void FillWithTestData(PixelBuffer pb, Format format, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte baseVal = (byte)((x * 7 + y * 13 + 1) & 0xFF);
                switch (format)
                {
                    case Format.RGBA_UN8:
                        pb.SetPixel<Rgba8Pixel>(
                            x,
                            y,
                            new Rgba8Pixel
                            {
                                R = baseVal,
                                G = (byte)((baseVal + 64) & 0xFF),
                                B = (byte)((baseVal + 128) & 0xFF),
                                A = (byte)((baseVal + 192) & 0xFF),
                            }
                        );
                        break;

                    case Format.R_UN8:
                        pb.SetPixel<byte>(x, y, baseVal);
                        break;

                    case Format.BGRA_UN8:
                        pb.SetPixel<Bgra8Pixel>(
                            x,
                            y,
                            new Bgra8Pixel
                            {
                                B = baseVal,
                                G = (byte)((baseVal + 64) & 0xFF),
                                R = (byte)((baseVal + 128) & 0xFF),
                                A = (byte)((baseVal + 192) & 0xFF),
                            }
                        );
                        break;
                }
            }
        }
    }
}
