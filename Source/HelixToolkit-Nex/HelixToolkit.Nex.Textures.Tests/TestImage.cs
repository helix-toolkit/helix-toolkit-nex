using System.Numerics;
using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Generates images for tests.
/// </summary>
public static class TestImage
{
    /// <summary>
    /// Creates an image with a color gradient in the given pixel format.
    /// </summary>
    /// <remarks>
    /// Supports all non-packed and non-compressed pixel formats.
    /// </remarks>
    /// <param name="width">The width of each image in pixels. Must be >= 2.</param>
    /// <param name="height">The height of each image in pixels. Must be >= 2.</param>
    /// <param name="format">The pixel format of the image.</param>
    /// <param name="opaque">
    /// If true, sets the alpha channel to opaque for every pixel. Set this to true when using a
    /// pixel format that has an alpha channel but encoding to a file format that does not support
    /// transparency such as JPEG.
    /// </param>
    public static Image Create(
        int width = 6,
        int height = 6,
        Format format = Format.RGBA_UN8,
        bool opaque = false
    )
    {
        return format switch
        {
            // Single-channel formats
            Format.R_UN8 => Create(width, height, format, GetColorGray, GetPixel8),
            Format.R_UI16 => Create(width, height, format, GetColorGray, GetPixel16),
            Format.R_UI32 => Create(width, height, format, GetColorGray, GetPixel32),
            Format.R_UN16 => Create(width, height, format, GetColorGray, GetPixel16),
            Format.R_F16 => Create(width, height, format, GetColorGray, GetPixelF16),
            Format.R_F32 => Create(width, height, format, GetColorGray, GetPixelF32),

            // Two-channel formats
            Format.RG_UN8 => Create(width, height, format, GetColorRg, GetPixelRg8),
            Format.RG_UI16 => Create(width, height, format, GetColorRg, GetPixelRg16),
            Format.RG_UI32 => Create(width, height, format, GetColorRg, GetPixelRg32),
            Format.RG_UN16 => Create(width, height, format, GetColorRg, GetPixelRg16),
            Format.RG_F16 => Create(width, height, format, GetColorRg, GetPixelRgF16),
            Format.RG_F32 => Create(width, height, format, GetColorRg, GetPixelRgF32),

            // Four-channel formats
            Format.RGBA_UN8 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba8),
            Format.RGBA_UI32 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba32),
            Format.RGBA_F16 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgbaF16),
            Format.RGBA_F32 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgbaF32),
            Format.RGBA_SRGB8 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba8),

            Format.BGRA_UN8 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelBgra8),
            Format.BGRA_SRGB8 => Create(width, height, format, GetColorRgbaGetter(opaque), GetPixelBgra8),

            _ => throw new NotImplementedException(),
        };
    }

    /// <summary>
    /// Creates a pair of images with the same color gradient. The first image is in the given format, and
    /// the second image is the expected result of encoding the first image to a file and decoding it to
    /// <see cref="Format.RGBA_UN8"/>.
    /// </summary>
    /// <remarks>
    /// Supports all non-packed and non-compressed pixel formats.
    /// </remarks>
    /// <param name="width">The width of each image in pixels. Must be >= 2.</param>
    /// <param name="height">The height of each image in pixels. Must be >= 2.</param>
    /// <param name="format">The format of the image to encode.</param>
    /// <param name="opaque">
    /// If true, sets the alpha channel to opaque for every pixel. Set this to true when using a
    /// pixel format that has an alpha channel but encoding to a file format that does not support
    /// transparency such as JPEG.
    /// </param>
    public static (Image Input, Image Expected) CreateEncodeDecodePair(
        int width = 6,
        int height = 6,
        Format format = Format.RGBA_UN8,
        bool opaque = false
    )
    {
        return format switch
        {
            // Single-channel formats
            Format.R_UN8 => CreatePair(width, height, format, GetColorGray, GetPixel8),
            Format.R_UI16 => CreatePair(width, height, format, GetColorGray, GetPixel16),
            Format.R_UI32 => CreatePair(width, height, format, GetColorGray, GetPixel32),
            Format.R_UN16 => CreatePair(width, height, format, GetColorGray, GetPixel16),
            Format.R_F16 => CreatePair(width, height, format, GetColorGray, GetPixelF16),
            Format.R_F32 => CreatePair(width, height, format, GetColorGray, GetPixelF32),

            // Two-channel formats
            Format.RG_UN8 => CreatePair(width, height, format, GetColorRg, GetPixelRg8),
            Format.RG_UI16 => CreatePair(width, height, format, GetColorRg, GetPixelRg16),
            Format.RG_UI32 => CreatePair(width, height, format, GetColorRg, GetPixelRg32),
            Format.RG_UN16 => CreatePair(width, height, format, GetColorRg, GetPixelRg16),
            Format.RG_F16 => CreatePair(width, height, format, GetColorRg, GetPixelRgF16),
            Format.RG_F32 => CreatePair(width, height, format, GetColorRg, GetPixelRgF32),

            // Four-channel formats
            Format.RGBA_UN8 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba8),
            Format.RGBA_UI32 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba32),
            Format.RGBA_F16 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgbaF16),
            Format.RGBA_F32 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgbaF32),
            Format.RGBA_SRGB8 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelRgba8),

            Format.BGRA_UN8 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelBgra8),
            Format.BGRA_SRGB8 => CreatePair(width, height, format, GetColorRgbaGetter(opaque), GetPixelBgra8),

            _ => throw new NotImplementedException(),
        };
    }

    private static (Image Input, Image Expected) CreatePair<T>(
        int width,
        int height,
        Format format,
        Func<int, int, int, int, Vector4> getColor,
        Func<Vector4, T> colorToPixel
    )
        where T : unmanaged
    {
        var input = Create(width, height, format, getColor, colorToPixel);
        var expected = Create(width, height, Format.RGBA_UN8, getColor, GetPixelRgba8);
        return (input, expected);
    }

    private static Image Create<T>(
        int width,
        int height,
        Format format,
        Func<int, int, int, int, Vector4> getColor,
        Func<Vector4, T> colorToPixel
    )
        where T : unmanaged
    {
        HxDebug.Assert(width >= 2);
        HxDebug.Assert(height >= 2);

        var img = Image.New2D(width, height, 1, format);
        var pb = img.GetPixelBuffer(0, 0);

        for (var x = 0; x < pb.Width; x++)
        {
            for (var y = 0; y < pb.Height; y++)
            {
                var color = Quantize8Bit(getColor(x, y, pb.Width, pb.Height));
                var pixel = colorToPixel(color);

                pb.SetPixel(x, y, pixel);
            }
        }

        return img;
    }

    private static Func<int, int, int, int, Vector4> GetColorRgbaGetter(bool opaque)
    {
        return opaque ? GetColorRgb : GetColorRgba;
    }

    /// <summary>
    /// Gets the color at a location to create test image that uses all four color components.
    ///
    /// The image is red in the top-left corner, green in the top-right, blue in the bottom-left,
    /// and transparent in the bottom-right.
    /// </summary>
    private static Vector4 GetColorRgba(int x, int y, int width, int height)
    {
        var maxX = (float)(width - 1);
        var maxY = (float)(height - 1);

        var r = 1.0f - (x / maxX) - (y / maxY);
        var g = (x / maxX) - (y / maxY);
        var b = (y / maxY) - (x / maxX);
        var a = 2.0f - (x / maxX) - (y / maxY);

        return new(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Math.Clamp(a, 0, 1));
    }

    /// <summary>
    /// Gets the color at a location to create an opaque test image that uses the RGB components.
    ///
    /// The image is the same as GetColorRgba() but it is fully opaque.
    /// </summary>
    private static Vector4 GetColorRgb(int x, int y, int width, int height)
    {
        var c = GetColorRgba(x, y, width, height);
        c.W = 1;
        return c;
    }

    /// <summary>
    /// Gets the color at a location to create an opaque test image that uses the red and green components.
    ///
    /// The image is black in the top-left corner, red in the top-right, green in the bottom-left,
    /// and yellow in the bottom-right.
    /// </summary>
    private static Vector4 GetColorRg(int x, int y, int width, int height)
    {
        var r = x / (float)(width - 1);
        var g = y / (float)(height - 1);

        return new(r, g, 0, 1);
    }

    /// <summary>
    /// Gets the color at a location to create an opaque, grayscale test image.
    ///
    /// The image is black in the top-left corner and white in the bottom-right.
    /// </summary>
    private static Vector4 GetColorGray(int x, int y, int width, int height)
    {
        var v = (x + y) / (float)(width - 1 + height - 1);

        return new(v, v, v, 1);
    }

    /// <summary>
    /// Quantizes a color's components to values that will not lose precision when
    /// converted to <see cref="Format.RGBA_UN8"/>.
    /// </summary>
    public static Vector4 Quantize8Bit(Vector4 v)
    {
        return new(
            ToU8(v.X) / 255.0f,
            ToU8(v.Y) / 255.0f,
            ToU8(v.Z) / 255.0f,
            ToU8(v.W) / 255.0f
        );
    }

    /// <summary>Scales a [0, 1] float to an 8-bit unsigned integer.</summary>
    private static byte ToU8(float v) => (byte)Math.Round(v * byte.MaxValue);

    /// <summary>Scales a [0, 1] float to a 16-bit unsigned integer.</summary>
    private static ushort ToU16(float v) => (ushort)Math.Round(v * ushort.MaxValue);

    /// <summary>Scales a [0, 1] float to a 32-bit unsigned integer.</summary>
    private static uint ToU32(float v) => (uint)Math.Round(v * uint.MaxValue);

    private static byte GetPixel8(Vector4 c) => ToU8(c.X);
    private static ushort GetPixel16(Vector4 c) => ToU16(c.X);
    private static uint GetPixel32(Vector4 c) => ToU32(c.X);

    private static Half GetPixelF16(Vector4 c) => (Half)c.X;
    private static float GetPixelF32(Vector4 c) => c.X;

    private static Rg8Pixel GetPixelRg8(Vector4 c) => new() { R = ToU8(c.X), G = ToU8(c.Y) };
    private static Rg16Pixel GetPixelRg16(Vector4 c) => new() { R = ToU16(c.X), G = ToU16(c.Y) };
    private static Rg32Pixel GetPixelRg32(Vector4 c) => new() { R = ToU32(c.X), G = ToU32(c.Y) };

    private static RgF16Pixel GetPixelRgF16(Vector4 c) => new() { R = (Half)c.X, G = (Half)c.Y };
    private static RgF32Pixel GetPixelRgF32(Vector4 c) => new() { R = c.X, G = c.Y };

    private static Rgba8Pixel GetPixelRgba8(Vector4 c)
        => new() { R = ToU8(c.X), G = ToU8(c.Y), B = ToU8(c.Z), A = ToU8(c.W) };

    private static Rgba32Pixel GetPixelRgba32(Vector4 c)
        => new() { R = ToU32(c.X), G = ToU32(c.Y), B = ToU32(c.Z), A = ToU32(c.W) };

    private static RgbaF16Pixel GetPixelRgbaF16(Vector4 c)
        => new() { R = (Half)c.X, G = (Half)c.Y, B = (Half)c.Z, A = (Half)c.W };

    private static RgbaF32Pixel GetPixelRgbaF32(Vector4 c)
        => new() { R = c.X, G = c.Y, B = c.Z, A = c.W };

    private static Bgra8Pixel GetPixelBgra8(Vector4 c)
        => new() { R = ToU8(c.X), G = ToU8(c.Y), B = ToU8(c.Z), A = ToU8(c.W) };

    /// <summary>
    /// Represents a pixel with red and green channels and 8-bit integers per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Rg8Pixel
    {
        public byte R;
        public byte G;
    }

    /// <summary>
    /// Represents a pixel with red and green channels and 16-bit integers per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Rg16Pixel
    {
        public ushort R;
        public ushort G;
    }

    /// <summary>
    /// Represents a pixel with red and green channels and 32-bit integers per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Rg32Pixel
    {
        public uint R;
        public uint G;
    }

    /// <summary>
    /// Represents a pixel with red and green channels and half precision floats per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RgF16Pixel
    {
        public Half R;
        public Half G;
    }

    /// <summary>
    /// Represents a pixel with red and green channels and single precision floats per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct RgF32Pixel
    {
        public float R;
        public float G;
    }

    /// <summary>
    /// Represents a pixel in RGBA order with 32-bit integers per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Rgba32Pixel
    {
        public uint R;
        public uint G;
        public uint B;
        public uint A;
    }

    /// <summary>
    /// Represents a pixel in RGBA order with half precision floats per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct RgbaF16Pixel
    {
        public Half R;
        public Half G;
        public Half B;
        public Half A;
    }

    /// <summary>
    /// Represents a pixel in RGBA order with single precision floats per channel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct RgbaF32Pixel
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }
}
