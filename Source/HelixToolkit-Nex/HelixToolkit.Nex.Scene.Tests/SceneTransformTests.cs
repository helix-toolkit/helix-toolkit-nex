using System.Numerics;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;

[TestClass]
public sealed class SceneTransformTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void SortAndUpdate(World world)
    {
        world.SortSceneNodes();
        world.UpdateTransforms();
    }

    private static void AssertMatrix4x4Equal(
        Matrix4x4 expected,
        Matrix4x4 actual,
        string message = "",
        float tolerance = 1e-5f
    )
    {
        Assert.AreEqual(expected.M11, actual.M11, tolerance, $"{message} M11");
        Assert.AreEqual(expected.M12, actual.M12, tolerance, $"{message} M12");
        Assert.AreEqual(expected.M13, actual.M13, tolerance, $"{message} M13");
        Assert.AreEqual(expected.M14, actual.M14, tolerance, $"{message} M14");
        Assert.AreEqual(expected.M21, actual.M21, tolerance, $"{message} M21");
        Assert.AreEqual(expected.M22, actual.M22, tolerance, $"{message} M22");
        Assert.AreEqual(expected.M23, actual.M23, tolerance, $"{message} M23");
        Assert.AreEqual(expected.M24, actual.M24, tolerance, $"{message} M24");
        Assert.AreEqual(expected.M31, actual.M31, tolerance, $"{message} M31");
        Assert.AreEqual(expected.M32, actual.M32, tolerance, $"{message} M32");
        Assert.AreEqual(expected.M33, actual.M33, tolerance, $"{message} M33");
        Assert.AreEqual(expected.M34, actual.M34, tolerance, $"{message} M34");
        Assert.AreEqual(expected.M41, actual.M41, tolerance, $"{message} M41");
        Assert.AreEqual(expected.M42, actual.M42, tolerance, $"{message} M42");
        Assert.AreEqual(expected.M43, actual.M43, tolerance, $"{message} M43");
        Assert.AreEqual(expected.M44, actual.M44, tolerance, $"{message} M44");
    }

    // -------------------------------------------------------------------------
    // Root node — no parent
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RootNodeTranslation()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        root.Transform.Translation = new Vector3(1, 2, 3);

        SortAndUpdate(world);

        var expected = Matrix4x4.CreateTranslation(1, 2, 3);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root translation");
    }

    [TestMethod]
    public void RootNodeScale()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        root.Transform.Scale = new Vector3(2, 3, 4);

        SortAndUpdate(world);

        var expected = Matrix4x4.CreateScale(2, 3, 4);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root scale");
    }

    [TestMethod]
    public void RootNodeRotation()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        root.Transform.Rotation = rotation;

        SortAndUpdate(world);

        var expected = Matrix4x4.CreateFromQuaternion(rotation);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root rotation");
    }

    [TestMethod]
    public void RootNodeCombinedTRS()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var scale = new Vector3(2, 2, 2);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var translation = new Vector3(10, 0, 0);
        root.Transform.Scale = scale;
        root.Transform.Rotation = rotation;
        root.Transform.Translation = translation;

        SortAndUpdate(world);

        var expected =
            Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root combined TRS");
    }

    // -------------------------------------------------------------------------
    // Single parent–child
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ChildInheritsParentTranslation()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        parent.Transform.Translation = new Vector3(5, 0, 0);

        SortAndUpdate(world);

        var expectedParent = Matrix4x4.CreateTranslation(5, 0, 0);
        var expectedChild = expectedParent * Matrix4x4.Identity; // child has identity local
        AssertMatrix4x4Equal(expectedParent, parent.WorldTransform.Value, "Parent world");
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child inherits parent translation");
    }

    [TestMethod]
    public void ChildWithLocalTranslationAddsToParent()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        parent.Transform.Translation = new Vector3(5, 0, 0);
        child.Transform.Translation = new Vector3(0, 3, 0);

        SortAndUpdate(world);

        var parentWorld = Matrix4x4.CreateTranslation(5, 0, 0);
        var expectedChild = parentWorld * Matrix4x4.CreateTranslation(0, 3, 0);
        AssertMatrix4x4Equal(parentWorld, parent.WorldTransform.Value, "Parent world");
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child with local offset");
    }

    [TestMethod]
    public void ChildInheritsParentScale()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        parent.Transform.Scale = new Vector3(3, 3, 3);

        SortAndUpdate(world);

        var expectedParent = Matrix4x4.CreateScale(3, 3, 3);
        AssertMatrix4x4Equal(expectedParent, parent.WorldTransform.Value, "Parent scale");
        AssertMatrix4x4Equal(expectedParent, child.WorldTransform.Value, "Child inherits parent scale");
    }

    [TestMethod]
    public void ChildScaleMultipliesParentScale()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        parent.Transform.Scale = new Vector3(2, 2, 2);
        child.Transform.Scale = new Vector3(3, 3, 3);

        SortAndUpdate(world);

        var parentWorld = Matrix4x4.CreateScale(2, 2, 2);
        var expectedChild = parentWorld * Matrix4x4.CreateScale(3, 3, 3);
        AssertMatrix4x4Equal(parentWorld, parent.WorldTransform.Value, "Parent scale world");
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child scale multiplied");
    }

    [TestMethod]
    public void ChildInheritsParentRotation()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        parent.Transform.Rotation = rotation;

        SortAndUpdate(world);

        var expectedParent = Matrix4x4.CreateFromQuaternion(rotation);
        AssertMatrix4x4Equal(expectedParent, parent.WorldTransform.Value, "Parent rotation world");
        AssertMatrix4x4Equal(expectedParent, child.WorldTransform.Value, "Child inherits parent rotation");
    }

    [TestMethod]
    public void ChildWithLocalRotationCombinesWithParent()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        var parentRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var childRot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4);
        parent.Transform.Rotation = parentRot;
        child.Transform.Rotation = childRot;

        SortAndUpdate(world);

        var parentWorld = Matrix4x4.CreateFromQuaternion(parentRot);
        var expectedChild = parentWorld * Matrix4x4.CreateFromQuaternion(childRot);
        AssertMatrix4x4Equal(parentWorld, parent.WorldTransform.Value, "Parent rotation world");
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child with local rotation");
    }

    [TestMethod]
    public void ChildInheritsParentCombinedTRS()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        var scale = new Vector3(2, 1, 1);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6);
        var translation = new Vector3(4, 5, 6);
        parent.Transform.Scale = scale;
        parent.Transform.Rotation = rotation;
        parent.Transform.Translation = translation;

        child.Transform.Translation = new Vector3(1, 0, 0);

        SortAndUpdate(world);

        var parentWorld =
            Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
        var expectedChild = parentWorld * Matrix4x4.CreateTranslation(1, 0, 0);

        AssertMatrix4x4Equal(parentWorld, parent.WorldTransform.Value, "Parent combined TRS");
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child with combined parent TRS");
    }

    // -------------------------------------------------------------------------
    // Multi-level hierarchy
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ThreeLevelHierarchyTranslation()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        root.AddChild(child);
        child.AddChild(grandChild);

        root.Transform.Translation = new Vector3(1, 0, 0);
        child.Transform.Translation = new Vector3(0, 2, 0);
        grandChild.Transform.Translation = new Vector3(0, 0, 3);

        SortAndUpdate(world);

        var rootWorld = Matrix4x4.CreateTranslation(1, 0, 0);
        var childWorld = rootWorld * Matrix4x4.CreateTranslation(0, 2, 0);
        var grandChildWorld = childWorld * Matrix4x4.CreateTranslation(0, 0, 3);

        AssertMatrix4x4Equal(rootWorld, root.WorldTransform.Value, "Root world");
        AssertMatrix4x4Equal(childWorld, child.WorldTransform.Value, "Child world");
        AssertMatrix4x4Equal(grandChildWorld, grandChild.WorldTransform.Value, "GrandChild world");
    }

    [TestMethod]
    public void ThreeLevelHierarchyScale()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        root.AddChild(child);
        child.AddChild(grandChild);

        root.Transform.Scale = new Vector3(2, 2, 2);
        child.Transform.Scale = new Vector3(3, 3, 3);
        grandChild.Transform.Scale = new Vector3(4, 4, 4);

        SortAndUpdate(world);

        var rootWorld = Matrix4x4.CreateScale(2, 2, 2);
        var childWorld = rootWorld * Matrix4x4.CreateScale(3, 3, 3);
        var grandChildWorld = childWorld * Matrix4x4.CreateScale(4, 4, 4);

        AssertMatrix4x4Equal(rootWorld, root.WorldTransform.Value, "Root scale world");
        AssertMatrix4x4Equal(childWorld, child.WorldTransform.Value, "Child scale world");
        AssertMatrix4x4Equal(grandChildWorld, grandChild.WorldTransform.Value, "GrandChild scale world");
    }

    [TestMethod]
    public void FiveLevelHierarchyTransformPropagation()
    {
        using var world = World.CreateWorld();
        var nodes = new Node[5];
        nodes[0] = new Node(world, "Level0");
        for (int i = 1; i < nodes.Length; ++i)
        {
            nodes[i] = new Node(world, $"Level{i}");
            nodes[i - 1].AddChild(nodes[i]);
        }

        var translation = new Vector3(1, 1, 1);
        nodes[0].Transform.Translation = translation;

        SortAndUpdate(world);

        var expectedWorld = Matrix4x4.CreateTranslation(translation);
        foreach (var node in nodes)
        {
            AssertMatrix4x4Equal(
                expectedWorld,
                node.WorldTransform.Value,
                $"{node.Name} should inherit root translation"
            );
        }
    }

    [TestMethod]
    public void SiblingNodesHaveIndependentTransforms()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child1 = new Node(world, "Child1");
        var child2 = new Node(world, "Child2");
        root.AddChild(child1);
        root.AddChild(child2);

        root.Transform.Translation = new Vector3(1, 0, 0);
        child1.Transform.Translation = new Vector3(0, 2, 0);
        child2.Transform.Translation = new Vector3(0, -2, 0);

        SortAndUpdate(world);

        var rootWorld = Matrix4x4.CreateTranslation(1, 0, 0);
        var expected1 = rootWorld * Matrix4x4.CreateTranslation(0, 2, 0);
        var expected2 = rootWorld * Matrix4x4.CreateTranslation(0, -2, 0);

        AssertMatrix4x4Equal(expected1, child1.WorldTransform.Value, "Child1 independent");
        AssertMatrix4x4Equal(expected2, child2.WorldTransform.Value, "Child2 independent");
    }

    // -------------------------------------------------------------------------
    // Dirty flag propagation
    // -------------------------------------------------------------------------

    [TestMethod]
    public void UpdateTransformsClearsDirtyFlag()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        root.AddChild(child);

        root.Transform.Translation = new Vector3(1, 2, 3);

        SortAndUpdate(world);

        Assert.IsFalse(root.Transform.IsWorldDirty, "Root dirty flag should be cleared after update");
        Assert.IsFalse(child.Transform.IsWorldDirty, "Child dirty flag should be cleared after update");
    }

    [TestMethod]
    public void ChangingParentTransformMakesChildDirtyAfterUpdate()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        root.AddChild(child);

        SortAndUpdate(world);
        Assert.IsFalse(root.Transform.IsWorldDirty);
        Assert.IsFalse(child.Transform.IsWorldDirty);

        // Mutate the parent's transform — the child's world transform must update
        root.Transform.Translation = new Vector3(10, 0, 0);

        SortAndUpdate(world);

        var expected = Matrix4x4.CreateTranslation(10, 0, 0);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root updated");
        AssertMatrix4x4Equal(expected, child.WorldTransform.Value, "Child re-computed after parent change");
        Assert.IsFalse(root.Transform.IsWorldDirty, "Root should not be dirty after update");
        Assert.IsFalse(child.Transform.IsWorldDirty, "Child should not be dirty after update");
    }

    [TestMethod]
    public void MultipleMutationsBetweenUpdates()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        root.AddChild(child);

        root.Transform.Translation = new Vector3(1, 0, 0);
        root.Transform.Translation = new Vector3(2, 0, 0);
        root.Transform.Translation = new Vector3(3, 0, 0);

        SortAndUpdate(world);

        var expected = Matrix4x4.CreateTranslation(3, 0, 0);
        AssertMatrix4x4Equal(expected, root.WorldTransform.Value, "Root final translation");
        AssertMatrix4x4Equal(expected, child.WorldTransform.Value, "Child final translation");
    }

    [TestMethod]
    public void NoUpdateNeededWhenTransformUnchanged()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        root.Transform.Translation = new Vector3(5, 0, 0);

        SortAndUpdate(world);

        // Capture timestamp; a second update with no changes should not alter the result
        var worldTransformBefore = root.WorldTransform.Value;

        SortAndUpdate(world);

        AssertMatrix4x4Equal(worldTransformBefore, root.WorldTransform.Value, "World transform unchanged");
        Assert.IsFalse(root.Transform.IsWorldDirty);
    }

    // -------------------------------------------------------------------------
    // Reparenting and transform correctness
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TransformCorrectAfterReparentingToNodeWithDifferentTransform()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var parentA = new Node(world, "ParentA");
        var parentB = new Node(world, "ParentB");
        var leaf = new Node(world, "Leaf");

        root.AddChild(parentA);
        root.AddChild(parentB);
        parentA.AddChild(leaf);

        parentA.Transform.Translation = new Vector3(10, 0, 0);
        parentB.Transform.Translation = new Vector3(0, 10, 0);

        SortAndUpdate(world);

        var expectedUnderA = Matrix4x4.CreateTranslation(10, 0, 0);
        AssertMatrix4x4Equal(expectedUnderA, leaf.WorldTransform.Value, "Leaf under ParentA");

        // Move leaf from parentA to parentB
        parentA.RemoveChild(leaf);
        parentB.AddChild(leaf);

        SortAndUpdate(world);

        var expectedUnderB = Matrix4x4.CreateTranslation(0, 10, 0);
        AssertMatrix4x4Equal(expectedUnderB, leaf.WorldTransform.Value, "Leaf after reparenting to ParentB");
    }

    [TestMethod]
    public void TransformCorrectAfterDetachingFromParent()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        parent.Transform.Translation = new Vector3(5, 5, 0);
        child.Transform.Translation = new Vector3(1, 0, 0);

        SortAndUpdate(world);

        var expectedChild = Matrix4x4.CreateTranslation(5, 5, 0) * Matrix4x4.CreateTranslation(1, 0, 0);
        AssertMatrix4x4Equal(expectedChild, child.WorldTransform.Value, "Child world before detach");

        parent.RemoveChild(child);

        SortAndUpdate(world);

        // After detach, child is root-level: world = identity * local = local
        var expectedAfterDetach = Matrix4x4.CreateTranslation(1, 0, 0);
        AssertMatrix4x4Equal(expectedAfterDetach, child.WorldTransform.Value, "Child world after detach");

        parent.Dispose();
        child.Dispose();
    }

    [TestMethod]
    public void SubtreeTransformUpdatedAfterParentMoves()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        root.AddChild(child);
        child.AddChild(grandChild);

        child.Transform.Translation = new Vector3(1, 0, 0);
        grandChild.Transform.Translation = new Vector3(2, 0, 0);

        SortAndUpdate(world);

        var childWorld = Matrix4x4.CreateTranslation(1, 0, 0);
        var grandChildWorld = childWorld * Matrix4x4.CreateTranslation(2, 0, 0);

        AssertMatrix4x4Equal(childWorld, child.WorldTransform.Value, "Child initial");
        AssertMatrix4x4Equal(grandChildWorld, grandChild.WorldTransform.Value, "GrandChild initial");

        // Move root — entire subtree must update
        root.Transform.Translation = new Vector3(100, 0, 0);

        SortAndUpdate(world);

        var rootWorld = Matrix4x4.CreateTranslation(100, 0, 0);
        var newChildWorld = rootWorld * Matrix4x4.CreateTranslation(1, 0, 0);
        var newGrandChildWorld = newChildWorld * Matrix4x4.CreateTranslation(2, 0, 0);

        AssertMatrix4x4Equal(rootWorld, root.WorldTransform.Value, "Root moved");
        AssertMatrix4x4Equal(newChildWorld, child.WorldTransform.Value, "Child after root move");
        AssertMatrix4x4Equal(newGrandChildWorld, grandChild.WorldTransform.Value, "GrandChild after root move");
    }

    // -------------------------------------------------------------------------
    // Identity and default state
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NewNodeHasIdentityWorldTransform()
    {
        using var world = World.CreateWorld();
        var node = new Node(world, "Node");

        SortAndUpdate(world);

        AssertMatrix4x4Equal(Matrix4x4.Identity, node.WorldTransform.Value, "New node should have identity world transform");
    }

    [TestMethod]
    public void ChildOfIdentityParentHasIdentityWorldTransform()
    {
        using var world = World.CreateWorld();
        var parent = new Node(world, "Parent");
        var child = new Node(world, "Child");
        parent.AddChild(child);

        SortAndUpdate(world);

        AssertMatrix4x4Equal(Matrix4x4.Identity, parent.WorldTransform.Value, "Parent identity");
        AssertMatrix4x4Equal(Matrix4x4.Identity, child.WorldTransform.Value, "Child of identity parent is identity");
    }

    // -------------------------------------------------------------------------
    // Flatten-based UpdateTransforms
    // -------------------------------------------------------------------------

    [TestMethod]
    public void FlattenBasedUpdateTransformsProducesCorrectHierarchicalResult()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        var grandChild = new Node(world, "GrandChild");
        root.AddChild(child);
        child.AddChild(grandChild);

        root.Transform.Translation = new Vector3(1, 0, 0);
        child.Transform.Translation = new Vector3(0, 2, 0);
        grandChild.Transform.Translation = new Vector3(0, 0, 3);

        var sortedNodes = new List<Node>();
        ((IReadOnlyList<Node>)[root]).Flatten(null, sortedNodes);
        sortedNodes.UpdateTransforms();

        var rootWorld = Matrix4x4.CreateTranslation(1, 0, 0);
        var childWorld = rootWorld * Matrix4x4.CreateTranslation(0, 2, 0);
        var grandChildWorld = childWorld * Matrix4x4.CreateTranslation(0, 0, 3);

        AssertMatrix4x4Equal(rootWorld, root.WorldTransform.Value, "Root (flatten-based)");
        AssertMatrix4x4Equal(childWorld, child.WorldTransform.Value, "Child (flatten-based)");
        AssertMatrix4x4Equal(grandChildWorld, grandChild.WorldTransform.Value, "GrandChild (flatten-based)");
    }

    [TestMethod]
    public void FlattenBasedAndWorldUpdateTransformsAgree()
    {
        using var world = World.CreateWorld();
        var root = new Node(world, "Root");
        var child = new Node(world, "Child");
        root.AddChild(child);

        root.Transform.Scale = new Vector3(2, 2, 2);
        child.Transform.Translation = new Vector3(3, 0, 0);

        // Use flatten-based path
        var sortedNodes = new List<Node>();
        ((IReadOnlyList<Node>)[root]).Flatten(null, sortedNodes);
        sortedNodes.UpdateTransforms();

        var flattenChildWorld = child.WorldTransform.Value;

        // Reset dirty and use world-based path
        root.Transform.MarkWorldDirty();
        child.Transform.MarkWorldDirty();

        world.SortSceneNodes();
        world.UpdateTransforms();

        AssertMatrix4x4Equal(
            flattenChildWorld,
            child.WorldTransform.Value,
            "Both update paths must agree on child world transform"
        );
    }
}
