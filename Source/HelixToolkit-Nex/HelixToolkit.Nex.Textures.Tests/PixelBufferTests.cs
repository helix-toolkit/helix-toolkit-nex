using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class PixelBufferTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Allocates an unmanaged buffer, runs the action, then frees the buffer.
    /// </summary>
    private static void WithBuffer(int sizeBytes, Action<IntPtr> action)
    {
        IntPtr ptr = Marshal.AllocHGlobal(sizeBytes);
        try
        {
            action(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // RGBA_UN8 is 4 bytes per pixel — convenient for uint pixel values.
    private const Format TestFormat = Format.RGBA_UN8;
    private const int PixelBytes = 4; // 32 bpp / 8

    private static PixelBuffer MakeBuffer(int width, int height, IntPtr ptr, int? rowStride = null)
    {
        int stride = rowStride ?? width * PixelBytes;
        int bufferStride = stride * height;
        return new PixelBuffer(width, height, TestFormat, stride, bufferStride, ptr);
    }

    // -------------------------------------------------------------------------
    // Unit tests — constructor / error paths
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Constructor_IntPtrZero_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            new PixelBuffer(4, 4, TestFormat, 16, 64, IntPtr.Zero)
        );
    }

    [TestMethod]
    public void CopyTo_MismatchedWidth_ThrowsArgumentException()
    {
        WithBuffer(
            16,
            src =>
                WithBuffer(
                    32,
                    dst =>
                    {
                        var srcBuf = new PixelBuffer(2, 2, TestFormat, 8, 16, src);
                        var dstBuf = new PixelBuffer(4, 2, TestFormat, 16, 32, dst);
                        Assert.ThrowsException<ArgumentException>(() => srcBuf.CopyTo(dstBuf));
                    }
                )
        );
    }

    [TestMethod]
    public void CopyTo_MismatchedHeight_ThrowsArgumentException()
    {
        WithBuffer(
            16,
            src =>
                WithBuffer(
                    32,
                    dst =>
                    {
                        var srcBuf = new PixelBuffer(2, 2, TestFormat, 8, 16, src);
                        var dstBuf = new PixelBuffer(2, 4, TestFormat, 8, 32, dst);
                        Assert.ThrowsException<ArgumentException>(() => srcBuf.CopyTo(dstBuf));
                    }
                )
        );
    }

    [TestMethod]
    public void FormatSetter_DifferentPixelSize_ThrowsArgumentException()
    {
        WithBuffer(
            16,
            ptr =>
            {
                var buf = new PixelBuffer(2, 2, Format.RGBA_UN8, 8, 16, ptr);
                // R_UN8 is 1 byte per pixel vs 4 bytes for RGBA_UN8
                Assert.ThrowsException<ArgumentException>(() => buf.Format = Format.R_UN8);
            }
        );
    }

    [TestMethod]
    public void FormatSetter_SamePixelSize_UpdatesFormat()
    {
        WithBuffer(
            16,
            ptr =>
            {
                // RGBA_UN8 and BGRA_UN8 are both 4 bytes per pixel
                var buf = new PixelBuffer(2, 2, Format.RGBA_UN8, 8, 16, ptr);
                buf.Format = Format.BGRA_UN8;
                Assert.AreEqual(Format.BGRA_UN8, buf.Format);
            }
        );
    }

    [TestMethod]
    public void GetPixel_SetPixel_BasicCorrectness()
    {
        WithBuffer(
            4 * 4 * PixelBytes,
            ptr =>
            {
                var buf = MakeBuffer(4, 4, ptr);
                uint expected = 0xDEADBEEF;
                buf.SetPixel<uint>(2, 3, expected);
                uint actual = buf.GetPixel<uint>(2, 3);
                Assert.AreEqual(expected, actual);
            }
        );
    }

    [TestMethod]
    public void CopyTo_SameStride_BulkCopyPath_PreservesPixels()
    {
        int w = 4,
            h = 4;
        int stride = w * PixelBytes;
        int bufSize = stride * h;

        WithBuffer(
            bufSize,
            src =>
                WithBuffer(
                    bufSize,
                    dst =>
                    {
                        var srcBuf = new PixelBuffer(w, h, TestFormat, stride, bufSize, src);
                        var dstBuf = new PixelBuffer(w, h, TestFormat, stride, bufSize, dst);

                        // Write distinct values to each pixel in source
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                srcBuf.SetPixel<uint>(x, y, (uint)(y * w + x + 1));

                        srcBuf.CopyTo(dstBuf);

                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                Assert.AreEqual(
                                    srcBuf.GetPixel<uint>(x, y),
                                    dstBuf.GetPixel<uint>(x, y),
                                    $"Pixel mismatch at ({x},{y})"
                                );
                    }
                )
        );
    }

    [TestMethod]
    public void CopyTo_DifferentStride_RowByRowPath_PreservesPixels()
    {
        int w = 4,
            h = 4;
        int srcStride = w * PixelBytes; // tight
        int dstStride = w * PixelBytes + 8; // padded (different stride)
        int srcBufSize = srcStride * h;
        int dstBufSize = dstStride * h;

        WithBuffer(
            srcBufSize,
            src =>
                WithBuffer(
                    dstBufSize,
                    dst =>
                    {
                        var srcBuf = new PixelBuffer(w, h, TestFormat, srcStride, srcBufSize, src);
                        var dstBuf = new PixelBuffer(w, h, TestFormat, dstStride, dstBufSize, dst);

                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                srcBuf.SetPixel<uint>(x, y, (uint)(y * w + x + 100));

                        srcBuf.CopyTo(dstBuf);

                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                Assert.AreEqual(
                                    srcBuf.GetPixel<uint>(x, y),
                                    dstBuf.GetPixel<uint>(x, y),
                                    $"Pixel mismatch at ({x},{y})"
                                );
                    }
                )
        );
    }

    // -------------------------------------------------------------------------
    // Property 11: PixelBuffer SetPixel/GetPixel round-trip
    // Feature: texture-loading, Property 11: For any valid PixelBuffer,
    // uncompressed format, valid (x,y), SetPixel then GetPixel returns original value
    // Validates: Requirements 13.2, 13.3
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property11_SetPixel_GetPixel_RoundTrip()
    {
        // Generate: width [1,64], height [1,64], x in [0,w-1], y in [0,h-1], pixel value
        var gen =
            from w in Gen.Choose(1, 64)
            from h in Gen.Choose(1, 64)
            from x in Gen.Choose(0, w - 1)
            from y in Gen.Choose(0, h - 1)
            from value in Gen.Choose(0, int.MaxValue).Select(v => (uint)v)
            select (w, h, x, y, value);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h, int x, int y, uint value) t) =>
                {
                    int stride = t.w * PixelBytes;
                    int bufSize = stride * t.h;
                    IntPtr ptr = Marshal.AllocHGlobal(bufSize);
                    try
                    {
                        var buf = new PixelBuffer(t.w, t.h, TestFormat, stride, bufSize, ptr);
                        buf.SetPixel<uint>(t.x, t.y, t.value);
                        uint actual = buf.GetPixel<uint>(t.x, t.y);
                        return actual == t.value;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Property 12: PixelBuffer CopyTo preserves pixel data
    // Feature: texture-loading, Property 12: For any two PixelBuffers with same
    // dimensions and format, after CopyTo all pixels are equal
    // Validates: Requirements 13.4
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property12_CopyTo_PreservesAllPixelData()
    {
        // Generate: width [1,32], height [1,32], optional destination padding [0,16]
        var gen =
            from w in Gen.Choose(1, 32)
            from h in Gen.Choose(1, 32)
            from dstPad in Gen.Choose(0, 16)
            select (w, h, dstPad);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h, int dstPad) t) =>
                {
                    int srcStride = t.w * PixelBytes;
                    int dstStride = t.w * PixelBytes + t.dstPad;
                    int srcBufSize = srcStride * t.h;
                    int dstBufSize = dstStride * t.h;

                    IntPtr srcPtr = Marshal.AllocHGlobal(srcBufSize);
                    IntPtr dstPtr = Marshal.AllocHGlobal(dstBufSize);
                    try
                    {
                        var srcBuf = new PixelBuffer(
                            t.w,
                            t.h,
                            TestFormat,
                            srcStride,
                            srcBufSize,
                            srcPtr
                        );
                        var dstBuf = new PixelBuffer(
                            t.w,
                            t.h,
                            TestFormat,
                            dstStride,
                            dstBufSize,
                            dstPtr
                        );

                        // Fill source with deterministic values
                        for (int y = 0; y < t.h; y++)
                            for (int x = 0; x < t.w; x++)
                                srcBuf.SetPixel<uint>(x, y, (uint)(y * t.w + x + 1));

                        srcBuf.CopyTo(dstBuf);

                        // Verify all pixels match
                        for (int y = 0; y < t.h; y++)
                            for (int x = 0; x < t.w; x++)
                                if (srcBuf.GetPixel<uint>(x, y) != dstBuf.GetPixel<uint>(x, y))
                                    return false;

                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(srcPtr);
                        Marshal.FreeHGlobal(dstPtr);
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
