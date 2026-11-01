using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// TIFF image decoder/encoder backed by LibTiff.NET.
/// </summary>
/// <remarks>
/// <para>All images are decoded to <see cref="Format.RGBA_UN8"/> (Rgba32) for maximum compatibility.</para>
/// <para>Encoding supports most single-channel and RGBA pixel formats. Two-channel formats are expanded to RGB.</para>
/// </remarks>
public sealed class TiffDecoder : IImageDecoder
{
    /// <summary>
    /// Shared singleton instance registered for the TIFF file type.
    /// </summary>
    public static readonly TiffDecoder Instance = new();

    private TiffDecoder() { }

    /// <inheritdoc/>
    public unsafe Image? Decode(nint dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        if (dataPointer == IntPtr.Zero || dataSize <= 0)
        {
            return null;
        }

        using var stream = new UnmanagedMemoryStream((byte*)dataPointer, dataSize);
        using var tif = Tiff.ClientOpen("in-memory", "r", stream, new TiffStream());

        if (tif is null)
        {
            // Failed to read the stream as a TIFF image.
            return null;
        }

        return ConvertToImage(tif);
    }

    /// <inheritdoc/>
    public void Save(PixelBuffer[] pixelBuffers, int count, ImageDescription description, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(pixelBuffers);
        ArgumentNullException.ThrowIfNull(stream);

        if (count == 0 || pixelBuffers.Length == 0)
        {
            throw new ArgumentException("No pixel buffers to save");
        }

        // Save mip0 of the first array slice
        var pb = pixelBuffers[0];

        // Disposing the Tiff object returned by ClientOpen will close the given stream, but we
        // don't want the stream provided by the caller to be closed, so wrap it in another stream
        // that forwards everything to the original stream but doesn't close it when closed.
        using var forwardingStream = new ForwardingStream(stream);
        using var tif = Tiff.ClientOpen("in-memory", "w", forwardingStream, new TiffStream());

        tif.SetField(TiffTag.IMAGEWIDTH, pb.Width);
        tif.SetField(TiffTag.IMAGELENGTH, pb.Height);
        tif.SetField(TiffTag.ROWSPERSTRIP, pb.Height);

        tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tif.SetField(TiffTag.COMPRESSION, Compression.LZW);

        var info = GetFormatInfo(pb.Format);

        tif.SetField(TiffTag.PHOTOMETRIC, info.Photometric);
        tif.SetField(TiffTag.SAMPLEFORMAT, info.SampleFormat);
        tif.SetField(TiffTag.BITSPERSAMPLE, info.BitsPerSample);
        tif.SetField(TiffTag.SAMPLESPERPIXEL, info.SamplesPerPixel);
        if (info.HasAlpha)
        {
            tif.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.UNASSALPHA });
        }

        var buffer = new byte[pb.Width * info.BytesPerPixel];

        for (var i = 0; i < pb.Height; i++)
        {
            ConvertRowToTif(pb, i, buffer);
            tif.WriteScanline(buffer, i);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="Tiff"/> image to a Nex <see cref="Image"/> with <see cref="Format.RGBA_UN8"/>.
    /// </summary>
    private static unsafe Image? ConvertToImage(Tiff tif)
    {
        var width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        var height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

        var image = Image.New2D(width, height, 1, Format.RGBA_UN8);
        var pb = image.GetPixelBuffer(0, 0);

        var raster = new int[height * width];

        if (!tif.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
        {
            return null;
        }

        var srcSpan = MemoryMarshal.Cast<int, byte>(raster);
        var dstSpan = new Span<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);

        HxDebug.Assert(srcSpan.Length == dstSpan.Length);

        srcSpan.CopyTo(dstSpan);

        return image;
    }

    /// <summary>
    /// Converts one row of a <see cref="PixelBuffer"/> to RGBA and writes it to a byte buffer.
    /// </summary>
    private static unsafe void ConvertRowToTif(PixelBuffer pb, int y, Span<byte> dest)
    {
        var srcPtr = (byte*)pb.DataPointer + (y * pb.RowStride);
        var src = new ReadOnlySpan<byte>(srcPtr, pb.RowStride);

        switch (pb.Format)
        {
            // Two-channel formats need to be expanded to RGB
            case Format.RG_UN8:
                for (var offset = 0; offset < dest.Length; offset += 3)
                {
                    dest[offset + 0] = src[offset + 0];
                    dest[offset + 1] = src[offset + 1];
                    dest[offset + 2] = 0;
                }
                break;

            case Format.RG_UI16:
            case Format.RG_UN16:
            case Format.RG_F16:
            {
                var src16 = MemoryMarshal.Cast<byte, ushort>(src);
                var dest16 = MemoryMarshal.Cast<byte, ushort>(dest);

                for (var offset = 0; offset < dest16.Length; offset += 3)
                {
                    dest16[offset + 0] = src16[offset + 0];
                    dest16[offset + 1] = src16[offset + 1];
                    dest16[offset + 2] = 0;
                }
                break;
            }

            case Format.RG_UI32:
            case Format.RG_F32:
            {
                var src32 = MemoryMarshal.Cast<byte, uint>(src);
                var dest32 = MemoryMarshal.Cast<byte, uint>(dest);

                for (var offset = 0; offset < dest32.Length; offset += 3)
                {
                    dest32[offset + 0] = src32[offset + 0];
                    dest32[offset + 1] = src32[offset + 1];
                    dest32[offset + 2] = 0;
                }
                break;
            }

            // BGRA formats need to be byte swapped to RGBA
            case Format.BGRA_UN8:
            case Format.BGRA_SRGB8:
                for (var offset = 0; offset < dest.Length; offset += 4)
                {
                    dest[offset + 0] = src[offset + 2];
                    dest[offset + 1] = src[offset + 1];
                    dest[offset + 2] = src[offset + 0];
                    dest[offset + 3] = src[offset + 3];
                }
                break;

            // All other supported formats can be copied directly
            default:
                HxDebug.Assert(src.Length == dest.Length);
                src.CopyTo(dest);
                break;
        }
    }

    private static FormatInfo GetFormatInfo(Format format)
    {
        return format switch
        {
            // Single-channel formats
            Format.R_UN8 => FormatInfo.Single(8),
            Format.R_UI16 => FormatInfo.Single(16),
            Format.R_UI32 => FormatInfo.Single(32),
            Format.R_UN16 => FormatInfo.Single(16),
            Format.R_F16 => FormatInfo.Single(16, SampleFormat.IEEEFP),
            Format.R_F32 => FormatInfo.Single(32, SampleFormat.IEEEFP),

            // Two-channel formats (expanded to RGB)
            Format.RG_UN8 => FormatInfo.Rgb(8),
            Format.RG_UI16 => FormatInfo.Rgb(16),
            Format.RG_UI32 => FormatInfo.Rgb(32),
            Format.RG_UN16 => FormatInfo.Rgb(16),
            Format.RG_F16 => FormatInfo.Rgb(16, SampleFormat.IEEEFP),
            Format.RG_F32 => FormatInfo.Rgb(32, SampleFormat.IEEEFP),

            // Four-channel RGBA formats
            Format.RGBA_UN8 => FormatInfo.Rgba(8),
            Format.RGBA_UI32 => FormatInfo.Rgba(32),
            Format.RGBA_F32 => FormatInfo.Rgba(32, SampleFormat.IEEEFP),
            Format.RGBA_SRGB8 => FormatInfo.Rgba(8),
            Format.BGRA_UN8 => FormatInfo.Rgba(8),
            Format.BGRA_SRGB8 => FormatInfo.Rgba(8),

            _ => throw new ArgumentException("Unsupported format"),
        };
    }

    private record FormatInfo
    {
        public Photometric Photometric { get; set; }
        public SampleFormat SampleFormat { get; set; }
        public int BitsPerSample { get; set; }
        public int SamplesPerPixel { get; set; }
        public bool HasAlpha { get; set; }

        public int BytesPerPixel => ((BitsPerSample * SamplesPerPixel) + 7) / 8;

        public static FormatInfo Single(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.MINISBLACK,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 1,
        };

        public static FormatInfo Rgb(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.RGB,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 3,
        };

        public static FormatInfo Rgba(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.RGB,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 4,
            HasAlpha = true,
        };
    }

    private class ForwardingStream(Stream target) : Stream
    {
        public override bool CanRead => target.CanRead;

        public override bool CanSeek => target.CanSeek;

        public override bool CanWrite => target.CanWrite;

        public override long Length => target.Length;

        public override long Position
        {
            get => target.Position;
            set => target.Position = value;
        }

        public override void Flush() => target.Flush();

        public override int Read(byte[] buffer, int offset, int count) => target.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => target.Seek(offset, origin);

        public override void SetLength(long value) => target.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => target.Write(buffer, offset, count);
    }
}
