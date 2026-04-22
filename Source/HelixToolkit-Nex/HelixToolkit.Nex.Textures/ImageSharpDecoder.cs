using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Image decoder/encoder backed by SixLabors.ImageSharp.
/// Supports PNG, JPG, BMP, GIF, TIFF, WebP, and TGA formats.
/// All images are decoded to <see cref="Format.RGBA_UN8"/> (Rgba32) for maximum compatibility.
/// </summary>
public sealed class ImageSharpDecoder : IImageDecoder
{
    /// <summary>
    /// Shared singleton instance registered for all supported file types.
    /// </summary>
    public static readonly ImageSharpDecoder Instance = new();

    private ImageSharpDecoder() { }

    /// <inheritdoc/>
    public unsafe Image? Decode(IntPtr dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        if (dataPointer == IntPtr.Zero || dataSize <= 0)
            return null;

        // Wrap the unmanaged memory in an UnmanagedMemoryStream — zero-copy read
        using var stream = new UnmanagedMemoryStream((byte*)dataPointer, dataSize);

        SixLabors.ImageSharp.Image? imgSharp;
        try
        {
            imgSharp = SixLabors.ImageSharp.Image.Load(stream);
        }
        catch
        {
            // Not a format ImageSharp recognises
            return null;
        }

        using (imgSharp)
        {
            return ConvertToImage(imgSharp);
        }
    }

    /// <inheritdoc/>
    public void Save(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream
    )
    {
        if (count == 0 || pixelBuffers.Length == 0)
            throw new ArgumentException("No pixel buffers to save");

        // Save mip0 of the first array slice
        var pb = pixelBuffers[0];
        using var img = BuildImageSharp(pb);
        img.SaveAsPng(stream);
    }

    // -------------------------------------------------------------------------
    // Per-format save helpers exposed for use via Image.Save(stream, fileType)
    // -------------------------------------------------------------------------

    internal void SaveAs(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream,
        ImageFileType fileType
    )
    {
        if (count == 0 || pixelBuffers.Length == 0)
            throw new ArgumentException("No pixel buffers to save");

        var pb = pixelBuffers[0];
        using var img = BuildImageSharp(pb);

        switch (fileType)
        {
            case ImageFileType.Png:
                img.SaveAsPng(stream);
                break;
            case ImageFileType.Jpg:
                img.SaveAsJpeg(stream);
                break;
            case ImageFileType.Bmp:
                img.SaveAsBmp(stream);
                break;
            case ImageFileType.Gif:
                img.SaveAsGif(stream);
                break;
            case ImageFileType.Tiff:
                img.SaveAsTiff(stream);
                break;
            case ImageFileType.Webp:
                img.SaveAsWebp(stream);
                break;
            case ImageFileType.Tga:
                img.SaveAsTga(stream);
                break;
            default:
                img.SaveAsPng(stream);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts any ImageSharp image to a Nex <see cref="Image"/> with <see cref="Format.RGBA_UN8"/>.
    /// </summary>
    private static unsafe Image ConvertToImage(SixLabors.ImageSharp.Image imgSharp)
    {
        // Convert to Rgba32 — the universal 8-bit RGBA format that maps to Format.RGBA_UN8
        using var rgba = imgSharp.CloneAs<Rgba32>();

        int width = rgba.Width;
        int height = rgba.Height;

        var nexImage = Image.New2D(width, height, 1, Format.RGBA_UN8);
        var pb = nexImage.GetPixelBuffer(0, 0);

        // Copy row by row to handle any internal ImageSharp padding
        rgba.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var srcRow = accessor.GetRowSpan(y);
                var dstPtr = (byte*)pb.DataPointer + (long)y * pb.RowStride;
                var dstSpan = new Span<byte>(dstPtr, pb.RowStride);
                MemoryMarshal.AsBytes(srcRow).CopyTo(dstSpan);
            }
        });

        return nexImage;
    }

    /// <summary>
    /// Builds an ImageSharp <see cref="Image{Rgba32}"/> from a <see cref="PixelBuffer"/>.
    /// Only <see cref="Format.RGBA_UN8"/> and <see cref="Format.BGRA_UN8"/> are supported for saving.
    /// </summary>
    private static unsafe SixLabors.ImageSharp.Image<Rgba32> BuildImageSharp(PixelBuffer pb)
    {
        var img = new SixLabors.ImageSharp.Image<Rgba32>(pb.Width, pb.Height);

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < pb.Height; y++)
            {
                var dstRow = accessor.GetRowSpan(y);
                var srcPtr = (byte*)pb.DataPointer + (long)y * pb.RowStride;

                if (pb.Format == Format.RGBA_UN8)
                {
                    var srcSpan = new ReadOnlySpan<byte>(srcPtr, pb.Width * 4);
                    MemoryMarshal.Cast<byte, Rgba32>(srcSpan).CopyTo(dstRow);
                }
                else if (pb.Format == Format.BGRA_UN8)
                {
                    // Swap R and B channels
                    for (int x = 0; x < pb.Width; x++)
                    {
                        byte b = srcPtr[x * 4 + 0];
                        byte g = srcPtr[x * 4 + 1];
                        byte r = srcPtr[x * 4 + 2];
                        byte a = srcPtr[x * 4 + 3];
                        dstRow[x] = new Rgba32(r, g, b, a);
                    }
                }
                else
                {
                    // Fallback: treat as RGBA_UN8
                    var srcSpan = new ReadOnlySpan<byte>(srcPtr, pb.Width * 4);
                    MemoryMarshal.Cast<byte, Rgba32>(srcSpan).CopyTo(dstRow);
                }
            }
        });

        return img;
    }
}
