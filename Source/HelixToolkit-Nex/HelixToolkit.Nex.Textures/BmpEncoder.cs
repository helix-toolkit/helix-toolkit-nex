using System.Runtime.InteropServices;
using System.Text;
using HelixToolkit.Nex.Graphics;
using WritePixelsAction = System.Action<System.IO.BinaryWriter, System.ReadOnlySpan<byte>>;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// BMP image encoder. Supports <see cref="Format.RGBA_UN8"/> and <see cref="Format.BGRA_UN8"/>.
/// </summary>
public sealed class BmpEncoder : IImageDecoder
{
    /// <summary>
    /// Shared singleton instance registered for encoding the BMP file type.
    /// </summary>
    public static readonly BmpEncoder Instance = new();

    private BmpEncoder() { }

    public Image? Decode(nint dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        throw new NotSupportedException();
    }

    public void Save(PixelBuffer[] pixelBuffers, int count, ImageDescription description, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(pixelBuffers);
        ArgumentNullException.ThrowIfNull(stream);

        // Save mip0 of the first array slice
        var pb = pixelBuffers[0];

        WritePixelsAction writePixels = pb.Format switch
        {
            Format.RGBA_UN8 or Format.RGBA_SRGB8 => WritePixelsRgba,
            Format.BGRA_UN8 or Format.BGRA_SRGB8 => WritePixelsBgra,
            _ => throw new ArgumentException("Unsupported format"),
        };

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        var pixelDataSize = (uint)(pb.Width * pb.Height * 4);
        var pixelDataOffset = FileHeaderSize + InfoHeaderSize;
        var fileSize = pixelDataOffset + pixelDataSize;

        // BITMAPFILEHEADER
        writer.Write("BM"u8);           // bfType
        writer.Write(fileSize);         // bfSize   
        writer.Write((ushort)0);        // bfReserved1
        writer.Write((ushort)0);        // bfReserved2
        writer.Write(pixelDataOffset);  // bfOffBits

        // BITMAPINFOHEADER
        writer.Write(InfoHeaderSize);   // biSize
        writer.Write(pb.Width);         // biWidth
        writer.Write(pb.Height);        // biHeight
        writer.Write((ushort)1);        // biPlanes
        writer.Write((ushort)32);       // biBitCount
        writer.Write((uint)0);          // biCompression = BI_RGB
        writer.Write(pixelDataSize);    // biSizeImage
        writer.Write(2385);             // biXPelsPerMeter = 72 DPI
        writer.Write(2385);             // biYPelsPerMeter = 72 DPI
        writer.Write((uint)0);          // biClrUsed
        writer.Write((uint)0);          // biClrImportant

        WritePixelData(writer, pb, writePixels);
    }

    private const uint FileHeaderSize = 14;
    private const uint InfoHeaderSize = 40;

    private static unsafe void WritePixelData(BinaryWriter writer, PixelBuffer pb, WritePixelsAction writePixels)
    {
        var buffer = new ReadOnlySpan<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);

        // BMP rows are ordered bottom to top
        for (var y = pb.Height - 1; y >= 0; y--)
        {
            var row = buffer.Slice(pb.RowStride * y, pb.RowStride);
            writePixels(writer, row);
        }
    }

    private static void WritePixelsRgba(BinaryWriter writer, ReadOnlySpan<byte> buffer)
    {
        for (var offset = 0; offset < buffer.Length; offset += 4)
        {
            writer.Write(buffer[offset + 2]);
            writer.Write(buffer[offset + 1]);
            writer.Write(buffer[offset + 0]);
            writer.Write(buffer[offset + 3]);
        }
    }

    private static void WritePixelsBgra(BinaryWriter writer, ReadOnlySpan<byte> buffer)
    {
        writer.Write(buffer);
    }
}
