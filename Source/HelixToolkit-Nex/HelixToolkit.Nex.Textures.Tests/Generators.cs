using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// FsCheck generators for types that already exist (Format enum).
/// Generators for ImageDescription and TextureDimension will be added in task 2/3
/// once those types are implemented.
/// </summary>
public static class Generators
{
    // All valid Format values excluding Invalid
    private static readonly Format[] AllValidFormats =
    [
        Format.R_UN8,
        Format.R_UI16,
        Format.R_UI32,
        Format.R_UN16,
        Format.R_F16,
        Format.R_F32,
        Format.RG_UN8,
        Format.RG_UI16,
        Format.RG_UI32,
        Format.RG_UN16,
        Format.RG_F16,
        Format.RG_F32,
        Format.RGBA_UN8,
        Format.RGBA_UI32,
        Format.RGBA_F16,
        Format.RGBA_F32,
        Format.RGBA_SRGB8,
        Format.BGRA_UN8,
        Format.BGRA_SRGB8,
        Format.A2B10G10R10_UN,
        Format.A2R10G10B10_UN,
        Format.ETC2_RGB8,
        Format.ETC2_SRGB8,
        Format.BC7_RGBA,
    ];

    // Uncompressed formats with known bits-per-pixel
    private static readonly Format[] UncompressedFormats =
    [
        Format.R_UN8,
        Format.R_UI16,
        Format.R_UI32,
        Format.R_UN16,
        Format.R_F16,
        Format.R_F32,
        Format.RG_UN8,
        Format.RG_UI16,
        Format.RG_UI32,
        Format.RG_UN16,
        Format.RG_F16,
        Format.RG_F32,
        Format.RGBA_UN8,
        Format.RGBA_UI32,
        Format.RGBA_F16,
        Format.RGBA_F32,
        Format.RGBA_SRGB8,
        Format.BGRA_UN8,
        Format.BGRA_SRGB8,
        Format.A2B10G10R10_UN,
        Format.A2R10G10B10_UN,
    ];

    // Compressed formats
    private static readonly Format[] CompressedFormats =
    [
        Format.ETC2_RGB8,
        Format.ETC2_SRGB8,
        Format.BC7_RGBA,
    ];

    /// <summary>
    /// Generates valid Format values (excluding Invalid).
    /// </summary>
    public static Arbitrary<Format> NexFormatArb() => Arb.From(Gen.Elements(AllValidFormats));

    /// <summary>
    /// Generates uncompressed Format values only (formats with known bits-per-pixel).
    /// </summary>
    public static Arbitrary<Format> UncompressedFormatArb() =>
        Arb.From(Gen.Elements(UncompressedFormats));

    /// <summary>
    /// Generates compressed Format values only.
    /// </summary>
    public static Arbitrary<Format> CompressedFormatArb() =>
        Arb.From(Gen.Elements(CompressedFormats));

    /// <summary>
    /// Generates positive integers in the range [1, 8192] suitable for texture dimensions.
    /// </summary>
    public static Arbitrary<int> PositiveDimension() => Arb.From(Gen.Choose(1, 8192));

    // Uncompressed formats only (for image descriptions that need known bpp)
    private static readonly Format[] MappableFormats =
    [
        Format.RGBA_UN8,
        Format.RGBA_SRGB8,
        Format.BGRA_UN8,
        Format.BGRA_SRGB8,
        Format.R_UN8,
        Format.R_UN16,
        Format.R_F16,
        Format.R_F32,
        Format.RG_UN8,
        Format.RG_F16,
        Format.RG_F32,
        Format.RGBA_F16,
        Format.RGBA_F32,
        Format.BC7_RGBA,
        Format.A2R10G10B10_UN,
    ];

    /// <summary>
    /// Generates valid <see cref="ImageDescription"/> instances with random dimensions, formats, and mip levels.
    /// </summary>
    public static Arbitrary<ImageDescription> ValidImageDescription() =>
        Arb.From(
            from dim in Gen.Elements(
                TextureDimension.Texture2D,
                TextureDimension.Texture3D,
                TextureDimension.TextureCube
            )
            from w in Gen.Choose(1, 256)
            from h in Gen.Choose(1, 256)
            from d in dim == TextureDimension.Texture3D ? Gen.Choose(1, 32) : Gen.Constant(1)
            from mips in Gen.Choose(1, 8)
            from fmt in Gen.Elements(MappableFormats)
            let arraySize = dim == TextureDimension.TextureCube ? 6 : 1
            select new ImageDescription
            {
                Dimension = dim,
                Width = w,
                Height = h,
                Depth = d,
                ArraySize = arraySize,
                MipLevels = mips,
                Format = fmt,
            }
        );

    // TODO: ValidPixelBufferParams — implement after task 6 (PixelBuffer)
    // public static Arbitrary<(int width, int height, Format format)> ValidPixelBufferParams() { ... }
}
