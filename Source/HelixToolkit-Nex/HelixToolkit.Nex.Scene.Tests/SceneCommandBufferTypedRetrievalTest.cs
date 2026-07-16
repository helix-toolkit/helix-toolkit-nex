using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 5 (feature: engine-node-command-buffer): Typed retrieval after flush
/// returns the same instance assignable to T.
///
/// For any <see cref="TypedDeferredNode{T}"/> recorded with a factory that returns a node of
/// subtype <c>T</c>, after a successful flush the typed retrieval
/// (<c>TryGetMaterializedNode&lt;T&gt;</c>) returns the exact instance the factory produced, typed
/// as <c>T</c> with no cast required, and repeated retrievals return the reference-equal same
/// instance.
///
/// Validates: Requirements 2.2, 2.3, 2.4, 5.2
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferTypedRetrievalTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer. Each instance carries a
    /// unique tag so the produced and retrieved instances can be matched by identity in addition
    /// to reference equality.
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
    public void Property5_TypedRetrievalAfterFlush_ReturnsSameInstanceAssignableToT()
    {
        // Feature: engine-node-command-buffer, Property 5
        // For any TypedDeferredNode<T> recorded with a factory that returns a node of subtype T,
        // after a successful flush the typed retrieval returns the exact instance the factory
        // produced, typed as T with no cast required, and repeated retrievals return the
        // reference-equal same instance.
        // Validates: Requirements 2.2, 2.3, 2.4, 5.2

        // A program is described by a custom-node count n (>= 1) and a seed that deterministically
        // chooses how many base node-creation commands to interleave before each custom one. The
        // interleaving exercises retrieval across a mixed buffer where custom slots are not
        // contiguous.
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

                        // Each factory records the instance it produced during flush so we can
                        // assert the retrieval returns that exact reference.
                        var produced = new TaggedNode[t.n];
                        var handles = new TypedDeferredNode<TaggedNode>[t.n];

                        for (var i = 0; i < t.n; i++)
                        {
                            var interleave = rng.Next(0, 3);
                            for (var b = 0; b < interleave; b++)
                            {
                                scb.RecordCreateNode();
                            }

                            var tag = i;
                            var slot = i;
                            var code = scb.TryRecordCreateNode<TaggedNode>(
                                w =>
                                {
                                    var node = new TaggedNode(w, tag);
                                    produced[slot] = node;
                                    return node;
                                },
                                out var handle);

                            if (code != ResultCode.Ok)
                            {
                                return false;
                            }

                            handles[i] = handle;
                        }

                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        for (var i = 0; i < t.n; i++)
                        {
                            // (a) Retrieval succeeds and returns the node typed as T with no cast.
                            var code = scb.TryGetMaterializedNode<TaggedNode>(handles[i], out var node);
                            if (code != ResultCode.Ok)
                            {
                                return false;
                            }

                            // (b) The returned instance is exactly the one the factory produced.
                            if (!ReferenceEquals(node, produced[i]))
                            {
                                return false;
                            }

                            // (c) It is assignable to T and carries the recorded tag.
                            if (node is null || node.Tag != i)
                            {
                                return false;
                            }

                            // (d) Repeated retrievals return the reference-equal same instance.
                            var code2 = scb.TryGetMaterializedNode<TaggedNode>(handles[i], out var node2);
                            if (code2 != ResultCode.Ok || !ReferenceEquals(node, node2))
                            {
                                return false;
                            }
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
    public void TypedRetrieval_ReturnsExactFactoryInstance()
    {
        // Feature: engine-node-command-buffer, Property 5 (concrete example)
        // The retrieved node is the exact instance produced by the factory, typed as T.
        // Validates: Requirements 2.2, 2.3, 5.2
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            TaggedNode? created = null;

            var handle = scb.RecordCreateNode<TaggedNode>(w =>
            {
                created = new TaggedNode(w, 42);
                return created;
            });

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);

            var code = scb.TryGetMaterializedNode<TaggedNode>(handle, out var node);
            Assert.AreEqual(ResultCode.Ok, code);
            // No cast needed: 'node' is statically typed as TaggedNode.
            Assert.IsNotNull(node);
            Assert.AreSame(created, node);
            Assert.AreEqual(42, node.Tag);
        }
        finally
        {
            world.Dispose();
        }
    }

    [TestMethod]
    public void TypedRetrieval_RepeatedCalls_ReturnSameInstance()
    {
        // Feature: engine-node-command-buffer, Property 5 (concrete example)
        // Repeated retrievals for the same handle return the reference-equal same instance.
        // Validates: Requirements 2.4
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var handle = scb.RecordCreateNode<TaggedNode>(w => new TaggedNode(w, 7));

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);

            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode<TaggedNode>(handle, out var first));
            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode<TaggedNode>(handle, out var second));

            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
        }
        finally
        {
            world.Dispose();
        }
    }
}
