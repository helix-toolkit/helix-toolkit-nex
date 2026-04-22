using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Unit tests for DDSCodec loading — error paths and successful loading scenarios.
/// </summary>
[TestClass]
public class DDSCodecTests
{
    // -------------------------------------------------------------------------
    // Helpers to build minimal valid DDS data in memory
    // -------------------------------------------------------------------------

    private const uint DDS_MAGIC = 0x20534444u; // "DDS "
    private const uint DDPF_FOURCC = 0x00000004u;
    private const uint DDPF_RGBA = 0x00000041u;
    private const uint DDSD_CAPS = 0x00000001u;
    private const uint DDSD_HEIGHT = 0x00000002u;
    private const uint DDSD_WIDTH = 0x00000004u;
    private const uint DDSD_PIXELFORMAT = 0x00001000u;
    private const uint DDSD_DEPTH = 0x00800000u;
    private const uint DDSCAPS2_CUBEMAP = 0x00000200u;
    private const uint DDSCAPS2_CUBEMAP_ALLFACES = 0x0000fe00u;

    // DDS_PIXELFORMAT size = 32 bytes, DDS_HEADER size = 124 bytes
    private const int DDS_PIXELFORMAT_SIZE = 32;
    private const int DDS_HEADER_SIZE = 124;
    private const int DDS_HEADER_DXT10_SIZE = 20;

    // DXGI format values
    private const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28u;
    private const uint DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29u;
    private const uint D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3u;
    private const uint D3D10_RESOURCE_DIMENSION_TEXTURE3D = 4u;
    private const uint DDS_RESOURCE_MISC_TEXTURECUBE = 0x4u;

