namespace HelixToolkit.Nex.Graphics;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

/// <summary>
/// Provides compile-time constants used throughout the graphics system.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Maximum number of color attachments that can be bound to a framebuffer or render pass.
    /// </summary>
    public const uint8 MAX_COLOR_ATTACHMENTS = 8;

    /// <summary>
    /// Maximum number of mip levels supported for textures.
    /// </summary>
    public const uint8 MAX_MIP_LEVELS = 16;

    /// <summary>
    /// Maximum number of specialization constants that can be defined.
    /// </summary>
    public const uint8 SPECIALIZATION_CONSTANTS_MAX = 16;

    /// <summary>
    /// Maximum size of a ray tracing shader group.
    /// </summary>
    public const uint8 MAX_RAY_TRACING_SHADER_GROUP_SIZE = 4;
}

/// <summary>
/// Defines the graphics API backend being used.
/// </summary>
public enum BackendFlavor : uint8_t
{
    /// <summary>
    /// No backend or invalid backend.
    /// </summary>
    Invalid,

    /// <summary>
    /// Vulkan graphics API backend.
    /// </summary>
    Vulkan,
};

/// <summary>
/// Defines the format of indices in an index buffer.
/// </summary>
public enum IndexFormat : uint8_t
{
    /// <summary>
    /// 8-bit unsigned integer indices.
    /// </summary>
    UI8,

    /// <summary>
    /// 16-bit unsigned integer indices.
    /// </summary>
    UI16,

    /// <summary>
    /// 32-bit unsigned integer indices.
    /// </summary>
    UI32,
}

/// <summary>
/// Defines the primitive topology for rendering.
/// </summary>
public enum Topology : uint8_t
{
    /// <summary>
    /// Individual points.
    /// </summary>
    Point,

    /// <summary>
    /// Individual line segments (2 vertices per line).
    /// </summary>
    Line,

    /// <summary>
    /// Connected line segments forming a strip.
    /// </summary>
    LineStrip,

    /// <summary>
    /// Individual triangles (3 vertices per triangle).
    /// </summary>
    Triangle,

    /// <summary>
    /// Connected triangles forming a strip.
    /// </summary>
    TriangleStrip,

    /// <summary>
    /// Patch primitives for tessellation shaders.
    /// </summary>
    Patch,
}

/// <summary>
/// Defines the color space for textures and swapchains.
/// </summary>
public enum ColorSpace : uint8_t
{
    /// <summary>
    /// sRGB color space with linear encoding.
    /// </summary>
    SRGB_LINEAR,

    /// <summary>
    /// sRGB color space with non-linear (gamma-corrected) encoding.
    /// </summary>
    SRGB_NONLINEAR,

    /// <summary>
    /// Extended sRGB color space with linear encoding (wider gamut).
    /// </summary>
    SRGB_EXTENDED_LINEAR,

    /// <summary>
    /// HDR10 color space (ST2084 PQ transfer function with BT.2020 primaries).
    /// </summary>
    HDR10,
}

/// <summary>
/// Defines the type of texture resource.
/// </summary>
public enum TextureType : uint8_t
{
    /// <summary>
    /// 2D texture.
    /// </summary>
    Texture2D,

    /// <summary>
    /// 3D (volumetric) texture.
    /// </summary>
    Texture3D,

    /// <summary>
    /// Cube map texture (6 faces).
    /// </summary>
    TextureCube,
}

/// <summary>
/// Defines the minification and magnification filter mode for texture sampling.
/// </summary>
public enum SamplerFilter : uint8_t
{
    /// <summary>
    /// Nearest-neighbor filtering (no interpolation).
    /// </summary>
    Nearest = 0,

    /// <summary>
    /// Linear (bilinear/trilinear) filtering.
    /// </summary>
    Linear,
}

/// <summary>
/// Defines the mipmap filtering mode for texture sampling.
/// </summary>
public enum SamplerMip : uint8_t
{
    /// <summary>
    /// Mipmapping disabled (use base level only).
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Nearest mipmap level selection.
    /// </summary>
    Nearest,

    /// <summary>
    /// Linear interpolation between mipmap levels.
    /// </summary>
    Linear,
}

/// <summary>
/// Defines the texture coordinate wrapping/addressing mode.
/// </summary>
public enum SamplerWrap : uint8_t
{
    /// <summary>
    /// Repeat the texture (tiling).
    /// </summary>
    Repeat = 0,

