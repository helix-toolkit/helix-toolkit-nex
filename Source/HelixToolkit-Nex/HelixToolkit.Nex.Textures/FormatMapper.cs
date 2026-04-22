using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Provides bidirectional mapping between DXGI format values (used in DDS files)
/// and the Nex <see cref="Format"/> enum.
/// </summary>
public static class FormatMapper
{
    /// <summary>Maps a DXGI format to the corresponding Nex Format. Returns Format.Invalid if not supported.</summary>
    internal static Format DxgiToNex(DxgiFormat dxgiFormat) =>
        dxgiFormat switch
        {
            DxgiFormat.R8G8B8A8_UNorm => Format.RGBA_UN8,
            DxgiFormat.R8G8B8A8_UNorm_SRgb => Format.RGBA_SRGB8,
            DxgiFormat.B8G8R8A8_UNorm => Format.BGRA_UN8,
            DxgiFormat.B8G8R8A8_UNorm_SRgb => Format.BGRA_SRGB8,
            DxgiFormat.R8_UNorm => Format.R_UN8,
            DxgiFormat.R16_UNorm => Format.R_UN16,
            DxgiFormat.R16_Float => Format.R_F16,
            DxgiFormat.R32_Float => Format.R_F32,
            DxgiFormat.R8G8_UNorm => Format.RG_UN8,
            DxgiFormat.R16G16_Float => Format.RG_F16,
            DxgiFormat.R32G32_Float => Format.RG_F32,
            DxgiFormat.R16G16B16A16_Float => Format.RGBA_F16,
            DxgiFormat.R32G32B32A32_Float => Format.RGBA_F32,
            DxgiFormat.BC7_UNorm => Format.BC7_RGBA,
            DxgiFormat.R10G10B10A2_UNorm => Format.A2R10G10B10_UN,
            _ => Format.Invalid,
        };

    /// <summary>Maps a Nex Format to the corresponding DXGI format. Returns DxgiFormat.Unknown if not supported.</summary>
    internal static DxgiFormat NexToDxgi(Format nexFormat) =>
        nexFormat switch
        {
            Format.RGBA_UN8 => DxgiFormat.R8G8B8A8_UNorm,
            Format.RGBA_SRGB8 => DxgiFormat.R8G8B8A8_UNorm_SRgb,
            Format.BGRA_UN8 => DxgiFormat.B8G8R8A8_UNorm,
            Format.BGRA_SRGB8 => DxgiFormat.B8G8R8A8_UNorm_SRgb,
            Format.R_UN8 => DxgiFormat.R8_UNorm,
            Format.R_UN16 => DxgiFormat.R16_UNorm,
            Format.R_F16 => DxgiFormat.R16_Float,
            Format.R_F32 => DxgiFormat.R32_Float,
            Format.RG_UN8 => DxgiFormat.R8G8_UNorm,
            Format.RG_F16 => DxgiFormat.R16G16_Float,
            Format.RG_F32 => DxgiFormat.R32G32_Float,
            Format.RGBA_F16 => DxgiFormat.R16G16B16A16_Float,
            Format.RGBA_F32 => DxgiFormat.R32G32B32A32_Float,
            Format.BC7_RGBA => DxgiFormat.BC7_UNorm,
            Format.A2R10G10B10_UN => DxgiFormat.R10G10B10A2_UNorm,
            _ => DxgiFormat.Unknown,
        };

    /// <summary>Returns true if the DXGI format has a corresponding Nex format.</summary>
    internal static bool IsSupported(DxgiFormat dxgiFormat) =>
        DxgiToNex(dxgiFormat) != Format.Invalid;
}
