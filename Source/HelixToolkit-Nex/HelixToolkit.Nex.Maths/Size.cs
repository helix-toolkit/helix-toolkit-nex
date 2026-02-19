namespace HelixToolkit.Nex.Maths;

public readonly struct Size(int width, int height) : IEquatable<Size>
{
    public static Size Empty => new(0, 0);
    public readonly int Width = width;
    public readonly int Height = height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString()
    {
        return $"{Width}x{Height}";
    }

    public bool Equals(Size other)
    {
        return Width == other.Width && Height == other.Height;
    }

    public static implicit operator Size((int width, int height) size)
    {
        return new Size(size.width, size.height);
    }

    public static implicit operator Vector2(Size size)
    {
        return new Vector2(size.Width, size.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is Size size && Equals(size);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    public static bool operator ==(Size left, Size right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Size left, Size right)
    {
        return !(left == right);
    }
}