    /// <summary>
    /// Clamp coordinates to [0, 1] range (edge pixels extend).
    /// </summary>
    Clamp,

    /// <summary>
    /// Repeat with mirroring at boundaries.
    /// </summary>
    MirrorRepeat,

    /// <summary>
    /// Clamp to a specified border color.
    /// </summary>
    ClampToBorder,
}

/// <summary>
/// Defines the type of hardware device (GPU).
/// </summary>
public enum HWDeviceType
{
    /// <summary>
    /// Discrete GPU (dedicated graphics card).
    /// </summary>
    Discrete = 1,

    /// <summary>
    /// External GPU (e.g., via Thunderbolt).
    /// </summary>
    External = 2,

    /// <summary>
    /// Integrated GPU (built into CPU).
    /// </summary>
    Integrated = 3,

    /// <summary>
    /// Software renderer (CPU-based).
    /// </summary>
    Software = 4,
}

/// <summary>
/// Describes the properties of a hardware device (GPU).
/// </summary>
public struct HWDeviceDesc
{
    /// <summary>
    /// Maximum length of the device name string.
    /// </summary>
    public const uint32_t MAX_PHYSICAL_DEVICE_NAME_SIZE = 256;

    /// <summary>
    /// Unique identifier (GUID) for the device.
    /// </summary>
    public nuint Guid;

    /// <summary>
    /// The type of hardware device.
    /// </summary>
    public HWDeviceType Type;

    /// <summary>
    /// The name of the device (e.g., "NVIDIA GeForce RTX 3080").
    /// </summary>
    public string Name;
}

/// <summary>
/// Defines the memory storage type for buffers and textures.
/// </summary>
public enum StorageType
{
    /// <summary>
    /// Device-local memory (GPU VRAM), fastest for GPU access.
    /// </summary>
    Device,

    /// <summary>
    /// Host-visible memory (CPU-accessible), allows direct CPU read/write.
    /// </summary>
    HostVisible,

    /// <summary>
    /// Memoryless storage (tile memory, no backing store), used for transient attachments.
    /// </summary>
    Memoryless,
}

/// <summary>
/// Defines which faces to cull during rasterization.
/// </summary>
public enum CullMode : uint8_t
{
    /// <summary>
    /// No face culling (render both front and back faces).
    /// </summary>
    None,

    /// <summary>
    /// Cull front-facing triangles.
    /// </summary>
    Front,

    /// <summary>
    /// Cull back-facing triangles.
    /// </summary>
    Back,
}

/// <summary>
/// Defines the winding order that determines front-facing triangles.
/// </summary>
public enum WindingMode : uint8_t
{
    /// <summary>
    /// Counter-clockwise winding defines front faces.
    /// </summary>
    CCW,

    /// <summary>
    /// Clockwise winding defines front faces.
    /// </summary>
    CW,
}

/// <summary>
/// Represents 3D dimensions (width, height, depth).
/// </summary>
/// <param name="width">Width dimension. Defaults to 1.</param>
/// <param name="height">Height dimension. Defaults to 1.</param>
/// <param name="depth">Depth dimension. Defaults to 1.</param>
public struct Dimensions(uint32_t width = 1, uint32_t height = 1, uint32_t depth = 1)
{
    /// <summary>
    /// Width dimension.
    /// </summary>
    public uint32_t Width = width;

    /// <summary>
    /// Height dimension.
    /// </summary>
    public uint32_t Height = height;

    /// <summary>
    /// Depth dimension.
    /// </summary>
    public uint32_t Depth = depth;

    /// <summary>
    /// Divides the width dimension by a value, keeping height and depth unchanged.
    /// </summary>
    /// <param name="v">The divisor.</param>
    /// <returns>New dimensions with divided width.</returns>
    public Dimensions Divide1D(uint32_t v)
    {
        return new Dimensions
        {
            Width = this.Width / v,
            Height = this.Height,
            Depth = this.Depth,
        };
    }

    /// <summary>
    /// Divides the width and height dimensions by a value, keeping depth unchanged.
    /// </summary>
    /// <param name="v">The divisor.</param>
    /// <returns>New dimensions with divided width and height.</returns>
    public Dimensions Divide2D(uint32_t v)
    {
        return new Dimensions
        {
            Width = this.Width / v,
            Height = this.Height / v,
            Depth = this.Depth,
        };
    }

