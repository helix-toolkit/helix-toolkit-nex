using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 1 (feature: engine-node-command-buffer): Recording returns distinct,
/// valid, unresolved typed handles in order.
///
/// For any sequence of N custom-node recordings into a single buffer, the N returned
/// <see cref="TypedDeferredNode{T}"/> handles are each valid, pairwise distinct, and unresolved
/// before flush, and flush materializes them in the recorded order relative to all other
/// recorded commands.
///
/// Validates: Requirements 1.1, 1.4, 2.1
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferTypedHandleOrderTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer. The factory stamps it
    /// with a monotonically increasing sequence number captured from a shared counter at the
    /// moment the factory runs (i.e. during flush), so the relative materialization order of the
    /// custom nodes is observable after flush.
    /// </summary>
    private sealed class OrderTrackingNode : Node
    {
        public int MaterializationOrder { get; }

        public OrderTrackingNode(World world, int materializationOrder)
            : base(world)
        {
            MaterializationOrder = materializationOrder;
        }
    }

    [TestMethod]
    public void Property1_RecordingReturnsDistinctValidUnresolvedHandles_FlushMaterializesInOrder()
    {
        // Feature: engine-node-command-buffer, Property 1
        // For any sequence of N custom-node recordings into a single buffer, the returned
        // TypedDeferredNode<T> handles are each valid, pairwise distinct, and unresolved before
        // flush, and flush materializes them in the recorded order relative to all other recorded
        // commands.
        // Validates: Requirements 1.1, 1.4, 2.1

        // A program is described by a custom-node count n (>= 1) and a seed that deterministically
        // chooses, before each custom recording, how many base node-creation commands to
        // interleave. Interleaving base creations exercises the "relative to all other recorded
        // commands" clause: the custom factories must still be invoked in recorded order even when
        // other commands sit between them.
        var gen =
            from n in Gen.Choose(1, 25)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        var scb = new SceneCommandBuffer();
                        var rng = new Random(t.seed);

                        // A shared counter the instrumented factories read-and-increment when they
                        // run during flush; the value stamped into each node is its relative
                        // materialization order across all factory invocations.
                        var nextOrder = 0;

                        var handles = new TypedDeferredNode<OrderTrackingNode>[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            // Interleave 0..2 base node-creation commands before each custom one.
                            var interleave = rng.Next(0, 3);
                            for (var b = 0; b < interleave; b++)
                            {
                                scb.RecordCreateNode();
                            }

                            var code = scb.TryRecordCreateNode<OrderTrackingNode>(
                                w => new OrderTrackingNode(w, nextOrder++),
                                out var handle);

                            if (code != ResultCode.Ok)
                            {
                                return false;
                            }

                            handles[i] = handle;
                        }

                        // (a) Each returned handle is valid.
                        for (var i = 0; i < t.n; i++)
                        {
                            if (!handles[i].IsValid)
                            {
                                return false;
                            }
                        }

                        // (b) The handles are pairwise distinct.
                        var distinct = new HashSet<TypedDeferredNode<OrderTrackingNode>>();
                        for (var i = 0; i < t.n; i++)
                        {
                            if (!distinct.Add(handles[i]))
                            {
                                return false;
                            }
                        }

                        // (c) Unresolved before flush: nothing is materialized yet and no factory
                        //     has run (the shared order counter is untouched).
                        if (scb.MaterializedNodes.Count != 0 || nextOrder != 0)
                        {
                            return false;
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (scb.MaterializedNodes.ContainsKey(handles[i]))
                            {
                                return false;
                            }
                        }

                        // Flush materializes every recorded command on the owning thread.
                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        // Every custom factory ran exactly once.
                        if (nextOrder != t.n)
                        {
                            return false;
                        }

                        // (d) Each handle resolves to a materialized node of the recorded subtype,
                        //     and they are materialized in the recorded order: the i-th recorded
                        //     custom node has the i-th smallest materialization order.
                        var previousOrder = -1;
                        for (var i = 0; i < t.n; i++)
                        {
                            if (!scb.MaterializedNodes.TryGetValue(handles[i], out var node))
                            {
                                return false;
                            }

                            if (node is not OrderTrackingNode tracked)
                            {
                                return false;
                            }

                            // Strictly increasing in recording order => materialized in order
                            // relative to all other recorded commands.
                            if (tracked.MaterializationOrder <= previousOrder)
                            {
                                return false;
                            }

                            previousOrder = tracked.MaterializationOrder;
                        }

                        return true;
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
    public void RecordedHandles_AreValidDistinctAndUnresolvedBeforeFlush()
    {
        // Feature: engine-node-command-buffer, Property 1 (concrete example)
        // Three custom recordings yield three valid, distinct handles, none resolved before flush.
        // Validates: Requirements 1.1, 1.4, 2.1
        var scb = new SceneCommandBuffer();

        var h0 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, 0));
        var h1 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, 1));
        var h2 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, 2));

        Assert.IsTrue(h0.IsValid);
        Assert.IsTrue(h1.IsValid);
        Assert.IsTrue(h2.IsValid);

        Assert.AreNotEqual(h0, h1);
        Assert.AreNotEqual(h1, h2);
        Assert.AreNotEqual(h0, h2);

        // Unresolved before flush.
        Assert.AreEqual(0, scb.MaterializedNodes.Count);
        Assert.IsFalse(scb.MaterializedNodes.ContainsKey(h0));
        Assert.IsFalse(scb.MaterializedNodes.ContainsKey(h1));
        Assert.IsFalse(scb.MaterializedNodes.ContainsKey(h2));
    }

    [TestMethod]
    public void Flush_MaterializesCustomNodesInRecordedOrder()
    {
        // Feature: engine-node-command-buffer, Property 1 (concrete example)
        // Flush invokes the recorded factories in recording order, even with interleaved base
        // node-creation commands between them.
        // Validates: Requirements 1.1, 1.4, 2.1
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var order = 0;

            var h0 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, order++));
            scb.RecordCreateNode(); // interleaved base node
            var h1 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, order++));
            scb.RecordCreateNode(); // interleaved base node
            var h2 = scb.RecordCreateNode<OrderTrackingNode>(w => new OrderTrackingNode(w, order++));

            // No factory ran during recording.
            Assert.AreEqual(0, order);

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);
            Assert.AreEqual(3, order, "All three factories should have run exactly once.");

            var n0 = (OrderTrackingNode)scb.MaterializedNodes[h0];
            var n1 = (OrderTrackingNode)scb.MaterializedNodes[h1];
            var n2 = (OrderTrackingNode)scb.MaterializedNodes[h2];

            Assert.IsTrue(n0.MaterializationOrder < n1.MaterializationOrder);
            Assert.IsTrue(n1.MaterializationOrder < n2.MaterializationOrder);
        }
        finally
        {
            world.Dispose();
        }
    }
}
