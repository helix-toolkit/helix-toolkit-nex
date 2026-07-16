using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 3: A null factory is rejected and leaves the buffer unchanged.
///
/// For any buffer holding an arbitrary recorded prefix, recording a custom-node command with a
/// null factory returns an error result, appends no command (<c>PendingCount</c> is unchanged),
/// and yields an invalid handle.
///
/// Validates: Requirements 1.5, 3.5, 4.5.
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferNullFactoryTest
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
    public void Property3_NullFactory_IsRejected_BufferUnchanged()
    {
        // Feature: engine-node-command-buffer, Property 3: A null factory is rejected and leaves
        // the buffer unchanged.
        // Validates: Requirements 1.5, 3.5, 4.5
        //
        // The arbitrary recorded prefix is described by a count of base-node creations and a count
        // of custom-node creations recorded (in interleaved order). After recording the prefix we
        // capture PendingCount, attempt to record a custom node with a null factory, and assert the
        // rejection contract: Invalid result, an invalid handle, and an unchanged PendingCount.
        // Recording one more valid command afterward must still succeed, proving the buffer state
        // was left intact.
        var gen =
            from baseCount in Gen.Choose(0, 15)
            from customCount in Gen.Choose(0, 15)
                // 'seed' drives a deterministic interleaving of base vs custom creations.
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (baseCount, customCount, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int baseCount, int customCount, int seed) t) =>
                {
                    var scb = new SceneCommandBuffer();

                    // Record an arbitrary prefix interleaving base and custom node creations in a
                    // deterministic, seed-driven order.
                    var rng = new Random(t.seed);
                    var basesLeft = t.baseCount;
                    var customsLeft = t.customCount;
                    while (basesLeft > 0 || customsLeft > 0)
                    {
                        var recordCustom = customsLeft > 0 && (basesLeft == 0 || rng.Next(2) == 1);
                        if (recordCustom)
                        {
                            _ = scb.RecordCreateNode<TestNode>(world => new TestNode(world));
                            customsLeft--;
                        }
                        else
                        {
                            _ = scb.RecordCreateNode();
                            basesLeft--;
                        }
                    }

                    var pendingBefore = scb.PendingCount;

                    // Record a custom-node command with a null factory.
                    Func<World, TestNode>? nullFactory = null;
                    var code = scb.TryRecordCreateNode(nullFactory!, out var handle);

                    // (1) Error result indicating the null factory.
                    if (code != ResultCode.InvalidState)
                    {
                        return false;
                    }

                    // (2) Invalid handle.
                    if (handle.IsValid)
                    {
                        return false;
                    }

                    // (3) No command appended: PendingCount unchanged.
                    if (scb.PendingCount != pendingBefore)
                    {
                        return false;
                    }

                    // The convenience overload behaves identically and likewise appends nothing.
                    var convenienceHandle = scb.RecordCreateNode<TestNode>(null!);
                    if (convenienceHandle.IsValid || scb.PendingCount != pendingBefore)
                    {
                        return false;
                    }

                    // The buffer remains usable: a subsequent valid recording still succeeds and
                    // appends exactly one command.
                    var ok = scb.TryRecordCreateNode<TestNode>(world => new TestNode(world), out var validHandle);
                    return ok == ResultCode.Ok
                        && validHandle.IsValid
                        && scb.PendingCount == pendingBefore + 1;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void NullFactory_OnEmptyBuffer_IsRejected_NoCommandAppended()
    {
        // Feature: engine-node-command-buffer, Property 3: concrete example on an empty buffer.
        // Validates: Requirements 1.5, 3.5, 4.5
        var scb = new SceneCommandBuffer();
        Assert.AreEqual(0, scb.PendingCount);

        Func<World, TestNode>? nullFactory = null;
        var code = scb.TryRecordCreateNode(nullFactory!, out var handle);

        Assert.AreEqual(ResultCode.InvalidState, code, "Null factory must be rejected.");
        Assert.IsFalse(handle.IsValid, "A rejected recording must yield an invalid handle.");
        Assert.AreEqual(0, scb.PendingCount, "A rejected recording must not append a command.");
    }

    [TestMethod]
    public void NullFactory_AfterRecordedPrefix_LeavesPriorCommandsUnchanged()
    {
        // Feature: engine-node-command-buffer, Property 3: concrete example with a recorded prefix.
        // Validates: Requirements 1.5, 3.5, 4.5
        var scb = new SceneCommandBuffer();
        var a = scb.RecordCreateNode();
        var b = scb.RecordCreateNode<TestNode>(world => new TestNode(world));
        Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(a, b));

        var pendingBefore = scb.PendingCount;
        Assert.AreEqual(3, pendingBefore);

        // Null factory through both the Try and the convenience overload.
        Assert.AreEqual(
            ResultCode.InvalidState,
            scb.TryRecordCreateNode<TestNode>(null!, out var handle));
        Assert.IsFalse(handle.IsValid);
        Assert.AreEqual(pendingBefore, scb.PendingCount, "Null factory must not change pending commands.");

        var convenience = scb.RecordCreateNode<TestNode>(null!);
        Assert.IsFalse(convenience.IsValid);
        Assert.AreEqual(pendingBefore, scb.PendingCount, "Null factory must not change pending commands.");

        // The prior prefix is intact: a flush materializes exactly the recorded hierarchy.
        var world = World.CreateWorld();
        try
        {
            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success, "Flush should succeed with the prior prefix unchanged.");
            Assert.AreSame(scb.MaterializedNodes[a], scb.MaterializedNodes[b].Parent);
        }
        finally
        {
            world.Dispose();
        }
    }
}
