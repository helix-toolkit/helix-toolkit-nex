namespace HelixToolkit.Nex.Maths;

public record Size(int Width, int Height)
{
    public static Size Empty => new(0, 0);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public override string ToString()
    {
        return $"{Width}x{Height}";
    }
    public static implicit operator Size((int width, int height) size)
    {
        return new Size(size.width, size.height);
    }
    public static implicit operator Vector2(Size size)
    {
        return new Vector2(size.Width, size.Height);
    }
}
