using Arch.Core;
using Arch.Core.Extensions;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;

[TestClass]
public sealed class SceneTests
{
    [TestMethod]
    public void CreateScene()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
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
        var root = new Node(world) { Name = "Root Node" };
        var child = new Node(world) { Name = "Child Node" };
        root.AddChild(child);

        Assert.IsTrue(root.HasChildren);
        Assert.AreEqual(1, root.ChildCount);
        Assert.IsTrue(child.HasParent);
        Assert.AreEqual(root, child.Parent);
        Assert.AreEqual(1, child.Info.Level);
        root.Dispose();
        Assert.IsFalse(
            root.Alive,
            "Root Node should be disposed and not alive after calling Dispose."
        );
        Assert.IsFalse(
            child.Alive,
            "Child Node should be disposed and not alive after calling Dispose."
        );
    }

    [TestMethod]
    public void RemoveChildToNode()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
        var child = new Node(world) { Name = "Child Node" };
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
        var root = new Node(world) { Name = "Root Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var grandChild1 = new Node(world) { Name = "GrandChild Node 1" };
        var grandChild2 = new Node(world) { Name = "GrandChild Node 2" };
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
        Assert.IsFalse(
            root.Alive,
            "Root Node should be disposed and not alive after calling Dispose."
        );
        Assert.IsFalse(
            child1.Alive,
            "Child Node 1 should be disposed and not alive after calling Dispose."
        );
        Assert.IsFalse(
            child2.Alive,
            "Child Node 2 should be disposed and not alive after calling Dispose."
        );
        Assert.IsFalse(
            grandChild1.Alive,
            "GrandChild Node 1 should be disposed and not alive after calling Dispose."
        );
        Assert.IsFalse(
            grandChild2.Alive,
            "GrandChild Node 2 should be disposed and not alive after calling Dispose."
        );
    }

    [TestMethod]
    public void RemoveMultiLayerChildren()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var grandChild1 = new Node(world) { Name = "GrandChild Node 1" };
        var grandChild2 = new Node(world) { Name = "GrandChild Node 2" };
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

    [TestMethod]
    public void NodeEnabledByDefault()
    {
        var world = World.Create();
        var node = new Node(world) { Name = "Test Node" };
        Assert.IsTrue(node.Enabled, "Node should be enabled by default");
        node.Dispose();
    }

    [TestMethod]
    public void DisableNode()
    {
        var world = World.Create();
        var node = new Node(world) { Name = "Test Node" };
        node.Enabled = false;
        Assert.IsFalse(node.Enabled, "Node should be disabled after setting SelfEnabled to false");
        node.Dispose();
    }

    [TestMethod]
    public void EnableNode()
    {
        var world = World.Create();
        var node = new Node(world) { Name = "Test Node" };
        node.Enabled = false;
        Assert.IsFalse(node.Enabled);
        node.Enabled = true;
        Assert.IsTrue(node.Enabled, "Node should be enabled after setting SelfEnabled to true");
        node.Dispose();
    }

    [TestMethod]
    public void DisableParentDisablesChild()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child = new Node(world) { Name = "Child Node" };
        parent.AddChild(child);

        Assert.IsTrue(parent.Enabled, "Parent should be enabled by default");
        Assert.IsTrue(child.Enabled, "Child should be enabled by default");

        parent.Enabled = false;

        Assert.IsFalse(parent.Enabled, "Parent should be disabled");
        Assert.IsFalse(child.Enabled, "Child should be disabled when parent is disabled");

        parent.Dispose();
    }

    [TestMethod]
    public void EnableParentEnablesChild()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child = new Node(world) { Name = "Child Node" };
        parent.AddChild(child);

        parent.Enabled = false;
        Assert.IsFalse(parent.Enabled);
        Assert.IsFalse(child.Enabled);

        parent.Enabled = true;

        Assert.IsTrue(parent.Enabled, "Parent should be enabled");
        Assert.IsTrue(child.Enabled, "Child should be enabled when parent is enabled");

        parent.Dispose();
    }

    [TestMethod]
    public void DisableChildDoesNotAffectParent()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child = new Node(world) { Name = "Child Node" };
        parent.AddChild(child);

        child.Enabled = false;

        Assert.IsTrue(parent.Enabled, "Parent should remain enabled when child is disabled");
        Assert.IsFalse(child.Enabled, "Child should be disabled");

        parent.Dispose();
    }

    [TestMethod]
    public void DisabledChildRemainsDisabledWhenParentReEnabled()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child = new Node(world) { Name = "Child Node" };
        parent.AddChild(child);

        child.Enabled = false;
        Assert.IsFalse(child.Enabled);

        parent.Enabled = false;
        parent.Enabled = true;

        Assert.IsTrue(parent.Enabled, "Parent should be enabled");
        Assert.IsFalse(child.Enabled, "Child should remain disabled as it was explicitly disabled");

        parent.Dispose();
    }

    [TestMethod]
    public void DisableParentAffectsMultipleChildren()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var child3 = new Node(world) { Name = "Child Node 3" };
        parent.AddChild(child1);
        parent.AddChild(child2);
        parent.AddChild(child3);

        Assert.IsTrue(child1.Enabled);
        Assert.IsTrue(child2.Enabled);
        Assert.IsTrue(child3.Enabled);

        parent.Enabled = false;

        Assert.IsFalse(parent.Enabled, "Parent should be disabled");
        Assert.IsFalse(child1.Enabled, "Child 1 should be disabled");
        Assert.IsFalse(child2.Enabled, "Child 2 should be disabled");
        Assert.IsFalse(child3.Enabled, "Child 3 should be disabled");

        parent.Dispose();
    }

    [TestMethod]
    public void DisableParentPropagatesThroughHierarchy()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var grandChild1 = new Node(world) { Name = "GrandChild Node 1" };
        var grandChild2 = new Node(world) { Name = "GrandChild Node 2" };
        var greatGrandChild = new Node(world) { Name = "GreatGrandChild Node" };

        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild1);
        child2.AddChild(grandChild2);
        grandChild1.AddChild(greatGrandChild);

        Assert.IsTrue(root.Enabled);
        Assert.IsTrue(child1.Enabled);
        Assert.IsTrue(child2.Enabled);
        Assert.IsTrue(grandChild1.Enabled);
        Assert.IsTrue(grandChild2.Enabled);
        Assert.IsTrue(greatGrandChild.Enabled);

        root.Enabled = false;

        Assert.IsFalse(root.Enabled, "Root should be disabled");
        Assert.IsFalse(child1.Enabled, "Child 1 should be disabled");
        Assert.IsFalse(child2.Enabled, "Child 2 should be disabled");
        Assert.IsFalse(grandChild1.Enabled, "GrandChild 1 should be disabled");
        Assert.IsFalse(grandChild2.Enabled, "GrandChild 2 should be disabled");
        Assert.IsFalse(greatGrandChild.Enabled, "GreatGrandChild should be disabled");

        root.Dispose();
    }

    [TestMethod]
    public void DisableMiddleLevelNodeAffectsDescendantsOnly()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var grandChild1 = new Node(world) { Name = "GrandChild Node 1" };
        var grandChild2 = new Node(world) { Name = "GrandChild Node 2" };

        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild1);
        child2.AddChild(grandChild2);

        child1.Enabled = false;

        Assert.IsTrue(root.Enabled, "Root should remain enabled");
        Assert.IsFalse(child1.Enabled, "Child 1 should be disabled");
        Assert.IsTrue(child2.Enabled, "Child 2 should remain enabled");
        Assert.IsFalse(grandChild1.Enabled, "GrandChild 1 should be disabled");
        Assert.IsTrue(grandChild2.Enabled, "GrandChild 2 should remain enabled");

        root.Dispose();
    }

    [TestMethod]
    public void EnableHierarchyRespectsSelfDisabledNodes()
    {
        var world = World.Create();
        var root = new Node(world) { Name = "Root Node" };
        var child1 = new Node(world) { Name = "Child Node 1" };
        var child2 = new Node(world) { Name = "Child Node 2" };
        var grandChild1 = new Node(world) { Name = "GrandChild Node 1" };

        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild1);

        // Disable child2 explicitly
        child2.Enabled = false;
        Assert.IsFalse(child2.Enabled);

        // Disable root
        root.Enabled = false;
        Assert.IsFalse(root.Enabled);
        Assert.IsFalse(child1.Enabled);
        Assert.IsFalse(child2.Enabled);
        Assert.IsFalse(grandChild1.Enabled);

        // Re-enable root
        root.Enabled = true;
        Assert.IsTrue(root.Enabled, "Root should be enabled");
        Assert.IsTrue(child1.Enabled, "Child 1 should be enabled");
        Assert.IsFalse(child2.Enabled, "Child 2 should remain disabled (was explicitly disabled)");
        Assert.IsTrue(grandChild1.Enabled, "GrandChild 1 should be enabled");

        root.Dispose();
    }

    [TestMethod]
    public void AddChildToDisabledParent()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        parent.Enabled = false;

        var child = new Node(world) { Name = "Child Node" };
        Assert.IsTrue(child.Enabled, "Child should be enabled before adding to disabled parent");

        parent.AddChild(child);

        Assert.IsFalse(parent.Enabled, "Parent should remain disabled");
        Assert.IsFalse(child.Enabled, "Child should be disabled when added to disabled parent");

        parent.Dispose();
    }

    [TestMethod]
    public void RemoveChildFromDisabledParentRestoresChildState()
    {
        var world = World.Create();
        var parent = new Node(world) { Name = "Parent Node" };
        var child = new Node(world) { Name = "Child Node" };
        parent.AddChild(child);

        parent.Enabled = false;
        Assert.IsFalse(child.Enabled);

        parent.RemoveChild(child);

        Assert.IsTrue(
            child.Enabled,
            "Child should be enabled after being removed from disabled parent"
        );

        parent.Dispose();
        child.Dispose();
    }
}
