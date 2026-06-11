using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine.Scene;

public class BillboardNode : Node
{
    public BillboardNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(new BillboardDrawInfo());
        IsRenderable = true;
    }

    public BillboardNode(World world, string name, BillboardDrawInfo component)
        : this(world, name, ref component) { }

    public BillboardNode(World world, string name, ref BillboardDrawInfo component)
        : this(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the billboard geometry.
    /// </summary>
    public BillboardDrawInfo Billboard
    {
        get => Entity.Get<BillboardDrawInfo>();
        set
        {
            Entity.Set(ref value);
        }
    }

    /// <summary>
    /// Gets or sets the number of billboards to render for this node.
    /// </summary>
    public int BillboardCount => Entity.Get<BillboardDrawInfo>().BillboardCount;
}
