namespace HelixToolkit.Nex.Graphics;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
public static class Constants
{
    public const uint8 MAX_COLOR_ATTACHMENTS = 8;
    public const uint8 MAX_MIP_LEVELS = 16;
    public const uint8 SPECIALIZATION_CONSTANTS_MAX = 16;
    public const uint8 MAX_RAY_TRACING_SHADER_GROUP_SIZE = 4;
}


public enum BackendFlavor : uint8_t
{
    Invalid,
    Vulkan,
};

public enum IndexFormat : uint8_t
{
    UI8,
    UI16,
    UI32,
}

public enum Topology : uint8_t
{
    Point,
    Line,
    LineStrip,
    Triangle,
    TriangleStrip,
    Patch,
}

public enum ColorSpace : uint8_t
{
    SRGB_LINEAR,
    SRGB_NONLINEAR,
    SRGB_EXTENDED_LINEAR,
    HDR10,
}

public enum TextureType : uint8_t
{
    Texture2D,
    Texture3D,
    TextureCube,
}

public enum SamplerFilter : uint8_t
{
    Nearest = 0,
    Linear
}
public enum SamplerMip : uint8_t
{
    Disabled = 0,
    Nearest,
    Linear
}
public enum SamplerWrap : uint8_t
{
    Repeat = 0,
    Clamp,
    MirrorRepeat,
    ClampToBorder,
}

public enum HWDeviceType
{
    Discrete = 1,
    External = 2,
    Integrated = 3,
    Software = 4,
}

public struct HWDeviceDesc
{
    public const uint32_t MAX_PHYSICAL_DEVICE_NAME_SIZE = 256;
    public nuint Guid;
    public HWDeviceType Type;
    public string Name;
}

public enum StorageType
{
    Device,
    HostVisible,
    Memoryless
}

public enum CullMode : uint8_t { None, Front, Back }
public enum WindingMode : uint8_t { CCW, CW }

public enum ResultCode
{
    Unknown,
    Ok,
    ArgumentOutOfRange,
    RuntimeError,
    NotSupported,
    ArgumentError,
    OutOfMemory,
    ArgumentNull,
    InvalidState,
    CompileError,
}

public struct Dimensions(uint32_t width = 1, uint32_t height = 1, uint32_t depth = 1)
{
    public uint32_t Width = width;
    public uint32_t Height = height;
    public uint32_t Depth = depth;

    public Dimensions Divide1D(uint32_t v)
    {
        return new Dimensions { Width = this.Width / v, Height = this.Height, Depth = this.Depth };
    }

    public Dimensions Divide2D(uint32_t v)
    {
        return new Dimensions { Width = this.Width / v, Height = this.Height / v, Depth = this.Depth };
    }

    public Dimensions Divide3D(uint32_t v)
    {
        return new Dimensions { Width = this.Width / v, Height = this.Height / v, Depth = this.Depth / v };
    }
}

public enum CompareOp : uint8_t
{
    Never = 0,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    AlwaysPass
}

public enum StencilOp : uint8_t
{
    Keep = 0,
    Zero,
    Replace,
    IncrementClamp,
    DecrementClamp,
    Invert,
    IncrementWrap,
    DecrementWrap
}

public enum BlendOp : uint8_t
{
    Add = 0,
    Subtract,
    ReverseSubtract,
    Min,
    Max
}

public enum BlendFactor : uint8_t
{
    Zero = 0,
    One,
    SrcColor,
    OneMinusSrcColor,
    SrcAlpha,
    OneMinusSrcAlpha,
    DstColor,
    OneMinusDstColor,
    DstAlpha,
    OneMinusDstAlpha,
    SrcAlphaSaturated,
    BlendColor,
    OneMinusBlendColor,
    BlendAlpha,
    OneMinusBlendAlpha,
    Src1Color,
    OneMinusSrc1Color,
    Src1Alpha,
    OneMinusSrc1Alpha
}

public struct SamplerStateDesc()
{
    public SamplerFilter MinFilter = SamplerFilter.Linear;
    public SamplerFilter MagFilter = SamplerFilter.Linear;
    public SamplerMip MipMap = SamplerMip.Disabled;
    public SamplerWrap WrapU = SamplerWrap.Repeat;
    public SamplerWrap WrapV = SamplerWrap.Repeat;
    public SamplerWrap WrapW = SamplerWrap.Repeat;
    public CompareOp DepthCompareOp = CompareOp.LessEqual;
    public uint8_t MipLodMin;
    public uint8_t MipLodMax = 15;
    public uint8_t MaxAnisotropic = 1;
    public bool DepthCompareEnabled = false;
    public string DebugName = string.Empty;
}

