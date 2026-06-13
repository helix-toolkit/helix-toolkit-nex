using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.SDF;

namespace HelixToolkit.Nex.Scene;

public static class Extensions
{
    public static BoundingBox GetMeshBound(this Node node)
    {
        var world = node.World;
        var nodeInfos = world.GetComponents<NodeInfo>();
        var worldTransforms = world.GetComponents<WorldTransform>();
        var meshes = world.GetComponents<MeshDrawInfo>();
        var mergedBound = BoundingBox.Empty;
        for (int i = 0; i < nodeInfos.Count; ++i)
        {
            ref var nodeInfo = ref nodeInfos[i];
            var entity = world.GetEntity(nodeInfo.EntityId);
            if (!entity.Valid || !entity.Has<MeshDrawInfo>())
            {
                continue;
            }
            ref var mesh = ref meshes[entity];
            if (!mesh.Valid)
            {
                continue;
            }
            ref var transform = ref worldTransforms[entity];
            var bound = mesh.Geometry!.BoundingBoxLocal.Transform(transform.Value);
            if (mergedBound.IsEmpty)
            {
                mergedBound = bound;
            }
            else
            {
                mergedBound.Minimum = Vector3.Min(mergedBound.Minimum, bound.Minimum);
                mergedBound.Maximum = Vector3.Max(mergedBound.Maximum, bound.Maximum);
            }
        }
        return mergedBound;
    }

    public static Node CreateNode(this World world, string name)
    {
        return new Node(world, name);
    }

    public static MeshNode CreateMeshNode(this World world, string name)
    {
        return new MeshNode(world, name);
    }

    public static MeshNode CreateMeshNode(this World world, string name, MeshDrawInfo component)
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
        PointCloudDrawInfo component
    )
    {
        return new PointCloudNode(world, name, ref component);
    }

    public static LineNode CreateLineNode(this World world, string name)
    {
        return new LineNode(world, name);
    }

    public static LineNode CreateLineNode(this World world, string name, LineDrawInfo component)
    {
        return new LineNode(world, name, ref component);
    }

    public static BillboardNode CreateBillboardNode(this World world, string name)
    {
        return new BillboardNode(world, name);
    }

    public static BillboardNode CreateBillboardNode(
        this World world,
        string name,
        BillboardDrawInfo component
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
