using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Unit tests for Image.Load source variants and Image.Register.
/// Requirements: 8.1-8.7, 9.4
/// </summary>
[TestClass]
[DoNotParallelize]
public class ImageLoadTests
{
    [TestInitialize]
    public void Setup()
    {
        // Ensure the Tga slot always has a null-returning loader so it doesn't interfere
        Image.Register(ImageFileType.Tga, (_, _, _, _) => null, (_, _, _, _) => { });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const uint DDS_MAGIC = 0x20534444u;
    private const uint DDPF_RGBA = 0x00000041u;
    private const uint DDSD_CAPS = 0x00000001u;
    private const uint DDSD_HEIGHT = 0x00000002u;
    private const uint DDSD_WIDTH = 0x00000004u;
    private const uint DDSD_PIXELFORMAT = 0x00001000u;
    private const int DDS_PIXELFORMAT_SIZE = 32;
    private const int DDS_HEADER_SIZE = 124;

    private static void WriteUInt32(byte[] buf, ref int pos, uint value)
    {
        buf[pos++] = (byte)(value & 0xff);
        buf[pos++] = (byte)((value >> 8) & 0xff);
        buf[pos++] = (byte)((value >> 16) & 0xff);
        buf[pos++] = (byte)((value >> 24) & 0xff);
    }

    /// <summary>Builds a minimal valid DDS file for a 2D RGBA8 texture.</summary>
    private static byte[] BuildMinimalDds2D(int width = 4, int height = 4)
    {
        int pixelDataSize = width * height * 4;
        int totalSize = 4 + DDS_HEADER_SIZE + pixelDataSize;
        var buf = new byte[totalSize];
        int pos = 0;

        WriteUInt32(buf, ref pos, DDS_MAGIC);
        WriteUInt32(buf, ref pos, (uint)DDS_HEADER_SIZE);
        WriteUInt32(buf, ref pos, DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT);
        WriteUInt32(buf, ref pos, (uint)height);
        WriteUInt32(buf, ref pos, (uint)width);
        WriteUInt32(buf, ref pos, (uint)(width * 4));
        WriteUInt32(buf, ref pos, 1u);
        WriteUInt32(buf, ref pos, 1u);
        for (int i = 0; i < 11; i++)
            WriteUInt32(buf, ref pos, 0u);

        WriteUInt32(buf, ref pos, (uint)DDS_PIXELFORMAT_SIZE);
        WriteUInt32(buf, ref pos, DDPF_RGBA);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 32u);
        WriteUInt32(buf, ref pos, 0x000000ffu);
        WriteUInt32(buf, ref pos, 0x0000ff00u);
        WriteUInt32(buf, ref pos, 0x00ff0000u);
        WriteUInt32(buf, ref pos, 0xff000000u);

        WriteUInt32(buf, ref pos, 0x00001000u);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 0u);

