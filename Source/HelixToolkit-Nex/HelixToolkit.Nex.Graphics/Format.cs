namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Defines the format of vertex attributes in a vertex buffer.
/// </summary>
public enum VertexFormat
{
    /// <summary>
    /// Invalid or unspecified vertex format.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// Single 32-bit float component.
    /// </summary>
    Float1,

    /// <summary>
    /// Two 32-bit float components (vec2).
    /// </summary>
    Float2,

    /// <summary>
    /// Three 32-bit float components (vec3).
    /// </summary>
    Float3,

    /// <summary>
    /// Four 32-bit float components (vec4).
    /// </summary>
    Float4,

    /// <summary>
    /// Single signed 8-bit integer.
    /// </summary>
    Byte1,

    /// <summary>
    /// Two signed 8-bit integers.
    /// </summary>
    Byte2,

    /// <summary>
    /// Three signed 8-bit integers.
    /// </summary>
    Byte3,

    /// <summary>
    /// Four signed 8-bit integers.
    /// </summary>
    Byte4,

    /// <summary>
    /// Single unsigned 8-bit integer.
    /// </summary>
    UByte1,

    /// <summary>
    /// Two unsigned 8-bit integers.
    /// </summary>
    UByte2,

    /// <summary>
    /// Three unsigned 8-bit integers.
    /// </summary>
  UByte3,

    /// <summary>
  /// Four unsigned 8-bit integers.
    /// </summary>
    UByte4,

    /// <summary>
    /// Single signed 16-bit integer.
    /// </summary>
    Short1,

    /// <summary>
    /// Two signed 16-bit integers.
    /// </summary>
    Short2,

    /// <summary>
    /// Three signed 16-bit integers.
    /// </summary>
    Short3,

 /// <summary>
    /// Four signed 16-bit integers.
    /// </summary>
    Short4,

/// <summary>
    /// Single unsigned 16-bit integer.
    /// </summary>
    UShort1,

    /// <summary>
    /// Two unsigned 16-bit integers.
    /// </summary>
    UShort2,

    /// <summary>
    /// Three unsigned 16-bit integers.
    /// </summary>
    UShort3,

    /// <summary>
    /// Four unsigned 16-bit integers.
    /// </summary>
    UShort4,

    /// <summary>
    /// Two signed 8-bit normalized integers (mapped to [-1, 1]).
    /// </summary>
    Byte2Norm,

    /// <summary>
    /// Four signed 8-bit normalized integers (mapped to [-1, 1]).
    /// </summary>
    Byte4Norm,

    /// <summary>
  /// Two unsigned 8-bit normalized integers (mapped to [0, 1]).
    /// </summary>
    UByte2Norm,

    /// <summary>
    /// Four unsigned 8-bit normalized integers (mapped to [0, 1]).
    /// </summary>
    UByte4Norm,

    /// <summary>
    /// Two signed 16-bit normalized integers (mapped to [-1, 1]).
    /// </summary>
    Short2Norm,

  /// <summary>
    /// Four signed 16-bit normalized integers (mapped to [-1, 1]).
    /// </summary>
    Short4Norm,

    /// <summary>
    /// Two unsigned 16-bit normalized integers (mapped to [0, 1]).
    /// </summary>
    UShort2Norm,

    /// <summary>
    /// Four unsigned 16-bit normalized integers (mapped to [0, 1]).
    /// </summary>
    UShort4Norm,

    /// <summary>
    /// Single signed 32-bit integer.
    /// </summary>
    Int1,

    /// <summary>
    /// Two signed 32-bit integers.
    /// </summary>
    Int2,

    /// <summary>
    /// Three signed 32-bit integers.
    /// </summary>
    Int3,

    /// <summary>
    /// Four signed 32-bit integers.
    /// </summary>
    Int4,

    /// <summary>
    /// Single unsigned 32-bit integer.
    /// </summary>
    UInt1,

    /// <summary>
    /// Two unsigned 32-bit integers.
    /// </summary>
    UInt2,

    /// <summary>
    /// Three unsigned 32-bit integers.
    /// </summary>
    UInt3,

    /// <summary>
    /// Four unsigned 32-bit integers.
    /// </summary>
  UInt4,

    /// <summary>
    /// Single 16-bit floating point value (half float).
    /// </summary>
    HalfFloat1,

    /// <summary>
    /// Two 16-bit floating point values (half float).
    /// </summary>
    HalfFloat2,

    /// <summary>
    /// Three 16-bit floating point values (half float).
    /// </summary>
    HalfFloat3,

    /// <summary>
    /// Four 16-bit floating point values (half float).
    /// </summary>
    HalfFloat4,

    /// <summary>
    /// Packed format: 2 bits for alpha, 10 bits each for RGB components (reversed order).
    /// </summary>
    Int_2_10_10_10_REV,
}

