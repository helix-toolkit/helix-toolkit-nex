using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for the single-writer recording contract: if two recording operations are attempted
/// concurrently on the same <see cref="SceneCommandBuffer"/>, the concurrent operation is
/// rejected and the buffer's recorded state is preserved.
///
/// Validates: Requirements 10.5.
///
/// <para>
/// The buffer enforces this with an <see cref="System.Threading.Interlocked.CompareExchange(ref int, int, int)"/>
/// re-entrancy guard (<c>0</c> = idle, <c>1</c> = recording) that brackets the body of every
/// public recording method. A second thread whose recording operation overlaps the guarded
/// region of the first finds the guard already set to <c>1</c>, is rejected (returns an invalid
/// handle for the create methods, or <see cref="ResultCode.InvalidState"/> for
/// the property methods), and leaves the buffer's recorded state untouched.
/// </para>
/// <para>
/// The guard is held only for the (very short, synchronous) body of a recording method and there
/// is no consumer callback during that window, so a deterministic re-entrant overlap cannot be
/// forced through the public API. These tests therefore drive two threads into the guarded region
/// concurrently with a <see cref="System.Threading.Barrier"/> and assert two things:
/// (1) the buffer never ends up corrupted — after concurrent hammering, <c>PendingCount</c>
/// reflects exactly the accepted operations and a subsequent flush succeeds and materializes
/// exactly that many nodes; and (2) under sustained contention at least one overlapping operation
/// is observably rejected.
/// </para>
/// <para>
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </para>
/// </summary>
[TestClass]
public class SceneCommandBufferConcurrentRecordingTest
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

    /// <summary>
    /// The hard invariant of the single-writer contract: under concurrent recording the buffer is
    /// never corrupted. Two barrier-synchronized threads each attempt a sequence of create
    /// operations whose guarded regions overlap; afterward the number of pending commands equals
    /// exactly the number of accepted (valid-handle) operations, and a flush succeeds and
    /// materializes exactly that many nodes — i.e. rejected operations mutated nothing.
    ///
    /// Validates: Requirements 10.5.
    /// </summary>
    [TestMethod]
    [Timeout(60000)]
    public void ConcurrentRecording_PreservesRecordedState_AndFlushesConsistently()
    {
        // Feature: engine-node-command-buffer, Requirement 10.5.
        const int phases = 5000;

        var scb = new SceneCommandBuffer();

        // Two writers; each phase both fire a create operation simultaneously so their guarded
        // regions race. We count, per thread, how many operations were ACCEPTED (returned a valid
        // handle). A rejected operation must append nothing, so the total accepted count is the
        // only thing that may have grown the buffer.
        using var barrier = new Barrier(2);

        var acceptedByThread = new int[2];

        void Writer(int id)
        {
            var accepted = 0;
            for (var phase = 0; phase < phases; phase++)
            {
                // Release both threads into the guarded region at (as near as possible) the same
                // instant so their CompareExchange calls contend.
                barrier.SignalAndWait();

                // Alternate between the base create and the custom-node create so both guarded
                // paths are exercised under contention.
                if (((phase + id) & 1) == 0)
                {
                    var handle = scb.RecordCreateNode();
                    if (handle.IsValid)
                    {
                        accepted++;
                    }
                }
                else
                {
                    var code = scb.TryRecordCreateNode<TestNode>(w => new TestNode(w), out var handle);
                    if (code == ResultCode.Ok && handle.IsValid)
                    {
                        accepted++;
                    }
                }
            }

            acceptedByThread[id] = accepted;
        }

        var t0 = new Thread(() => Writer(0));
        var t1 = new Thread(() => Writer(1));
        t0.Start();
        t1.Start();
        t0.Join();
        t1.Join();

        var totalAccepted = acceptedByThread[0] + acceptedByThread[1];

        // (1) The buffer recorded exactly the accepted operations — no rejected operation left a
        // partial/duplicate command behind, and no accepted operation was lost.
        Assert.AreEqual(
            totalAccepted,
            scb.PendingCount,
            "PendingCount must equal the number of accepted recording operations.");

        // (2) The recorded state is internally consistent: a flush onto a valid world succeeds and
        // materializes exactly one node per accepted create operation.
        var world = World.CreateWorld();
        try
        {
            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success, "Flush of the concurrently-recorded buffer must succeed.");
            Assert.AreEqual(
                totalAccepted,
                scb.MaterializedNodes.Count,
                "A consistent buffer materializes exactly one node per accepted create operation.");
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Under sustained contention, at least one overlapping recording operation is observably
    /// rejected (the second writer finds the guard held and gets an invalid handle). The
    /// experiment is repeated with fresh buffers until a rejection is observed, so the assertion
    /// is resilient to the inherent raciness of when two guarded regions actually overlap.
    ///
    /// Validates: Requirements 10.5.
    /// </summary>
    [TestMethod]
    [Timeout(60000)]
    public void ConcurrentRecording_SecondOverlappingOperation_IsRejected()
    {
        // Feature: engine-node-command-buffer, Requirement 10.5.
        const int phasesPerAttempt = 20000;
        const int maxAttempts = 20;

        long observedRejections = 0;

        for (var attempt = 0; attempt < maxAttempts && observedRejections == 0; attempt++)
        {
            var scb = new SceneCommandBuffer();
            using var barrier = new Barrier(2);

            long rejections = 0;
            long accepted = 0;

            void Writer()
            {
                for (var phase = 0; phase < phasesPerAttempt; phase++)
                {
                    barrier.SignalAndWait();
                    var handle = scb.RecordCreateNode();
                    if (handle.IsValid)
                    {
                        Interlocked.Increment(ref accepted);
                    }
                    else
                    {
                        // A default (invalid) handle here can only come from the re-entrancy guard
                        // rejecting an overlapping operation — RecordCreateNode has no other
                        // rejection path.
                        Interlocked.Increment(ref rejections);
                    }
                }
            }

            var t0 = new Thread(Writer);
            var t1 = new Thread(Writer);
            t0.Start();
            t1.Start();
            t0.Join();
            t1.Join();

            observedRejections += rejections;

            // Cross-check the invariant on every attempt: accepted operations exactly account for
            // the pending commands, so rejected operations preserved the recorded state.
            Assert.AreEqual(
                accepted,
                scb.PendingCount,
                "Even when overlapping operations are rejected, PendingCount must equal the accepted count.");
        }

        Assert.IsTrue(
            observedRejections > 0,
            "Expected at least one overlapping recording operation to be rejected under contention.");
    }
}
