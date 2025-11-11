namespace HelixToolkit.Nex.Maths;

/// <summary>
/// Represents a scissor rectangle used for clipping rendering output.
/// </summary>
/// <param name="x">The X coordinate of the upper-left corner of the scissor rectangle.</param>
/// <param name="y">The Y coordinate of the upper-left corner of the scissor rectangle.</param>
/// <param name="w">The width of the scissor rectangle.</param>
/// <param name="h">The height of the scissor rectangle.</param>
/// <remarks>
/// A scissor rectangle defines a region of the screen where rendering is allowed.
/// Pixels outside this rectangle are discarded during rasterization.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScissorRect(uint x = 0, uint y = 0, uint w = 0, uint h = 0) : IEquatable<ScissorRect>
{
    /// <summary>
    /// The X coordinate of the upper-left corner of the scissor rectangle.
    /// </summary>
    public uint X = x;

    /// <summary>
    /// The Y coordinate of the upper-left corner of the scissor rectangle.
    /// </summary>
    public uint Y = y;

    /// <summary>
    /// The width of the scissor rectangle in pixels.
    /// </summary>
    public uint Width = w;

    /// <summary>
    /// The height of the scissor rectangle in pixels.
    /// </summary>
    public uint Height = h;

    /// <summary>
    /// Determines whether the specified <see cref="ScissorRect"/> is equal to this instance.
    /// </summary>
    /// <param name="other">The <see cref="ScissorRect"/> to compare with this instance.</param>
    /// <returns>True if the specified rectangle is equal to this instance; otherwise, false.</returns>
    public readonly bool Equals(ScissorRect other)
    {
        return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    /// <summary>
    /// Determines whether the specified object is equal to this instance.
    /// </summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns>True if the specified object is a <see cref="ScissorRect"/> and is equal to this instance; otherwise, false.</returns>
    public readonly override bool Equals(object? obj)
    {
        return obj is ScissorRect rect && Equals(rect);
    }

    /// <summary>
    /// Determines whether two <see cref="ScissorRect"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance to compare.</param>
    /// <param name="right">The second instance to compare.</param>
    /// <returns>True if the instances are equal; otherwise, false.</returns>
    public static bool operator ==(ScissorRect left, ScissorRect right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="ScissorRect"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance to compare.</param>
    /// <param name="right">The second instance to compare.</param>
    /// <returns>True if the instances are not equal; otherwise, false.</returns>
    public static bool operator !=(ScissorRect left, ScissorRect right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }
}
