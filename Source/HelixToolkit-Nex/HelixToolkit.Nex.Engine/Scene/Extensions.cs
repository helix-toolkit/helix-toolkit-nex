using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

public static class Extensions
{
    public static Node CreateNode(this World world, string name)
    {
        return new Node(world, name);
    }

    public static MeshNode CreateMeshNode(this World world, string name)
    {
        return new MeshNode(world, name);
    }

    public static MeshNode CreateMeshNode(this World world, string name, MeshComponent component)
    {
        return new MeshNode(world, name, component);
    }

    public static PointCloudNode CreatePointCloudNode(this World world, string name)
    {
        return new PointCloudNode(world, name);
    }

    public static PointCloudNode CreatePointCloudNode(
        this World world,
        string name,
        PointCloudComponent component
    )
    {
        return new PointCloudNode(world, name, component);
    }
}
