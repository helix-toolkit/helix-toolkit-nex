using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Describes the dimensions, format, and layout of a texture image.
/// </summary>
public struct ImageDescription : IEquatable<ImageDescription>
{
    /// <summary>The texture dimension type (2D, 3D, or Cube).</summary>
    public TextureDimension Dimension;

    /// <summary>Width of the texture in texels.</summary>
    public int Width;

    /// <summary>Height of the texture in texels.</summary>
    public int Height;

    /// <summary>Depth of the texture in texels (for 3D textures).</summary>
    public int Depth;

    /// <summary>Number of array slices (6 for cube maps).</summary>
    public int ArraySize;

    /// <summary>Number of mipmap levels.</summary>
    public int MipLevels;

    /// <summary>Pixel format of the texture.</summary>
    public Format Format;

    /// <inheritdoc/>
    public readonly bool Equals(ImageDescription other)
    {
        return Dimension == other.Dimension
            && Width == other.Width
            && Height == other.Height
            && Depth == other.Depth
            && ArraySize == other.ArraySize
            && MipLevels == other.MipLevels
            && Format == other.Format;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImageDescription other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Dimension, Width, Height, Depth, ArraySize, MipLevels, Format);
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ImageDescription left, ImageDescription right) =>
        left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ImageDescription left, ImageDescription right) =>
        !left.Equals(right);

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        return $"Dimension: {Dimension}, Width: {Width}, Height: {Height}, Depth: {Depth}, Format: {Format}, ArraySize: {ArraySize}, MipLevels: {MipLevels}";
    }
}