public struct StencilState()
{
    public StencilOp StencilFailureOp = StencilOp.Keep;
    public StencilOp DepthFailureOp = StencilOp.Keep;
    public StencilOp DepthStencilPassOp = StencilOp.Keep;
    public CompareOp StencilCompareOp = CompareOp.AlwaysPass;
    public uint32_t ReadMask = 0xFFFFFFFF;
    public uint32_t WriteMask = 0xFFFFFFFF;
}

public struct DepthState()
{
    public CompareOp CompareOp = CompareOp.AlwaysPass;
    public bool IsDepthWriteEnabled = false;
}

public enum PolygonMode : uint8_t
{
    Fill = 0,
    Line = 1,
    Point = 2,
}

public enum LoadOp : uint8_t
{
    Invalid = 0,
    DontCare,
    Load,
    Clear,
    None,
}

public enum StoreOp : uint8_t
{
    DontCare = 0,
    Store,
    MsaaResolve,
    None,
}

public enum ResolveMode : uint8_t
{
    None = 0,
    SampleZero, // always supported
    Average,
    Min,
    Max,
}

public enum ShaderStage : uint8_t
{
    Vertex,
    TessellationControl,
    TessellationEvaluation,
    Geometry,
    Fragment,
    Compute,
    Task,
    Mesh,
}

public struct ColorAttachment()
{
    public Format Format = Format.Invalid;
    public bool BlendEnabled = false;
    public BlendOp RgbBlendOp = BlendOp.Add;
    public BlendOp AlphaBlendOp = BlendOp.Add;
    public BlendFactor SrcRGBBlendFactor = BlendFactor.One;
    public BlendFactor SrcAlphaBlendFactor = BlendFactor.One;
    public BlendFactor DstRGBBlendFactor = BlendFactor.Zero;
    public BlendFactor DstAlphaBlendFactor = BlendFactor.Zero;
}

public struct Offset3D(int32_t x = 0, int32_t y = 0, int32_t z = 0)
{
    public int32_t x = x;
    public int32_t y = y;
    public int32_t z = z;
}

[StructLayout(LayoutKind.Sequential, Size = sizeof(uint64_t))]
public struct SubmitHandle
{
    public uint32_t BufferIndex = 0;
    public uint32_t SubmitId = 0;

    public SubmitHandle() { }

    public SubmitHandle(uint64_t handle)
    {
        HxDebug.Assert(handle != 0, "Invalid submit handle");
        BufferIndex = (uint32_t)(handle & 0xffffffff);
        SubmitId = (uint32_t)(handle >> 32);
    }

    public readonly bool Empty => SubmitId == 0;

    public readonly uint64_t Handle => ((uint64_t)SubmitId << 32) + BufferIndex;

    public static readonly SubmitHandle Null = new();
}



[StructLayout(LayoutKind.Explicit, Size = sizeof(uint32_t), Pack = 1)]
public readonly struct TextureFormatProperties
{
    [FieldOffset(0)] public readonly Format Format;
    [FieldOffset(5)] public readonly uint8_t BytesPerBlock;
    [FieldOffset(8)] public readonly uint8_t BlockWidth;
    [FieldOffset(11)] public readonly uint8_t BlockHeight;
    [FieldOffset(13)] public readonly uint8_t MinBlocksX;
    [FieldOffset(15)] public readonly uint8_t MinBlocksY;
    [FieldOffset(16)] public readonly bool Depth;
    [FieldOffset(17)] public readonly bool Stencil;
    [FieldOffset(18)] public readonly bool Compressed;
    [FieldOffset(20)] public readonly uint8_t NumPlanes;

    public TextureFormatProperties(Format format, uint8_t bytesPerBlock,
        uint8_t blockWidth = 1, uint8_t blockHeight = 1,
        uint8_t minBlocksX = 1, uint8_t minBlocksY = 1,
        bool depth = false, bool stencil = false,
        bool compressed = false, uint8_t numPlanes = 1)
    {
        this.Format = format;
        this.BytesPerBlock = bytesPerBlock;
        this.BlockWidth = blockWidth;
        this.BlockHeight = blockHeight;
        this.MinBlocksX = minBlocksX;
        this.MinBlocksY = minBlocksY;
        this.Depth = depth;
        this.Stencil = stencil;
        this.Compressed = compressed;
        this.NumPlanes = numPlanes;
    }

    public static readonly TextureFormatProperties[] Properties =
    [
        new (Format.Invalid, 1),
        new (Format.R_UN8, 1),
        new (Format.R_UI16, 2),
        new (Format.R_UI32, 4),
        new (Format.R_UN16, 2),
        new (Format.R_F16, 2),
        new (Format.R_F32, 4),
        new (Format.RG_UN8, 2),
        new (Format.RG_UI16, 4),
        new (Format.RG_UI32, 8),
        new (Format.RG_UN16, 4),
        new (Format.RG_F16, 4),
        new (Format.RG_F32, 8),
        new (Format.RGBA_UN8, 4),
        new (Format.RGBA_UI32, 16),
        new (Format.RGBA_F16, 8),
        new (Format.RGBA_F32, 16),
        new (Format.RGBA_SRGB8, 4),
        new (Format.BGRA_UN8, 4),
        new (Format.BGRA_SRGB8, 4),
        new (Format.A2B10G10R10_UN, 4),
        new (Format.A2R10G10B10_UN, 4),
        new (Format.ETC2_RGB8, 8, 4, 4, compressed:true),
        new (Format.ETC2_SRGB8, 8, 4, 4, compressed:true),
        new (Format.BC7_RGBA, 16, 4, 4 , compressed:true),
        new (Format.Z_UN16, 2, depth:true),
        new (Format.Z_UN24, 3, depth:true),
        new (Format.Z_F32, 4, depth:true),
        new (Format.Z_UN24_S_UI8, 4, depth:true, stencil:true),
        new (Format.Z_F32_S_UI8, 4, depth:true, stencil:true),
        new (Format.YUV_NV12, 2, compressed:true, numPlanes:2),
        new (Format.YUV_420p, 1, compressed:true, numPlanes:3),
    ];

    public static ref TextureFormatProperties GetProperty(Format format)
    {
        if (format == Format.Invalid || (uint8_t)format >= Properties.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Invalid texture format");
        }
        ref var props = ref Properties[(int)format];
        HxDebug.Assert(props.Format == format);
        return ref props;
    }
};

