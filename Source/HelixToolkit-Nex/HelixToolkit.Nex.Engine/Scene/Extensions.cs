using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;

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
        return new MeshNode(world, name, ref component);
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
        return new PointCloudNode(world, name, ref component);
    }

    public static BillboardNode CreateBillboardNode(this World world, string name)
    {
        return new BillboardNode(world, name);
    }

    public static BillboardNode CreateBillboardNode(
        this World world,
        string name,
        BillboardComponent component
    )
    {
        return new BillboardNode(world, name, ref component);
    }

    public static BillboardNode CreateBillboard(
        this BillboardNode node,
        string text,
        SDFFontAtlas atlas,
        float fontSize,
        Color4 color,
        Color4? background,
        BillboardAnchor anchor = BillboardAnchor.Center,
        string materialName = "SDFFont",
        bool fixedSize = true,
        float cullDistance = 0
    )
    {
        var comp = TextLayoutHelper.CreateTextBillboard(
            text,
            atlas,
            fontSize,
            color,
            background,
            anchor,
            materialName: materialName,
            fixedSize: fixedSize,
            cullDistance: cullDistance
        );
        node.Billboard = comp;
        return node;
    }
}
