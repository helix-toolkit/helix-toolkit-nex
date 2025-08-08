namespace HelixToolkit.Nex.Tests.Scene;

using Arch.Core;
using Arch.Core.Extensions;
using HelixToolkit.Nex.Scene;

[TestClass]
public sealed class SceneTests
{
    [TestMethod]
    public void CreateScene()
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        Assert.IsNotNull(root);
        Assert.AreEqual("Root Node", root.Name);
        Assert.AreEqual(0, root.ChildCount);
        Assert.IsFalse(root.HasChildren);
        Assert.IsFalse(root.HasParent);
        Assert.IsNull(root.Parent);
        Assert.IsNotNull(root.World);
        Assert.IsNotNull(root.Entity);
        Assert.IsTrue(root.Entity.Has<NodeInfo>());
        Assert.IsTrue(root.Entity.Has<Transform>());
        Assert.AreEqual(0, root.Info.Level);
        root.Dispose();
        Assert.IsFalse(root.Alive, "Node should be disposed and not alive after calling Dispose.");
    }

    [TestMethod]
    public void AddChildToNode()
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var child = new Node(world)
        {
            Name = "Child Node"
        };
        root.AddChild(child);

        Assert.IsTrue(root.HasChildren);
        Assert.AreEqual(1, root.ChildCount);
        Assert.IsTrue(child.HasParent);
        Assert.AreEqual(root, child.Parent);
        Assert.AreEqual(1, child.Info.Level);
        root.Dispose();
        Assert.IsFalse(root.Alive, "Root Node should be disposed and not alive after calling Dispose.");
        Assert.IsFalse(child.Alive, "Child Node should be disposed and not alive after calling Dispose.");
    }

    [TestMethod]
    public void RemoveChildToNode()
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var child = new Node(world)
        {
            Name = "Child Node"
        };
        root.AddChild(child);
        Assert.IsTrue(root.HasChildren);
        Assert.AreEqual(1, root.ChildCount);
        Assert.IsTrue(child.HasParent);
        Assert.AreEqual(root, child.Parent);
        Assert.AreEqual(1, child.Info.Level);
        root.RemoveChild(child);
        Assert.IsFalse(root.HasChildren);
        Assert.AreEqual(0, root.ChildCount);
        Assert.IsFalse(child.HasParent);
        Assert.IsNull(child.Parent);
        Assert.AreEqual(0, child.Info.Level);
    }

    [TestMethod]
    public void AddMultiLayerChildren()
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var child1 = new Node(world)
        {
            Name = "Child Node 1"
        };
        var child2 = new Node(world)
        {
            Name = "Child Node 2"
        };
        var grandChild1 = new Node(world)
        {
            Name = "GrandChild Node 1"
        };
        var grandChild2 = new Node(world)
        {
            Name = "GrandChild Node 2"
        };
        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild1);
        child2.AddChild(grandChild2);
        Assert.IsTrue(root.HasChildren);
        Assert.AreEqual(2, root.ChildCount);
        Assert.IsTrue(child1.HasChildren);
        Assert.AreEqual(1, child1.ChildCount);
        Assert.IsTrue(child2.HasChildren);
        Assert.AreEqual(1, child2.ChildCount);
        Assert.IsTrue(grandChild1.HasParent);
        Assert.AreEqual(child1, grandChild1.Parent);
        Assert.AreEqual(2, grandChild1.Info.Level);
        Assert.IsTrue(grandChild2.HasParent);
        Assert.AreEqual(child2, grandChild2.Parent);
        Assert.AreEqual(2, grandChild2.Info.Level);
        Assert.IsTrue(root.Children?.Contains(child1) ?? false);
        Assert.IsTrue(root.Children?.Contains(child2) ?? false);
        Assert.IsTrue(child1.Children?.Contains(grandChild1) ?? false);
        Assert.IsTrue(child2.Children?.Contains(grandChild2) ?? false);
        Assert.IsTrue(root.Children is not null && root.Children.Count == 2);
        Assert.IsTrue(child1.Children is not null && child1.Children.Count == 1);
        Assert.IsTrue(child2.Children is not null && child2.Children.Count == 1);
        Assert.IsTrue(grandChild1.Children is not null && grandChild1.Children.Count == 0);
        Assert.IsTrue(grandChild2.Children is not null && grandChild2.Children.Count == 0);

        root.Dispose();
        Assert.IsFalse(root.Alive, "Root Node should be disposed and not alive after calling Dispose.");
        Assert.IsFalse(child1.Alive, "Child Node 1 should be disposed and not alive after calling Dispose.");
        Assert.IsFalse(child2.Alive, "Child Node 2 should be disposed and not alive after calling Dispose.");
        Assert.IsFalse(grandChild1.Alive, "GrandChild Node 1 should be disposed and not alive after calling Dispose.");
        Assert.IsFalse(grandChild2.Alive, "GrandChild Node 2 should be disposed and not alive after calling Dispose.");
    }

    [TestMethod]
    public void RemoveMultiLayerChildren()
    {
        var world = World.Create();
        var root = new Node(world)
        {
            Name = "Root Node"
        };
        var child1 = new Node(world)
        {
            Name = "Child Node 1"
        };
        var child2 = new Node(world)
        {
            Name = "Child Node 2"
        };
        var grandChild1 = new Node(world)
        {
            Name = "GrandChild Node 1"
        };
        var grandChild2 = new Node(world)
        {
            Name = "GrandChild Node 2"
        };
        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild1);
        child2.AddChild(grandChild2);

        root.RemoveChild(child1);

        Assert.IsTrue(root.HasChildren);
        Assert.AreEqual(1, root.ChildCount);
        Assert.IsFalse(child1.HasParent);
        Assert.IsNull(child1.Parent);
        Assert.AreEqual(0, child1.Info.Level);
        Assert.AreEqual(child1, grandChild1.Parent);
        Assert.AreEqual(1, grandChild1.Info.Level);

        Assert.IsTrue(child2.HasChildren);
        Assert.AreEqual(1, child2.ChildCount);
        Assert.IsTrue(grandChild2.HasParent);
        Assert.AreEqual(child2, grandChild2.Parent);
        Assert.AreEqual(2, grandChild2.Info.Level);
    }
}