    /// <summary>
    /// Divides all three dimensions (width, height, depth) by a value.
    /// </summary>
    /// <param name="v">The divisor.</param>
    /// <returns>New dimensions with all components divided.</returns>
    public Dimensions Divide3D(uint32_t v)
    {
        return new Dimensions
        {
            Width = this.Width / v,
            Height = this.Height / v,
            Depth = this.Depth / v,
        };
    }
}

/// <summary>
/// Defines comparison operations for depth and stencil testing.
/// </summary>
public enum CompareOp : uint8_t
{
    /// <summary>
    /// Test never passes.
    /// </summary>
    Never = 0,

    /// <summary>
    /// Test passes if the incoming value is less than the stored value.
    /// </summary>
    Less,

    /// <summary>
    /// Test passes if the incoming value equals the stored value.
    /// </summary>
    Equal,

    /// <summary>
    /// Test passes if the incoming value is less than or equal to the stored value.
    /// </summary>
    LessEqual,

    /// <summary>
    /// Test passes if the incoming value is greater than the stored value.
    /// </summary>
    Greater,

    /// <summary>
    /// Test passes if the incoming value does not equal the stored value.
    /// </summary>
    NotEqual,

    /// <summary>
    /// Test passes if the incoming value is greater than or equal to the stored value.
    /// </summary>
    GreaterEqual,

    /// <summary>
    /// Test always passes.
    /// </summary>
    AlwaysPass,
}

/// <summary>
/// Defines stencil operations to perform based on test results.
/// </summary>
public enum StencilOp : uint8_t
{
    /// <summary>
    /// Keep the current stencil value.
    /// </summary>
    Keep = 0,

    /// <summary>
    /// Set the stencil value to zero.
    /// </summary>
    Zero,

    /// <summary>
    /// Replace the stencil value with the reference value.
    /// </summary>
    Replace,

    /// <summary>
    /// Increment the stencil value and clamp to maximum.
    /// </summary>
    IncrementClamp,

    /// <summary>
    /// Decrement the stencil value and clamp to zero.
    /// </summary>
    DecrementClamp,

    /// <summary>
    /// Bitwise invert the stencil value.
    /// </summary>
    Invert,

    /// <summary>
    /// Increment the stencil value with wrapping.
    /// </summary>
    IncrementWrap,

    /// <summary>
    /// Decrement the stencil value with wrapping.
    /// </summary>
    DecrementWrap,
}

/// <summary>
/// Defines blend operations for color blending.
/// </summary>
public enum BlendOp : uint8_t
{
    /// <summary>
    /// Add source and destination: src + dst.
    /// </summary>
    Add = 0,

    /// <summary>
    /// Subtract destination from source: src - dst.
    /// </summary>
    Subtract,

    /// <summary>
    /// Subtract source from destination: dst - src.
    /// </summary>
    ReverseSubtract,

    /// <summary>
    /// Minimum of source and destination: min(src, dst).
    /// </summary>
    Min,

    /// <summary>
    /// Maximum of source and destination: max(src, dst).
    /// </summary>
    Max,
}

/// <summary>
/// Defines blend factors for color blending calculations.
/// </summary>
public enum BlendFactor : uint8_t
{
    /// <summary>
    /// Blend factor is (0, 0, 0, 0).
    /// </summary>
    Zero = 0,

    /// <summary>
    /// Blend factor is (1, 1, 1, 1).
    /// </summary>
    One,

    /// <summary>
    /// Blend factor is the source color (Rs, Gs, Bs, As).
    /// </summary>
    SrcColor,

    /// <summary>
    /// Blend factor is one minus the source color (1-Rs, 1-Gs, 1-Bs, 1-As).
    /// </summary>
    OneMinusSrcColor,

    /// <summary>
    /// Blend factor is the source alpha (As, As, As, As).
    /// </summary>
    SrcAlpha,

    /// <summary>
    /// Blend factor is one minus the source alpha (1-As, 1-As, 1-As, 1-As).
    /// </summary>
    OneMinusSrcAlpha,

    /// <summary>
    /// Blend factor is the destination color (Rd, Gd, Bd, Ad).
    /// </summary>
    DstColor,

