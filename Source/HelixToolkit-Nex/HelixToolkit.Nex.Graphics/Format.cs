

namespace HelixToolkit.Nex.Graphics;

public enum VertexFormat
{
    Invalid = 0,

    Float1,
    Float2,
    Float3,
    Float4,

    Byte1,
    Byte2,
    Byte3,
    Byte4,

    UByte1,
    UByte2,
    UByte3,
    UByte4,

    Short1,
    Short2,
    Short3,
    Short4,

    UShort1,
    UShort2,
    UShort3,
    UShort4,

    Byte2Norm,
    Byte4Norm,

    UByte2Norm,
    UByte4Norm,

    Short2Norm,
    Short4Norm,

    UShort2Norm,
    UShort4Norm,

    Int1,
    Int2,
    Int3,
    Int4,

    UInt1,
    UInt2,
    UInt3,
    UInt4,

    HalfFloat1,
    HalfFloat2,
    HalfFloat3,
    HalfFloat4,

    Int_2_10_10_10_REV,
}

public enum Format : uint8_t
{
    Invalid = 0,

    R_UN8,
    R_UI16,
    R_UI32,
    R_UN16,
    R_F16,
    R_F32,

    RG_UN8,
    RG_UI16,
    RG_UI32,
    RG_UN16,
    RG_F16,
    RG_F32,

    RGBA_UN8,
    RGBA_UI32,
    RGBA_F16,
    RGBA_F32,
    RGBA_SRGB8,

    BGRA_UN8,
    BGRA_SRGB8,

    A2B10G10R10_UN,
    A2R10G10B10_UN,

    ETC2_RGB8,
    ETC2_SRGB8,
    BC7_RGBA,

    Z_UN16,
    Z_UN24,
    Z_F32,
    Z_UN24_S_UI8,
    Z_F32_S_UI8,

    YUV_NV12,
    YUV_420p,
}
