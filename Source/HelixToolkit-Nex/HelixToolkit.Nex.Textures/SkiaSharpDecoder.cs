using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using SkiaSharp;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Image decoder/encoder backed by SkiaSharp.
/// Supports both decode and encode for PNG, JPG, and WebP formats. Supports decode only for BMP and GIF.
/// </summary>
/// <remarks>
/// <para>All images are decoded to <see cref="Format.RGBA_UN8"/> (Rgba32) for maximum compatibility.</para>
/// <para>
/// Encoding supports all pixel formats that have a matching value in <see cref="SKColorType"/>.
/// Save() defaults to saving a PNG image. Use SaveAs() to save JPG or WebP instead.
/// </para>
/// </remarks>
public sealed class SkiaSharpDecoder : IImageDecoder
{
    /// <summary>
    /// Shared singleton instance registered for all supported file types.
    /// </summary>
    public static readonly SkiaSharpDecoder Instance = new();

    private SkiaSharpDecoder() { }

    /// <inheritdoc/>
    public unsafe Image? Decode(IntPtr dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        if (dataPointer == IntPtr.Zero || dataSize <= 0)
        {
            return null;
        }

        using var stream = new UnmanagedMemoryStream((byte*)dataPointer, dataSize);
        using var codec = SKCodec.Create(stream, out var result);

        if (codec is null || result != SKCodecResult.Success)
        {
            // Not a format SkiaSharp recognizes
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = SKBitmap.Decode(codec, info);

        if (bitmap is null)
        {
            return null;
        }

        return ConvertToImage(bitmap);
    }

    /// <inheritdoc/>
    public void Save(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream
    )
    {
        ArgumentNullException.ThrowIfNull(pixelBuffers);
        ArgumentNullException.ThrowIfNull(stream);

        SaveAs(pixelBuffers, count, description, stream, ImageFileType.Png);
    }

    internal static void SaveAs(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream,
        ImageFileType fileType
    )
    {
        if (count == 0 || pixelBuffers.Length == 0)
        {
            throw new ArgumentException("No pixel buffers to save");
        }

        var encodedFormat = fileType switch
        {
            ImageFileType.Png => SKEncodedImageFormat.Png,
            ImageFileType.Jpg => SKEncodedImageFormat.Jpeg,
            ImageFileType.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };

        // Save mip0 of the first array slice
        var pb = pixelBuffers[0];
        using var bitmap = ConvertToSKBitmap(pb);
        bitmap.Encode(stream, encodedFormat, 100);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a SkiaSharp <see cref="SKBitmap"/> to a Nex <see cref="Image"/> with <see cref="Format.RGBA_UN8"/>.
    /// The bitmap must be decoded with <see cref="SKColorType.Rgba8888"/> and <see cref="SKAlphaType.Unpremul"/>.
    /// </summary>
    private static unsafe Image ConvertToImage(SKBitmap bitmap)
    {
        HxDebug.Assert(bitmap.ColorType == SKColorType.Rgba8888);
        HxDebug.Assert(bitmap.AlphaType == SKAlphaType.Unpremul);

        var image = Image.New2D(bitmap.Width, bitmap.Height, 1, Format.RGBA_UN8);
        var pb = image.GetPixelBuffer(0, 0);

        var srcSpan = bitmap.GetPixelSpan();
        var dstSpan = new Span<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);

        HxDebug.Assert(srcSpan.Length == dstSpan.Length);

        srcSpan.CopyTo(dstSpan);

        return image;
    }

    /// <summary>
    /// Builds a SkiaSharp <see cref="SKBitmap"/> from a <see cref="PixelBuffer"/>.
    /// </summary>
    private static unsafe SKBitmap ConvertToSKBitmap(PixelBuffer pb)
    {
        var colorType = pb.Format switch
        {
            Format.R_UN8 => SKColorType.R8Unorm,
            Format.R_UN16 => SKColorType.R16Unorm,
            Format.R_F16 => SKColorType.RF16,
            Format.RG_UN8 => SKColorType.Rg88,
            Format.RG_F16 => SKColorType.RgF16,
            Format.RGBA_UN8 => SKColorType.Rgba8888,
            Format.RGBA_F16 => SKColorType.RgbaF16,
            Format.RGBA_F32 => SKColorType.RgbaF32,
            Format.RGBA_SRGB8 => SKColorType.Srgba8888,
            Format.BGRA_UN8 => SKColorType.Bgra8888,
            Format.A_UN8 => SKColorType.Alpha8,
            _ => throw new ArgumentException("Unsupported format"),
        };

        var bitmap = new SKBitmap(pb.Width, pb.Height, colorType, SKAlphaType.Unpremul);

        var srcSpan = new ReadOnlySpan<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);
        var dstSpan = bitmap.GetPixelSpan();

        HxDebug.Assert(srcSpan.Length == dstSpan.Length);

        srcSpan.CopyTo(dstSpan);

        return bitmap;
    }
}