    /// <summary>
    /// Blend factor is one minus the destination color (1-Rd, 1-Gd, 1-Bd, 1-Ad).
    /// </summary>
    OneMinusDstColor,

    /// <summary>
    /// Blend factor is the destination alpha (Ad, Ad, Ad, Ad).
    /// </summary>
    DstAlpha,

    /// <summary>
    /// Blend factor is one minus the destination alpha (1-Ad, 1-Ad, 1-Ad, 1-Ad).
    /// </summary>
    OneMinusDstAlpha,

    /// <summary>
    /// Blend factor is saturated source alpha (min(As, 1-Ad), ...).
    /// </summary>
    SrcAlphaSaturated,

    /// <summary>
    /// Blend factor is the constant blend color.
    /// </summary>
    BlendColor,

    /// <summary>
    /// Blend factor is one minus the constant blend color.
    /// </summary>
    OneMinusBlendColor,

    /// <summary>
    /// Blend factor is the constant blend alpha.
    /// </summary>
    BlendAlpha,

    /// <summary>
    /// Blend factor is one minus the constant blend alpha.
    /// </summary>
    OneMinusBlendAlpha,

    /// <summary>
    /// Blend factor is the secondary source color (dual-source blending).
    /// </summary>
    Src1Color,

    /// <summary>
    /// Blend factor is one minus the secondary source color.
    /// </summary>
    OneMinusSrc1Color,

    /// <summary>
    /// Blend factor is the secondary source alpha (dual-source blending).
    /// </summary>
    Src1Alpha,

    /// <summary>
    /// Blend factor is one minus the secondary source alpha.
    /// </summary>
    OneMinusSrc1Alpha,
}

/// <summary>
/// Describes the configuration for a texture sampler.
/// </summary>
public struct SamplerStateDesc()
{
    /// <summary>
    /// Minification filter mode. Defaults to Linear.
    /// </summary>
    public SamplerFilter MinFilter = SamplerFilter.Linear;

    /// <summary>
    /// Magnification filter mode. Defaults to Linear.
    /// </summary>
    public SamplerFilter MagFilter = SamplerFilter.Linear;

    /// <summary>
    /// Mipmap filter mode. Defaults to Disabled.
    /// </summary>
    public SamplerMip MipMap = SamplerMip.Disabled;

    /// <summary>
    /// Texture wrap mode for U coordinate. Defaults to Repeat.
    /// </summary>
    public SamplerWrap WrapU = SamplerWrap.Repeat;

    /// <summary>
    /// Texture wrap mode for V coordinate. Defaults to Repeat.
    /// </summary>
    public SamplerWrap WrapV = SamplerWrap.Repeat;

    /// <summary>
    /// Texture wrap mode for W coordinate. Defaults to Repeat.
    /// </summary>
    public SamplerWrap WrapW = SamplerWrap.Repeat;

    /// <summary>
    /// Comparison operation for depth comparison samplers. Defaults to LessEqual.
    /// </summary>
    public CompareOp DepthCompareOp = CompareOp.LessEqual;

    /// <summary>
    /// Minimum mipmap level to use.
    /// </summary>
    public uint8_t MipLodMin;

    /// <summary>
    /// Maximum mipmap level to use. Defaults to 15.
    /// </summary>
    public uint8_t MipLodMax = 15;

    /// <summary>
    /// Maximum anisotropic filtering samples. Defaults to 1 (no anisotropic filtering).
    /// </summary>
    public uint8_t MaxAnisotropic = 1;

    /// <summary>
    /// Whether depth comparison is enabled. Defaults to false.
    /// </summary>
    public bool DepthCompareEnabled = false;

    /// <summary>
    /// Optional debug name for the sampler.
    /// </summary>
    public string DebugName = string.Empty;
}

/// <summary>
/// Describes stencil test and operation configuration.
/// </summary>
public struct StencilState()
{
    /// <summary>
    /// Operation to perform when stencil test fails. Defaults to Keep.
    /// </summary>
    public StencilOp StencilFailureOp = StencilOp.Keep;

    /// <summary>
    /// Operation to perform when stencil test passes but depth test fails. Defaults to Keep.
    /// </summary>
    public StencilOp DepthFailureOp = StencilOp.Keep;

    /// <summary>
    /// Operation to perform when both stencil and depth tests pass. Defaults to Keep.
    /// </summary>
    public StencilOp DepthStencilPassOp = StencilOp.Keep;

