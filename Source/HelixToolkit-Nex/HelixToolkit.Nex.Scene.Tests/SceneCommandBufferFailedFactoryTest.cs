using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 12: A failed factory stops the flush, clears remaining work, and
/// preserves the applied prefix.
///
/// For any recorded program in which exactly one factory at position <c>k</c> fails during flush
/// (by returning <see langword="null"/> or by throwing), the flush stops at <c>k</c>, returns an
/// error result that identifies the failed command by its zero-based recorded position (and, for a
/// thrown exception, carries a message including the exception's message), leaves no pending
/// commands, and leaves the nodes materialized from the prefix <c>[0, k)</c> in a state equivalent
/// to direct construction of that same prefix.
///
/// Validates: Requirements 5.4, 5.5, 9.3, 9.5.
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferFailedFactoryTest
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

    /// <summary>The two ways a recorded factory can fail during flush.</summary>
    private enum FailureMode
    {
        /// <summary>The factory returns <see langword="null"/>.</summary>
        ReturnsNull,

        /// <summary>The factory throws an exception.</summary>
        Throws,
    }

    // The exception message a throwing factory raises; the failure result's Message must contain it.
    private const string ThrownMessage = "factory-blew-up-marker-7f3a";

    [TestMethod]
    public void Property12_FailedFactory_StopsFlush_ClearsRemainingWork_PreservesPrefix()
    {
        // Feature: engine-node-command-buffer, Property 12: A failed factory stops the flush,
        // clears remaining work, and preserves the applied prefix.
        // Validates: Requirements 5.4, 5.5, 9.3, 9.5
        //
        // A program is described by:
        //   - prefixNodes: base-node creations recorded before the failing factory; these all
        //     succeed and are materialized into the world (the prefix [0, k) creations).
        //   - suffixNodes: base-node creations recorded AFTER the failing factory; these must
        //     never be applied because the flush aborts at the failing command.
        //   - seed: deterministically derives the parent-child links wired among the prefix nodes
        //     so the prefix spans flat, deep, and branching shapes.
        // Both failure modes (null return and throw) are exercised for every generated program.
        var gen =
            from prefixNodes in Gen.Choose(0, 20)
            from suffixNodes in Gen.Choose(0, 10)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (prefixNodes, suffixNodes, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int prefixNodes, int suffixNodes, int seed) t) =>
                    RunCase(t.prefixNodes, t.suffixNodes, t.seed, FailureMode.ReturnsNull)
                    && RunCase(t.prefixNodes, t.suffixNodes, t.seed, FailureMode.Throws)
            )
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Records the prefix, injects a single failing factory, records an un-applied suffix, flushes,
    /// and asserts the Property 12 contract for the given <paramref name="mode"/>.
    /// </summary>
    private static bool RunCase(int prefixNodes, int suffixNodes, int seed, FailureMode mode)
    {
        var bufferWorld = World.CreateWorld();
        var directWorld = World.CreateWorld();
        try
        {
            // Derive parent assignments for the prefix nodes: parent[i] in [-1, i - 1], where -1
            // means "root". Every parent index is strictly less than its child, so the result is an
            // acyclic forest in which each node has at most one parent (the single-parent rule).
            var rng = new Random(seed);
            var parent = new int[prefixNodes];
            for (var i = 0; i < prefixNodes; i++)
            {
                parent[i] = rng.Next(-1, i);
            }

            // ---- Record the prefix into the buffer: base-node creations then parent-child links.
            var scb = new SceneCommandBuffer();
            var deferred = new DeferredNode[prefixNodes];
            for (var i = 0; i < prefixNodes; i++)
            {
                deferred[i] = scb.RecordCreateNode();
            }

            for (var i = 0; i < prefixNodes; i++)
            {
                if (parent[i] >= 0)
                {
                    if (scb.RecordAddChild(deferred[parent[i]], deferred[i]) != ResultCode.Ok)
                    {
                        return false;
                    }
                }
            }

            // The failing factory is appended at the current end of the buffer, so its zero-based
            // recorded position k equals the number of prefix commands recorded so far.
            var failedIndex = scb.PendingCount;

            // ---- Inject exactly one failing factory at position k.
            Func<World, TestNode> failingFactory = mode == FailureMode.ReturnsNull
                ? _ => null!
                : _ => throw new InvalidOperationException(ThrownMessage);
            _ = scb.RecordCreateNode(failingFactory);

            // ---- Record a suffix that must never be applied (flush aborts at the failing factory).
            for (var i = 0; i < suffixNodes; i++)
            {
                _ = scb.RecordCreateNode();
            }

            // ---- Direct construction of the same prefix [0, k) onto an independent world.
            var directNodes = new Node[prefixNodes];
            for (var i = 0; i < prefixNodes; i++)
            {
                directNodes[i] = new Node(directWorld);
            }
            for (var i = 0; i < prefixNodes; i++)
            {
                if (parent[i] >= 0)
                {
                    directNodes[parent[i]].AddChild(directNodes[i]);
                }
            }

            // ---- Flush onto the buffer world: the failing factory aborts the flush at position k.
            var result = scb.Flush(bufferWorld);

            // (1) The flush failed at the failing factory's recorded position with a descriptive
            // message. A null-returning and a throwing factory both surface as ResultCode.InvalidState.
            if (result.Success)
            {
                return false;
            }
            if (result.Code != ResultCode.InvalidState)
            {
                return false;
            }
            if (result.FailedCommandIndex != failedIndex)
            {
                return false;
            }
            if (string.IsNullOrEmpty(result.Message))
            {
                return false;
            }

            // (2) For a thrown exception the failure message includes the exception's message.
            if (mode == FailureMode.Throws && !result.Message!.Contains(ThrownMessage))
            {
                return false;
            }

            // (3) No pending commands remain: the failing command and the entire un-applied suffix
            // were cleared (Req 9.3).
            if (scb.PendingCount != 0)
            {
                return false;
            }

            // (4) The prefix [0, k) was materialized in the world equivalently to direct
            // construction: the failing factory created no node (null/throw before placement), so
            // the world holds exactly the prefix nodes, with the same level structure (Req 9.5).
            if (bufferWorld.Count != prefixNodes || directWorld.Count != prefixNodes)
            {
                return false;
            }
            if (!LevelMultisetsMatch(bufferWorld, directWorld))
            {
                return false;
            }

            return true;
        }
        finally
        {
            bufferWorld.Dispose();
            directWorld.Dispose();
        }
    }

    /// <summary>
    /// Compares two worlds by the sorted multiset of their nodes' <see cref="NodeInfo.Level"/>
    /// values, a structural proxy that does not depend on node identity.
    /// </summary>
    private static bool LevelMultisetsMatch(World a, World b)
    {
        var levelsA = NodeLevels(a);
        var levelsB = NodeLevels(b);
        if (levelsA.Count != levelsB.Count)
        {
            return false;
        }
        levelsA.Sort();
        levelsB.Sort();
        for (var i = 0; i < levelsA.Count; i++)
        {
            if (levelsA[i] != levelsB[i])
            {
                return false;
            }
        }
        return true;
    }

    private static List<int> NodeLevels(World world)
    {
        var levels = new List<int>();
        foreach (var entity in world)
        {
            if (entity.TryGet<NodeInfo>(out var info))
            {
                levels.Add(info.Level);
            }
        }
        return levels;
    }

    [TestMethod]
    public void NullReturningFactory_AtKnownPosition_FailsAtK_ClearsWork_KeepsPrefix()
    {
        // Feature: engine-node-command-buffer, Property 12: concrete example, null-returning factory.
        // Validates: Requirements 5.4, 9.3, 9.5
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var a = scb.RecordCreateNode();
            var b = scb.RecordCreateNode();
            Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(a, b));

            // The failing factory sits at index 3 (two creates + one add-child).
            var failedIndex = scb.PendingCount;
            Assert.AreEqual(3, failedIndex);
            _ = scb.RecordCreateNode<TestNode>(_ => null!);

            // A suffix that must be discarded when the flush aborts.
            _ = scb.RecordCreateNode();
            _ = scb.RecordCreateNode();

            var result = scb.Flush(world);

            Assert.IsFalse(result.Success, "A null-returning factory must fail the flush.");
            Assert.AreEqual(ResultCode.InvalidState, result.Code);
            Assert.AreEqual(failedIndex, result.FailedCommandIndex, "Failure must identify the failing command by position.");
            Assert.IsFalse(string.IsNullOrEmpty(result.Message));
            Assert.AreEqual(0, scb.PendingCount, "Remaining work (failing command + suffix) must be cleared.");
            Assert.AreEqual(2, world.Count, "The two prefix nodes must remain materialized in the world.");
        }
        finally
        {
            world.Dispose();
        }
    }

    [TestMethod]
    public void ThrowingFactory_AtKnownPosition_FailsAtK_MessageIncludesThrownMessage()
    {
        // Feature: engine-node-command-buffer, Property 12: concrete example, throwing factory.
        // Validates: Requirements 5.5, 9.3, 9.5
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();
            var a = scb.RecordCreateNode();
            _ = scb.RecordCreateNode();

            var failedIndex = scb.PendingCount;
            Assert.AreEqual(2, failedIndex);
            _ = scb.RecordCreateNode<TestNode>(_ => throw new InvalidOperationException(ThrownMessage));

            _ = scb.RecordCreateNode();

            var result = scb.Flush(world);

            Assert.IsFalse(result.Success, "A throwing factory must fail the flush.");
            Assert.AreEqual(ResultCode.InvalidState, result.Code);
            Assert.AreEqual(failedIndex, result.FailedCommandIndex);
            Assert.IsNotNull(result.Message);
            StringAssert.Contains(result.Message, ThrownMessage, "The failure message must include the thrown exception's message.");
            Assert.AreEqual(0, scb.PendingCount, "Remaining work must be cleared.");
            Assert.AreEqual(2, world.Count, "The prefix nodes must remain materialized in the world.");
            _ = a;
        }
        finally
        {
            world.Dispose();
        }
    }
}
