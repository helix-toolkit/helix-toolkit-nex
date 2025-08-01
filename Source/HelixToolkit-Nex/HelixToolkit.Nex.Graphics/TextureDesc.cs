namespace HelixToolkit.Nex.Graphics;

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