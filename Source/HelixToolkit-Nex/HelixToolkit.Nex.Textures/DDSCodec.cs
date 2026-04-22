using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

internal static class DDSCodec
{
    [Flags]
    private enum ConversionFlags
    {
        None = 0x0,
        Expand = 0x1,
        NoAlpha = 0x2,
        Swizzle = 0x4,
        Pal8 = 0x8,
        Format888 = 0x10,
        Format565 = 0x20,
        Format5551 = 0x40,
        Format4444 = 0x80,
        Format44 = 0x100,
        Format332 = 0x200,
        Format8332 = 0x400,
        FormatA8P8 = 0x800,
        CopyMemory = 0x1000,
        DX10 = 0x10000,
    }

    private readonly struct LegacyMap
    {
        public readonly DxgiFormat Format;
        public readonly ConversionFlags ConvFlags;
        public readonly uint PfFlags;
        public readonly uint FourCC;
        public readonly uint RGBBitCount;
        public readonly uint RBitMask;
        public readonly uint GBitMask;
        public readonly uint BBitMask;
        public readonly uint ABitMask;

        public LegacyMap(
            DxgiFormat format,
            ConversionFlags convFlags,
            uint pfFlags,
            uint fourCC,
            uint rgbBitCount,
            uint rBitMask,
            uint gBitMask,
            uint bBitMask,
            uint aBitMask
        )
        {
            Format = format;
            ConvFlags = convFlags;
            PfFlags = pfFlags;
            FourCC = fourCC;
            RGBBitCount = rgbBitCount;
            RBitMask = rBitMask;
            GBitMask = gBitMask;
            BBitMask = bBitMask;
            ABitMask = aBitMask;
        }
    }