    /// <summary>
    /// Comparison operation for stencil testing. Defaults to AlwaysPass.
    /// </summary>
    public CompareOp StencilCompareOp = CompareOp.AlwaysPass;

    /// <summary>
    /// Bitmask for reading stencil values during test. Defaults to 0xFFFFFFFF (all bits).
    /// </summary>
    public uint32_t ReadMask = 0xFFFFFFFF;

    /// <summary>
    /// Bitmask for writing stencil values. Defaults to 0xFFFFFFFF (all bits).
    /// </summary>
    public uint32_t WriteMask = 0xFFFFFFFF;
}

/// <summary>
/// Describes depth test configuration.
/// </summary>
public struct DepthState()
{
    /// <summary>
    /// Comparison operation for depth testing. Defaults to AlwaysPass.
    /// </summary>
    public CompareOp CompareOp = CompareOp.AlwaysPass;

    /// <summary>
    /// Whether depth writes are enabled. Defaults to false.
    /// </summary>
    public bool IsDepthWriteEnabled = false;
}

/// <summary>
/// Defines the polygon rasterization mode.
/// </summary>
public enum PolygonMode : uint8_t
{
    /// <summary>
    /// Fill polygons (solid rendering).
    /// </summary>
    Fill = 0,

    /// <summary>
    /// Draw polygon edges as lines (wireframe).
    /// </summary>
    Line = 1,

    /// <summary>
    /// Draw polygon vertices as points.
    /// </summary>
    Point = 2,
}

/// <summary>
/// Defines the load operation for render pass attachments.
/// </summary>
public enum LoadOp : uint8_t
{
    /// <summary>
    /// Invalid or unspecified load operation.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// Don't care about previous contents (undefined).
    /// </summary>
    DontCare,

    /// <summary>
    /// Load the existing contents.
    /// </summary>
    Load,

    /// <summary>
    /// Clear the attachment to a specified value.
    /// </summary>
    Clear,

    /// <summary>
    /// No load operation (attachment not used).
    /// </summary>
    None,
}

/// <summary>
/// Defines the store operation for render pass attachments.
/// </summary>
public enum StoreOp : uint8_t
{
    /// <summary>
    /// Don't care about storing results (discard).
    /// </summary>
    DontCare = 0,

    /// <summary>
    /// Store the results to memory.
    /// </summary>
    Store,

    /// <summary>
    /// Resolve multisampled attachment to single-sampled.
    /// </summary>
    MsaaResolve,

    /// <summary>
    /// No store operation (attachment not used).
    /// </summary>
    None,
}

/// <summary>
/// Defines the resolve mode for multisampled attachments.
/// </summary>
public enum ResolveMode : uint8_t
{
    /// <summary>
    /// No resolve operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Resolve by taking sample zero (always supported).
    /// </summary>
    SampleZero, // always supported

    /// <summary>
    /// Resolve by averaging all samples.
    /// </summary>
    Average,

    /// <summary>
    /// Resolve by taking the minimum value across samples.
    /// </summary>
    Min,

    /// <summary>
    /// Resolve by taking the maximum value across samples.
    /// </summary>
    Max,
}

/// <summary>
/// Defines the shader pipeline stage.
/// </summary>
public enum ShaderStage : uint8_t
{
    /// <summary>
    /// Vertex shader stage.
    /// </summary>
    Vertex,

    /// <summary>
    /// Tessellation control shader stage (hull shader).
    /// </summary>
    TessellationControl,

    /// <summary>
    /// Tessellation evaluation shader stage (domain shader).
    /// </summary>
    TessellationEvaluation,

    /// <summary>
    /// Geometry shader stage.
    /// </summary>
    Geometry,

    /// <summary>
    /// Fragment shader stage (pixel shader).
    /// </summary>
    Fragment,

    /// <summary>
    /// Compute shader stage.
    /// </summary>
    Compute,

    /// <summary>
    /// Task shader stage (for mesh shading pipeline).
    /// </summary>
    Task,

    /// <summary>
    /// Mesh shader stage.
    /// </summary>
    Mesh,
}

/// <summary>
/// Describes a color attachment's format and blending configuration.
/// </summary>
public struct ColorAttachment()
{
    /// <summary>
    /// Pixel format of the color attachment. Defaults to Invalid.
    /// </summary>
    public Format Format = Format.Invalid;