/// <summary>
/// Defines pixel and texture formats supported by the graphics system.
/// </summary>
public enum Format : uint8_t
{
    /// <summary>
    /// Invalid or unspecified format.
    /// </summary>
    Invalid = 0,

    // Single-channel formats
    /// <summary>
    /// Single-channel 8-bit unsigned normalized integer (range [0, 1]).
    /// </summary>
    R_UN8,

    /// <summary>
    /// Single-channel 16-bit unsigned integer.
    /// </summary>
    R_UI16,

    /// <summary>
    /// Single-channel 32-bit unsigned integer.
    /// </summary>
  R_UI32,

    /// <summary>
    /// Single-channel 16-bit unsigned normalized integer (range [0, 1]).
    /// </summary>
    R_UN16,

    /// <summary>
    /// Single-channel 16-bit floating point.
    /// </summary>
    R_F16,

    /// <summary>
  /// Single-channel 32-bit floating point.
    /// </summary>
    R_F32,

    // Two-channel formats
    /// <summary>
    /// Two-channel 8-bit unsigned normalized integer per channel.
    /// </summary>
    RG_UN8,

    /// <summary>
    /// Two-channel 16-bit unsigned integer per channel.
    /// </summary>
    RG_UI16,

    /// <summary>
    /// Two-channel 32-bit unsigned integer per channel.
    /// </summary>
    RG_UI32,

    /// <summary>
    /// Two-channel 16-bit unsigned normalized integer per channel.
    /// </summary>
    RG_UN16,

    /// <summary>
    /// Two-channel 16-bit floating point per channel.
    /// </summary>
    RG_F16,

    /// <summary>
 /// Two-channel 32-bit floating point per channel.
    /// </summary>
    RG_F32,

    // Four-channel RGBA formats
    /// <summary>
    /// Four-channel 8-bit unsigned normalized integer per channel (RGBA).
    /// </summary>
    RGBA_UN8,

    /// <summary>
    /// Four-channel 32-bit unsigned integer per channel (RGBA).
    /// </summary>
    RGBA_UI32,

/// <summary>
  /// Four-channel 16-bit floating point per channel (RGBA).
  /// </summary>
    RGBA_F16,

    /// <summary>
    /// Four-channel 32-bit floating point per channel (RGBA).
    /// </summary>
    RGBA_F32,

    /// <summary>
    /// Four-channel 8-bit unsigned normalized integer in sRGB color space (RGBA).
    /// </summary>
    RGBA_SRGB8,

    // Four-channel BGRA formats
    /// <summary>
  /// Four-channel 8-bit unsigned normalized integer per channel (BGRA).
    /// </summary>
    BGRA_UN8,

    /// <summary>
    /// Four-channel 8-bit unsigned normalized integer in sRGB color space (BGRA).
    /// </summary>
    BGRA_SRGB8,

    // Packed formats
    /// <summary>
    /// Packed format: 2 bits alpha, 10 bits blue, 10 bits green, 10 bits red (unsigned normalized).
    /// </summary>
    A2B10G10R10_UN,

    /// <summary>
    /// Packed format: 2 bits alpha, 10 bits red, 10 bits green, 10 bits blue (unsigned normalized).
    /// </summary>
  A2R10G10B10_UN,

    // Compressed formats
    /// <summary>
    /// ETC2 compressed RGB format (8 bits per pixel).
    /// </summary>
    ETC2_RGB8,

    /// <summary>
    /// ETC2 compressed sRGB format (8 bits per pixel).
    /// </summary>
    ETC2_SRGB8,

    /// <summary>
    /// BC7 compressed RGBA format (high-quality block compression).
    /// </summary>
    BC7_RGBA,

    // Depth and stencil formats
    /// <summary>
    /// 16-bit unsigned normalized depth format.
    /// </summary>
    Z_UN16,

    /// <summary>
    /// 24-bit unsigned normalized depth format.
    /// </summary>
Z_UN24,

    /// <summary>
    /// 32-bit floating point depth format.
    /// </summary>
    Z_F32,

    /// <summary>
    /// Combined 24-bit unsigned normalized depth and 8-bit unsigned integer stencil.
    /// </summary>
    Z_UN24_S_UI8,

    /// <summary>
    /// Combined 32-bit floating point depth and 8-bit unsigned integer stencil.
    /// </summary>
    Z_F32_S_UI8,

    // Video/YUV formats
    /// <summary>
    /// YUV 4:2:0 planar format (NV12), two planes.
    /// </summary>
    YUV_NV12,

    /// <summary>
  /// YUV 4:2:0 planar format (I420/YV12), three planes.
    /// </summary>
    YUV_420p,
}
