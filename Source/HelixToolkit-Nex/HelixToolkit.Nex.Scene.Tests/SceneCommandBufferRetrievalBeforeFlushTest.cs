using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 6 (feature: engine-node-command-buffer): Retrieval before materialization
/// is rejected.
///
/// For any <see cref="TypedDeferredNode{T}"/> that has been recorded but not yet materialized by a
/// successful flush, the typed retrieval (<c>TryGetMaterializedNode&lt;T&gt;</c>) returns an error
/// result indicating the node is not ready (<c>ResultCode.NotReady</c>) and returns no node
/// (<see langword="null"/>).
///
/// Validates: Requirements 2.5
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferRetrievalBeforeFlushTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to drive the
    /// generic custom-node recording API without depending on any Engine type.
    /// </summary>
    private sealed class TestNode : Node
    {
        public TestNode(World world)
            : base(world)
        {
        }
    }

    [TestMethod]
    public void Property6_RetrievalBeforeMaterialization_IsRejected()
    {
        // Feature: engine-node-command-buffer, Property 6: Retrieval before materialization is
        // rejected.
        //
        // For any TypedDeferredNode<T> that has been recorded but not yet materialized by a
        // successful flush, the typed retrieval returns NotReady and yields no node.
        //
        // Validates: Requirements 2.5
        //
        // A program is described by a count of base-node creations and a count of custom-node
        // creations recorded in a deterministic, seed-driven interleaving. After recording the
        // prefix (but BEFORE any flush), every custom handle's typed retrieval must report
        // NotReady with a null out-node. The 'targetSeed' selects which recorded custom handle is
        // probed first to make the property exercise handles at varying recorded positions.
        var gen =
            from baseCount in Gen.Choose(0, 15)
            from customCount in Gen.Choose(1, 15)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (baseCount, customCount, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int baseCount, int customCount, int seed) t) =>
                {
                    var scb = new SceneCommandBuffer();

                    // Record an arbitrary prefix interleaving base and custom node creations in a
                    // deterministic, seed-driven order, collecting the custom handles.
                    var rng = new Random(t.seed);
                    var basesLeft = t.baseCount;
                    var customsLeft = t.customCount;
                    var handles = new List<TypedDeferredNode<TestNode>>(t.customCount);
                    while (basesLeft > 0 || customsLeft > 0)
                    {
                        var recordCustom = customsLeft > 0 && (basesLeft == 0 || rng.Next(2) == 1);
                        if (recordCustom)
                        {
                            handles.Add(scb.RecordCreateNode<TestNode>(world => new TestNode(world)));
                            customsLeft--;
                        }
                        else
                        {
                            _ = scb.RecordCreateNode();
                            basesLeft--;
                        }
                    }

                    // No flush has happened yet: nothing is materialized.
                    if (scb.MaterializedNodes.Count != 0)
                    {
                        return false;
                    }

                    // Every recorded-but-unmaterialized custom handle is rejected with NotReady and
                    // yields no node.
                    foreach (var handle in handles)
                    {
                        var code = scb.TryGetMaterializedNode(handle, out var node);
                        if (code != ResultCode.NotReady)
                        {
                            return false;
                        }

                        if (node is not null)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Retrieval_OnFreshlyRecordedHandle_ReturnsNotReadyAndNullNode()
    {
        // Feature: engine-node-command-buffer, Property 6 (concrete example).
        // A single recorded handle, before any flush, retrieves as NotReady with a null node.
        // Validates: Requirements 2.5
        var scb = new SceneCommandBuffer();

        var handle = scb.RecordCreateNode<TestNode>(world => new TestNode(world));

        var code = scb.TryGetMaterializedNode(handle, out var node);

        Assert.AreEqual(
            ResultCode.NotReady,
            code,
            "Retrieval before a successful flush must report the node is not ready.");
        Assert.IsNull(node, "Retrieval before materialization must return no node.");
    }

    [TestMethod]
    public void Retrieval_BeforeFlush_ThenAfterFlush_TransitionsFromNotReadyToOk()
    {
        // Feature: engine-node-command-buffer, Property 6 (concrete example) paired with the
        // positive transition: the same handle that reports NotReady before flush resolves to Ok
        // afterward, confirming NotReady specifically denotes "not yet materialized".
        // Validates: Requirements 2.5
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var handle = scb.RecordCreateNode<TestNode>(w => new TestNode(w));

            // Before flush: NotReady, null node.
            Assert.AreEqual(
                ResultCode.NotReady,
                scb.TryGetMaterializedNode(handle, out var before));
            Assert.IsNull(before);

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);

            // After a successful flush: Ok, non-null node.
            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode(handle, out var after));
            Assert.IsNotNull(after);
        }
        finally
        {
            world.Dispose();
        }
    }
}
