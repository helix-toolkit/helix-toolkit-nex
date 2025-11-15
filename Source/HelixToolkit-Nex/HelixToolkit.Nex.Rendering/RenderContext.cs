namespace HelixToolkit.Nex.Rendering;

public sealed class RenderContext(World world)
{
    public World World { get; } = world;
}
