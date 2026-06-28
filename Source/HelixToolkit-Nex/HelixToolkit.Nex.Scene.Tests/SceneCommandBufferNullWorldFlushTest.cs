using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 14 (feature: engine-node-command-buffer): Flushing a null or disposed
/// world is rejected and retains pending commands.
///
/// For any recorded buffer, flushing onto a null or disposed world returns an error result
/// (<c>ResultCode.WorldNotValid</c>), performs no mutation observable in any world, and retains
/// all pending commands so a subsequent flush onto a valid world succeeds and materializes the
/// recorded program.
///
/// Validates: Requirements 9.2
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferNullWorldFlushTest
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
    public void Property14_FlushNullWorld_IsRejected_RetainsPending_ThenValidFlushSucceeds()
    {
        // Feature: engine-node-command-buffer, Property 14
        // Flushing onto a NULL world returns WorldNotValid, materializes nothing, retains all
        // pending commands, and a subsequent flush onto a fresh valid world succeeds and
        // materializes the recorded program.
        // Validates: Requirements 9.2
        RunRejectedFlushProperty(useDisposedWorld: false);
    }

    [TestMethod]
    public void Property14_FlushDisposedWorld_IsRejected_RetainsPending_ThenValidFlushSucceeds()
    {
        // Feature: engine-node-command-buffer, Property 14
        // Flushing onto a DISPOSED world (whose Id has been reset to 0 via Dispose) returns
        // WorldNotValid, materializes nothing, retains all pending commands, and a subsequent
        // flush onto a fresh valid world succeeds and materializes the recorded program.
        // Validates: Requirements 9.2
        RunRejectedFlushProperty(useDisposedWorld: true);
    }

    /// <summary>
    /// Drives Property 14 over arbitrary recorded programs. A program is described by a count of
    /// base-node creations, a count of custom-node creations recorded in a deterministic,
    /// seed-driven interleaving, plus a seed-derived parent chain that adds parent/child
    /// relationships (so the retained program covers create + add-child commands, not just
    /// creation). The <paramref name="useDisposedWorld"/> flag selects which invalid-world variant
    /// is exercised: a <see langword="null"/> world, or a real world that has been disposed
    /// (Id reset to 0).
    /// </summary>
    private static void RunRejectedFlushProperty(bool useDisposedWorld)
    {
        var gen =
            from baseCount in Gen.Choose(0, 12)
            from customCount in Gen.Choose(0, 12)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (baseCount, customCount, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int baseCount, int customCount, int seed) t) =>
                {
                    var validWorld = World.CreateWorld();

                    // A disposed world used as the invalid target. Created up front and disposed so
                    // its Id is reset to 0, matching the disposed-world detection in Flush.
                    World? disposedWorld = null;
                    if (useDisposedWorld)
                    {
                        disposedWorld = World.CreateWorld();
                        disposedWorld.Dispose();
                    }

                    try
                    {
                        var scb = new SceneCommandBuffer();
                        var rng = new Random(t.seed);

                        // Record an arbitrary interleaving of base and custom node creations,
                        // tracking each created handle (as an untyped DeferredNode) in creation
                        // order, and collecting the custom handles for typed-retrieval checks.
                        var basesLeft = t.baseCount;
                        var customsLeft = t.customCount;
                        var allHandles = new List<DeferredNode>(t.baseCount + t.customCount);
                        var customHandles = new List<TypedDeferredNode<TestNode>>(t.customCount);

                        while (basesLeft > 0 || customsLeft > 0)
                        {
                            var recordCustom = customsLeft > 0 && (basesLeft == 0 || rng.Next(2) == 1);
                            if (recordCustom)
                            {
                                var handle = scb.RecordCreateNode<TestNode>(world => new TestNode(world));
                                customHandles.Add(handle);
                                allHandles.Add(handle);
                                customsLeft--;
                            }
                            else
                            {
                                allHandles.Add(scb.RecordCreateNode());
                                basesLeft--;
                            }
                        }

                        // Add a seed-derived parent chain so the retained program also covers
                        // add-child commands. parent[i] in [-1, i - 1]; -1 means "root". Strictly
                        // increasing child index guarantees an acyclic single-parent forest.
                        for (var i = 0; i < allHandles.Count; i++)
                        {
                            var p = rng.Next(-1, i);
                            if (p >= 0)
                            {
                                var code = scb.RecordAddChild(allHandles[p], allHandles[i]);
                                if (code != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }
                        }

                        var pendingBefore = scb.PendingCount;

                        // ---- Rejected flush onto the invalid (null or disposed) world. ----
                        var rejected = useDisposedWorld
                            ? scb.Flush(disposedWorld!)
                            : scb.Flush(null!);

                        // (a) The result is a failure with WorldNotValid.
                        if (rejected.Success)
                        {
                            return false;
                        }
                        if (rejected.Code != ResultCode.WorldNotValid)
                        {
                            return false;
                        }

                        // (b) No mutation is observable: nothing materialized, and every custom
                        // handle still reports NotReady.
                        if (scb.MaterializedNodes.Count != 0)
                        {
                            return false;
                        }
                        foreach (var h in customHandles)
                        {
                            if (scb.TryGetMaterializedNode(h, out var n) != ResultCode.NotReady
                                || n is not null)
                            {
                                return false;
                            }
                        }

                        // (c) All pending commands are retained (count unchanged).
                        if (scb.PendingCount != pendingBefore)
                        {
                            return false;
                        }

                        // ---- A subsequent flush onto a fresh valid world succeeds. ----
                        var ok = scb.Flush(validWorld);
                        if (!ok.Success)
                        {
                            return false;
                        }

                        // The recorded program materialized: buffer is empty and every created
                        // node resolved.
                        if (scb.PendingCount != 0)
                        {
                            return false;
                        }
                        if (scb.MaterializedNodes.Count != allHandles.Count)
                        {
                            return false;
                        }
                        foreach (var h in customHandles)
                        {
                            if (scb.TryGetMaterializedNode(h, out var node) != ResultCode.Ok
                                || node is null)
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        validWorld.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void FlushNullWorld_ReturnsWorldNotValid_AndRetainsPending()
    {
        // Feature: engine-node-command-buffer, Property 14 (concrete example - null world).
        // Validates: Requirements 9.2
        var scb = new SceneCommandBuffer();
        var custom = scb.RecordCreateNode<TestNode>(w => new TestNode(w));
        _ = scb.RecordCreateNode();
        var pendingBefore = scb.PendingCount;

        var result = scb.Flush(null!);

        Assert.IsFalse(result.Success, "Flushing a null world must fail.");
        Assert.AreEqual(
            ResultCode.WorldNotValid,
            result.Code,
            "Flushing a null world must report WorldNotValid.");
        Assert.AreEqual(pendingBefore, scb.PendingCount, "Pending commands must be retained.");
        Assert.AreEqual(0, scb.MaterializedNodes.Count, "No node may be materialized by a rejected flush.");
        Assert.AreEqual(
            ResultCode.NotReady,
            scb.TryGetMaterializedNode(custom, out var node),
            "The custom handle must remain unmaterialized after a rejected flush.");
        Assert.IsNull(node);

        // A subsequent flush onto a fresh valid world succeeds and materializes the program.
        var world = World.CreateWorld();
        try
        {
            var ok = scb.Flush(world);
            Assert.IsTrue(ok.Success, "Flush onto a valid world after a rejected flush must succeed.");
            Assert.AreEqual(0, scb.PendingCount, "A successful flush must empty the buffer.");
            Assert.AreEqual(2, scb.MaterializedNodes.Count, "Both recorded nodes must be materialized.");
            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode(custom, out var materialized));
            Assert.IsNotNull(materialized);
        }
        finally
        {
            world.Dispose();
        }
    }

    [TestMethod]
    public void FlushDisposedWorld_ReturnsWorldNotValid_AndRetainsPending()
    {
        // Feature: engine-node-command-buffer, Property 14 (concrete example - disposed world).
        // A World whose Id has been reset to 0 via Dispose is treated as invalid.
        // Validates: Requirements 9.2
        var disposed = World.CreateWorld();
        disposed.Dispose();

        var scb = new SceneCommandBuffer();
        var custom = scb.RecordCreateNode<TestNode>(w => new TestNode(w));
        _ = scb.RecordCreateNode();
        var pendingBefore = scb.PendingCount;

        var result = scb.Flush(disposed);

        Assert.IsFalse(result.Success, "Flushing a disposed world must fail.");
        Assert.AreEqual(
            ResultCode.WorldNotValid,
            result.Code,
            "Flushing a disposed world must report WorldNotValid.");
        Assert.AreEqual(pendingBefore, scb.PendingCount, "Pending commands must be retained.");
        Assert.AreEqual(0, scb.MaterializedNodes.Count, "No node may be materialized by a rejected flush.");
        Assert.AreEqual(
            ResultCode.NotReady,
            scb.TryGetMaterializedNode(custom, out var node),
            "The custom handle must remain unmaterialized after a rejected flush.");
        Assert.IsNull(node);

        // A subsequent flush onto a fresh valid world succeeds and materializes the program.
        var world = World.CreateWorld();
        try
        {
            var ok = scb.Flush(world);
            Assert.IsTrue(ok.Success, "Flush onto a valid world after a rejected flush must succeed.");
            Assert.AreEqual(0, scb.PendingCount, "A successful flush must empty the buffer.");
            Assert.AreEqual(2, scb.MaterializedNodes.Count, "Both recorded nodes must be materialized.");
            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode(custom, out var materialized));
            Assert.IsNotNull(materialized);
        }
        finally
        {
            world.Dispose();
        }
    }
}
