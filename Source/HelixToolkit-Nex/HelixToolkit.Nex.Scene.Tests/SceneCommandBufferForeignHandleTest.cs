using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 15: Foreign handles and double-parenting are rejected consistently.
///
/// Property 15 (design.md): For any handle produced by a different buffer, any
/// hierarchy/property operation referencing it is rejected; and recording a parent-child
/// relationship for a child handle that already has a recorded parent is rejected, leaving the
/// previously recorded relationship unchanged.
///
/// Validates: Requirements 7.5, 7.7, 9.1
///
/// A second <see cref="SceneCommandBuffer"/> instance produces the foreign handles, and a
/// test-local <see cref="Node"/> subtype (<see cref="ForeignSubNode"/>) produces a foreign
/// <see cref="TypedDeferredNode{T}"/> exercised through its implicit conversion to
/// <see cref="DeferredNode"/>.
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferForeignHandleTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to produce a
    /// foreign <see cref="TypedDeferredNode{T}"/>. The factory is never invoked in these tests
    /// (no flush of the foreign buffer occurs), so no <see cref="World"/> is required.
    /// </summary>
    private sealed class ForeignSubNode : Node
    {
        public ForeignSubNode(World world) : base(world) { }
    }

    // ---------------------------------------------------------------------------------------
    // Branch (1): FOREIGN-HANDLE rejection (Requirements 7.7, 9.1).
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Property15_ForeignHandle_AllOperationsRejected_AppendNothing()
    {
        // Feature: engine-node-command-buffer, Property 15: Foreign handles are rejected consistently
        // For any handle produced by a DIFFERENT buffer, every hierarchy/property operation on this
        // buffer (RecordAddChild, RecordName, RecordLocalTransform, RecordRenderable) that references
        // the foreign handle is rejected with ResultCode.InvalidState and appends no command, leaving the
        // recorded pending commands unchanged.
        // Validates: Requirements 7.7, 9.1

        // A case is two node counts (own buffer A, foreign buffer B) plus index picks into each.
        // Indices are reduced modulo the corresponding count so they are always in range.
        var gen =
            from n in Gen.Choose(1, 20)
            from m in Gen.Choose(1, 20)
            from foreignPick in Gen.Choose(0, 19)
            from ownPick in Gen.Choose(0, 19)
            select (n, m, foreignPick, ownPick);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int m, int foreignPick, int ownPick) t) =>
                {
                    var bufferA = new SceneCommandBuffer();
                    var bufferB = new SceneCommandBuffer();

                    // Own handles (produced by bufferA).
                    var ownNodes = new DeferredNode[t.n];
                    for (var i = 0; i < t.n; i++)
                    {
                        ownNodes[i] = bufferA.RecordCreateNode();
                    }

                    // Foreign handles (produced by bufferB).
                    var foreignNodes = new DeferredNode[t.m];
                    for (var i = 0; i < t.m; i++)
                    {
                        foreignNodes[i] = bufferB.RecordCreateNode();
                    }

                    var own = ownNodes[t.ownPick % t.n];
                    var foreign = foreignNodes[t.foreignPick % t.m];
                    var foreign2 = foreignNodes[(t.foreignPick + 1) % t.m];

                    var pendingBefore = bufferA.PendingCount;

                    // Property/name/transform/renderable operations on a foreign handle are rejected.
                    if (bufferA.RecordName(foreign, "x") != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    var tf = new Transform();
                    if (bufferA.RecordLocalTransform(foreign, in tf) != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    if (bufferA.RecordRenderable(foreign, true) != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    // Parent-child operations are rejected whenever EITHER handle is foreign.
                    if (bufferA.RecordAddChild(foreign, own) != ResultCode.InvalidState)
                    {
                        return false; // foreign parent, own child
                    }

                    if (bufferA.RecordAddChild(own, foreign) != ResultCode.InvalidState)
                    {
                        return false; // own parent, foreign child
                    }

                    if (bufferA.RecordAddChild(foreign, foreign2) != ResultCode.InvalidState)
                    {
                        return false; // both foreign
                    }

                    // None of the rejected operations appended a command.
                    return bufferA.PendingCount == pendingBefore;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void ForeignTypedHandle_FromCustomNodeSubtype_Rejected_AppendNothing()
    {
        // Feature: engine-node-command-buffer, Property 15: Foreign handles are rejected consistently
        // A foreign TypedDeferredNode<T> (from a different buffer, built for a test-local Node
        // subtype) implicitly converts to a DeferredNode; every operation referencing it on this
        // buffer is rejected and appends no command.
        // Validates: Requirements 7.7, 9.1
        var bufferA = new SceneCommandBuffer();
        var bufferB = new SceneCommandBuffer();

        var own = bufferA.RecordCreateNode();

        // Foreign typed handle produced by bufferB for a Scene-unknown subtype. The factory is
        // never invoked here (bufferB is not flushed), so no World is constructed.
        TypedDeferredNode<ForeignSubNode> foreignTyped =
            bufferB.RecordCreateNode(world => new ForeignSubNode(world));
        Assert.IsTrue(foreignTyped.IsValid, "Foreign typed handle should be valid in its own buffer.");

        var pendingBefore = bufferA.PendingCount;

        DeferredNode foreign = foreignTyped; // implicit conversion to the untyped handle

        Assert.AreEqual(ResultCode.InvalidState, bufferA.RecordName(foreign, "x"));
        var tf = new Transform();
        Assert.AreEqual(ResultCode.InvalidState, bufferA.RecordLocalTransform(foreign, in tf));
        Assert.AreEqual(ResultCode.InvalidState, bufferA.RecordRenderable(foreign));
        Assert.AreEqual(ResultCode.InvalidState, bufferA.RecordAddChild(own, foreign));
        Assert.AreEqual(ResultCode.InvalidState, bufferA.RecordAddChild(foreign, own));

        Assert.AreEqual(pendingBefore, bufferA.PendingCount, "No rejected operation may append a command.");
    }

    // ---------------------------------------------------------------------------------------
    // Branch (2): DOUBLE-PARENT rejection leaves the previously recorded relationship unchanged
    // (Requirement 7.5).
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Property15_DoubleParent_Rejected_PreviousRelationshipUnchanged()
    {
        // Feature: engine-node-command-buffer, Property 15: Double-parenting is rejected consistently
        // For any child handle that already has a recorded parent, a subsequent parent-child
        // recording naming that child is rejected (ResultCode.InvalidState) and appends no command; the
        // previously recorded relationship is unchanged, which a flush confirms (the child is
        // parented to the FIRST recorded parent only).
        // Validates: Requirements 7.5

        // n >= 3 so parent1, parent2, and child can be three distinct nodes; degenerate draws
        // (not three distinct roles) hold vacuously.
        var gen =
            from n in Gen.Choose(3, 25)
            from a in Gen.Choose(0, n - 1)
            from b in Gen.Choose(0, n - 1)
            from c in Gen.Choose(0, n - 1)
            select (n, a, b, c);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int a, int b, int c) t) =>
                {
                    if (t.a == t.b || t.a == t.c || t.b == t.c)
                    {
                        return true; // need three distinct nodes
                    }

                    var scb = new SceneCommandBuffer();
                    var nodes = new DeferredNode[t.n];
                    for (var i = 0; i < t.n; i++)
                    {
                        nodes[i] = scb.RecordCreateNode();
                    }

                    var parent1 = nodes[t.a];
                    var parent2 = nodes[t.b];
                    var child = nodes[t.c];

                    // First parenting succeeds and records the relationship.
                    if (scb.RecordAddChild(parent1, child) != ResultCode.Ok)
                    {
                        return false;
                    }

                    var pendingAfterFirst = scb.PendingCount;

                    // A second parenting naming the same child (different parent) is rejected and
                    // appends nothing.
                    if (scb.RecordAddChild(parent2, child) != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    if (scb.PendingCount != pendingAfterFirst)
                    {
                        return false;
                    }

                    // The previously recorded relationship is unchanged: flushing materializes the
                    // child parented to parent1 only, never to the rejected parent2.
                    var world = World.CreateWorld();
                    try
                    {
                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        var parent1Node = scb.MaterializedNodes[parent1];
                        var parent2Node = scb.MaterializedNodes[parent2];
                        var childNode = scb.MaterializedNodes[child];

                        return ReferenceEquals(childNode.Parent, parent1Node)
                            && !ReferenceEquals(childNode.Parent, parent2Node);
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void DoubleParent_SameChildReRecorded_Rejected_PendingUnchanged()
    {
        // Feature: engine-node-command-buffer, Property 15: Double-parenting is rejected consistently
        // Concrete example: re-recording any parent for a child that already has a recorded parent
        // is rejected and appends nothing.
        // Validates: Requirements 7.5
        var scb = new SceneCommandBuffer();
        var parent1 = scb.RecordCreateNode();
        var parent2 = scb.RecordCreateNode();
        var child = scb.RecordCreateNode();

        Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(parent1, child));
        var pending = scb.PendingCount;

        // Different parent for the already-parented child: rejected.
        Assert.AreEqual(ResultCode.InvalidState, scb.RecordAddChild(parent2, child));
        // Same parent again for the already-parented child: also rejected.
        Assert.AreEqual(ResultCode.InvalidState, scb.RecordAddChild(parent1, child));

        Assert.AreEqual(pending, scb.PendingCount, "Rejected double-parent recordings must not append commands.");
    }
}
