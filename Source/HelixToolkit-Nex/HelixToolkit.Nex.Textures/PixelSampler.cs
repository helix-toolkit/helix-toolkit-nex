using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Internal helper that reads a single byte channel value from a pixel buffer,
/// handling per-format byte layout differences.
/// </summary>
internal static class PixelSampler
{
    /// <summary>
    /// Reads the requested channel from pixel (x, y) in the given pixel buffer.
    /// All reads are raw byte reads — no floating-point conversion occurs.
    /// </summary>
    /// <param name="pixelBuffer">The pixel buffer to sample from.</param>
    /// <param name="format">The pixel format of the buffer.</param>
    /// <param name="channel">The color channel to read.</param>
    /// <param name="x">The x coordinate of the pixel.</param>
    /// <param name="y">The y coordinate of the pixel.</param>
    /// <returns>The raw byte value of the requested channel at (x, y).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="format"/> is not a supported source format.
    /// </exception>
    internal static byte Sample(
        PixelBuffer pixelBuffer,
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
                var pixel = pixelBuffer.GetPixel<Rgba8Pixel>(x, y);
                return channel switch
                {
                    ChannelComponent.R => pixel.R,
                    ChannelComponent.G => pixel.G,
                    ChannelComponent.B => pixel.B,
                    ChannelComponent.A => pixel.A,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
                };
            }

            case Format.R_UN8:
            {
                var value = pixelBuffer.GetPixel<byte>(x, y);
                return channel switch
                {
                    ChannelComponent.R => value,
                    ChannelComponent.G => 0,
                    ChannelComponent.B => 0,
                    ChannelComponent.A => 255,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
                };
            }

            case Format.BGRA_UN8:
            {
                var pixel = pixelBuffer.GetPixel<Bgra8Pixel>(x, y);
                return channel switch
                {
                    ChannelComponent.R => pixel.R,
                    ChannelComponent.G => pixel.G,
                    ChannelComponent.B => pixel.B,
                    ChannelComponent.A => pixel.A,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
                };
            }

            default:
                throw new ArgumentException(
                    $"Source image format '{format}' is not supported by OmrTextureCombiner. "
                        + $"Supported formats: {Format.RGBA_UN8}, {Format.R_UN8}, {Format.BGRA_UN8}.",
                    nameof(format)
                );
        }
    }
}

/// <summary>
/// Represents a pixel in RGBA byte order (R at byte offset 0, G at 1, B at 2, A at 3).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Rgba8Pixel
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;
}

/// <summary>
/// Represents a pixel in BGRA byte order (B at byte offset 0, G at 1, R at 2, A at 3).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Bgra8Pixel
{
    public byte B; // byte offset 0
    public byte G; // byte offset 1
    public byte R; // byte offset 2
    public byte A; // byte offset 3
}
