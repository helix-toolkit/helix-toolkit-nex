using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 13 (feature: engine-node-command-buffer): A successful flush empties the
/// buffer and re-flush is a no-op.
///
/// For any fully successful flush, <see cref="SceneCommandBuffer.PendingCount"/> is zero
/// afterward and an immediate second flush (with nothing recorded in between) leaves the target
/// world's contents unchanged.
///
/// Validates: Requirements 5.6, 9.4
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferSuccessfulFlushTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to prove the
    /// buffer materializes arbitrary subtypes through the factory mechanism.
    /// </summary>
    private sealed class TaggedNode : Node
    {
        public int Tag { get; }

        public TaggedNode(World world, int tag)
            : base(world)
        {
            Tag = tag;
        }
    }

    [TestMethod]
    public void Property13_SuccessfulFlush_EmptiesBuffer_And_ReFlushIsNoOp()
    {
        // Feature: engine-node-command-buffer, Property 13
        // For any fully successful flush, PendingCount is zero afterward and an immediate second
        // flush (with nothing recorded in between) leaves the target world's contents unchanged.
        // Validates: Requirements 5.6, 9.4

        // A program is described by a count of base node-creation commands, a count of custom
        // node-creation commands, and a seed that deterministically chooses interleaving and which
        // recorded property/hierarchy operations to apply. This exercises empty buffers, single
        // recordings, mixed base/custom buffers, and buffers carrying name/transform/renderable
        // and parent-child commands.
        var gen =
            from baseCount in Gen.Choose(0, 15)
            from customCount in Gen.Choose(0, 15)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (baseCount, customCount, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int baseCount, int customCount, int seed) t) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        var scb = new SceneCommandBuffer();
                        var rng = new Random(t.seed);

                        // Record an interleaved mix of base and custom node-creation commands and
                        // collect their handles so we can wire up some parent/child + property
                        // commands afterward.
                        var handles = new List<DeferredNode>();

                        var remainingBase = t.baseCount;
                        var remainingCustom = t.customCount;
                        var tag = 0;
                        while (remainingBase > 0 || remainingCustom > 0)
                        {
                            var pickCustom = remainingCustom > 0 &&
                                (remainingBase == 0 || rng.Next(0, 2) == 0);
                            if (pickCustom)
                            {
                                var capturedTag = tag++;
                                var code = scb.TryRecordCreateNode<TaggedNode>(
                                    w => new TaggedNode(w, capturedTag),
                                    out var handle);
                                if (code != ResultCode.Ok)
                                {
                                    return false;
                                }

                                handles.Add(handle);
                                remainingCustom--;
                            }
                            else
                            {
                                handles.Add(scb.RecordCreateNode());
                                remainingBase--;
                            }
                        }

                        // Record some property commands and a simple parent-child chain to make the
                        // flush carry more than bare node-creation commands.
                        for (var i = 0; i < handles.Count; i++)
                        {
                            if (rng.Next(0, 2) == 0)
                            {
                                scb.RecordName(handles[i], $"Node {i}");
                            }

                            if (rng.Next(0, 2) == 0)
                            {
                                scb.RecordRenderable(handles[i], rng.Next(0, 2) == 0);
                            }
                        }

                        // Chain consecutive handles as parent -> child (each child has at most one
                        // recorded parent, so these are all valid).
                        for (var i = 1; i < handles.Count; i++)
                        {
                            if (rng.Next(0, 2) == 0)
                            {
                                scb.RecordAddChild(handles[i - 1], handles[i]);
                            }
                        }

                        var expectedPending = handles.Count == 0 ? 0 : scb.PendingCount;

                        // ---- First flush: must succeed.
                        var firstFlush = scb.Flush(world);
                        if (!firstFlush.Success)
                        {
                            return false;
                        }

                        // (a) A successful flush empties the buffer (Req 5.6, 9.4).
                        if (scb.PendingCount != 0)
                        {
                            return false;
                        }

                        // The world now holds exactly one entity per created node.
                        var nodeCountAfterFirstFlush = world.Count;
                        if (nodeCountAfterFirstFlush != handles.Count)
                        {
                            return false;
                        }

                        // ---- Second immediate flush with nothing recorded in between.
                        var secondFlush = scb.Flush(world);
                        if (!secondFlush.Success)
                        {
                            return false;
                        }

                        // (b) The second flush is a no-op: the world's contents are unchanged and
                        // the buffer remains empty.
                        if (world.Count != nodeCountAfterFirstFlush)
                        {
                            return false;
                        }

                        if (scb.PendingCount != 0)
                        {
                            return false;
                        }

                        return expectedPending >= 0;
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
    public void SuccessfulFlush_EmptiesBuffer()
    {
        // Feature: engine-node-command-buffer, Property 13 (concrete example)
        // After a successful flush, PendingCount is zero.
        // Validates: Requirements 5.6, 9.4
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            scb.RecordCreateNode();
            scb.RecordCreateNode<TaggedNode>(w => new TaggedNode(w, 1));

            Assert.AreEqual(2, scb.PendingCount);

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);
            Assert.AreEqual(0, scb.PendingCount);
        }
        finally
        {
            world.Dispose();
        }
    }

    [TestMethod]
    public void SecondImmediateFlush_IsNoOp()
    {
        // Feature: engine-node-command-buffer, Property 13 (concrete example)
        // A second immediate flush (nothing recorded in between) leaves the world unchanged.
        // Validates: Requirements 5.6, 9.4
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var parent = scb.RecordCreateNode("parent");
            var child = scb.RecordCreateNode<TaggedNode>(w => new TaggedNode(w, 7));
            scb.RecordAddChild(parent, child);

            var firstFlush = scb.Flush(world);
            Assert.IsTrue(firstFlush.Success);
            Assert.AreEqual(0, scb.PendingCount);

            // Capture an observable: the world's node count after the first flush.
            var worldCountBefore = world.Count;

            var secondFlush = scb.Flush(world);
            Assert.IsTrue(secondFlush.Success);

            // No-op: the world's contents are unchanged and the buffer stays empty.
            Assert.AreEqual(worldCountBefore, world.Count);
            Assert.AreEqual(0, scb.PendingCount);
        }
        finally
        {
            world.Dispose();
        }
    }
}
