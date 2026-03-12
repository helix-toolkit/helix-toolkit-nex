using System.Numerics;
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
        using var world = World.CreateWorld();
        var root = new Node(world) { Name = "Root Node" };
        {
            var node = root;
            for (int i = 0; i < nodeCount - 1; ++i)
            {
                var child = new Node(world) { Name = $"Child Node {i}" };
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
                Assert.IsTrue(
                    sortedNodes.IndexOf(n.Parent!) < i,
                    $"Parent of {n.Name} should be before it in the sorted list."
                );
            }
        }
    }

    [DataTestMethod]
    [DataRow(4, 4)]
    [DataRow(3, 10)]
    public void FlattenSceneMultiple(int childCount, int level)
    {
        using var world = World.CreateWorld();
        var root = new Node(world) { Name = "Root Node" };
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
                Assert.IsTrue(
                    sortedNodes.IndexOf(n.Parent!) < i,
                    $"Parent of {n.Name} should be before it in the sorted list."
                );
            }
        }
    }

    [DataTestMethod]
    [DataRow(4, 4)]
    public void TransformUpdate(int childCount, int level)
    {
        using var world = World.CreateWorld();
        var root = new Node(world) { Name = "Root Node" };
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
            Assert.IsFalse(
                n.Transform.IsWorldDirty,
                $"Node {n.Name} should not have dirty world transform after update."
            );
            Assert.AreEqual(transform, n.WorldTransform.Value);
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
            Assert.IsFalse(
                n.Transform.IsWorldDirty,
                $"Node {n.Name} should not have dirty world transform after update."
            );
            Assert.AreEqual(transform, n.WorldTransform.Value);
        }
    }

    [DataTestMethod]
    [DataRow(4, 4)]
    public void TransformUpdateWithComponentSorting(int childCount, int level)
    {
        using var world = World.CreateWorld();
        var root = new Node(world) { Name = "Root Node" };
        var total = SceneBuilderUtils.AddChildRecursively(root, 0, level, childCount, world) + 1;
        world.SortSceneNodes();

        var components = world.GetComponents<NodeInfo>();
        int minLevel = 0;
        foreach (var comp in components)
        {
            Assert.IsTrue(comp.Level >= minLevel);
            level = Math.Max(comp.Level, minLevel);
        }

        world.UpdateTransforms();

        var transforms = world.GetComponents<Transform>();
        for (int i = 0; i < transforms.Count; ++i)
        {
            ref var transform = ref transforms[i];
            Assert.IsFalse(
                transform.IsWorldDirty,
                $"Transform component at index {i} should not be dirty after update."
            );
        }
    }

    /// <summary>
    /// Helper: assert that every node in a sorted component list satisfies
    /// parent-before-child ordering.
    /// </summary>
    private static void AssertComponentsSortedByLevel(World world)
    {
        var components = world.GetComponents<NodeInfo>();
        int prevLevel = 0;
        for (int i = 0; i < components.Count; ++i)
        {
            Assert.IsTrue(
                components[i].Level >= prevLevel,
                $"NodeInfo component at index {i} has level {components[i].Level} " +
                $"which is less than previous level {prevLevel}."
            );
            prevLevel = components[i].Level;
        }
    }

    /// <summary>
    /// Helper: assert that every node can still reach the correct level via its entity.
    /// </summary>
    private static void AssertNodeLevelsConsistent(IEnumerable<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.Alive)
            {
                continue;
            }
            Assert.AreEqual(
                node.Info.Level,
                node.Level,
                $"Node '{node.Name}' has inconsistent level."
            );
            if (node.HasParent)
            {
                Assert.AreEqual(
                    node.Parent!.Level + 1,
                    node.Level,
                    $"Node '{node.Name}' level should be parent level + 1."
                );
            }
        }
    }

    // -------------------------------------------------------------------------
    // Sort → change hierarchy → sort again
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build a tree, sort, then reparent a child to a different node, sort again.
    /// The second sort must reflect the updated levels.
    /// </summary>
    [TestMethod]
    public void SortAfterReparenting()
    {
        using var world = World.CreateWorld();
        // root → child1 → grandChild
        //      → child2
        var root = new Node(world, "Root");          // level 0
        var child1 = new Node(world, "Child1");      // level 1
        var child2 = new Node(world, "Child2");      // level 1
        var grandChild = new Node(world, "GrandChild"); // level 2

        root.AddChild(child1);
        root.AddChild(child2);
        child1.AddChild(grandChild);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent([root, child1, child2, grandChild]);
        Assert.AreEqual(2, grandChild.Level);

        // Reparent grandChild from child1 → child2 (level stays 2, but parent changes)
        child1.RemoveChild(grandChild);
        child2.AddChild(grandChild);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent([root, child1, child2, grandChild]);
        Assert.AreEqual(child2, grandChild.Parent);
        Assert.AreEqual(2, grandChild.Level);
    }

    /// <summary>
    /// Build a deep chain, sort, then move the whole sub-tree one level deeper, sort again.
    /// All descendant levels must be updated correctly.
    /// </summary>
    [TestMethod]
    public void SortAfterMovingSubtreeDeeperLevel()
    {
        using var world = World.CreateWorld();
        // root → child → grandChild → greatGrandChild
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        var greatGrandChild = new Node(world, "GreatGrandChild");
        var newParent = new Node(world, "NewParent"); // initially a root-level sibling

        root.AddChild(child);
        child.AddChild(grandChild);
        grandChild.AddChild(greatGrandChild);
        root.AddChild(newParent);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(0, root.Level);
        Assert.AreEqual(1, child.Level);
        Assert.AreEqual(2, grandChild.Level);
        Assert.AreEqual(3, greatGrandChild.Level);
        Assert.AreEqual(1, newParent.Level);

        // Move newParent under child — now newParent is level 2
        // and child subtree stays as is.
        root.RemoveChild(newParent);
        child.AddChild(newParent);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(2, newParent.Level);
        AssertNodeLevelsConsistent([root, child, grandChild, greatGrandChild, newParent]);
    }

    /// <summary>
    /// Sort, then move a whole branch to a shallower position, sort again.
    /// All descendant levels must decrease accordingly.
    /// </summary>
    [TestMethod]
    public void SortAfterMovingSubtreeShallowerLevel()
    {
        using var world = World.CreateWorld();
        // root → a → b → c
        var root = new Node(world, "Root");
        var a = new Node(world, "A");
        var b = new Node(world, "B");
        var c = new Node(world, "C");
        root.AddChild(a);
        a.AddChild(b);
        b.AddChild(c);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(3, c.Level);

        // Detach b (and c) from a, attach directly to root
        a.RemoveChild(b);
        root.AddChild(b);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(1, b.Level);
        Assert.AreEqual(2, c.Level);
        AssertNodeLevelsConsistent([root, a, b, c]);
    }

    /// <summary>
    /// Sort multiple times interleaved with adding new child nodes at each step.
    /// </summary>
    [TestMethod]
    public void RepeatSortWithIncrementalChildAdditions()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        // Add first level children and re-sort.
        var children = new List<Node>();
        for (int i = 0; i < 4; ++i)
        {
            var child = new Node(world, $"Child{i}");
            root.AddChild(child);
            children.Add(child);
        }
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        // Add grandchildren under each child and re-sort.
        var grandChildren = new List<Node>();
        foreach (var child in children)
        {
            for (int j = 0; j < 3; ++j)
            {
                var gc = new Node(world, $"GC_{child.Name}_{j}");
                child.AddChild(gc);
                grandChildren.Add(gc);
            }
        }
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent([root, .. children, .. grandChildren]);
    }

    /// <summary>
    /// Sort, remove some nodes, add new ones at different levels, sort again.
    /// </summary>
    [TestMethod]
    [DataRow(4, 3)]
    public void SortAfterRemoveAndAddNodes(int childCount, int levels)
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        SceneBuilderUtils.AddChildRecursively(root, 0, levels, childCount, world);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        // Dispose all direct children of root (along with their subtrees).
        var rootChildren = root.Children!.ToArray();
        foreach (var child in rootChildren)
        {
            root.RemoveChild(child);
            child.Dispose();
        }

        Assert.AreEqual(0, root.ChildCount);
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        // Add a fresh set of children.
        for (int i = 0; i < childCount; ++i)
        {
            var newChild = new Node(world, $"NewChild{i}");
            root.AddChild(newChild);
            var newGrandChild = new Node(world, $"NewGC{i}");
            newChild.AddChild(newGrandChild);
        }

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent(
            root.Children!.SelectMany(c => c.Children!.Append(c)).Append(root)
        );
    }

    /// <summary>
    /// Reparent a node multiple times between sorts; the final sort must be correct.
    /// </summary>
    [TestMethod]
    public void SortAfterMultipleReparentingRounds()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var a = new Node(world, "A");
        var b = new Node(world, "B");
        var c = new Node(world, "C");
        var wanderer = new Node(world, "Wanderer");

        root.AddChild(a);
        root.AddChild(b);
        a.AddChild(c);
        a.AddChild(wanderer);   // wanderer starts at level 2

        // Round 1: sort, then move wanderer under b
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(2, wanderer.Level);

        a.RemoveChild(wanderer);
        b.AddChild(wanderer);   // level still 2, different parent

        // Round 2: sort, then move wanderer under c
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(2, wanderer.Level);
        Assert.AreEqual(b, wanderer.Parent);

        b.RemoveChild(wanderer);
        c.AddChild(wanderer);   // level now 3

        // Round 3: final sort
        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(3, wanderer.Level);
        Assert.AreEqual(c, wanderer.Parent);
        AssertNodeLevelsConsistent([root, a, b, c, wanderer]);
    }

    /// <summary>
    /// Promote a deep node to become a root-level node, sort multiple times.
    /// </summary>
    [TestMethod]
    public void SortAfterDetachingDeepNodeToRoot()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var l1 = new Node(world, "L1");
        var l2 = new Node(world, "L2");
        var l3 = new Node(world, "L3"); // starts at level 3

        root.AddChild(l1);
        l1.AddChild(l2);
        l2.AddChild(l3);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        Assert.AreEqual(3, l3.Level);

        // Detach l3 from its parent (becomes a root-level node, level 0)
        l2.RemoveChild(l3);
        Assert.AreEqual(0, l3.Level);
        Assert.IsFalse(l3.HasParent);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent([root, l1, l2, l3]);
        Assert.AreEqual(0, l3.Level);
    }

    /// <summary>
    /// After component sort, UpdateTransforms via World must still produce correct
    /// world transforms when nodes have been reparented since the last sort.
    /// </summary>
    [TestMethod]
    public void TransformUpdateAfterReparentingAndReSort()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var a = new Node(world, "A");
        var b = new Node(world, "B");
        var leaf = new Node(world, "Leaf");

        root.AddChild(a);
        root.AddChild(b);
        a.AddChild(leaf);

        var scale = new Vector3(2, 2, 2);
        root.Transform.Scale = scale;

        world.SortSceneNodes();
        world.UpdateTransforms();

        var expectedRootWorld = Matrix4x4.CreateScale(scale);
        Assert.AreEqual(expectedRootWorld, root.WorldTransform.Value);
        Assert.AreEqual(expectedRootWorld, leaf.WorldTransform.Value,
            "Leaf under A under Root should inherit root's scale.");

        // Reparent leaf from A to B; transforms should update correctly after re-sort.
        a.RemoveChild(leaf);
        b.AddChild(leaf);

        var bScale = new Vector3(3, 3, 3);
        b.Transform.Scale = bScale;

        world.SortSceneNodes();
        world.UpdateTransforms();

        var expectedBWorld = expectedRootWorld * Matrix4x4.CreateScale(bScale);
        var expectedLeafWorld = expectedBWorld;

        Assert.AreEqual(expectedBWorld, b.WorldTransform.Value,
            "B's world transform should be root × B's local scale.");
        Assert.AreEqual(expectedLeafWorld, leaf.WorldTransform.Value,
            "Leaf (now under B) should inherit B's world transform.");
    }

    /// <summary>
    /// Continuously grow the tree level-by-level, sorting after each level is added,
    /// and verify both level ordering and transform correctness at each step.
    /// </summary>
    [TestMethod]
    public void IncrementalDepthSortAndTransformUpdate()
    {
        const int maxDepth = 5;
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        root.Transform.Scale = new Vector3(2, 2, 2);

        var currentLevel = new List<Node> { root };

        for (int depth = 1; depth <= maxDepth; ++depth)
        {
            var nextLevel = new List<Node>();
            foreach (var parent in currentLevel)
            {
                var child = new Node(world, $"Node_D{depth}_{parent.Name}");
                parent.AddChild(child);
                nextLevel.Add(child);
            }
            currentLevel = nextLevel;

            world.SortSceneNodes();
            AssertComponentsSortedByLevel(world);
            world.UpdateTransforms();

            // Every node at `depth` should have a non-dirty transform.
            foreach (var node in currentLevel)
            {
                Assert.IsFalse(node.Transform.IsWorldDirty,
                    $"Node '{node.Name}' at depth {depth} should not be dirty after UpdateTransforms.");
            }
        }
    }

    /// <summary>
    /// Sort with a filter condition (Flatten with condition); nodes excluded by the
    /// condition should not appear in the flattened list.
    /// </summary>
    [TestMethod]
    public void FlattenWithConditionExcludesDisabledNodes()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var enabled = new Node(world, "Enabled");
        var disabled = new Node(world, "Disabled");
        var childOfDisabled = new Node(world, "ChildOfDisabled");

        root.AddChild(enabled);
        root.AddChild(disabled);
        disabled.AddChild(childOfDisabled);
        disabled.Enabled = false;

        var sortedNodes = new List<Node>();
        Node[] roots = [root];
        roots.Flatten(n => n.Enabled, sortedNodes);

        Assert.IsTrue(sortedNodes.Contains(root), "Root should be in the sorted list.");
        Assert.IsTrue(sortedNodes.Contains(enabled), "Enabled node should be in the sorted list.");
        Assert.IsFalse(sortedNodes.Contains(disabled), "Disabled node should be excluded.");
        Assert.IsFalse(sortedNodes.Contains(childOfDisabled),
            "Child of disabled node should be excluded.");
    }

    /// <summary>
    /// Sort, then disable a node, re-enable it, and sort again.
    /// Levels and transforms must remain valid.
    /// </summary>
    [TestMethod]
    public void SortAfterDisableAndReEnableNode()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        root.AddChild(child);
        child.AddChild(grandChild);

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        child.Enabled = false;
        Assert.IsFalse(grandChild.Enabled, "GrandChild should be disabled when child is disabled.");

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);

        child.Enabled = true;
        Assert.IsTrue(grandChild.Enabled, "GrandChild should be re-enabled when child is re-enabled.");

        world.SortSceneNodes();
        AssertComponentsSortedByLevel(world);
        AssertNodeLevelsConsistent([root, child, grandChild]);

        world.UpdateTransforms();
        Assert.IsFalse(root.Transform.IsWorldDirty);
        Assert.IsFalse(child.Transform.IsWorldDirty);
        Assert.IsFalse(grandChild.Transform.IsWorldDirty);
    }
}
