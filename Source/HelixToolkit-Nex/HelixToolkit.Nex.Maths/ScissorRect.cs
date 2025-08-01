namespace HelixToolkit.Nex.Maths;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScissorRect(uint x = 0, uint y = 0, uint w = 0, uint h = 0) : IEquatable<ScissorRect>
{
    public uint X = x;
    public uint Y = y;
    public uint Width = w;
    public uint Height = h;

    public readonly bool Equals(ScissorRect other)
    {
        return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is ScissorRect rect && Equals(rect);
    }
    public static bool operator ==(ScissorRect left, ScissorRect right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ScissorRect left, ScissorRect right)
    {
        return !(left == right);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }
}
