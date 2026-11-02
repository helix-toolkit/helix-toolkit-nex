using System.Runtime.InteropServices;
using System.Text;
using HelixToolkit.Nex.Graphics;
using StbImageNext;
using WritePixelsAction = System.Action<System.IO.BinaryWriter, System.ReadOnlySpan<byte>>;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// TGA image decoder/encoder backed by StbImageNext.
/// </summary>
/// <remarks>
/// <para>All images are decoded to <see cref="Format.RGBA_UN8"/> (Rgba32) for maximum compatibility.</para>
/// <para>Encoding supports <see cref="Format.RGBA_UN8"/> and <see cref="Format.BGRA_UN8"/>.</para>
/// </remarks>
public sealed class TgaDecoder : IImageDecoder
{
    /// <summary>
    /// Shared singleton instance registered for the TGA file type.
    /// </summary>
    public static readonly TgaDecoder Instance = new();

    private TgaDecoder() { }

    /// <inheritdoc/>
    public unsafe Image? Decode(nint dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        if (dataPointer == IntPtr.Zero || dataSize <= 0)
        {
            return null;
        }

        using var stream = new UnmanagedMemoryStream((byte*)dataPointer, dataSize);

        try
        {
            var image = StbImageNext.Decoders.TgaDecoder.Load(stream, ColorComponents.RedGreenBlueAlpha);

            return ConvertToImage(image);
        }
        catch (Exception)
        {
            // StbImageNext throws Exception on invalid data, so there is no more specific
            // exception type we can catch here.
            return null;
        }
    }

    /// <inheritdoc/>
    public void Save(PixelBuffer[] pixelBuffers, int count, ImageDescription description, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(pixelBuffers);
        ArgumentNullException.ThrowIfNull(stream);

        // Save mip0 of the first array slice
        var pb = pixelBuffers[0];

        if (pb.Width > ushort.MaxValue || pb.Height > ushort.MaxValue)
        {
            throw new ArgumentException("Image is too large for TGA format");
        }

        WritePixelsAction writePixels = pb.Format switch
        {
            Format.RGBA_UN8 or Format.RGBA_SRGB8 => WritePixelsRgba,
            Format.BGRA_UN8 or Format.BGRA_SRGB8 => WritePixelsBgra,
            _ => throw new ArgumentException("Unsupported format"),
        };

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        WriteTgaHeader(writer, pb);
        WriteTgaImageData(writer, pb, writePixels);
        WriteTgaFooter(writer);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a StbImage <see cref="ImageResult"/> to a Nex <see cref="Image"/> with <see cref="Format.RGBA_UN8"/>.
    /// The input image must be decoded with <see cref="ColorComponents.RedGreenBlueAlpha"/>.
    /// </summary>
    private static unsafe Image ConvertToImage(ImageResult stbImage)
    {
        HxDebug.Assert(stbImage.Comp == ColorComponents.RedGreenBlueAlpha);

        var nexImage = Image.New2D(stbImage.Width, stbImage.Height, 1, Graphics.Format.RGBA_UN8);
        var pb = nexImage.GetPixelBuffer(0, 0);

        var dstSpan = new Span<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);

        HxDebug.Assert(stbImage.Data.Length == dstSpan.Length);

        stbImage.Data.CopyTo(dstSpan);

        return nexImage;
    }

    private const byte OriginTopLeft = 0x20;
    private const byte AlphaChannelBits = 8;
    private const byte ImageDescriptor = OriginTopLeft | AlphaChannelBits;

    private static void WriteTgaHeader(BinaryWriter writer, PixelBuffer pb)
    {
        writer.Write((byte)0);          // ID length
        writer.Write((byte)0);          // No color map
        writer.Write((byte)2);          // Image type = uncompressed, true-color
        writer.Write([0, 0, 0, 0, 0]);  // Unused color map specification
        writer.Write((ushort)0);        // X origin
        writer.Write((ushort)0);        // Y origin
        writer.Write((ushort)pb.Width);
        writer.Write((ushort)pb.Height);
        writer.Write((byte)32);         // Pixel depth
        writer.Write(ImageDescriptor);
    }

    private static unsafe void WriteTgaImageData(BinaryWriter writer, PixelBuffer pb, WritePixelsAction writePixels)
    {
        var buffer = new ReadOnlySpan<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);
        writePixels(writer, buffer);
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

    private static void WriteTgaFooter(BinaryWriter writer)
    {
        writer.Write((uint)0);  // No extension area
        writer.Write((uint)0);  // No developer area
        writer.Write("TRUEVISION-XFILE.\0"u8);
    }
}