public static class FormatExtensions
{
    public static bool IsCompressedFormat(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].Compressed;
    }
    public static bool IsDepthFormat(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].Depth;
    }

    public static bool IsDepthOrStencilFormat(this Format format)
    {
        return (TextureFormatProperties.Properties[(int)format].Depth || TextureFormatProperties.Properties[(int)format].Stencil);
    }

    public static uint32_t GetNumImagePlanes(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].NumPlanes;
    }

    public static uint8_t GetBytesPerBlock(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].BytesPerBlock;
    }

    public static uint32_t GetVertexFormatSize(this VertexFormat format)
    {
        return format switch
        {
            VertexFormat.Float1 => 4,
            VertexFormat.Float2 => 8,
            VertexFormat.Float3 => 12,
            VertexFormat.Float4 => 16,
            VertexFormat.Byte1 => 1,
            VertexFormat.Byte2 => 2,
            VertexFormat.Byte3 => 3,
            VertexFormat.Byte4 => 4,
            VertexFormat.UByte1 => 1,
            VertexFormat.UByte2 => 2,
            VertexFormat.UByte3 => 3,
            VertexFormat.UByte4 => 4,
            VertexFormat.Short1 => 2,
            VertexFormat.Short2 => 4,
            VertexFormat.Short3 => 6,
            VertexFormat.Short4 => 8,
            VertexFormat.UShort1 => 2,
            VertexFormat.UShort2 => 4,
            VertexFormat.UShort3 => 6,
            VertexFormat.UShort4 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    public static uint32_t GetTextureBytesPerLayer(this Format format, uint32_t width, uint32_t height, uint32_t level)
    {
        if (format == Format.Invalid)
            return 0;
        uint32_t levelWidth = Math.Max(1u, width >> (int)level);
        uint32_t levelHeight = Math.Max(1u, height >> (int)level);
        ref var properties = ref TextureFormatProperties.Properties[(int)format];
        if (properties.Compressed)
        {
            uint32_t blockWidth = Math.Max(1u, properties.BlockWidth);
            uint32_t blockHeight = Math.Max(1u, properties.BlockHeight);
            uint32_t numBlocksX = (levelWidth + blockWidth - 1) / blockWidth;
            uint32_t numBlocksY = (levelHeight + blockHeight - 1) / blockHeight;
            return properties.BytesPerBlock * numBlocksX * numBlocksY;
        }
        else
        {
            return properties.BytesPerBlock * levelWidth * levelHeight;
        }
    }

    public static uint32_t GetTextureBytesPerPlane(this Format format, uint32_t width, uint32_t height, uint32_t plane)
    {
        ref var properties = ref TextureFormatProperties.Properties[(int)format];
        switch (format)
        {
            case Format.YUV_NV12:
                return width * height / (plane + 1);
            case Format.YUV_420p:
                return (plane == 0) ? width * height : (width / 2) * (height / 2);
        }
        return GetTextureBytesPerLayer(format, width, height, 0);
    }
}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.