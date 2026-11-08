// This software is based in part on LibTiff.NET:
// https://github.com/BitMiracle/libtiff.net
//
// Copyright (c) 2008-2016, Bit Miracle
// http://www.bitmiracle.com
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//   Redistributions of source code must retain the above copyright notice,
//   this list of conditions and the following disclaimer.
//
//   Redistributions in binary form must reproduce the above copyright
//   notice, this list of conditions and the following disclaimer in the
//   documentation and/or other materials provided with the distribution.
//
//   Neither the name of the Bit Miracle nor the names of its contributors
//   may be used to endorse or promote products derived from this software
//   without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
// IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
// TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
// PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL BIT MIRACLE BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.

using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// TIFF image decoder/encoder backed by LibTiff.NET.
/// </summary>
/// <remarks>
/// <para>All images are decoded to <see cref="Format.RGBA_UN8"/> (Rgba32) for maximum compatibility.</para>
/// <para>
/// Encoding supports all non-compressed, non-packed pixel formats with 8 or 16-bits per sample.
/// Two-channel formats are expanded to RGB.
/// </para>
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

        // Disposing the Tiff object returned by ClientOpen will close the stream, but we don't
        // want the stream provided by the caller to be closed, so wrap it in another stream
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

        var raster = ReadRgbaImage(tif, width, height);
        if (raster is null)
        {
            return null;
        }

        var srcSpan = MemoryMarshal.Cast<int, byte>(raster);
        var dstSpan = new Span<byte>((byte*)pb.DataPointer, pb.RowStride * pb.Height);

        HxDebug.Assert(srcSpan.Length == dstSpan.Length);

        srcSpan.CopyTo(dstSpan);

        return image;
    }

    private static int[]? ReadRgbaImage(Tiff tif, int width, int height)
    {
        if (!tif.RGBAImageOK(out var _))
        {
            return null;
        }

        var img = TiffRgbaImage.Create(tif, stopOnError: true, errorMsg: out var _);
        if (img is null)
        {
            return null;
        }

        img.ReqOrientation = Orientation.TOPLEFT;

        OverridePutDelegates(tif, img);

        var raster = new int[height * width];
        if (!img.GetRaster(raster, 0, width, height))
        {
            return null;
        }

        return raster;
    }

    private static void OverridePutDelegates(Tiff tif, TiffRgbaImage img)
    {
        var result = tif.GetFieldDefaulted(TiffTag.SAMPLEFORMAT);
        var format = (SampleFormat)result[0].ToByte();

        switch (format)
        {
            // The default handlers for unassociated alpha will premultiply color data by
            // the alpha. We want the original data, so override the put delegates if needed.
            case SampleFormat.UINT:
                if (img.Photometric != Photometric.RGB || img.Alpha != ExtraSample.UNASSALPHA)
                {
                    break;
                }
                switch (img.BitsPerSample)
                {
                    case 8:
                        img.PutContig = PutRgbaContig8BitTile;
                        img.PutSeparate = PutRgbaSeparate8BitTile;
                        break;

                    case 16:
                        img.PutContig = PutRgbaContig16BitTile;
                        img.PutSeparate = PutRgbaSeparate16BitTile;
                        break;
                }
                break;

            // TiffRgbaImage does not check for floating point data and will read it as unsigned
            // integers instead. Override the put delegates with ones that do support floats.
            case SampleFormat.IEEEFP:
                switch (img.BitsPerSample)
                {
                    case 16:
                        switch (img.Photometric)
                        {
                            case Photometric.RGB:
                                if (img.Alpha == ExtraSample.UNSPECIFIED)
                                {
                                    img.PutContig = PutRgbContigFp16BitTile;
                                    img.PutSeparate = PutRgbSeparateFp16BitTile;
                                }
                                else
                                {
                                    img.PutContig = PutRgbaContigFp16BitTile;
                                    img.PutSeparate = PutRgbaSeparateFp16BitTile;
                                }
                                break;

                            case Photometric.MINISBLACK:
                                img.PutContig = PutFp16BitMinIsBlackTile;
                                break;

                            case Photometric.MINISWHITE:
                                img.PutContig = PutFp16BitMinIsWhiteTile;
                                break;
                        }
                        break;
                }
                break;
        }
    }

    /// <summary>
    /// Converts one row of a <see cref="PixelBuffer"/> to RGBA and writes it to a byte buffer.
    /// </summary>
    private static void ConvertRowToTif(PixelBuffer pb, int y, Span<byte> dest)
    {
        var src = pb.GetRowSpan(y);

        switch (pb.Format)
        {
            // Two-channel formats need to be expanded to RGB
            case Format.RG_UN8:
                for (var x = 0; x < pb.Width; x++)
                {
                    dest[(x * 3) + 0] = src[(x * 2) + 0];
                    dest[(x * 3) + 1] = src[(x * 2) + 1];
                    dest[(x * 3) + 2] = 0;
                }
                break;

            case Format.RG_UI16:
            case Format.RG_UN16:
            case Format.RG_F16:
            {
                var src16 = MemoryMarshal.Cast<byte, ushort>(src);
                var dest16 = MemoryMarshal.Cast<byte, ushort>(dest);

                for (var x = 0; x < pb.Width; x++)
                {
                    dest16[(x * 3) + 0] = src16[(x * 2) + 0];
                    dest16[(x * 3) + 1] = src16[(x * 2) + 1];
                    dest16[(x * 3) + 2] = 0;
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

    /// <summary>
    /// Gets information that should be written to TIFF fields for
    /// an image with the given pixel format.
    /// </summary>
    private static FieldInfo GetFormatInfo(Format format)
    {
        return format switch
        {
            // Single-channel formats
            Format.R_UN8 => FieldInfo.Single(8),
            Format.R_UI16 => FieldInfo.Single(16),
            Format.R_UN16 => FieldInfo.Single(16),
            Format.R_F16 => FieldInfo.Single(16, SampleFormat.IEEEFP),

            // Two-channel formats (expanded to RGB)
            Format.RG_UN8 => FieldInfo.Rgb(8),
            Format.RG_UI16 => FieldInfo.Rgb(16),
            Format.RG_UN16 => FieldInfo.Rgb(16),
            Format.RG_F16 => FieldInfo.Rgb(16, SampleFormat.IEEEFP),

            // Four-channel RGBA formats
            Format.RGBA_UN8 => FieldInfo.Rgba(8),
            Format.RGBA_F16 => FieldInfo.Rgba(16, SampleFormat.IEEEFP),
            Format.RGBA_SRGB8 => FieldInfo.Rgba(8),
            Format.BGRA_UN8 => FieldInfo.Rgba(8),
            Format.BGRA_SRGB8 => FieldInfo.Rgba(8),

            _ => throw new ArgumentException("Unsupported format"),
        };
    }

    private record FieldInfo
    {
        public Photometric Photometric { get; set; }
        public SampleFormat SampleFormat { get; set; }
        public int BitsPerSample { get; set; }
        public int SamplesPerPixel { get; set; }
        public bool HasAlpha { get; set; }

        public int BytesPerPixel => ((BitsPerSample * SamplesPerPixel) + 7) / 8;

        public static FieldInfo Single(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.MINISBLACK,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 1,
        };

        public static FieldInfo Rgb(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.RGB,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 3,
        };

        public static FieldInfo Rgba(int bitsPerSample, SampleFormat format = SampleFormat.UINT) => new()
        {
            Photometric = Photometric.RGB,
            SampleFormat = format,
            BitsPerSample = bitsPerSample,
            SamplesPerPixel = 4,
            HasAlpha = true,
        };
    }

    /// <summary>
    /// Stream that passes through everything to another stream but does not close that stream
    /// when it is disposed.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Pixel data to RGBA conversion functions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packs RGBA values into a 32-bit integer.
    /// </summary>
    private static int Pack4(int r, int g, int b, int a) => r | (g << 8) | (b << 16) | (a << 24);

    /// <summary>
    /// Rescales a 16-bit integer to a byte.
    /// </summary>
    private static int ShortToByte(short v) => (v >> 8) & 0xff;

    /// <summary>
    /// Rescales a 16-bit floating point number to a byte.
    /// </summary>
    private static byte HalfToByte(Half v) => (byte)Math.Clamp(((float)v * byte.MaxValue) + 0.5f, 0, byte.MaxValue);

    /// <summary>
    /// Based on put16bitbwtile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but changed to support 16-bit floating point data. 0 is black and 1 is white.
    /// </summary>
    private static void PutFp16BitMinIsBlackTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;

        while (height-- > 0)
        {
            var wp = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan(offset));
            var wpPos = 0;

            for (x = width; x-- > 0;)
            {
                var v = HalfToByte(wp[wpPos]);

                raster[rasterOffset] = Pack4(v, v, v, 255);
                rasterOffset++;
                offset += 2 * samplesPerPixel;
                wpPos += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            offset += bufferShift;
        }
    }

    /// <summary>
    /// Based on put16bitbwtile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but changed to support 16-bit floating point data. 0 is white and 1 is black.
    /// </summary>
    private static void PutFp16BitMinIsWhiteTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;

        while (height-- > 0)
        {
            var wp = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan(offset));
            var wpPos = 0;

            for (x = width; x-- > 0;)
            {
                var v = 255 - HalfToByte(wp[wpPos]);

                raster[rasterOffset] = Pack4(v, v, v, 255);
                rasterOffset++;
                offset += 2 * samplesPerPixel;
                wpPos += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            offset += bufferShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAcontig8bittile() from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha.
    /// </summary>
    private static void PutRgbaContig8BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;
        bufferShift *= samplesPerPixel;

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = buffer[offset];
                var g = buffer[offset + 1];
                var b = buffer[offset + 2];
                var a = buffer[offset + 3];

                raster[rasterOffset] = Pack4(r, g, b, a);
                rasterOffset++;
                offset += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            offset += bufferShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAcontig16bittile() from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha.
    /// </summary>
    private static void PutRgbaContig16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;
        bufferShift *= samplesPerPixel;

        var wp = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(offset));
        var wpPos = 0;

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = ShortToByte(wp[wpPos]);
                var g = ShortToByte(wp[wpPos + 1]);
                var b = ShortToByte(wp[wpPos + 2]);
                var a = ShortToByte(wp[wpPos + 3]);

                raster[rasterOffset] = Pack4(r, g, b, a);
                rasterOffset++;
                wpPos += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            wpPos += bufferShift;
        }
    }

    /// <summary>
    /// Based on putRGBcontig16bittile() from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but changed to support 16-bit floating point data.
    /// </summary>
    private static void PutRgbContigFp16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;
        bufferShift *= samplesPerPixel;

        var wp = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan(offset));
        var wpPos = 0;

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = HalfToByte(wp[wpPos]);
                var g = HalfToByte(wp[wpPos + 1]);
                var b = HalfToByte(wp[wpPos + 2]);

                raster[rasterOffset] = Pack4(r, g, b, 255);
                rasterOffset++;
                wpPos += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            wpPos += bufferShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAcontig16bittile() from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha and changed to support 16-bit floating point data.
    /// </summary>
    private static void PutRgbaContigFp16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset,
        int bufferShift
    )
    {
        int samplesPerPixel = img.SamplesPerPixel;
        bufferShift *= samplesPerPixel;

        var wp = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan(offset));
        var wpPos = 0;

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = HalfToByte(wp[wpPos]);
                var g = HalfToByte(wp[wpPos + 1]);
                var b = HalfToByte(wp[wpPos + 2]);
                var a = HalfToByte(wp[wpPos + 3]);

                raster[rasterOffset] = Pack4(r, g, b, a);
                rasterOffset++;
                wpPos += samplesPerPixel;
            }

            rasterOffset += rasterShift;
            wpPos += bufferShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAseparate8bittile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha.
    /// </summary>
    private static void PutRgbaSeparate8BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset1,
        int offset2,
        int offset3,
        int offset4,
        int bufferShift
    )
    {
        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                int rv = buffer[offset1];
                int gv = buffer[offset2];
                int bv = buffer[offset3];
                int av = buffer[offset4];
                raster[rasterOffset] = Pack4(rv, gv, bv, av);
                rasterOffset++;
                offset1++;
                offset2++;
                offset3++;
                offset4++;
            }

            offset1 += bufferShift;
            offset2 += bufferShift;
            offset3 += bufferShift;
            offset4 += bufferShift;

            rasterOffset += rasterShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAseparate16bittile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha.
    /// </summary>
    private static void PutRgbaSeparate16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset1,
        int offset2,
        int offset3,
        int offset4,
        int bufferShift
    )
    {
        var wrgba = MemoryMarshal.Cast<byte, short>(buffer.AsSpan());

        offset1 /= sizeof(short);
        offset2 /= sizeof(short);
        offset3 /= sizeof(short);
        offset4 /= sizeof(short);

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = ShortToByte(wrgba[offset1]);
                var g = ShortToByte(wrgba[offset2]);
                var b = ShortToByte(wrgba[offset3]);
                var a = ShortToByte(wrgba[offset4]);
                raster[rasterOffset] = Pack4(r, g, b, a);
                rasterOffset++;
                offset1++;
                offset2++;
                offset3++;
                offset4++;
            }

            offset1 += bufferShift;
            offset2 += bufferShift;
            offset3 += bufferShift;
            offset4 += bufferShift;

            rasterOffset += rasterShift;
        }
    }

    /// <summary>
    /// Based on putRGBseparate16bittile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but changed to support 16-bit floating point data.
    /// </summary>
    private static void PutRgbSeparateFp16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset1,
        int offset2,
        int offset3,
        int offset4,
        int bufferShift
    )
    {
        var wrgb = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan());

        unsafe
        {
            offset1 /= sizeof(Half);
            offset2 /= sizeof(Half);
            offset3 /= sizeof(Half);
        }

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = HalfToByte(wrgb[offset1]);
                var g = HalfToByte(wrgb[offset2]);
                var b = HalfToByte(wrgb[offset3]);
                raster[rasterOffset] = Pack4(r, g, b, 255);
                rasterOffset++;
                offset1++;
                offset2++;
                offset3++;
            }

            offset1 += bufferShift;
            offset2 += bufferShift;
            offset3 += bufferShift;
            rasterOffset += rasterShift;
        }
    }

    /// <summary>
    /// Based on putRGBUAseparate16bittile from https://github.com/BitMiracle/libtiff.net/blob/master/LibTiff/TiffRgbaImage.cs
    /// but without premultiplying alpha and changed to support 16-bit floating point data.
    /// </summary>
    private static void PutRgbaSeparateFp16BitTile(
        TiffRgbaImage img,
        int[] raster,
        int rasterOffset,
        int rasterShift,
        int x,
        int y,
        int width,
        int height,
        byte[] buffer,
        int offset1,
        int offset2,
        int offset3,
        int offset4,
        int bufferShift
    )
    {
        var wrgba = MemoryMarshal.Cast<byte, Half>(buffer.AsSpan());

        unsafe
        {
            offset1 /= sizeof(Half);
            offset2 /= sizeof(Half);
            offset3 /= sizeof(Half);
            offset4 /= sizeof(Half);
        }

        while (height-- > 0)
        {
            for (x = width; x-- > 0;)
            {
                var r = HalfToByte(wrgba[offset1]);
                var g = HalfToByte(wrgba[offset2]);
                var b = HalfToByte(wrgba[offset3]);
                var a = HalfToByte(wrgba[offset4]);
                raster[rasterOffset] = Pack4(r, g, b, a);
                rasterOffset++;
                offset1++;
                offset2++;
                offset3++;
                offset4++;
            }

            offset1 += bufferShift;
            offset2 += bufferShift;
            offset3 += bufferShift;
            offset4 += bufferShift;
            rasterOffset += rasterShift;
        }
    }
}
