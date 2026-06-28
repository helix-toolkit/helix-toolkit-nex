using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferForeignHandleRejectionTest
{
    /// <summary>
    /// Simple blittable test component used to exercise set-component recording.
    /// </summary>
    internal struct Probe
    {
        public int X;
        public long Y;
    }

    [TestMethod]
    public void Property3_ForeignDeferredHandles_AreRejected()
    {
        // Feature: ecs-command-buffer, Property 3: Foreign deferred handles are rejected
        // For any two distinct command buffers A and B and any DeferredEntity produced by B,
        // recording a set-component or remove-component command on A that references B's
        // handle returns an error result and leaves A's PendingCount unchanged.
        // Validates: Requirements 2.3
        var gen =
            from aCreates in Gen.Choose(0, 20)
            from bCreates in Gen.Choose(1, 20)   // B must create at least one entity to yield a handle
            from bPick in Gen.Choose(0, 19)      // which of B's handles to use as the foreign handle
            from value in Gen.Choose(-100000, 100000)
            select (aCreates, bCreates, bPick, value);

        Prop.ForAll(
                Arb.From(gen),
                ((int aCreates, int bCreates, int bPick, int value) t) =>
                {
                    var a = new CommandBuffer();
                    var b = new CommandBuffer();

                    // Record a random number of commands into A first.
                    for (var i = 0; i < t.aCreates; i++)
                    {
                        a.RecordCreateEntity();
                    }

                    // Produce one or more DeferredEntity handles from B, and select one
                    // of them as the foreign handle to use against A.
                    DeferredEntity foreign = default;
                    var pick = t.bPick % t.bCreates;
                    for (var i = 0; i < t.bCreates; i++)
                    {
                        var h = b.RecordCreateEntity();
                        if (i == pick)
                        {
                            foreign = h;
                        }
                    }

                    // Capture A's pending count before the rejected calls.
                    var before = a.PendingCount;

                    var setResult = a.RecordSet(foreign, new Probe { X = t.value, Y = t.value });
                    var removeResult = a.RecordRemove<Probe>(foreign);

                    // Both record operations must be rejected with InvalidState, and A's
                    // pending count must be unchanged by the rejected calls.
                    return setResult == ResultCode.InvalidState
                        && removeResult == ResultCode.InvalidState
                        && a.PendingCount == before;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
