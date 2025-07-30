using System.Diagnostics.CodeAnalysis;

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
    Repeat = 0, Clamp,
    MirrorRepeat
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
    public const uint32_t LVK_MAX_PHYSICAL_DEVICE_NAME_SIZE = 256;
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

public struct ComputePipelineDesc()
{
    public ShaderModuleHandle smComp;
    public SpecializationConstantDesc SpecInfo;
    public string EntryPoint = "main";
    public string DebugName = string.Empty;
}

public enum PolygonMode : uint8_t
{
    Fill = 0,
    Line = 1,
    Point = 2,
}

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

public struct VertexAttribute()
{
    public uint32_t Location; // a buffer which contains this attribute stream
    public uint32_t Binding;
    public VertexFormat Format; // per-element format
    public size_t Offset; // an offset where the first element of this attribute stream starts
}

public struct VertexInputBinding()
{
    public uint32_t Stride;
}

public struct VertexInput()
{
    public const uint32_t MAX_VERTEX_BINDINGS = 16;
    public const uint32_t MAX_VERTEX_ATTRIBUTES = 16;

    public readonly VertexInputBinding[] Bindings = new VertexInputBinding[MAX_VERTEX_BINDINGS];

    public readonly VertexAttribute[] Attributes = new VertexAttribute[MAX_VERTEX_ATTRIBUTES];

    public readonly uint32_t BindingCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_BINDINGS; i++)
        {
            if (Bindings[i].Stride == 0)
                return i;
        }
        return MAX_VERTEX_BINDINGS;
    }

    public readonly uint32_t AttributeCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_ATTRIBUTES; i++)
        {
            if (Attributes[i].Format == VertexFormat.Invalid)
                return i;
        }
        return MAX_VERTEX_ATTRIBUTES;
    }

    public readonly uint32_t GetVertexSize()
    {
        uint32_t vertexSize = 0;
        for (uint32_t i = 0; i < MAX_VERTEX_ATTRIBUTES && Attributes[i].Format != VertexFormat.Invalid; i++)
        {
            HxDebug.Assert(Attributes[i].Offset == vertexSize, "Unsupported vertex attributes format");
            vertexSize += Attributes[i].Format.GetVertexFormatSize();
        }
        return vertexSize;
    }

    public static readonly VertexInput Null = new();
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

public enum ShaderDataType
{
    Auto, // automatically detect the type based on the data
    Spirv,
    Glsl
}

public struct ShaderDefine(string name, string? value = null)
{
    public string Name = name;
    public string? Value = value;

    public override readonly string ToString()
    {
        return $"#define {Name} {Value ?? ""}";
    }
}

public struct ShaderModuleDesc()
{
    public ShaderStage Stage;
    public ShaderDataType DataType = ShaderDataType.Auto; // default is SPIR-V
    public nint Data;
    public size_t DataSize;

    public ShaderDefine[] Defines = [];
    public string DebugName = string.Empty;
}

public struct SpecializationConstantEntry()
{
    public uint32_t ConstantId;
    public uint32_t Offset; // offset within ShaderSpecializationConstantDesc::data
    public size_t Size;
};

public struct SpecializationConstantDesc()
{
    public const uint8_t SPECIALIZATION_CONSTANTS_MAX = 16;

    public readonly SpecializationConstantEntry[] Entries = new SpecializationConstantEntry[SPECIALIZATION_CONSTANTS_MAX];

    public byte[] Data = [];

    public readonly uint32_t NumSpecializationConstants()
    {
        for (uint32_t i = 0; i < SPECIALIZATION_CONSTANTS_MAX; i++)
        {
            if (Entries[i].Size == 0)
                return i;
        }
        return SPECIALIZATION_CONSTANTS_MAX;
    }
};

public struct RenderPipelineDesc()
{
    public Topology Topology = Topology.Triangle;

    public VertexInput VertexInput = VertexInput.Null;

    public ShaderModuleHandle SmVert = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmTesc = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmTese = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmGeom = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmTask = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmMesh = ShaderModuleHandle.Null;
    public ShaderModuleHandle SmFrag = ShaderModuleHandle.Null;

    public SpecializationConstantDesc SpecInfo = new();

    public string EntryPointVert = "main";
    public string EntryPointTesc = "main";
    public string EntryPointTese = "main";
    public string EntryPointGeom = "main";
    public string EntryPointTask = "main";
    public string EntryPointMesh = "main";
    public string EntryPointFrag = "main";

    public readonly ColorAttachment[] Color = new ColorAttachment[Constants.MAX_COLOR_ATTACHMENTS];
    public Format DepthFormat = Format.Invalid;
    public Format StencilFormat = Format.Invalid;

    public CullMode CullMode = CullMode.Back;
    public WindingMode FrontFaceWinding = WindingMode.CCW;
    public PolygonMode PolygonMode = PolygonMode.Fill;

    public StencilState BackFaceStencil;
    public StencilState FrontFaceStencil;

    public uint32_t SamplesCount = 1u;
    public uint32_t PatchControlPoints = 0;
    public float MinSampleShading = 0.0f;

    public string DebugName = string.Empty;

    public readonly uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (Color[i].Format == Format.Invalid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }
}