    /// <summary>
    /// Whether blending is enabled for this attachment. Defaults to false.
    /// </summary>
    public bool BlendEnabled = false;

    /// <summary>
    /// Blend operation for RGB channels. Defaults to Add.
    /// </summary>
    public BlendOp RgbBlendOp = BlendOp.Add;

    /// <summary>
    /// Blend operation for alpha channel. Defaults to Add.
    /// </summary>
    public BlendOp AlphaBlendOp = BlendOp.Add;

    /// <summary>
    /// Source blend factor for RGB channels. Defaults to One.
    /// </summary>
    public BlendFactor SrcRGBBlendFactor = BlendFactor.One;

    /// <summary>
    /// Source blend factor for alpha channel. Defaults to One.
    /// </summary>
    public BlendFactor SrcAlphaBlendFactor = BlendFactor.One;

    /// <summary>
    /// Destination blend factor for RGB channels. Defaults to Zero.
    /// </summary>
    public BlendFactor DstRGBBlendFactor = BlendFactor.Zero;

    /// <summary>
    /// Destination blend factor for alpha channel. Defaults to Zero.
    /// </summary>
    public BlendFactor DstAlphaBlendFactor = BlendFactor.Zero;
}

/// <summary>
/// Represents a 3D offset (position) with signed integer coordinates.
/// </summary>
/// <param name="x">X coordinate. Defaults to 0.</param>
/// <param name="y">Y coordinate. Defaults to 0.</param>
/// <param name="z">Z coordinate. Defaults to 0.</param>
public struct Offset3D(int32_t x = 0, int32_t y = 0, int32_t z = 0)
{
    /// <summary>
    /// X coordinate.
    /// </summary>
    public int32_t X = x;

    /// <summary>
    /// Y coordinate.
    /// </summary>
    public int32_t Y = y;

    /// <summary>
    /// Z coordinate.
    /// </summary>
    public int32_t Z = z;
}

/// <summary>
/// Represents a handle for tracking command buffer submissions.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = sizeof(uint64_t))]
public struct SubmitHandle
{
    /// <summary>
    /// The command buffer index.
    /// </summary>
    public uint32_t BufferIndex = 0;

    /// <summary>
    /// The submission ID.
    /// </summary>
    public uint32_t SubmitId = 0;

    /// <summary>
    /// Initializes a new empty submit handle.
    /// </summary>
    public SubmitHandle() { }

    /// <summary>
    /// Initializes a submit handle from a packed 64-bit value.
    /// </summary>
    /// <param name="handle">The packed 64-bit handle value.</param>
    public SubmitHandle(uint64_t handle)
    {
        HxDebug.Assert(handle != 0, "Invalid submit handle");
        BufferIndex = (uint32_t)(handle & 0xffffffff);
        SubmitId = (uint32_t)(handle >> 32);
    }

    /// <summary>
    /// Gets whether this handle is empty (invalid).
    /// </summary>
    public readonly bool Empty => SubmitId == 0;

    /// <summary>
    /// Gets the packed 64-bit handle value.
    /// </summary>
    public readonly uint64_t Handle => ((uint64_t)SubmitId << 32) + BufferIndex;

    /// <summary>
    /// A predefined null/empty submit handle.
    /// </summary>
    public static readonly SubmitHandle Null = new();
}