    private static uint MakeFourCC(char a, char b, char c, char d) =>
        (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

    /// <summary>
    /// Builds a minimal valid DDS file for a 2D RGBA8 texture.
    /// </summary>
    private static byte[] BuildMinimalDds2D(int width, int height, int mipLevels = 1, byte[]? pixelData = null)
    {
        // Pixel data: width * height * 4 bytes per mip (simplified: just mip0)
        int pixelDataSize = width * height * 4;
        if (pixelData == null)
            pixelData = new byte[pixelDataSize];

        int totalSize = 4 + DDS_HEADER_SIZE + pixelData.Length;
        var buf = new byte[totalSize];
        int pos = 0;

        // Magic
        WriteUInt32(buf, ref pos, DDS_MAGIC);

        // DDS_HEADER
        WriteUInt32(buf, ref pos, (uint)DDS_HEADER_SIZE);                          // Size
        WriteUInt32(buf, ref pos, DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT); // Flags
        WriteUInt32(buf, ref pos, (uint)height);                                   // Height
        WriteUInt32(buf, ref pos, (uint)width);                                    // Width
        WriteUInt32(buf, ref pos, (uint)(width * 4));                              // PitchOrLinearSize
        WriteUInt32(buf, ref pos, 1u);                                             // Depth
        WriteUInt32(buf, ref pos, (uint)mipLevels);                                // MipMapCount
        // Reserved1[11]
        for (int i = 0; i < 11; i++)
            WriteUInt32(buf, ref pos, 0u);

        // DDS_PIXELFORMAT
        WriteUInt32(buf, ref pos, (uint)DDS_PIXELFORMAT_SIZE);  // Size
        WriteUInt32(buf, ref pos, DDPF_RGBA);                   // Flags
        WriteUInt32(buf, ref pos, 0u);                          // FourCC
        WriteUInt32(buf, ref pos, 32u);                         // RGBBitCount
        WriteUInt32(buf, ref pos, 0x000000ffu);                 // RBitMask (R8G8B8A8: R in low byte)
        WriteUInt32(buf, ref pos, 0x0000ff00u);                 // GBitMask
        WriteUInt32(buf, ref pos, 0x00ff0000u);                 // BBitMask
        WriteUInt32(buf, ref pos, 0xff000000u);                 // ABitMask

        // Caps, Caps2, Caps3, Caps4, Reserved2
        WriteUInt32(buf, ref pos, 0x00001000u); // Caps (DDSCAPS_TEXTURE)
        WriteUInt32(buf, ref pos, 0u);          // Caps2
        WriteUInt32(buf, ref pos, 0u);          // Caps3
        WriteUInt32(buf, ref pos, 0u);          // Caps4
        WriteUInt32(buf, ref pos, 0u);          // Reserved2

        // Pixel data
        Array.Copy(pixelData, 0, buf, pos, pixelData.Length);
        return buf;
    }

    /// <summary>
    /// Builds a minimal valid DDS file with a DX10 extended header.
    /// </summary>
    private static byte[] BuildDx10Dds(int width, int height, uint dxgiFormat, uint resourceDimension,
        uint arraySize = 1, uint miscFlag = 0, byte[]? pixelData = null)
    {
        int pixelDataSize = width * height * 4 * (int)arraySize;
        if (pixelData == null)
            pixelData = new byte[pixelDataSize];

        int totalSize = 4 + DDS_HEADER_SIZE + DDS_HEADER_DXT10_SIZE + pixelData.Length;
        var buf = new byte[totalSize];
        int pos = 0;

        // Magic
        WriteUInt32(buf, ref pos, DDS_MAGIC);

        // DDS_HEADER
        WriteUInt32(buf, ref pos, (uint)DDS_HEADER_SIZE);
        WriteUInt32(buf, ref pos, DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT);
        WriteUInt32(buf, ref pos, (uint)height);
        WriteUInt32(buf, ref pos, (uint)width);
        WriteUInt32(buf, ref pos, (uint)(width * 4));
        WriteUInt32(buf, ref pos, 1u);  // Depth
        WriteUInt32(buf, ref pos, 1u);  // MipMapCount
        for (int i = 0; i < 11; i++)
            WriteUInt32(buf, ref pos, 0u); // Reserved1

        // DDS_PIXELFORMAT with DX10 FourCC
        WriteUInt32(buf, ref pos, (uint)DDS_PIXELFORMAT_SIZE);
        WriteUInt32(buf, ref pos, DDPF_FOURCC);
        WriteUInt32(buf, ref pos, MakeFourCC('D', 'X', '1', '0')); // FourCC = DX10
        WriteUInt32(buf, ref pos, 0u); // RGBBitCount
        WriteUInt32(buf, ref pos, 0u); // RBitMask
        WriteUInt32(buf, ref pos, 0u); // GBitMask
        WriteUInt32(buf, ref pos, 0u); // BBitMask
        WriteUInt32(buf, ref pos, 0u); // ABitMask

        WriteUInt32(buf, ref pos, 0x00001000u); // Caps
        WriteUInt32(buf, ref pos, 0u);          // Caps2
        WriteUInt32(buf, ref pos, 0u);          // Caps3
        WriteUInt32(buf, ref pos, 0u);          // Caps4
        WriteUInt32(buf, ref pos, 0u);          // Reserved2

        // DDS_HEADER_DXT10
        WriteUInt32(buf, ref pos, dxgiFormat);
        WriteUInt32(buf, ref pos, resourceDimension);
        WriteUInt32(buf, ref pos, miscFlag);
        WriteUInt32(buf, ref pos, arraySize);
        WriteUInt32(buf, ref pos, 0u); // MiscFlags2

        // Pixel data
        Array.Copy(pixelData, 0, buf, pos, pixelData.Length);
        return buf;
    }

    /// <summary>
    /// Builds a minimal valid DDS cubemap (legacy header, all 6 faces).
    /// </summary>
    private static byte[] BuildCubemapDds(int faceSize)
    {
        int faceDataSize = faceSize * faceSize * 4;
        int pixelDataSize = faceDataSize * 6;
        var pixelData = new byte[pixelDataSize];

        int totalSize = 4 + DDS_HEADER_SIZE + pixelDataSize;
        var buf = new byte[totalSize];
        int pos = 0;

        WriteUInt32(buf, ref pos, DDS_MAGIC);

        // DDS_HEADER
        WriteUInt32(buf, ref pos, (uint)DDS_HEADER_SIZE);
        WriteUInt32(buf, ref pos, DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT);
        WriteUInt32(buf, ref pos, (uint)faceSize);
        WriteUInt32(buf, ref pos, (uint)faceSize);
        WriteUInt32(buf, ref pos, (uint)(faceSize * 4));
        WriteUInt32(buf, ref pos, 1u);
        WriteUInt32(buf, ref pos, 1u);
        for (int i = 0; i < 11; i++)
            WriteUInt32(buf, ref pos, 0u);

        // DDS_PIXELFORMAT (RGBA8)
        WriteUInt32(buf, ref pos, (uint)DDS_PIXELFORMAT_SIZE);
        WriteUInt32(buf, ref pos, DDPF_RGBA);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 32u);
        WriteUInt32(buf, ref pos, 0x000000ffu);
        WriteUInt32(buf, ref pos, 0x0000ff00u);
        WriteUInt32(buf, ref pos, 0x00ff0000u);
        WriteUInt32(buf, ref pos, 0xff000000u);

        WriteUInt32(buf, ref pos, 0x00001008u); // Caps (DDSCAPS_TEXTURE | DDSCAPS_COMPLEX)
        WriteUInt32(buf, ref pos, DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_ALLFACES); // Caps2
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 0u);
        WriteUInt32(buf, ref pos, 0u);

