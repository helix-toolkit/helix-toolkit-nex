using System.Numerics;
using Arch.Core;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;
[TestClass]
public sealed class SceneSortingTests
{
    [DataTestMethod]
    [DataRow(10)]
    [DataRow(1024)]
    public void FlattenSceneSingleChildren(int nodeCount)
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        {
            var node = root;
            for (int i = 0; i < nodeCount - 1; ++i)
            {
                var child = new Node(world)
                {
                    Name = $"Child Node {i}"
                };
                node.AddChild(child);
                node = child;
            }
        }

        var sortedNodes = new List<Node>();
        Node[] nodes = [root];
        nodes.Flatten(null, sortedNodes);
        Assert.AreEqual(nodeCount, sortedNodes.Count);
        for (int i = 0; i < sortedNodes.Count; ++i)
        {
            var n = sortedNodes[i];
            if (n.HasParent)
            {
                Assert.IsTrue(sortedNodes.IndexOf(n.Parent!) < i, $"Parent of {n.Name} should be before it in the sorted list.");
            }
        }
    }


    [DataTestMethod]
    [DataRow(4, 4)]
    [DataRow(3, 10)]
    public void FlattenSceneMultiple(int childCount, int level)
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var total = SceneBuilderUtils.AddChildRecursively(root, 0, level, childCount, world) + 1;

        var sortedNodes = new List<Node>(total);
        Node[] nodes = [root];
        nodes.Flatten(null, sortedNodes);
        Assert.AreEqual(
            total, // -1 for the root node
            sortedNodes.Count,
            "The number of sorted nodes should match the expected count."
        );
        for (int i = 0; i < sortedNodes.Count; ++i)
        {
            var n = sortedNodes[i];
            if (n.HasParent)
            {
                Assert.IsTrue(sortedNodes.IndexOf(n.Parent!) < i, $"Parent of {n.Name} should be before it in the sorted list.");
            }
        }
    }

    [DataTestMethod]
    [DataRow(4, 4)]
    public void TransformUpdate(int childCount, int level)
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var total = SceneBuilderUtils.AddChildRecursively(root, 0, level, childCount, world) + 1;

        var sortedNodes = new List<Node>(total);
        Node[] nodes = [root];
        nodes.Flatten(null, sortedNodes);
        sortedNodes.UpdateTransforms();

        root.Transform.Scale = new Vector3(1, 2, 3);
        sortedNodes.UpdateTransforms();
        var transform = Matrix4x4.CreateScale(1, 2, 3);

        for (int i = 0; i < sortedNodes.Count; ++i)
        {
            var n = sortedNodes[i];
            Assert.IsFalse(n.Transform.IsWorldDirty, $"Node {n.Name} should not have dirty world transform after update.");
            Assert.AreEqual(transform, n.Transform.WorldTransform);
        }

        for (int i = 0; i < root.ChildCount; ++i)
        {
            var child = root.Children![i];
            child.Transform.Scale = new Vector3(2, 3, 4);
            child.Transform.Translation = new Vector3(4, 5, 6);
        }
        sortedNodes.UpdateTransforms();

        transform *= Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(4, 5, 6);
        for (int i = 1; i < sortedNodes.Count; ++i)
        {
            var n = sortedNodes[i];
            Assert.IsFalse(n.Transform.IsWorldDirty, $"Node {n.Name} should not have dirty world transform after update.");
            Assert.AreEqual(transform, n.Transform.WorldTransform);
        }
    }
}