/// <summary>
/// Describes the properties of a texture format, including layout and capabilities.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = sizeof(uint32_t), Pack = 1)]
public readonly struct TextureFormatProperties
{
    [FieldOffset(0)]
    public readonly Format Format;

    [FieldOffset(5)]
    public readonly uint8_t BytesPerBlock;

    [FieldOffset(8)]
    public readonly uint8_t BlockWidth;

    [FieldOffset(11)]
    public readonly uint8_t BlockHeight;

    [FieldOffset(13)]
    public readonly uint8_t MinBlocksX;

    [FieldOffset(15)]
    public readonly uint8_t MinBlocksY;

    [FieldOffset(16)]
    public readonly bool Depth;

    [FieldOffset(17)]
    public readonly bool Stencil;

    [FieldOffset(18)]
    public readonly bool Compressed;

    [FieldOffset(20)]
    public readonly uint8_t NumPlanes;

    /// <summary>
    /// Initializes a new texture format properties descriptor.
    /// </summary>
    public TextureFormatProperties(
        Format format,
        uint8_t bytesPerBlock,
        uint8_t blockWidth = 1,
        uint8_t blockHeight = 1,
        uint8_t minBlocksX = 1,
        uint8_t minBlocksY = 1,
        bool depth = false,
        bool stencil = false,
        bool compressed = false,
        uint8_t numPlanes = 1
    )
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

    /// <summary>
    /// Array of predefined properties for all supported texture formats.
    /// </summary>
    public static readonly TextureFormatProperties[] Properties =
    [
        new(Format.Invalid, 1),
        new(Format.R_UN8, 1),
        new(Format.R_UI16, 2),
        new(Format.R_UI32, 4),
        new(Format.R_UN16, 2),
        new(Format.R_F16, 2),
        new(Format.R_F32, 4),
        new(Format.RG_UN8, 2),
        new(Format.RG_UI16, 4),
        new(Format.RG_UI32, 8),
        new(Format.RG_UN16, 4),
        new(Format.RG_F16, 4),
        new(Format.RG_F32, 8),
        new(Format.RGBA_UN8, 4),
        new(Format.RGBA_UI32, 16),
        new(Format.RGBA_F16, 8),
        new(Format.RGBA_F32, 16),
        new(Format.RGBA_SRGB8, 4),
        new(Format.BGRA_UN8, 4),
        new(Format.BGRA_SRGB8, 4),
        new(Format.A2B10G10R10_UN, 4),
        new(Format.A2R10G10B10_UN, 4),
        new(Format.ETC2_RGB8, 8, 4, 4, compressed: true),
        new(Format.ETC2_SRGB8, 8, 4, 4, compressed: true),
        new(Format.BC7_RGBA, 16, 4, 4, compressed: true),
        new(Format.Z_UN16, 2, depth: true),
        new(Format.Z_UN24, 3, depth: true),
        new(Format.Z_F32, 4, depth: true),
        new(Format.Z_UN24_S_UI8, 4, depth: true, stencil: true),
        new(Format.Z_F32_S_UI8, 4, depth: true, stencil: true),
        new(Format.YUV_NV12, 2, compressed: true, numPlanes: 2),
        new(Format.YUV_420p, 1, compressed: true, numPlanes: 3),
    ];

    /// <summary>
    /// Gets the properties for a specific texture format.
    /// </summary>
    /// <param name="format">The texture format.</param>
    /// <returns>A reference to the format properties.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the format is invalid or not supported.</exception>
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

/// <summary>
/// Provides extension methods for working with texture and vertex formats.
/// </summary>
public static class FormatExtensions
{
    /// <summary>
    /// Checks if a texture format is compressed.
    /// </summary>
    public static bool IsCompressedFormat(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].Compressed;
    }

    /// <summary>
    /// Checks if a texture format is a depth format.
    /// </summary>
    public static bool IsDepthFormat(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].Depth;
    }

    /// <summary>
    /// Checks if a texture format is a depth or stencil format.
    /// </summary>
    public static bool IsDepthOrStencilFormat(this Format format)
    {
        return (
            TextureFormatProperties.Properties[(int)format].Depth
            || TextureFormatProperties.Properties[(int)format].Stencil
        );
    }

    /// <summary>
    /// Gets the number of image planes for a format (relevant for multi-planar formats like YUV).
    /// </summary>
    public static uint32_t GetNumImagePlanes(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].NumPlanes;
    }

    /// <summary>
    /// Gets the number of bytes per block for a texture format.
    /// </summary>
    public static uint8_t GetBytesPerBlock(this Format format)
    {
        return TextureFormatProperties.Properties[(int)format].BytesPerBlock;
    }

    /// <summary>
    /// Gets the size in bytes of a vertex format.
    /// </summary>
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

    /// <summary>
    /// Calculates the total bytes required for a single layer of a texture at a specific mip level.
    /// </summary>
    public static uint32_t GetTextureBytesPerLayer(
        this Format format,
        uint32_t width,
        uint32_t height,
        uint32_t level
    )
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

    /// <summary>
    /// Calculates the bytes required for a specific plane of a multi-planar texture format.
    /// </summary>
    public static uint32_t GetTextureBytesPerPlane(
        this Format format,
        uint32_t width,
        uint32_t height,
        uint32_t plane
    )
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