public sealed class RenderPass
{
    public struct AttachmentDesc()
    {
        public LoadOp LoadOp = LoadOp.Invalid;
        public StoreOp StoreOp = StoreOp.Store;
        public ResolveMode ResolveMode = ResolveMode.Average;
        public uint8_t Layer = 0;
        public uint8_t Level = 0;
        public Color4 ClearColor = new(0, 0, 0, 0);
        public float ClearDepth = 1.0f;
        public uint32_t ClearStencil = 0;
    }

    public readonly AttachmentDesc[] Colors = new AttachmentDesc[Constants.MAX_COLOR_ATTACHMENTS];

    public AttachmentDesc Depth = new() { LoadOp = LoadOp.DontCare, StoreOp = StoreOp.DontCare };
    public AttachmentDesc Stencil = new() { LoadOp = LoadOp.Invalid, StoreOp = StoreOp.DontCare };
    public uint32_t LayerCount = 1;
    public uint32_t ViewMask;

    public RenderPass()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            Colors[i] = new AttachmentDesc();
        }
    }

    public uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (Colors[i].LoadOp == LoadOp.Invalid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }
}

public sealed class Framebuffer
{
    public struct AttachmentDesc()
    {
        public TextureHandle Texture = TextureHandle.Null;
        public TextureHandle ResolveTexture = TextureHandle.Null;
    };

    public readonly AttachmentDesc[] Colors = new AttachmentDesc[Constants.MAX_COLOR_ATTACHMENTS];
    public AttachmentDesc DepthStencil = new();

    public string DebugName = string.Empty;

    public Framebuffer()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            Colors[i] = new AttachmentDesc();
        }
    }

    public uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (!Colors[i].Texture.Valid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }

    public static readonly Framebuffer Null = new();
}

[Flags]
public enum BufferUsageBits : uint8_t
{
    None = 0,
    Index = 1 << 0,
    Vertex = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Indirect = 1 << 4,
    // ray tracing
    ShaderBindingTable = 1 << 5,
    AccelStructBuildInputReadOnly = 1 << 6,
    AccelStructStorage = 1 << 7
}

public struct BufferDesc(BufferUsageBits usage, StorageType storage, nint data, size_t dataSize, string? debugName = null)
{
    public BufferUsageBits Usage = usage;
    public StorageType Storage = storage;
    public nint Data = data;
    public size_t DataSize = dataSize;
    public string DebugName = debugName ?? string.Empty;
}

public struct Offset3D(int32_t x = 0, int32_t y = 0, int32_t z = 0)
{
    public int32_t x = x;
    public int32_t y = y;
    public int32_t z = z;
}

public struct TextureLayers()
{
    public uint32_t mipLevel;
    public uint32_t layer;
    public uint32_t numLayers = 1;
};

public struct TextureRangeDesc()
{
    public Offset3D offset;
    public Dimensions dimensions = new() { Width = 1, Height = 1, Depth = 1 };
    public uint32_t layer;
    public uint32_t numLayers = 1;
    public uint32_t mipLevel;
    public uint32_t numMipLevels = 1;
};

[Flags]
public enum TextureUsageBits : uint8_t
{
    None = 0,
    Sampled = 1 << 0,
    Storage = 1 << 1,
    Attachment = 1 << 2,
}

public enum Swizzle : uint8_t
{
    Default = 0,
    Swizzle_0,
    Swizzle_1,
    Swizzle_R,
    Swizzle_G,
    Swizzle_B,
    Swizzle_A,
};

public struct ComponentMapping()
{
    public Swizzle R = Swizzle.Default;
    public Swizzle G = Swizzle.Default;
    public Swizzle B = Swizzle.Default;
    public Swizzle A = Swizzle.Default;
    public readonly bool Identity()
    {
        return R == Swizzle.Default && G == Swizzle.Default && B == Swizzle.Default && A == Swizzle.Default;
    }
}

public struct TextureDesc()
{
    public TextureType Type = TextureType.Texture2D;
    public Format Format = Format.Invalid;
    public Dimensions Dimensions = new() { Width = 1, Height = 1, Depth = 1 };
    public uint32_t NumLayers = 1;
    public uint32_t NumSamples = 0;
    public TextureUsageBits Usage = TextureUsageBits.Sampled;
    public uint32_t NumMipLevels = 1;
    public StorageType Storage = StorageType.Device;
    public ComponentMapping Swizzle;
    public nint Data = nint.Zero;
    public size_t DataSize = 0; // size of the data to upload, if not null
    public uint32_t DataNumMipLevels = 1; // how many mip-levels we want to upload
    public bool GenerateMipmaps = false; // generate mip-levels immediately, valid only with non-null data
    public string DebugName = string.Empty;
}

public struct TextureViewDesc()
{
    public TextureType Type = TextureType.Texture2D;
    public uint32_t Layer;
    public uint32_t NumLayers = 1;
    public uint32_t MipLevel;
    public uint32_t NumMipLevels = 1;
    public ComponentMapping Swizzle;
}

public sealed class Dependencies
{
    public const uint32_t MAX_SUBMIT_DEPENDENCIES = 4;

    public readonly TextureHandle[] Textures = new TextureHandle[MAX_SUBMIT_DEPENDENCIES];

    public readonly BufferHandle[] Buffers = new BufferHandle[MAX_SUBMIT_DEPENDENCIES];

    public static readonly Dependencies Empty = new();

    public Dependencies()
    {
        for (uint32_t i = 0; i < MAX_SUBMIT_DEPENDENCIES; i++)
        {
            Textures[i] = TextureHandle.Null;
            Buffers[i] = BufferHandle.Null;
        }
    }
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