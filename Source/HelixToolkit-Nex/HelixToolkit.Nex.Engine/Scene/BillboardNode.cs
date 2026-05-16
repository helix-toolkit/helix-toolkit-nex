using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine.Scene;

public class BillboardNode : Node
{
    public BillboardNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(new BillboardComponent());
        IsRenderable = true;
    }

    public BillboardNode(World world, string name, BillboardComponent component)
        : this(world, name, ref component) { }

    public BillboardNode(World world, string name, ref BillboardComponent component)
        : this(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the billboard geometry.
    /// </summary>
    public BillboardComponent Billboard
    {
        get => Entity.Get<BillboardComponent>();
        set
        {
            Entity.Set(ref value);
        }
    }

    /// <summary>
    /// Gets or sets the number of billboards to render for this node.
    /// </summary>
    public int BillboardCount => Entity.Get<BillboardComponent>().BillboardCount;
}
