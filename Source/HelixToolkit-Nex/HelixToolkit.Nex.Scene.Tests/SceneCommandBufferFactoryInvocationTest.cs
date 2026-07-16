using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

[TestClass]
public class SceneCommandBufferFactoryInvocationTest
{
    [TestMethod]
    public void Property2_RecordingNeverInvokesFactoryOrTouchesWorld_FlushInvokesEachFactoryOnceOnOwningThread()
    {
        // Feature: engine-node-command-buffer, Property 2: Recording never invokes the factory or
        // touches a World; flush invokes each factory exactly once on the owning thread.
        //
        // For any sequence of custom-node recordings using instrumented factories, no factory is
        // invoked and no World is mutated during recording; and after a successful flush onto a
        // target world, each recorded factory has been invoked exactly once, on the flush thread,
        // with that target world as its argument.
        //
        // Validates: Requirements 1.2, 1.3, 3.1, 3.2, 5.1, 10.2, 10.3

        // A program is described by the number of custom-node creation commands to record. Varying
        // the count exercises empty buffers, single recordings, and longer sequences.
        var gen = Gen.Choose(0, 30);

        Prop.ForAll(
                Arb.From(gen),
                (int n) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        // Record from a thread that differs from the (later) flush thread so the
                        // "no World access during recording" contract is exercised off the owning
                        // thread (Req 10.2). The buffer carries no thread affinity.
                        var scb = new SceneCommandBuffer();
                        var factories = new InstrumentedFactory[n];
                        var handles = new TypedDeferredNode<ProbeNode>[n];

                        var recordingThreadId = -1;
                        var recordThread = new Thread(() =>
                        {
                            recordingThreadId = Environment.CurrentManagedThreadId;
                            for (var i = 0; i < n; i++)
                            {
                                factories[i] = new InstrumentedFactory();
                                handles[i] = scb.RecordCreateNode(factories[i].Factory);
                            }
                        });
                        recordThread.Start();
                        recordThread.Join();

                        // ---- During-recording assertions: no factory invoked, no World mutated.
                        for (var i = 0; i < n; i++)
                        {
                            if (factories[i].InvocationCount != 0)
                            {
                                // A factory was invoked during recording (Req 1.2, 1.3, 10.2).
                                return false;
                            }
                        }

                        // No World instance was mutated during recording: the target world holds
                        // no entities because the factories (which create the nodes) never ran
                        // (Req 3.1, 3.2, 10.2). The recorded count is also exactly n.
                        if (world.Count != 0 || scb.PendingCount != n)
                        {
                            return false;
                        }

                        // ---- Flush onto the target world on this (owning) thread.
                        var flushThreadId = Environment.CurrentManagedThreadId;
                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        // ---- After-flush assertions: each factory invoked exactly once, on the
                        // flush thread, with the target world as its argument (Req 5.1, 10.3).
                        for (var i = 0; i < n; i++)
                        {
                            var f = factories[i];
                            if (f.InvocationCount != 1)
                            {
                                return false;
                            }

                            if (f.CapturedThreadId != flushThreadId)
                            {
                                return false;
                            }

                            if (!ReferenceEquals(f.CapturedWorld, world))
                            {
                                return false;
                            }
                        }

                        // Sanity: recording ran on a different thread than the flush thread when
                        // there was at least one recording (off-thread isolation, Req 10.2/10.3).
                        if (n > 0 && recordingThreadId == flushThreadId)
                        {
                            return false;
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

    /// <summary>
    /// An instrumented node factory that counts how many times it is invoked and captures the
    /// calling thread and the <see cref="World"/> argument of its most recent invocation.
    /// </summary>
    private sealed class InstrumentedFactory
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public int CapturedThreadId { get; private set; }

        public World? CapturedWorld { get; private set; }

        public Func<World, ProbeNode> Factory => world =>
        {
            Interlocked.Increment(ref _invocationCount);
            CapturedThreadId = Environment.CurrentManagedThreadId;
            CapturedWorld = world;
            return new ProbeNode(world);
        };
    }

    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to prove the
    /// buffer materializes arbitrary subtypes through the factory mechanism.
    /// </summary>
    private sealed class ProbeNode : Node
    {
        public ProbeNode(World world)
            : base(world)
        {
        }
    }
}