        Array.Copy(pixelData, 0, buf, pos, pixelData.Length);
        return buf;
    }

    private static void WriteUInt32(byte[] buf, ref int pos, uint value)
    {
        buf[pos++] = (byte)(value & 0xff);
        buf[pos++] = (byte)((value >> 8) & 0xff);
        buf[pos++] = (byte)((value >> 16) & 0xff);
        buf[pos++] = (byte)((value >> 24) & 0xff);
    }

    private static Image? LoadFromBytes(byte[] data)
    {
        unsafe
        {
            fixed (byte* ptr = data)
                return Image.Load((IntPtr)ptr, data.Length, true);
        }
    }

    // -------------------------------------------------------------------------
    // Error path tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_DataTooSmall_ReturnsNull()
    {
        // Less than 128 bytes (minimum DDS header size)
        var tinyData = new byte[64];
        var result = LoadFromBytes(tinyData);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Load_InvalidMagicNumber_ReturnsNull()
    {
        var data = BuildMinimalDds2D(4, 4);
        // Corrupt the magic number
        data[0] = 0xFF;
        data[1] = 0xFF;
        data[2] = 0xFF;
        data[3] = 0xFF;
        var result = LoadFromBytes(data);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Load_InvalidHeaderSize_ReturnsNull()
    {
        var data = BuildMinimalDds2D(4, 4);
        // Corrupt the DDS_HEADER.Size field (bytes 4-7)
        data[4] = 0x00;
        data[5] = 0x00;
        data[6] = 0x00;
        data[7] = 0x00;
        var result = LoadFromBytes(data);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Load_InvalidPixelFormatSize_ReturnsNull()
    {
        var data = BuildMinimalDds2D(4, 4);
        // DDS_PIXELFORMAT.Size is at offset 4 + 124 - 32 = 96 (within header)
        // Actually: magic(4) + header fields before pixelformat:
        // Size(4)+Flags(4)+Height(4)+Width(4)+Pitch(4)+Depth(4)+MipMapCount(4)+Reserved1(44) = 72 bytes
        // So pixelformat starts at offset 4 + 72 = 76
        int pfOffset = 4 + 72;
        data[pfOffset] = 0x00;
        data[pfOffset + 1] = 0x00;
        data[pfOffset + 2] = 0x00;
        data[pfOffset + 3] = 0x00;
        var result = LoadFromBytes(data);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Load_DX10Header_ArraySizeZero_ThrowsInvalidOperationException()
    {
        var data = BuildDx10Dds(4, 4, DXGI_FORMAT_R8G8B8A8_UNORM, D3D10_RESOURCE_DIMENSION_TEXTURE2D, arraySize: 0);
        Assert.ThrowsException<InvalidOperationException>(() => LoadFromBytes(data));
    }

    [TestMethod]
    public void Load_CubemapWithoutAllFaces_ThrowsInvalidOperationException()
    {
        // Build a cubemap DDS but only set some face flags (not all 6)
        var data = BuildCubemapDds(4);
        // Overwrite Caps2 to only have CUBEMAP flag but not all faces
        // Caps2 is at: magic(4) + header_before_caps2
        // DDS_HEADER layout: Size(4)+Flags(4)+Height(4)+Width(4)+Pitch(4)+Depth(4)+MipMapCount(4)+Reserved1(44)+PixelFormat(32)+Caps(4) = 108 bytes
        // Caps2 is at offset 4 + 108 = 112
        int caps2Offset = 4 + 108;
        // Set only DDSCAPS2_CUBEMAP without all faces
        uint caps2 = DDSCAPS2_CUBEMAP; // missing face flags
        data[caps2Offset] = (byte)(caps2 & 0xff);
        data[caps2Offset + 1] = (byte)((caps2 >> 8) & 0xff);
        data[caps2Offset + 2] = (byte)((caps2 >> 16) & 0xff);
        data[caps2Offset + 3] = (byte)((caps2 >> 24) & 0xff);
        Assert.ThrowsException<InvalidOperationException>(() => LoadFromBytes(data));
    }

    [TestMethod]
    public void Load_Texture3D_DX10_ArraySizeGreaterThan1_ThrowsInvalidOperationException()
    {
        // DX10 Texture3D with ArraySize > 1 should throw
        var data = BuildDx10Dds(4, 4, DXGI_FORMAT_R8G8B8A8_UNORM, D3D10_RESOURCE_DIMENSION_TEXTURE3D, arraySize: 2);
        // Also need to set DDSD_DEPTH flag in the header flags
        // Header flags are at offset 4+4 = 8
        int flagsOffset = 4 + 4;
        uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_DEPTH;
        data[flagsOffset] = (byte)(flags & 0xff);
        data[flagsOffset + 1] = (byte)((flags >> 8) & 0xff);
        data[flagsOffset + 2] = (byte)((flags >> 16) & 0xff);
        data[flagsOffset + 3] = (byte)((flags >> 24) & 0xff);
        // Set depth in header (offset 4+20 = 24)
        int depthOffset = 4 + 20;
        data[depthOffset] = 4;
        data[depthOffset + 1] = 0;
        data[depthOffset + 2] = 0;
        data[depthOffset + 3] = 0;
        Assert.ThrowsException<InvalidOperationException>(() => LoadFromBytes(data));
    }

    // -------------------------------------------------------------------------
    // Successful loading tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Load_Minimal2D_RGBA8_ReturnsCorrectDescription()
    {
        var data = BuildMinimalDds2D(4, 4);
        using var image = LoadFromBytes(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(TextureDimension.Texture2D, image!.Description.Dimension);
        Assert.AreEqual(4, image.Description.Width);
        Assert.AreEqual(4, image.Description.Height);
        Assert.AreEqual(1, image.Description.Depth);
        Assert.AreEqual(1, image.Description.ArraySize);
        Assert.AreEqual(1, image.Description.MipLevels);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
    }

    [TestMethod]
    public void Load_DX10Header_2D_RGBA8_ReturnsCorrectDescription()
    {
        var data = BuildDx10Dds(8, 8, DXGI_FORMAT_R8G8B8A8_UNORM, D3D10_RESOURCE_DIMENSION_TEXTURE2D);
        using var image = LoadFromBytes(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(TextureDimension.Texture2D, image!.Description.Dimension);
        Assert.AreEqual(8, image.Description.Width);
        Assert.AreEqual(8, image.Description.Height);
        Assert.AreEqual(1, image.Description.ArraySize);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
    }

    [TestMethod]
    public void Load_DX10Header_Cubemap_ReturnsTextureCubeWithArraySize6()
    {
        // DX10 cubemap: Texture2D with MiscFlag = DDS_RESOURCE_MISC_TEXTURECUBE, ArraySize = 1
        // The codec multiplies ArraySize by 6 for cubemaps
        var data = BuildDx10Dds(4, 4, DXGI_FORMAT_R8G8B8A8_UNORM, D3D10_RESOURCE_DIMENSION_TEXTURE2D,
            arraySize: 1, miscFlag: DDS_RESOURCE_MISC_TEXTURECUBE,
            pixelData: new byte[4 * 4 * 4 * 6]);
        using var image = LoadFromBytes(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(TextureDimension.TextureCube, image!.Description.Dimension);
        Assert.AreEqual(6, image.Description.ArraySize);
    }

    [TestMethod]
    public void Load_LegacyCubemap_ReturnsTextureCubeWithArraySize6()
    {
        var data = BuildCubemapDds(4);
        using var image = LoadFromBytes(data);
        Assert.IsNotNull(image);
        Assert.AreEqual(TextureDimension.TextureCube, image!.Description.Dimension);
        Assert.AreEqual(6, image.Description.ArraySize);
        Assert.AreEqual(4, image.Description.Width);
        Assert.AreEqual(4, image.Description.Height);
    }

    [TestMethod]
    public void Load_2D_PixelDataIsPreserved()
    {
        // Create a 2x2 RGBA8 image with known pixel values
        // Note: DDS legacy RGBA8 maps A8B8G8R8 (R in low byte) to R8G8B8A8_UNorm
        var pixelData = new byte[]
        {
            0x11, 0x22, 0x33, 0xFF,  // pixel (0,0): R=0x11, G=0x22, B=0x33, A=0xFF
            0x44, 0x55, 0x66, 0xFF,  // pixel (1,0)
            0x77, 0x88, 0x99, 0xFF,  // pixel (0,1)
            0xAA, 0xBB, 0xCC, 0xFF,  // pixel (1,1)
        };
        var data = BuildMinimalDds2D(2, 2, pixelData: pixelData);
        using var image = LoadFromBytes(data);
        Assert.IsNotNull(image);
        var pb = image!.GetPixelBuffer(0, 0);
        Assert.AreEqual(2, pb.Width);
        Assert.AreEqual(2, pb.Height);
        Assert.AreEqual(8, pb.RowStride); // 2 pixels * 4 bytes
    }
}
