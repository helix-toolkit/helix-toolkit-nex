using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class PixelSamplerTests
{
    // =========================================================================
    // Property 3: ChannelRoundTripPreservesSourceBytes
    // Feature: omr-texture-combiner, Property 3: ChannelRoundTripPreservesSourceBytes
    // For any supported format, any ChannelComponent, and any pixel position (x, y),
    // PixelSampler.Sample returns the same byte that a direct struct read would return.
    // Validates: Requirements 2.3, 2.4, 2.5, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5
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

    [TestMethod]
    public void Property3_ChannelRoundTripPreservesSourceBytes()
    {
        // Generator: pick a supported format, create a small Image (4x4 to 16x16) with
        // random pixel data, pick a random ChannelComponent, and a random (x, y) within bounds.
        var gen =
            from format in Gen.Elements(SupportedFormats)
            from width in Gen.Choose(4, 16)
            from height in Gen.Choose(4, 16)
            from channel in Gen.Elements(AllChannels)
            from x in Gen.Choose(0, width - 1)
            from y in Gen.Choose(0, height - 1)
            select (format, width, height, channel, x, y);

        Prop.ForAll(
                Arb.From(gen),
                (
                    (Format format, int width, int height, ChannelComponent channel, int x, int y) t
                ) =>
                {
                    using var image = Image.New2D(t.width, t.height, 1, t.format);
                    var pb = image.GetPixelBuffer(0, 0);

                    // Fill with random-ish deterministic data based on coordinates
                    FillWithTestData(pb, t.format, t.width, t.height);

                    // Get the oracle value via direct struct read
                    byte expected = GetOracleValue(pb, t.format, t.channel, t.x, t.y);

                    // Get the sampler value
                    byte actual = PixelSampler.Sample(pb, t.format, t.channel, t.x, t.y);

                    return actual == expected;
                }
            )
            .Check(Config.QuickThrowOnFailure.WithMaxTest(200));
    }

    /// <summary>
    /// Fills the pixel buffer with deterministic test data based on pixel coordinates.
    /// </summary>
    private static void FillWithTestData(PixelBuffer pb, Format format, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Use coordinate-based values to ensure distinct, predictable bytes
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

    /// <summary>
    /// Returns the expected byte value for the given channel using direct struct reads as the oracle.
    /// </summary>
    private static byte GetOracleValue(
        PixelBuffer pb,
        Format format,
        ChannelComponent channel,
        int x,
        int y
    )
    {
        switch (format)
        {
            case Format.RGBA_UN8:
            {
                var pixel = pb.GetPixel<Rgba8Pixel>(x, y);
                return channel switch
                {
                    ChannelComponent.R => pixel.R,
                    ChannelComponent.G => pixel.G,
                    ChannelComponent.B => pixel.B,
                    ChannelComponent.A => pixel.A,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel)),
                };
            }

            case Format.R_UN8:
            {
                var value = pb.GetPixel<byte>(x, y);
                return channel switch
                {
                    ChannelComponent.R => value,
                    ChannelComponent.G => 0,
                    ChannelComponent.B => 0,
                    ChannelComponent.A => 255,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel)),
                };
            }

            case Format.BGRA_UN8:
            {
                var pixel = pb.GetPixel<Bgra8Pixel>(x, y);
                return channel switch
                {
                    ChannelComponent.R => pixel.R,
                    ChannelComponent.G => pixel.G,
                    ChannelComponent.B => pixel.B,
                    ChannelComponent.A => pixel.A,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel)),
                };
            }

            default:
                throw new ArgumentException($"Unsupported format: {format}");
        }
    }
}