        return buf;
    }

    // -------------------------------------------------------------------------
    // Req 8.1: Load from Stream
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromStream_ReturnsValidImage()
    {
        var data = BuildMinimalDds2D();
        using var ms = new MemoryStream(data);
        using var image = Image.Load(ms);
        Assert.IsNotNull(image);
        Assert.AreEqual(TextureDimension.Texture2D, image!.Description.Dimension);
        Assert.AreEqual(4, image.Description.Width);
        Assert.AreEqual(4, image.Description.Height);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
    }

    // -------------------------------------------------------------------------
    // Req 8.2: Load from file path
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromFilePath_ReturnsValidImage()
    {
        var data = BuildMinimalDds2D(8, 8);
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, data);
            using var image = Image.Load(tempFile);
            Assert.IsNotNull(image);
            Assert.AreEqual(8, image!.Description.Width);
            Assert.AreEqual(8, image.Description.Height);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // -------------------------------------------------------------------------
    // Req 8.3: Load from byte array - small array (copy path)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromByteArray_Small_ReturnsValidImage()
    {
        // Small array (< 85KB) - should copy
        var data = BuildMinimalDds2D(4, 4);
        Assert.IsTrue(data.Length < 85 * 1024, "Test data should be small");
        using var image = Image.Load(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(4, image!.Description.Width);
    }

    // -------------------------------------------------------------------------
    // Req 8.3: Load from byte array - large array (pin path)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromByteArray_Large_ReturnsValidImage()
    {
        // Build a large DDS (> 85KB) - use a 160x160 RGBA8 texture
        var data = BuildMinimalDds2D(160, 160);
        Assert.IsTrue(data.Length > 85 * 1024, "Test data should be large (> 85KB)");
        using var image = Image.Load(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(160, image!.Description.Width);
        Assert.AreEqual(160, image.Description.Height);
    }

    // -------------------------------------------------------------------------
    // Req 8.4, 8.5: Load from IntPtr - makeACopy = true
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromIntPtr_MakeACopyTrue_ReturnsValidImage()
    {
        var data = BuildMinimalDds2D();
        unsafe
        {
            fixed (byte* ptr = data)
            {
                using var image = Image.Load((IntPtr)ptr, data.Length, makeACopy: true);
                Assert.IsNotNull(image);
                Assert.AreEqual(4, image!.Description.Width);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Req 8.4, 8.6: Load from IntPtr - makeACopy = false (ownership transfer)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_FromIntPtr_MakeACopyFalse_ReturnsValidImage()
    {
        var data = BuildMinimalDds2D();
        // Allocate unmanaged copy so the image can own it
        IntPtr ptr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, ptr, data.Length);
        // makeACopy = false: image takes ownership of the buffer
        // The DDS codec may copy internally for conversion, so we just verify no crash
        using var image = Image.Load(ptr, data.Length, makeACopy: false);
        Assert.IsNotNull(image);
        Assert.AreEqual(4, image!.Description.Width);
        // Note: ptr ownership is transferred to the image; do not free it here
    }

    // -------------------------------------------------------------------------
    // Req 8.7: No loader matches - returns null
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_NoLoaderMatches_ReturnsNull()
    {
        // Pass garbage data that no loader can decode (128 bytes of non-DDS data)
        var garbage = new byte[128];
        for (int i = 0; i < garbage.Length; i++)
            garbage[i] = (byte)(i + 1); // starts with 0x01, not DDS magic 0x44

        using var image = Image.Load(garbage);
        Assert.IsNull(image);
    }

    // -------------------------------------------------------------------------
    // Req 9.4: Register with both null delegates throws ArgumentNullException
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Register_BothDelegatesNull_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            Image.Register(ImageFileType.Tga, null, null)
        );
    }

    // -------------------------------------------------------------------------
    // Req 9.1, 9.2: Register replaces existing loader
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Register_ReplacesExistingLoader_NewLoaderIsUsed()
    {
        // Use garbage data that DDS loader will reject (invalid magic), so only
        // our registered Tga loader will succeed.
        var garbage = new byte[128];
        garbage[0] = 0xAB; // not DDS magic

        Image? FirstLoader(IntPtr p, int size, bool copy, GCHandle? handle)
        {
            return Image.New2D(1, 1, 1, Format.RGBA_UN8);
        }

        Image? SecondLoader(IntPtr p, int size, bool copy, GCHandle? handle)
        {
            return Image.New2D(2, 2, 1, Format.RGBA_UN8);
        }

        // Register first loader
        Image.Register(ImageFileType.Tga, FirstLoader, null);

        Image? result1;
        unsafe
        {
            fixed (byte* ptr = garbage)
                result1 = Image.Load((IntPtr)ptr, garbage.Length, true);
        }
        Assert.IsNotNull(result1, "First loader should have returned a non-null image");
        Assert.AreEqual(1, result1!.Description.Width, "First loader should return 1x1 image");
        result1.Dispose();

        // Replace with second loader
        Image.Register(ImageFileType.Tga, SecondLoader, null);

        Image? result2;
        unsafe
        {
            fixed (byte* ptr = garbage)
                result2 = Image.Load((IntPtr)ptr, garbage.Length, true);
        }
        Assert.IsNotNull(result2, "Replacement loader should have returned a non-null image");
        Assert.AreEqual(
            2,
            result2!.Description.Width,
            "Replacement loader should return 2x2 image"
        );
        result2.Dispose();

        // Clean up: replace with a loader that always returns null (no-op saver to avoid ArgumentNullException)
        Image.Register(ImageFileType.Tga, (_, _, _, _) => null, (_, _, _, _) => { });
    }

    // -------------------------------------------------------------------------
    // Save round-trip: Save to stream then load back
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Save_ToDdsStream_ThenLoad_ProducesEquivalentImage()
    {
        using var original = Image.New2D(4, 4, 1, Format.RGBA_UN8);

        // Write known pixel data
        unsafe
        {
            var pb = original.GetPixelBuffer(0, 0);
            var ptr = (byte*)pb.DataPointer;
            for (int i = 0; i < pb.BufferStride; i++)
                ptr[i] = (byte)(i & 0xFF);
        }

        using var ms = new MemoryStream();
        original.Save(ms, ImageFileType.Dds);
        ms.Position = 0;

        using var loaded = Image.Load(ms);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(original.Description.Width, loaded!.Description.Width);
        Assert.AreEqual(original.Description.Height, loaded.Description.Height);
        Assert.AreEqual(original.Description.Format, loaded.Description.Format);
        Assert.AreEqual(original.Description.MipLevels, loaded.Description.MipLevels);
    }

    // -------------------------------------------------------------------------
    // Save to file path
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Save_ToFilePath_ThenLoad_ProducesEquivalentImage()
    {
        using var original = Image.New2D(8, 8, 1, Format.RGBA_UN8);
        string tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".dds");
        try
        {
            original.Save(tempFile, ImageFileType.Dds);
            using var loaded = Image.Load(tempFile);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(8, loaded!.Description.Width);
            Assert.AreEqual(8, loaded.Description.Height);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
