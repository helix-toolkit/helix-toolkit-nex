using System.Runtime.InteropServices;
using System.Text;
using HelixToolkit.Nex.Graphics;

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

        var masks = pb.Format switch
        {
            Format.RGBA_UN8 or Format.RGBA_SRGB8 => ColorMasks.Rgba,
            Format.BGRA_UN8 or Format.BGRA_SRGB8 => ColorMasks.Bgra,
            _ => throw new ArgumentException("Unsupported format"),
        };

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        var pixelDataSize = (uint)(pb.Width * pb.Height * pb.PixelSize);
        var pixelDataOffset = FileHeaderSize + InfoHeaderSize;
        var fileSize = pixelDataOffset + pixelDataSize;

        // BITMAPFILEHEADER
        writer.Write("BM"u8);           // bfType
        writer.Write(fileSize);         // bfSize   
        writer.Write((ushort)0);        // bfReserved1
        writer.Write((ushort)0);        // bfReserved2
        writer.Write(pixelDataOffset);  // bfOffBits

        // BITMAPV4HEADER 
        writer.Write(InfoHeaderSize);   // bV4Size
        writer.Write(pb.Width);         // bV4Width
        writer.Write(pb.Height);        // bV4Height
        writer.Write((ushort)1);        // bV4Planes
        writer.Write((ushort)32);       // bV4BitCount
        writer.Write((uint)3);          // bV4Compression = BI_BITFIELDS
        writer.Write(pixelDataSize);    // bV4SizeImage
        writer.Write(2385);             // bV4XPelsPerMeter = 72 DPI
        writer.Write(2385);             // bV4YPelsPerMeter = 72 DPI
        writer.Write((uint)0);          // bV4ClrUsed
        writer.Write((uint)0);          // bV4ClrImportant
        writer.Write(masks.R);          // bV4RedMask
        writer.Write(masks.G);          // bV4GreenMask
        writer.Write(masks.B);          // bV4BlueMask
        writer.Write(masks.A);          // bV4AlphaMask
        writer.Write((uint)0x73524742); // bV4CSType = LCS_sRGB
        writer.Write(new byte[36]);     // bV4Endpoints
        writer.Write((uint)0);          // bV4GammaRed
        writer.Write((uint)0);          // bV4GammaGreen
        writer.Write((uint)0);          // bV4GammaBlue

        WritePixelData(writer, pb);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private const uint FileHeaderSize = 14;
    private const uint InfoHeaderSize = 108;

    private static void WritePixelData(BinaryWriter writer, PixelBuffer pb)
    {
        // BMP rows are ordered bottom to top
        for (var y = pb.Height - 1; y >= 0; y--)
        {
            var row = pb.GetRowSpan(y);
            writer.Write(row);
        }
    }

    private struct ColorMasks
    {
        public static readonly ColorMasks Rgba = new()
        {
            R = 0x000000FF,
            G = 0x0000FF00,
            B = 0x00FF0000,
            A = 0xFF000000,
        };

        public static readonly ColorMasks Bgra = new()
        {
            R = 0x00FF0000,
            G = 0x0000FF00,
            B = 0x000000FF,
            A = 0xFF000000,
        };

        public uint R;
        public uint G;
        public uint B;
        public uint A;
    };
}
