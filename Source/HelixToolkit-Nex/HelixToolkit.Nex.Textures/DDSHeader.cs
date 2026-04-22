using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Textures;

internal static class DDSConstants
{
    public const uint MagicHeader = 0x20534444; // "DDS "

    // DDS_PIXELFORMAT flags
    public const uint DDPF_FOURCC = 0x00000004;
    public const uint DDPF_RGB = 0x00000040;
    public const uint DDPF_RGBA = 0x00000041;
    public const uint DDPF_LUMINANCE = 0x00020000;
    public const uint DDPF_LUMINANCEALPHA = 0x00020001;
    public const uint DDPF_ALPHA = 0x00000002;
    public const uint DDPF_PAL8 = 0x00000020;

    // DDS_HEADER flags
    public const uint DDSD_CAPS = 0x00000001;
    public const uint DDSD_HEIGHT = 0x00000002;
    public const uint DDSD_WIDTH = 0x00000004;
    public const uint DDSD_PITCH = 0x00000008;
    public const uint DDSD_PIXELFORMAT = 0x00001000;
    public const uint DDSD_MIPMAPCOUNT = 0x00020000;
    public const uint DDSD_LINEARSIZE = 0x00080000;
    public const uint DDSD_DEPTH = 0x00800000;

    // DDS_HEADER caps2 flags (cubemap)
    public const uint DDSCAPS2_CUBEMAP = 0x00000200;
    public const uint DDSCAPS2_CUBEMAP_POSITIVEX = 0x00000600;
    public const uint DDSCAPS2_CUBEMAP_NEGATIVEX = 0x00000a00;
    public const uint DDSCAPS2_CUBEMAP_POSITIVEY = 0x00001200;
    public const uint DDSCAPS2_CUBEMAP_NEGATIVEY = 0x00002200;
    public const uint DDSCAPS2_CUBEMAP_POSITIVEZ = 0x00004200;
    public const uint DDSCAPS2_CUBEMAP_NEGATIVEZ = 0x00008200;
    public const uint DDSCAPS2_CUBEMAP_ALLFACES =
        DDSCAPS2_CUBEMAP_POSITIVEX
        | DDSCAPS2_CUBEMAP_NEGATIVEX
        | DDSCAPS2_CUBEMAP_POSITIVEY
        | DDSCAPS2_CUBEMAP_NEGATIVEY
        | DDSCAPS2_CUBEMAP_POSITIVEZ
        | DDSCAPS2_CUBEMAP_NEGATIVEZ;
    public const uint DDSCAPS2_VOLUME = 0x00200000;

    // DX10 resource dimensions
    public const uint D3D10_RESOURCE_DIMENSION_TEXTURE1D = 2;
    public const uint D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3;
    public const uint D3D10_RESOURCE_DIMENSION_TEXTURE3D = 4;

    // DX10 misc flags
    public const uint DDS_RESOURCE_MISC_TEXTURECUBE = 0x4;

    // FourCC values
    public static readonly uint FOURCC_DX10 = MakeFourCC('D', 'X', '1', '0');
    public static readonly uint FOURCC_DXT1 = MakeFourCC('D', 'X', 'T', '1');
    public static readonly uint FOURCC_DXT2 = MakeFourCC('D', 'X', 'T', '2');
    public static readonly uint FOURCC_DXT3 = MakeFourCC('D', 'X', 'T', '3');
    public static readonly uint FOURCC_DXT4 = MakeFourCC('D', 'X', 'T', '4');
    public static readonly uint FOURCC_DXT5 = MakeFourCC('D', 'X', 'T', '5');
    public static readonly uint FOURCC_BC4U = MakeFourCC('B', 'C', '4', 'U');
    public static readonly uint FOURCC_BC4S = MakeFourCC('B', 'C', '4', 'S');
    public static readonly uint FOURCC_BC5U = MakeFourCC('B', 'C', '5', 'U');
    public static readonly uint FOURCC_BC5S = MakeFourCC('B', 'C', '5', 'S');
    public static readonly uint FOURCC_ATI1 = MakeFourCC('A', 'T', 'I', '1');
    public static readonly uint FOURCC_ATI2 = MakeFourCC('A', 'T', 'I', '2');
    public static readonly uint FOURCC_RGBG = MakeFourCC('R', 'G', 'B', 'G');
    public static readonly uint FOURCC_GRGB = MakeFourCC('G', 'R', 'G', 'B');

    public static uint MakeFourCC(char a, char b, char c, char d) =>
        (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct DDS_PIXELFORMAT
{
    public uint Size;
    public uint Flags;
    public uint FourCC;
    public uint RGBBitCount;
    public uint RBitMask;
    public uint GBitMask;
    public uint BBitMask;
    public uint ABitMask;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct DDS_HEADER
{
    public uint Size;
    public uint Flags;
    public uint Height;
    public uint Width;
    public uint PitchOrLinearSize;
    public uint Depth;
    public uint MipMapCount;
    public uint Reserved1_0,
        Reserved1_1,
        Reserved1_2,
        Reserved1_3,
        Reserved1_4,
        Reserved1_5,
        Reserved1_6,
        Reserved1_7,
        Reserved1_8,
        Reserved1_9,
        Reserved1_10;
    public DDS_PIXELFORMAT PixelFormat;
    public uint Caps;
    public uint Caps2;
    public uint Caps3;
    public uint Caps4;
    public uint Reserved2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct DDS_HEADER_DXT10
{
    public uint DxgiFormat;
    public uint ResourceDimension;
    public uint MiscFlag;
    public uint ArraySize;
    public uint MiscFlags2;
}