    // Legacy format table ported from SharpDX DDSHelper.LegacyMaps
    // Each entry: (DxgiFormat, ConversionFlags, PfFlags, FourCC, RGBBitCount, R, G, B, A masks)
    private static readonly LegacyMap[] LegacyMaps = new LegacyMap[]
    {
        // FourCC-based compressed formats
        new(
            DxgiFormat.BC1_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_DXT1,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC2_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_DXT3,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC3_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_DXT5,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC2_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_DXT2,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC3_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_DXT4,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC4_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_BC4U,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC4_SNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_BC4S,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC5_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_BC5U,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC5_SNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_BC5S,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC4_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_ATI1,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.BC5_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_ATI2,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.R8G8_B8G8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_RGBG,
            0,
            0,
            0,
            0,
            0
        ),
        new(
            DxgiFormat.G8R8_G8B8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            DDSConstants.FOURCC_GRGB,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_A8R8G8B8 -> B8G8R8A8_UNorm
        new(
            DxgiFormat.B8G8R8A8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_RGBA,
            0,
            32,
            0x00ff0000,
            0x0000ff00,
            0x000000ff,
            0xff000000
        ),
        // D3DFMT_X8R8G8B8 -> B8G8R8X8_UNorm
        new(
            DxgiFormat.B8G8R8X8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0x00ff0000,
            0x0000ff00,
            0x000000ff,
            0x00000000
        ),
        // D3DFMT_A8B8G8R8 -> R8G8B8A8_UNorm
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_RGBA,
            0,
            32,
            0x000000ff,
            0x0000ff00,
            0x00ff0000,
            0xff000000
        ),
        // D3DFMT_X8B8G8R8 -> R8G8B8A8_UNorm (no alpha)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.NoAlpha,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0x000000ff,
            0x0000ff00,
            0x00ff0000,
            0x00000000
        ),
        // D3DFMT_G16R16 -> R16G16_UNorm
        new(
            DxgiFormat.R16G16_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0x0000ffff,
            0xffff0000,
            0x00000000,
            0x00000000
        ),
        // D3DFMT_A2R10G10B10 (swizzle) -> R10G10B10A2_UNorm
        new(
            DxgiFormat.R10G10B10A2_UNorm,
            ConversionFlags.Swizzle,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0x000003ff,
            0x000ffc00,
            0x3ff00000,
            0xc0000000
        ),
        // D3DFMT_A2B10G10R10 -> R10G10B10A2_UNorm
        new(
            DxgiFormat.R10G10B10A2_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0x3ff00000,
            0x000ffc00,
            0x000003ff,
            0xc0000000
        ),
        // D3DFMT_R8G8B8 -> R8G8B8A8_UNorm (expand 24bpp)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.NoAlpha | ConversionFlags.Format888,
            DDSConstants.DDPF_RGB,
            0,
            24,
            0x00ff0000,
            0x0000ff00,
            0x000000ff,
            0x00000000
        ),
        // D3DFMT_R5G6B5 -> B5G6R5_UNorm
        new(
            DxgiFormat.B5G6R5_UNorm,
            ConversionFlags.Format565,
            DDSConstants.DDPF_RGB,
            0,
            16,
            0x0000f800,
            0x000007e0,
            0x0000001f,
            0x00000000
        ),
        // D3DFMT_A1R5G5B5 -> B5G5R5A1_UNorm
        new(
            DxgiFormat.B5G5R5A1_UNorm,
            ConversionFlags.Format5551,
            DDSConstants.DDPF_RGBA,
            0,
            16,
            0x00007c00,
            0x000003e0,
            0x0000001f,
            0x00008000
        ),
        // D3DFMT_X1R5G5B5 -> B5G5R5A1_UNorm (no alpha)
        new(
            DxgiFormat.B5G5R5A1_UNorm,
            ConversionFlags.Format5551 | ConversionFlags.NoAlpha,
            DDSConstants.DDPF_RGB,
            0,
            16,
            0x00007c00,
            0x000003e0,
            0x0000001f,
            0x00000000
        ),
        // D3DFMT_A8R3G3B2 -> R8G8B8A8_UNorm (expand 16bpp)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.Format8332,
            DDSConstants.DDPF_RGBA,
            0,
            16,
            0x00e0,
            0x001c,
            0x0003,
            0xff00
        ),
        // D3DFMT_R3G3B2 -> B5G6R5_UNorm (expand 8bpp)
        new(
            DxgiFormat.B5G6R5_UNorm,
            ConversionFlags.Expand | ConversionFlags.Format332,
            DDSConstants.DDPF_RGB,
            0,
            8,
            0xe0,
            0x1c,
            0x03,
            0x00
        ),
        // D3DFMT_L8 -> R8_UNorm
        new(
            DxgiFormat.R8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_LUMINANCE,
            0,
            8,
            0xff,
            0x00,
            0x00,
            0x00
        ),
        // D3DFMT_L16 -> R16_UNorm
        new(
            DxgiFormat.R16_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_LUMINANCE,
            0,
            16,
            0xffff,
            0x0000,
            0x0000,
            0x0000
        ),
        // D3DFMT_A8L8 -> R8G8_UNorm
        new(
            DxgiFormat.R8G8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_LUMINANCEALPHA,
            0,
            16,
            0x00ff,
            0x0000,
            0x0000,
            0xff00
        ),
        // D3DFMT_A8 -> A8_UNorm
        new(
            DxgiFormat.A8_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_ALPHA,
            0,
            8,
            0x00,
            0x00,
            0x00,
            0xff
        ),
        // D3DFMT_A16B16G16R16 (FourCC 36)
        new(
            DxgiFormat.R16G16B16A16_UNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            36,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_Q16W16V16U16 (FourCC 110)
        new(
            DxgiFormat.R16G16B16A16_SNorm,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            110,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_R16F (FourCC 111)
        new(
            DxgiFormat.R16_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            111,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_G16R16F (FourCC 112)
        new(
            DxgiFormat.R16G16_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            112,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_A16B16G16R16F (FourCC 113)
        new(
            DxgiFormat.R16G16B16A16_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            113,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_R32F (FourCC 114)
        new(
            DxgiFormat.R32_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            114,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_G32R32F (FourCC 115)
        new(
            DxgiFormat.R32G32_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            115,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_A32B32G32R32F (FourCC 116)
        new(
            DxgiFormat.R32G32B32A32_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_FOURCC,
            116,
            0,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_R32F (RGB mask variant)
        new(
            DxgiFormat.R32_Float,
            ConversionFlags.None,
            DDSConstants.DDPF_RGB,
            0,
            32,
            0xffffffff,
            0x00000000,
            0x00000000,
            0x00000000
        ),
        // D3DFMT_A8P8 (palette with alpha)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.Pal8 | ConversionFlags.FormatA8P8,
            DDSConstants.DDPF_PAL8,
            0,
            16,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_P8 (palette)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.Pal8,
            DDSConstants.DDPF_PAL8,
            0,
            8,
            0,
            0,
            0,
            0
        ),
        // D3DFMT_A4R4G4B4 -> R8G8B8A8_UNorm (expand 16bpp 4444)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.Format4444,
            DDSConstants.DDPF_RGBA,
            0,
            16,
            0x00000f00,
            0x000000f0,
            0x0000000f,
            0x0000f000
        ),
        // D3DFMT_X4R4G4B4 -> R8G8B8A8_UNorm (expand 16bpp 4444, no alpha)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.NoAlpha | ConversionFlags.Format4444,
            DDSConstants.DDPF_RGB,
            0,
            16,
            0x0f00,
            0x00f0,
            0x000f,
            0x0000
        ),
        // D3DFMT_A4L4 -> R8G8B8A8_UNorm (expand 8bpp 44)
        new(
            DxgiFormat.R8G8B8A8_UNorm,
            ConversionFlags.Expand | ConversionFlags.Format44,
            DDSConstants.DDPF_LUMINANCE,
            0,
            8,
            0x0f,
            0x00,
            0x00,
            0xf0
        ),
    };

    private static DxgiFormat GetDxgiFormat(
        ref DDS_PIXELFORMAT pixelFormat,
        out ConversionFlags conversionFlags
    )
    {
        conversionFlags = ConversionFlags.None;

        for (int i = 0; i < LegacyMaps.Length; i++)
        {
            ref readonly var entry = ref LegacyMaps[i];

            if ((pixelFormat.Flags & entry.PfFlags) == 0)
                continue;

            if ((entry.PfFlags & DDSConstants.DDPF_FOURCC) != 0)
            {
                if (pixelFormat.FourCC == entry.FourCC)
                {
                    conversionFlags = entry.ConvFlags;
                    return entry.Format;
                }
            }
            else if ((entry.PfFlags & DDSConstants.DDPF_PAL8) != 0)
            {
                if (pixelFormat.RGBBitCount == entry.RGBBitCount)
                {
                    conversionFlags = entry.ConvFlags;
                    return entry.Format;
                }
            }
            else if (
                pixelFormat.RGBBitCount == entry.RGBBitCount
                && pixelFormat.RBitMask == entry.RBitMask
                && pixelFormat.GBitMask == entry.GBitMask
                && pixelFormat.BBitMask == entry.BBitMask
                && pixelFormat.ABitMask == entry.ABitMask
            )
            {
                conversionFlags = entry.ConvFlags;
                return entry.Format;
            }
        }

        return DxgiFormat.Unknown;
    }

    private static unsafe bool DecodeDDSHeader(
        IntPtr pSource,
        int size,
        out ImageDescription description,
        out ConversionFlags convFlags,
        out int offset
    )
    {
        description = default;
        convFlags = ConversionFlags.None;
        offset = 0;

        int headerSize = sizeof(uint) + sizeof(DDS_HEADER);

        if (size < headerSize)
            return false;

        if (*(uint*)pSource != DDSConstants.MagicHeader)
            return false;

        var header = *(DDS_HEADER*)((byte*)pSource + sizeof(uint));

        if (header.Size != (uint)sizeof(DDS_HEADER))
            return false;
        if (header.PixelFormat.Size != (uint)sizeof(DDS_PIXELFORMAT))
            return false;

        description.MipLevels = (int)header.MipMapCount;
        if (description.MipLevels == 0)
            description.MipLevels = 1;

        // Check for DX10 extension header
        if (
            (header.PixelFormat.Flags & DDSConstants.DDPF_FOURCC) != 0
            && header.PixelFormat.FourCC == DDSConstants.FOURCC_DX10
        )
        {
            int dx10HeaderSize = headerSize + sizeof(DDS_HEADER_DXT10);
            if (size < dx10HeaderSize)
                return false;

            var dx10 = *(DDS_HEADER_DXT10*)((byte*)pSource + headerSize);
            convFlags |= ConversionFlags.DX10;
            offset = dx10HeaderSize;

            description.ArraySize = (int)dx10.ArraySize;
            if (description.ArraySize == 0)
                throw new InvalidOperationException(
                    "Unexpected ArraySize == 0 from DDS DX10 header"
                );

            var dxgiFormat = (DxgiFormat)dx10.DxgiFormat;
            description.Format = FormatMapper.DxgiToNex(dxgiFormat);

            switch (dx10.ResourceDimension)
            {
                case DDSConstants.D3D10_RESOURCE_DIMENSION_TEXTURE1D:
                    // Promote 1D to 2D with height=1
                    description.Width = (int)header.Width;
                    description.Height = 1;
                    description.Depth = 1;
                    description.Dimension = TextureDimension.Texture2D;
                    break;

                case DDSConstants.D3D10_RESOURCE_DIMENSION_TEXTURE2D:
                    if ((dx10.MiscFlag & DDSConstants.DDS_RESOURCE_MISC_TEXTURECUBE) != 0)
                    {
                        description.ArraySize *= 6;
                        description.Dimension = TextureDimension.TextureCube;
                    }
                    else
                    {
                        description.Dimension = TextureDimension.Texture2D;
                    }
                    description.Width = (int)header.Width;
                    description.Height = (int)header.Height;
                    description.Depth = 1;
                    break;

                case DDSConstants.D3D10_RESOURCE_DIMENSION_TEXTURE3D:
                    if ((header.Flags & DDSConstants.DDSD_DEPTH) == 0)
                        throw new InvalidOperationException(
                            "Texture3D missing DDSD_DEPTH flag from DDS DX10 header"
                        );
                    if (description.ArraySize > 1)
                        throw new InvalidOperationException(
                            "Unexpected ArraySize > 1 for Texture3D from DDS DX10 header"
                        );
                    description.Width = (int)header.Width;
                    description.Height = (int)header.Height;
                    description.Depth = (int)header.Depth;
                    description.Dimension = TextureDimension.Texture3D;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected resource dimension [{dx10.ResourceDimension}] from DDS DX10 header"
                    );
            }
        }
        else
        {
            // Legacy header
            offset = headerSize;
            description.ArraySize = 1;

            if ((header.Flags & DDSConstants.DDSD_DEPTH) != 0)
            {
                description.Width = (int)header.Width;
                description.Height = (int)header.Height;
                description.Depth = (int)header.Depth;
                description.Dimension = TextureDimension.Texture3D;
            }
            else
            {
                if ((header.Caps2 & DDSConstants.DDSCAPS2_CUBEMAP) != 0)
                {
                    // Require all 6 faces
                    if (
                        (header.Caps2 & DDSConstants.DDSCAPS2_CUBEMAP_ALLFACES)
                        != DDSConstants.DDSCAPS2_CUBEMAP_ALLFACES
                    )
                        throw new InvalidOperationException("Cubemap DDS must define all 6 faces");

                    description.ArraySize = 6;
                    description.Dimension = TextureDimension.TextureCube;
                }
                else
                {
                    description.Dimension = TextureDimension.Texture2D;
                }

                description.Width = (int)header.Width;
                description.Height = (int)header.Height;
                description.Depth = 1;
            }

            var pf = header.PixelFormat;
            var dxgiFormat = GetDxgiFormat(ref pf, out convFlags);
            if (dxgiFormat == DxgiFormat.Unknown)
                throw new InvalidOperationException(
                    "Unsupported or unknown pixel format in DDS header"
                );

            description.Format = FormatMapper.DxgiToNex(dxgiFormat);
        }

        return true;
    }

    public static unsafe Image? LoadFromMemory(
        IntPtr dataPointer,
        int dataSize,
        bool makeACopy,
        GCHandle? handle
    )
    {
        if (
            !DecodeDDSHeader(
                dataPointer,
                dataSize,
                out ImageDescription description,
                out ConversionFlags convFlags,
                out int offset
            )
        )
            return null;

        // Handle palette (8-bit indexed)
        int* pal8 = null;
        if ((convFlags & ConversionFlags.Pal8) != 0)
        {
            pal8 = (int*)((byte*)dataPointer + offset);
            offset += 256 * sizeof(uint);
        }

        if (dataSize < offset)
            throw new InvalidOperationException("DDS data is too small for the declared header");

        // Determine pitch flags for source data
        var cpFlags = PitchFlags.None;
        if ((convFlags & ConversionFlags.Expand) != 0)
        {
            if ((convFlags & ConversionFlags.Format888) != 0)
                cpFlags |= PitchFlags.Bpp24;
            else if (
                (
                    convFlags
                    & (
                        ConversionFlags.Format565
                        | ConversionFlags.Format5551
                        | ConversionFlags.Format4444
                        | ConversionFlags.Format8332
                        | ConversionFlags.FormatA8P8
                    )
                ) != 0
            )
                cpFlags |= PitchFlags.Bpp16;
            else if (
                (
                    convFlags
                    & (ConversionFlags.Format44 | ConversionFlags.Format332 | ConversionFlags.Pal8)
                ) != 0
            )
                cpFlags |= PitchFlags.Bpp8;
        }

        bool isCopyNeeded =
            (convFlags & (ConversionFlags.Expand | ConversionFlags.CopyMemory)) != 0 || makeACopy;

        // Create source image wrapping the raw DDS pixel data
        var srcImage = new Image(
            description,
            dataPointer,
            offset,
            isCopyNeeded ? null : handle,
            !isCopyNeeded,
            cpFlags
        );

        if (!isCopyNeeded && (convFlags & (ConversionFlags.Swizzle | ConversionFlags.NoAlpha)) == 0)
            return srcImage;

        // Need to create a destination image and copy/convert
        var dstImage = new Image(description, IntPtr.Zero, 0, null, false);

        var srcBuffers = srcImage.PixelBuffers;
        var dstBuffers = dstImage.PixelBuffers;

        bool setAlpha = (convFlags & ConversionFlags.NoAlpha) != 0;
        bool swizzle = (convFlags & ConversionFlags.Swizzle) != 0;

        int index = 0;
        int checkSize = dataSize - offset;

        for (int arrayIndex = 0; arrayIndex < description.ArraySize; arrayIndex++)
        {
            int d = description.Depth;
            for (int level = 0; level < description.MipLevels; level++)
            {
                for (int slice = 0; slice < d; slice++, index++)
                {
                    var src = srcBuffers[index];
                    var dst = dstBuffers[index];

                    checkSize -= src.BufferStride;
                    if (checkSize < 0)
                        throw new InvalidOperationException("Unexpected end of DDS buffer");

                    if (IsCompressedFormat(description.Format))
                    {
                        // Compressed: just copy the block data
                        int copySize = Math.Min(src.BufferStride, dst.BufferStride);
                        System.Buffer.MemoryCopy(
                            (void*)src.DataPointer,
                            (void*)dst.DataPointer,
                            dst.BufferStride,
                            copySize
                        );
                    }
                    else
                    {
                        IntPtr pSrc = src.DataPointer;
                        IntPtr pDst = dst.DataPointer;
                        int srcPitch = src.RowStride;
                        int dstPitch = dst.RowStride;

                        for (int h = 0; h < src.Height; h++)
                        {
                            if ((convFlags & ConversionFlags.Expand) != 0)
                            {
                                if (
                                    (
                                        convFlags
                                        & (ConversionFlags.Format565 | ConversionFlags.Format5551)
                                    ) != 0
                                )
                                {
                                    ExpandScanline(
                                        pDst,
                                        dstPitch,
                                        pSrc,
                                        srcPitch,
                                        (convFlags & ConversionFlags.Format565) != 0
                                            ? DxgiFormat.B5G6R5_UNorm
                                            : DxgiFormat.B5G5R5A1_UNorm,
                                        setAlpha
                                    );
                                }
                                else
                                {
                                    LegacyExpandScanline(
                                        pDst,
                                        dstPitch,
                                        pSrc,
                                        srcPitch,
                                        convFlags,
                                        pal8,
                                        setAlpha
                                    );
                                }
                            }
                            else if (swizzle)
                            {
                                SwizzleScanline(pDst, dstPitch, pSrc, srcPitch, setAlpha);
                            }
                            else
                            {
                                if (pSrc != pDst)
                                    CopyScanline(pDst, dstPitch, pSrc, srcPitch, setAlpha);
                            }

                            pSrc = (IntPtr)((byte*)pSrc + srcPitch);
                            pDst = (IntPtr)((byte*)pDst + dstPitch);
                        }
                    }
                }

                if (d > 1)
                    d >>= 1;
            }
        }

        srcImage.Dispose();
        return dstImage;
    }

    public static unsafe void SaveToStream(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream
    )
    {
        var dxgiFormat = FormatMapper.NexToDxgi(description.Format);
        if (dxgiFormat == DxgiFormat.Unknown)
            throw new InvalidOperationException(
                $"Format {description.Format} has no DXGI mapping and cannot be saved as DDS"
            );

        bool needDx10Header =
            description.ArraySize > 1
            && !(
                description.Dimension == TextureDimension.TextureCube && description.ArraySize == 6
            )
            && description.Dimension != TextureDimension.Texture2D;

        // Also use DX10 header for Texture3D or when array size > 1 for any dimension
        if (description.Dimension == TextureDimension.Texture3D || description.ArraySize > 1)
            needDx10Header = true;

        // Write magic
        WriteUInt32ToStream(stream, DDSConstants.MagicHeader);

        // Build DDS_HEADER
        var header = new DDS_HEADER();
        header.Size = (uint)sizeof(DDS_HEADER);
        header.Flags =
            DDSConstants.DDSD_CAPS
            | DDSConstants.DDSD_HEIGHT
            | DDSConstants.DDSD_WIDTH
            | DDSConstants.DDSD_PIXELFORMAT;
        header.Height = (uint)description.Height;
        header.Width = (uint)description.Width;
        header.Depth = (uint)description.Depth;
        header.MipMapCount = (uint)description.MipLevels;

        if (description.MipLevels > 1)
            header.Flags |= DDSConstants.DDSD_MIPMAPCOUNT;
        if (description.Dimension == TextureDimension.Texture3D)
            header.Flags |= DDSConstants.DDSD_DEPTH;

        // Compute pitch for mip0
        PitchCalculator.ComputePitch(
            description.Format,
            description.Width,
            description.Height,
            out int rowPitch,
            out int slicePitch,
            out _,
            out _
        );

        if (PitchCalculator.IsCompressed(description.Format))
        {
            header.Flags |= DDSConstants.DDSD_LINEARSIZE;
            header.PitchOrLinearSize = (uint)slicePitch;
        }
        else
        {
            header.Flags |= DDSConstants.DDSD_PITCH;
            header.PitchOrLinearSize = (uint)rowPitch;
        }

        // Caps
        header.Caps = 0x00001000; // DDSCAPS_TEXTURE
        if (description.MipLevels > 1)
            header.Caps |= 0x00400008; // DDSCAPS_COMPLEX | DDSCAPS_MIPMAP

        if (needDx10Header)
        {
            // Use DX10 pixel format
            header.PixelFormat.Size = (uint)sizeof(DDS_PIXELFORMAT);
            header.PixelFormat.Flags = DDSConstants.DDPF_FOURCC;
            header.PixelFormat.FourCC = DDSConstants.FOURCC_DX10;
        }
        else if (description.Dimension == TextureDimension.TextureCube)
        {
            // Legacy cubemap
            header.Caps |= 0x00000008; // DDSCAPS_COMPLEX
            header.Caps2 = DDSConstants.DDSCAPS2_CUBEMAP | DDSConstants.DDSCAPS2_CUBEMAP_ALLFACES;
            SetLegacyPixelFormat(ref header.PixelFormat, dxgiFormat);
        }
        else
        {
            SetLegacyPixelFormat(ref header.PixelFormat, dxgiFormat);
        }

        // Write DDS_HEADER
        WriteStructToStream(stream, header);

        // Write DX10 extended header if needed
        if (needDx10Header)
        {
            var dx10 = new DDS_HEADER_DXT10();
            dx10.DxgiFormat = (uint)dxgiFormat;
            dx10.ResourceDimension = description.Dimension switch
            {
                TextureDimension.Texture3D => DDSConstants.D3D10_RESOURCE_DIMENSION_TEXTURE3D,
                _ => DDSConstants.D3D10_RESOURCE_DIMENSION_TEXTURE2D,
            };
            dx10.MiscFlag =
                description.Dimension == TextureDimension.TextureCube
                    ? DDSConstants.DDS_RESOURCE_MISC_TEXTURECUBE
                    : 0u;
            dx10.ArraySize =
                description.Dimension == TextureDimension.TextureCube
                    ? (uint)(description.ArraySize / 6)
                    : (uint)description.ArraySize;
            dx10.MiscFlags2 = 0;
            WriteStructToStream(stream, dx10);
        }

        // Write pixel data: for each array slice, for each mip level, for each z-slice
        int index = 0;
        for (int arrayIndex = 0; arrayIndex < description.ArraySize; arrayIndex++)
        {
            int d = description.Depth;
            for (int level = 0; level < description.MipLevels; level++)
            {
                for (int zSlice = 0; zSlice < d; zSlice++, index++)
                {
                    if (index >= count)
                        break;
                    var pb = pixelBuffers[index];
                    // Write the pixel buffer data row by row (handles stride differences)
                    PitchCalculator.ComputePitch(
                        description.Format,
                        pb.Width,
                        pb.Height,
                        out int pbRowPitch,
                        out int pbSlicePitch,
                        out _,
                        out _
                    );

                    if (pb.RowStride == pbRowPitch)
                    {
                        // Contiguous — write entire slice at once
                        var span = new ReadOnlySpan<byte>((void*)pb.DataPointer, pbSlicePitch);
                        stream.Write(span);
                    }
                    else
                    {
                        // Write row by row
                        int rows = PitchCalculator.IsCompressed(description.Format)
                            ? Math.Max(1, (pb.Height + 3) / 4)
                            : pb.Height;
                        for (int row = 0; row < rows; row++)
                        {
                            var rowPtr = (byte*)pb.DataPointer + (long)row * pb.RowStride;
                            var span = new ReadOnlySpan<byte>(rowPtr, pbRowPitch);
                            stream.Write(span);
                        }
                    }
                }
                if (d > 1)
                    d >>= 1;
            }
        }
    }

    private static unsafe void SetLegacyPixelFormat(ref DDS_PIXELFORMAT pf, DxgiFormat dxgiFormat)
    {
        pf.Size = (uint)sizeof(DDS_PIXELFORMAT);

        switch (dxgiFormat)
        {
            case DxgiFormat.R8G8B8A8_UNorm:
                pf.Flags = DDSConstants.DDPF_RGBA;
                pf.RGBBitCount = 32;
                pf.RBitMask = 0x000000ff;
                pf.GBitMask = 0x0000ff00;
                pf.BBitMask = 0x00ff0000;
                pf.ABitMask = 0xff000000;
                break;
            case DxgiFormat.B8G8R8A8_UNorm:
                pf.Flags = DDSConstants.DDPF_RGBA;
                pf.RGBBitCount = 32;
                pf.RBitMask = 0x00ff0000;
                pf.GBitMask = 0x0000ff00;
                pf.BBitMask = 0x000000ff;
                pf.ABitMask = 0xff000000;
                break;
            case DxgiFormat.R8_UNorm:
                pf.Flags = DDSConstants.DDPF_LUMINANCE;
                pf.RGBBitCount = 8;
                pf.RBitMask = 0xff;
                break;
            case DxgiFormat.R8G8_UNorm:
                pf.Flags = DDSConstants.DDPF_LUMINANCEALPHA;
                pf.RGBBitCount = 16;
                pf.RBitMask = 0x00ff;
                pf.ABitMask = 0xff00;
                break;
            case DxgiFormat.R16_UNorm:
                pf.Flags = DDSConstants.DDPF_LUMINANCE;
                pf.RGBBitCount = 16;
                pf.RBitMask = 0xffff;
                break;
            case DxgiFormat.BC7_UNorm:
                // BC7 has no legacy representation — use FourCC with DX10 fallback
                // For simple 2D non-array, we still need DX10 for BC7
                pf.Flags = DDSConstants.DDPF_FOURCC;
                pf.FourCC = DDSConstants.FOURCC_DX10;
                break;
            default:
                // For all other formats (float, half, etc.) use DX10 FourCC
                pf.Flags = DDSConstants.DDPF_FOURCC;
                pf.FourCC = DDSConstants.FOURCC_DX10;
                break;
        }
    }

    private static void WriteUInt32ToStream(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 24) & 0xff));
    }

    private static unsafe void WriteStructToStream<T>(Stream stream, T value)
        where T : unmanaged
    {
        var span = new ReadOnlySpan<byte>(&value, sizeof(T));
        stream.Write(span);
    }

    private static bool IsCompressedFormat(Format fmt) => PitchCalculator.IsCompressed(fmt);

    private static unsafe void ExpandScanline(
        IntPtr pDst,
        int dstSize,
        IntPtr pSrc,
        int srcSize,
        DxgiFormat inFormat,
        bool setAlpha
    )
    {
        switch (inFormat)
        {
            case DxgiFormat.B5G6R5_UNorm:
            {
                var sPtr = (ushort*)pSrc;
                var dPtr = (uint*)pDst;
                for (
                    uint ocount = 0, icount = 0;
                    icount < (uint)srcSize && ocount < (uint)dstSize;
                    icount += 2, ocount += 4
                )
                {
                    var t = *(sPtr++);
                    var t1 = (uint)(((t & 0xf800) >> 8) | ((t & 0xe000) >> 13));
                    var t2 = (uint)(((t & 0x07e0) << 5) | ((t & 0x0600) >> 5));
                    var t3 = (uint)(((t & 0x001f) << 19) | ((t & 0x001c) << 14));
                    *(dPtr++) = t1 | t2 | t3 | 0xff000000;
                }
                break;
            }
            case DxgiFormat.B5G5R5A1_UNorm:
            {
                var sPtr = (ushort*)pSrc;
                var dPtr = (uint*)pDst;
                for (
                    uint ocount = 0, icount = 0;
                    icount < (uint)srcSize && ocount < (uint)dstSize;
                    icount += 2, ocount += 4
                )
                {
                    var t = *(sPtr++);
                    var t1 = (uint)(((t & 0x7c00) >> 7) | ((t & 0x7000) >> 12));
                    var t2 = (uint)(((t & 0x03e0) << 6) | ((t & 0x0380) << 1));
                    var t3 = (uint)(((t & 0x001f) << 19) | ((t & 0x001c) << 14));
                    var ta = setAlpha ? 0xff000000u : ((t & 0x8000u) != 0 ? 0xff000000u : 0u);
                    *(dPtr++) = t1 | t2 | t3 | ta;
                }
                break;
            }
        }
    }

    private static unsafe void LegacyExpandScanline(
        IntPtr pDst,
        int dstSize,
        IntPtr pSrc,
        int srcSize,
        ConversionFlags convFlags,
        int* pal8,
        bool setAlpha
    )
    {
        if ((convFlags & ConversionFlags.Format888) != 0)
        {
            // 24bpp BGR -> 32bpp RGBA
            var sPtr = (byte*)pSrc;
            var dPtr = (int*)pDst;
            for (
                int ocount = 0, icount = 0;
                icount < srcSize && ocount < dstSize;
                icount += 3, ocount += 4
            )
            {
                // Source is BGR, output is RGBA: R=sPtr[2], G=sPtr[1], B=sPtr[0], A=0xff
                int t1 = *(sPtr + 2) << 16;
                int t2 = *(sPtr + 1) << 8;
                int t3 = *sPtr;
                *(dPtr++) = t1 | t2 | t3 | unchecked((int)0xff000000);
                sPtr += 3;
            }
        }
        else if ((convFlags & ConversionFlags.Format332) != 0)
        {
            // 8bpp R3G3B2 -> 32bpp RGBA
            var sPtr = (byte*)pSrc;
            var dPtr = (int*)pDst;
            for (
                int ocount = 0, icount = 0;
                icount < srcSize && ocount < dstSize;
                icount++, ocount += 4
            )
            {
                var t = *(sPtr++);
                var t1 = (t & 0xe0) | ((t & 0xe0) >> 3) | ((t & 0xc0) >> 6);
                var t2 = ((t & 0x1c) << 11) | ((t & 0x1c) << 8) | ((t & 0x18) << 5);
                var t3 =
                    ((t & 0x03) << 22)
                    | ((t & 0x03) << 20)
                    | ((t & 0x03) << 18)
                    | ((t & 0x03) << 16);
                *(dPtr++) = (int)(t1 | t2 | t3 | 0xff000000u);
            }
        }
        else if ((convFlags & ConversionFlags.Format8332) != 0)
        {
            // 16bpp A8R3G3B2 -> 32bpp RGBA
            var sPtr = (short*)pSrc;
            var dPtr = (int*)pDst;
            for (
                int ocount = 0, icount = 0;
                icount < srcSize && ocount < dstSize;
                icount += 2, ocount += 4
            )
            {
                var t = *(sPtr++);
                var t1 = (uint)((t & 0x00e0) | ((t & 0x00e0) >> 3) | ((t & 0x00c0) >> 6));
                var t2 = (uint)(((t & 0x001c) << 11) | ((t & 0x001c) << 8) | ((t & 0x0018) << 5));
                var t3 = (uint)(
                    ((t & 0x0003) << 22)
                    | ((t & 0x0003) << 20)
                    | ((t & 0x0003) << 18)
                    | ((t & 0x0003) << 16)
                );
                var ta = setAlpha ? 0xff000000u : (uint)((t & 0xff00) << 16);
                *(dPtr++) = (int)(t1 | t2 | t3 | ta);
            }
        }
        else if ((convFlags & ConversionFlags.Format4444) != 0)
        {
            // 16bpp A4R4G4B4 -> 32bpp RGBA
            var sPtr = (short*)pSrc;
            var dPtr = (int*)pDst;
            for (
                int ocount = 0, icount = 0;
                icount < srcSize && ocount < dstSize;
                icount += 2, ocount += 4
            )
            {
                var t = *(sPtr++);
                var t1 = (uint)(((t & 0x0f00) >> 4) | ((t & 0x0f00) >> 8));
                var t2 = (uint)(((t & 0x00f0) << 8) | ((t & 0x00f0) << 4));
                var t3 = (uint)(((t & 0x000f) << 20) | ((t & 0x000f) << 16));
                var ta = setAlpha
                    ? 0xff000000u
                    : (uint)(((t & 0xf000) << 16) | ((t & 0xf000) << 12));
                *(dPtr++) = (int)(t1 | t2 | t3 | ta);
            }
        }
        else if ((convFlags & ConversionFlags.Format44) != 0)
        {
            // 8bpp A4L4 -> 32bpp RGBA
            var sPtr = (byte*)pSrc;
            var dPtr = (int*)pDst;
            for (
                int ocount = 0, icount = 0;
                icount < srcSize && ocount < dstSize;
                icount++, ocount += 4
            )
            {
                var t = *(sPtr++);
                var t1 = (uint)(((t & 0x0f) << 4) | (t & 0x0f));
                var ta = setAlpha ? 0xff000000u : (uint)(((t & 0xf0) << 24) | ((t & 0xf0) << 20));
                *(dPtr++) = (int)(t1 | (t1 << 8) | (t1 << 16) | ta);
            }
        }
        else if ((convFlags & ConversionFlags.Pal8) != 0 && pal8 != null)
        {
            if ((convFlags & ConversionFlags.FormatA8P8) != 0)
            {
                // 16bpp A8P8 palette
                var sPtr = (short*)pSrc;
                var dPtr = (int*)pDst;
                for (
                    int ocount = 0, icount = 0;
                    icount < srcSize && ocount < dstSize;
                    icount += 2, ocount += 4
                )
                {
                    var t = *(sPtr++);
                    var t1 = (uint)pal8[t & 0xff];
                    var ta = setAlpha ? 0xff000000u : (uint)((t & 0xff00) << 16);
                    *(dPtr++) = (int)(t1 | ta);
                }
            }
            else
            {
                // 8bpp P8 palette
                var sPtr = (byte*)pSrc;
                var dPtr = (int*)pDst;
                for (
                    int ocount = 0, icount = 0;
                    icount < srcSize && ocount < dstSize;
                    icount++, ocount += 4
                )
                {
                    *(dPtr++) = pal8[*(sPtr++)];
                }
            }
        }
    }

    private static unsafe void SwizzleScanline(
        IntPtr pDst,
        int dstSize,
        IntPtr pSrc,
        int srcSize,
        bool setAlpha
    )
    {
        // Swap R and B channels (BGRA <-> RGBA)
        if (pDst == pSrc)
        {
            var dPtr = (uint*)pDst;
            for (int count = 0; count < dstSize; count += 4)
            {
                var t = *dPtr;
                var t1 = (t & 0x00ff0000) >> 16;
                var t2 = (t & 0x000000ff) << 16;
                var t3 = t & 0x0000ff00;
                var ta = setAlpha ? 0xff000000u : (t & 0xff000000u);
                *(dPtr++) = t1 | t2 | t3 | ta;
            }
        }
        else
        {
            var sPtr = (uint*)pSrc;
            var dPtr = (uint*)pDst;
            int copySize = Math.Min(dstSize, srcSize);
            for (int count = 0; count < copySize; count += 4)
            {
                var t = *(sPtr++);
                var t1 = (t & 0x00ff0000) >> 16;
                var t2 = (t & 0x000000ff) << 16;
                var t3 = t & 0x0000ff00;
                var ta = setAlpha ? 0xff000000u : (t & 0xff000000u);
                *(dPtr++) = t1 | t2 | t3 | ta;
            }
        }
    }

    private static unsafe void CopyScanline(
        IntPtr pDst,
        int dstSize,
        IntPtr pSrc,
        int srcSize,
        bool setAlpha
    )
    {
        if (pDst == pSrc)
            return;
        System.Buffer.MemoryCopy((void*)pSrc, (void*)pDst, dstSize, Math.Min(dstSize, srcSize));
    }
}
