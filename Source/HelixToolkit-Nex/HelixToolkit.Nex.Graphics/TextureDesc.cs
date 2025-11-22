namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents specifications for texture layers within a texture resource.
/// </summary>
public struct TextureLayers()
{
    /// <summary>
    /// The mip level to access.
    /// </summary>
    public uint32_t MipLevel;

    /// <summary>
    /// The starting array layer index.
    /// </summary>
    public uint32_t Layer;

    /// <summary>
    /// The number of array layers to access. Defaults to 1.
    /// </summary>
    public uint32_t NumLayers = 1;
};

/// <summary>
/// Describes a range within a texture resource, including spatial dimensions, layers, and mip levels.
/// </summary>
public struct TextureRangeDesc()
{
    /// <summary>
    /// The 3D offset within the texture where the range begins.
    /// </summary>
    public Offset3D Offset;

    /// <summary>
    /// The dimensions of the range (width, height, depth). Defaults to 1x1x1.
    /// </summary>
    public Dimensions Dimensions = new()
    {
        Width = 1,
        Height = 1,
        Depth = 1,
    };

    /// <summary>
    /// The starting array layer index.
    /// </summary>
    public uint32_t Layer;

    /// <summary>
    /// The number of array layers in the range. Defaults to 1.
    /// </summary>
    public uint32_t NumLayers = 1;

    /// <summary>
    /// The starting mip level.
    /// </summary>
    public uint32_t MipLevel;

    /// <summary>
    /// The number of mip levels in the range. Defaults to 1.
    /// </summary>
    public uint32_t NumMipLevels = 1;
};

/// <summary>
/// Bitflags that describe how a texture will be used within the graphics system.
/// These values can be combined using a bitwise OR to indicate multiple usages.
/// </summary>
[Flags]
public enum TextureUsageBits : uint8_t
{
    /// <summary>
    /// No usage flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Texture can be sampled in shaders.
    /// </summary>
    Sampled = 1 << 0,

    /// <summary>
    /// Texture can be used as a storage image (read/write in compute shaders).
    /// </summary>
    Storage = 1 << 1,

    /// <summary>
    /// Texture can be used as a render target attachment (color, depth, or stencil).
    /// </summary>
    Attachment = 1 << 2,
}

/// <summary>
/// Defines component swizzling options for texture sampling.
/// </summary>
public enum Swizzle : uint8_t
{
    /// <summary>
    /// Use the default component mapping (identity).
    /// </summary>
    Default = 0,

    /// <summary>
    /// Swizzle to constant 0.
    /// </summary>
    Swizzle_0,

    /// <summary>
    /// Swizzle to constant 1.
    /// </summary>
    Swizzle_1,

    /// <summary>
    /// Map to the red component.
    /// </summary>
    Swizzle_R,

    /// <summary>
    /// Map to the green component.
    /// </summary>
    Swizzle_G,

    /// <summary>
    /// Map to the blue component.
    /// </summary>
    Swizzle_B,

    /// <summary>
    /// Map to the alpha component.
    /// </summary>
    Swizzle_A,
};

/// <summary>
/// Defines how texture components (R, G, B, A) are mapped during sampling.
/// </summary>
public struct ComponentMapping()
{
    /// <summary>
    /// Swizzle for the red component.
    /// </summary>
    public Swizzle R = Swizzle.Default;

    /// <summary>
    /// Swizzle for the green component.
    /// </summary>
    public Swizzle G = Swizzle.Default;

    /// <summary>
    /// Swizzle for the blue component.
    /// </summary>
    public Swizzle B = Swizzle.Default;

    /// <summary>
    /// Swizzle for the alpha component.
    /// </summary>
    public Swizzle A = Swizzle.Default;

    /// <summary>
    /// Checks if the component mapping is identity (all default).
    /// </summary>
    /// <returns>True if all components use default mapping; otherwise, false.</returns>
    public readonly bool Identity()
    {
        return R == Swizzle.Default
            && G == Swizzle.Default
            && B == Swizzle.Default
            && A == Swizzle.Default;
    }
}

/// <summary>
/// Describes the properties required to create or initialize a GPU texture resource.
/// </summary>
public struct TextureDesc()
{
    /// <summary>
    /// The type of texture (2D, 3D, or Cube).
    /// </summary>
    public TextureType Type = TextureType.Texture2D;

    /// <summary>
    /// The pixel format of the texture.
    /// </summary>
    public Format Format = Format.Invalid;

    /// <summary>
    /// The dimensions of the texture (width, height, depth). Defaults to 1x1x1.
    /// </summary>
    public Dimensions Dimensions = new()
    {
        Width = 1,
        Height = 1,
        Depth = 1,
    };

    /// <summary>
    /// The number of array layers. For cube textures, this must be a multiple of 6. Defaults to 1.
    /// </summary>
    public uint32_t NumLayers = 1;

    /// <summary>
    /// The number of samples for multisampling (MSAA). 0 or 1 means no multisampling. Defaults to 0.
    /// </summary>
    public uint32_t NumSamples = 0;

    /// <summary>
    /// Bitflags indicating how the texture will be used.
    /// </summary>
    public TextureUsageBits Usage = TextureUsageBits.Sampled;

    /// <summary>
    /// The number of mip levels. Defaults to 1 (no mipmapping).
    /// </summary>
    public uint32_t NumMipLevels = 1;

    /// <summary>
    /// The memory storage type for the texture.
    /// </summary>
    public StorageType Storage = StorageType.Device;

    /// <summary>
    /// Component swizzling configuration for the texture.
    /// </summary>
    public ComponentMapping Swizzle;

    /// <summary>
    /// Pointer to initial data to upload. Can be IntPtr.Zero if no initial data.
    /// </summary>
    public nint Data = nint.Zero;

    /// <summary>
    /// Size in bytes of the data pointed to by <see cref="Data"/>. Defaults to 0.
    /// </summary>
    public size_t DataSize = 0; // size of the data to upload, if not null

    /// <summary>
    /// The number of mip levels to upload from the initial data. Defaults to 1.
    /// </summary>
    public uint32_t DataNumMipLevels = 1; // how many mip-levels we want to upload

    /// <summary>
    /// Whether to automatically generate mipmaps after uploading initial data. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Only valid when <see cref="Data"/> is not null. If true, mipmaps are generated immediately after upload.
    /// </remarks>
    public bool GenerateMipmaps = false; // generate mip-levels immediately, valid only with non-null data
}

/// <summary>
/// Describes a view into an existing texture resource, allowing access to a subset of the texture.
/// </summary>
public struct TextureViewDesc()
{
    /// <summary>
    /// The type of texture view (can differ from the base texture type for certain conversions).
    /// </summary>
    public TextureType Type = TextureType.Texture2D;

    /// <summary>
    /// The starting array layer index for the view.
    /// </summary>
    public uint32_t Layer;

    /// <summary>
    /// The number of array layers in the view. Defaults to 1.
    /// </summary>
    public uint32_t NumLayers = 1;

    /// <summary>
    /// The starting mip level for the view.
    /// </summary>
    public uint32_t MipLevel;

    /// <summary>
    /// The number of mip levels in the view. Defaults to 1.
    /// </summary>
    public uint32_t NumMipLevels = 1;

    /// <summary>
    /// Component swizzling configuration for the texture view.
    /// </summary>
    public ComponentMapping Swizzle;
}
