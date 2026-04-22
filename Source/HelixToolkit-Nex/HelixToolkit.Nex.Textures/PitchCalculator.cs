using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Static utility for computing row pitch, slice pitch, and mipmap level counts.
/// </summary>
public static class PitchCalculator
{
    /// <summary>
    /// Returns bits per pixel for uncompressed Nex formats.
    /// Returns 0 for compressed or unknown formats.
    /// </summary>
    public static int GetBitsPerPixel(Format fmt) =>
        fmt switch
        {
            Format.R_UN8 => 8,
            Format.R_UI16 => 16,
            Format.R_UI32 => 32,
            Format.R_UN16 => 16,
            Format.R_F16 => 16,
            Format.R_F32 => 32,
            Format.RG_UN8 => 16,
            Format.RG_UI16 => 32,
            Format.RG_UI32 => 64,
            Format.RG_UN16 => 32,
            Format.RG_F16 => 32,
            Format.RG_F32 => 64,
            Format.RGBA_UN8 => 32,
            Format.RGBA_UI32 => 128,
            Format.RGBA_F16 => 64,
            Format.RGBA_F32 => 128,
            Format.RGBA_SRGB8 => 32,
            Format.BGRA_UN8 => 32,
            Format.BGRA_SRGB8 => 32,
            Format.A2B10G10R10_UN => 32,
            Format.A2R10G10B10_UN => 32,
            _ => 0, // compressed or unknown
        };

    /// <summary>
    /// Returns true if the format is a block-compressed format.
    /// </summary>
    public static bool IsCompressed(Format fmt) =>
        fmt switch
        {
            Format.ETC2_RGB8 or Format.ETC2_SRGB8 or Format.BC7_RGBA => true,
            _ => false,
        };

    /// <summary>
    /// Returns the byte size of a single 4x4 compression block for the given format.
    /// ETC2: 8 bytes per block. BC7: 16 bytes per block.
    /// </summary>
    private static int GetBlockByteSize(Format fmt) =>
        fmt switch
        {
            Format.ETC2_RGB8 or Format.ETC2_SRGB8 => 8,
            Format.BC7_RGBA => 16,
            _ => 0,
        };

    /// <summary>
    /// Computes row pitch, slice pitch, and block-aligned width/height counts for the given format and dimensions.
    /// </summary>
    /// <param name="fmt">The pixel format.</param>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="rowPitch">Output: bytes per row.</param>
    /// <param name="slicePitch">Output: bytes per 2D slice.</param>
    /// <param name="widthCount">Output: number of columns (blocks for compressed, pixels for uncompressed).</param>
    /// <param name="heightCount">Output: number of rows (blocks for compressed, pixels for uncompressed).</param>
    /// <param name="flags">Optional pitch computation flags.</param>
    public static void ComputePitch(
        Format fmt,
        int width,
        int height,
        out int rowPitch,
        out int slicePitch,
        out int widthCount,
        out int heightCount,
        PitchFlags flags = PitchFlags.None
    )
    {
        widthCount = width;
        heightCount = height;

        if (IsCompressed(fmt))
        {
            var blockSize = GetBlockByteSize(fmt);
            widthCount = Math.Max(1, (width + 3) / 4);
            heightCount = Math.Max(1, (height + 3) / 4);
            rowPitch = widthCount * blockSize;
            slicePitch = rowPitch * heightCount;
        }
        else
        {
            int bpp;
            if ((flags & PitchFlags.Bpp24) != 0)
                bpp = 24;
            else if ((flags & PitchFlags.Bpp16) != 0)
                bpp = 16;
            else if ((flags & PitchFlags.Bpp8) != 0)
                bpp = 8;
            else
                bpp = GetBitsPerPixel(fmt);

            if ((flags & PitchFlags.LegacyDword) != 0)
            {
                rowPitch = ((width * bpp) + 31) / 32 * 4;
            }
            else
            {
                rowPitch = ((width * bpp) + 7) / 8;
            }
            slicePitch = rowPitch * height;
        }
    }

    /// <summary>
    /// Returns the number of mipmap levels for a 2D texture with the given dimensions.
    /// Result equals 1 + floor(log2(max(width, height))).
    /// </summary>
    public static int CountMips(int width, int height)
    {
        var mipLevels = 1;
        while (height > 1 || width > 1)
        {
            ++mipLevels;
            if (height > 1)
                height >>= 1;
            if (width > 1)
                width >>= 1;
        }
        return mipLevels;
    }

    /// <summary>
    /// Returns the number of mipmap levels for a 3D texture with the given dimensions.
    /// Result equals 1 + floor(log2(max(width, height, depth))).
    /// </summary>
    public static int CountMips(int width, int height, int depth)
    {
        var mipLevels = 1;
        while (height > 1 || width > 1 || depth > 1)
        {
            ++mipLevels;
            if (height > 1)
                height >>= 1;
            if (width > 1)
                width >>= 1;
            if (depth > 1)
                depth >>= 1;
        }
        return mipLevels;
    }

    /// <summary>
    /// Returns the size of a mipmap level for a given base dimension.
    /// Result equals max(1, dimension >> mipLevel).
    /// </summary>
    public static int CalculateMipSize(int dimension, int mipLevel)
    {
        dimension >>= mipLevel;
        return dimension > 0 ? dimension : 1;
    }

    /// <summary>
    /// Resolves the mip level count for a 1D texture.
    /// </summary>
    public static int CalculateMipLevels(int width, MipMapCount mipLevels)
    {
        if (mipLevels > 1)
        {
            var maxMips = CountMips(width, 1);
            if (mipLevels > maxMips)
                throw new InvalidOperationException($"MipLevels must be <= {maxMips}");
        }
        else if (mipLevels == 0)
        {
            mipLevels = CountMips(width, 1);
        }
        else
        {
            mipLevels = 1;
        }
        return mipLevels;
    }

    /// <summary>
    /// Resolves the mip level count for a 2D texture.
    /// </summary>
    public static int CalculateMipLevels(int width, int height, MipMapCount mipLevels)
    {
        if (mipLevels > 1)
        {
            var maxMips = CountMips(width, height);
            if (mipLevels > maxMips)
                throw new InvalidOperationException($"MipLevels must be <= {maxMips}");
        }
        else if (mipLevels == 0)
        {
            mipLevels = CountMips(width, height);
        }
        else
        {
            mipLevels = 1;
        }
        return mipLevels;
    }

    /// <summary>
    /// Resolves the mip level count for a 3D texture.
    /// Requires power-of-two dimensions when mip count is greater than 1 or auto.
    /// </summary>
    public static int CalculateMipLevels(int width, int height, int depth, MipMapCount mipLevels)
    {
        if (mipLevels > 1)
        {
            if (!IsPow2(width) || !IsPow2(height) || !IsPow2(depth))
                throw new InvalidOperationException(
                    "Width/Height/Depth must be power of 2 for 3D textures with multiple mip levels"
                );
            var maxMips = CountMips(width, height, depth);
            if (mipLevels > maxMips)
                throw new InvalidOperationException($"MipLevels must be <= {maxMips}");
        }
        else if (mipLevels == 0)
        {
            if (!IsPow2(width) || !IsPow2(height) || !IsPow2(depth))
                throw new InvalidOperationException(
                    "Width/Height/Depth must be power of 2 for 3D textures with auto mip levels"
                );
            mipLevels = CountMips(width, height, depth);
        }
        else
        {
            mipLevels = 1;
        }
        return mipLevels;
    }

    private static bool IsPow2(int x) => x != 0 && (x & (x - 1)) == 0;
}
