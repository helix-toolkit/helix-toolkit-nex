namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Describes the layout of a single mipmap level, including dimensions, strides, and packed (block-aligned) dimensions.
/// </summary>
/// <remarks>
/// Initializes a new <see cref="MipMapDescription"/>.
/// </remarks>
public sealed class MipMapDescription(
    int width,
    int height,
    int depth,
    int rowStride,
    int depthStride,
    int widthPacked,
    int heightPacked
    ) : IEquatable<MipMapDescription>
{
    /// <summary>Width of this mipmap level in texels.</summary>
    public readonly int Width = width;

    /// <summary>Height of this mipmap level in texels.</summary>
    public readonly int Height = height;

    /// <summary>Depth of this mipmap level in texels (for 3D textures).</summary>
    public readonly int Depth = depth;

    /// <summary>Row stride in bytes (bytes per row of texels).</summary>
    public readonly int RowStride = rowStride;

    /// <summary>Depth stride in bytes (bytes per 2D slice).</summary>
    public readonly int DepthStride = depthStride;

    /// <summary>Total size of this mipmap level in bytes (DepthStride * Depth).</summary>
    public readonly int MipmapSize = depthStride * depth;

    /// <summary>Block-aligned width (for compressed formats, rounded up to block boundary).</summary>
    public readonly int WidthPacked = widthPacked;

    /// <summary>Block-aligned height (for compressed formats, rounded up to block boundary).</summary>
    public readonly int HeightPacked = heightPacked;

    /// <inheritdoc/>
    public bool Equals(MipMapDescription? other)
    {
        if (other is null)
            return false;
        return Width == other.Width
            && Height == other.Height
            && Depth == other.Depth
            && RowStride == other.RowStride
            && DepthStride == other.DepthStride
            && MipmapSize == other.MipmapSize
            && WidthPacked == other.WidthPacked
            && HeightPacked == other.HeightPacked;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MipMapDescription other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Width,
            Height,
            Depth,
            RowStride,
            DepthStride,
            MipmapSize,
            WidthPacked,
            HeightPacked
        );
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(MipMapDescription? left, MipMapDescription? right)
    {
        if (left is null)
            return right is null;
        return left.Equals(right);
    }

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(MipMapDescription? left, MipMapDescription? right) =>
        !(left == right);
}
