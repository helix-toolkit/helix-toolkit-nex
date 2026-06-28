using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 13: A child cannot be parented twice (Requirements 7.6).
///
/// Property 13 has two branches:
///   (1) RECORD TIME  — for any deferred child node that already has a recorded parent, a
///       subsequent parent-child recording naming that child returns an error result.
///   (2) FLUSH TIME   — for any recorded hierarchy in which a child would receive a second
///       parent during flush (from existing parent state in the target world), the flush
///       returns an error result.
///
/// See the test method comments for how each branch is exercised, and the comment on
/// <see cref="FlushTime_AddChildOnAlreadyParentedNode_Throws_WhichFlushTranslatesToError"/>
/// for why the flush-time catch branch inside <c>SceneCommandBuffer.Flush</c> is not directly
/// reachable through the public <c>SceneCommandBuffer</c> API and how the achievable flush-time
/// invariant is verified instead.
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferDoubleParentTest
{
    // ---------------------------------------------------------------------------------------
    // Branch (1): RECORD-TIME rejection.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Property13_RecordTime_SecondParentForRecordedChild_IsRejected()
    {
        // Feature: ecs-command-buffer, Property 13: A child cannot be parented twice
        // For any deferred child node that already has a recorded parent, a subsequent
        // parent-child recording naming that child returns an error result (and appends nothing).
        // Validates: Requirements 7.6

        // The hierarchy is described by a node count n (>= 3 so parent1, parent2 and child can
        // all exist) and three index picks. The generator picks indices for parent1, parent2 and
        // the child; degenerate draws (not three distinct nodes) hold vacuously. The first
        // AddChild(parent1, child) must succeed and every subsequent AddChild naming the same
        // child (whether parent2 or parent1 again) must be rejected with ResultCode.InvalidState
        // without changing PendingCount.
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
                    // Require three distinct roles; discard degenerate draws.
                    if (t.a == t.b || t.a == t.c || t.b == t.c)
                    {
                        return true; // vacuously holds for draws that don't supply 3 distinct nodes
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

                    // First parenting of the child succeeds.
                    if (scb.RecordAddChild(parent1, child) != ResultCode.Ok)
                    {
                        return false;
                    }

                    var pendingAfterFirst = scb.PendingCount;

                    // A second parenting naming the SAME child with a DIFFERENT parent is rejected.
                    if (scb.RecordAddChild(parent2, child) != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    // Re-recording the SAME parent for the same child is likewise rejected.
                    if (scb.RecordAddChild(parent1, child) != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    // Neither rejected recording appended a command.
                    return scb.PendingCount == pendingAfterFirst;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void RecordTime_FirstParentSucceeds_SecondDifferentParentRejected_PendingUnchanged()
    {
        // Feature: ecs-command-buffer, Property 13: A child cannot be parented twice
        // Concrete example of the record-time branch: parent1, parent2, child.
        // Validates: Requirements 7.6
        var scb = new SceneCommandBuffer();
        var parent1 = scb.RecordCreateNode();
        var parent2 = scb.RecordCreateNode();
        var child = scb.RecordCreateNode();

        // 3 create commands recorded so far.
        Assert.AreEqual(3, scb.PendingCount);

        Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(parent1, child));
        Assert.AreEqual(4, scb.PendingCount, "Successful AddChild should append exactly one command.");

        // Second parent for the same child is rejected and appends nothing.
        Assert.AreEqual(ResultCode.InvalidState, scb.RecordAddChild(parent2, child));
        Assert.AreEqual(4, scb.PendingCount, "Rejected AddChild must not append a command.");
    }

    [TestMethod]
    public void RecordTime_SameParentRecordedTwice_SecondRejected_PendingUnchanged()
    {
        // Feature: ecs-command-buffer, Property 13: A child cannot be parented twice
        // Re-recording the same (parent, child) pair is also rejected: the child already has a
        // recorded parent.
        // Validates: Requirements 7.6
        var scb = new SceneCommandBuffer();
        var parent = scb.RecordCreateNode();
        var child = scb.RecordCreateNode();

        Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(parent, child));
        var pending = scb.PendingCount;

        Assert.AreEqual(ResultCode.InvalidState, scb.RecordAddChild(parent, child));
        Assert.AreEqual(pending, scb.PendingCount, "Re-recording the same parent must not append a command.");
    }

    [TestMethod]
    public void RecordTime_RejectedDoubleParent_DoesNotAffectFlushedStructure()
    {
        // Feature: ecs-command-buffer, Property 13: A child cannot be parented twice
        // After a rejected second-parent recording, flushing materializes the hierarchy that
        // reflects only the FIRST (accepted) parent relationship.
        // Validates: Requirements 7.6
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var parent1 = scb.RecordCreateNode();
            var parent2 = scb.RecordCreateNode();
            var child = scb.RecordCreateNode();

            Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(parent1, child));
            Assert.AreEqual(ResultCode.InvalidState, scb.RecordAddChild(parent2, child));

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success, "Flush should succeed: only the first parenting was recorded.");

            var parent1Node = scb.MaterializedNodes[parent1];
            var parent2Node = scb.MaterializedNodes[parent2];
            var childNode = scb.MaterializedNodes[child];

            // The child is parented to parent1 only; parent2 never became its parent.
            Assert.AreSame(parent1Node, childNode.Parent, "Child must be parented to the first recorded parent.");
            Assert.AreNotSame(parent2Node, childNode.Parent, "Child must not be parented to the rejected parent.");
        }
        finally
        {
            world.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Branch (2): FLUSH-TIME rejection.
    //
    // The flush-time branch in SceneCommandBuffer.Flush is the catch over
    // InvalidOperationException thrown by Node.AddChild when the child already has a parent in
    // the world. That exception is the foundation the flush relies on to detect a second parent
    // arising from "existing parent state in the target World".
    //
    // NOTE ON REACHABILITY THROUGH THE PUBLIC API:
    // The flush catch cannot be reached purely through the public SceneCommandBuffer API. Record
    // time validation (RecordAddChild) guarantees that each child index appears as the child of
    // at most one AddChildCommand (it sets _recordedParent[child] on the first success and rejects
    // any later recording naming the same child). During flush every node is freshly created with
    // `new Node(world)` and therefore starts with no parent, and each node is the child argument
    // of at most one AddChild call — so AddChild never sees an already-parented child and never
    // throws. The buffer also never accepts an externally constructed (already-parented) Node as a
    // child: Flush only constructs its own nodes. Consequently the catch branch is defense-in-depth
    // for the underlying Node invariant rather than a state reachable from buffer recordings.
    //
    // The achievable flush-time case below verifies that invariant directly: it confirms that
    // Node.AddChild throws InvalidOperationException for an already-parented child (the exact
    // condition the flush translates into a failing SceneFlushResult). The record-time tests above
    // are what actually enforce Property 13's flush-time guarantee for any SceneCommandBuffer
    // recording.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void FlushTime_AddChildOnAlreadyParentedNode_Throws_WhichFlushTranslatesToError()
    {
        // Feature: ecs-command-buffer, Property 13: A child cannot be parented twice
        // Flush-time foundation: a node that already has a parent in the world cannot receive a
        // second parent — Node.AddChild throws InvalidOperationException. SceneCommandBuffer.Flush
        // catches exactly this exception and returns a failing SceneFlushResult identifying the
        // offending add-child command.
        // Validates: Requirements 7.6
        var world = World.CreateWorld();
        try
        {
            // Materialize a small hierarchy through a flush so the child ends up with a real
            // parent in the world (mirrors "existing parent state in the target World").
            var scb = new SceneCommandBuffer();
            var parent1 = scb.RecordCreateNode();
            var child = scb.RecordCreateNode();
            Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(parent1, child));
            Assert.IsTrue(scb.Flush(world).Success);

            var parent1Node = scb.MaterializedNodes[parent1];
            var childNode = scb.MaterializedNodes[child];
            Assert.AreSame(parent1Node, childNode.Parent);
            Assert.IsTrue(childNode.HasParent, "Materialized child must have a parent in the world.");

            // Attempting to give the already-parented child a second parent throws — this is the
            // precise InvalidOperationException that SceneCommandBuffer.Flush catches and converts
            // into a failing result that identifies the add-child command.
            var parent2Node = new Node(world);
            Assert.ThrowsException<InvalidOperationException>(() => parent2Node.AddChild(childNode));
        }
        finally
        {
            world.Dispose();
        }
    }
}
