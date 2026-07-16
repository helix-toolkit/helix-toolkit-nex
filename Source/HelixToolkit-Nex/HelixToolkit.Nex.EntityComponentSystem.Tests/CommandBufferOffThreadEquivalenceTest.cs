using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferOffThreadEquivalenceTest
{
    // Small set of blittable test component value types referenced by the generated
    // command program. Fields are integer-valued so component-value comparison is exact.
    internal struct CompA
    {
        public int X;
    }

    internal struct CompB
    {
        public int Y;
        public long Z;
    }

    internal struct CompC
    {
        public int W;
        public int V;
    }

    // A single deferred operation in the generated program. The program is built once on
    // the test thread so the off-thread recording path and the direct-application path
    // replay the exact same sequence of operations and values.
    private enum OpKind
    {
        Create,
        Set,
        Remove,
    }

    private readonly struct Op
    {
        public OpKind Kind { get; init; }
        public int Slot { get; init; }       // index into the created-entity lists
        public int CompType { get; init; }   // 0 = CompA, 1 = CompB, 2 = CompC
        public CompA A { get; init; }
        public CompB B { get; init; }
        public CompC C { get; init; }
    }

    [TestMethod]
    public void Property15_OffThreadRecording_IsEquivalentToOnThreadRecording()
    {
        // Feature: ecs-command-buffer, Property 15: Off-thread recording is equivalent to on-thread recording
        // For any valid sequence of operations recorded on a recording thread different from
        // the flush thread, flushing onto the target world produces the same world state as
        // recording the same sequence on the flush thread (and the same state as direct
        // application).
        // Validates: Requirements 10.1

        // A program is described by a length and a seed; the seed deterministically derives
        // a sequence of create/set/remove operations that reference only entities the program
        // has already created. The SAME derived program is applied two ways:
        //   - off-thread buffer path: recorded into a CommandBuffer on a DIFFERENT thread than
        //     the flush thread, then flushed onto world A on the test (owning) thread;
        //   - direct path: applied directly onto world B on the test thread, in order.
        // Because recording stores commands deterministically, the thread on which recording
        // happens must not affect the flushed result. Edge cases (empty programs, removes of
        // never-set components, repeated sets of the same type) arise naturally from the
        // random op stream.
        var gen =
            from n in Gen.Choose(0, 120)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    // Build the program deterministically on the test thread.
                    var program = BuildProgram(t.n, t.seed);

                    var bufferWorld = World.CreateWorld();
                    var directWorld = World.CreateWorld();
                    try
                    {
                        var cb = new CommandBuffer();

                        // Parallel mapping: the i-th created entity in each path corresponds.
                        var deferred = new List<DeferredEntity>();
                        var direct = new List<Entity>();

                        // --- Off-thread recording -------------------------------------
                        // Record the entire program into the command buffer on a thread
                        // that is different from the flush (test) thread. We wait for the
                        // recording thread to finish before flushing, so recording and
                        // flushing never overlap (single-writer recording, owning-thread
                        // flush).
                        var flushThreadId = Environment.CurrentManagedThreadId;
                        var recordThreadId = flushThreadId;

                        var recordingThread = new Thread(() =>
                        {
                            recordThreadId = Environment.CurrentManagedThreadId;
                            foreach (var op in program)
                            {
                                switch (op.Kind)
                                {
                                    case OpKind.Create:
                                        deferred.Add(cb.RecordCreateEntity());
                                        break;
                                    case OpKind.Set:
                                        switch (op.CompType)
                                        {
                                            case 0:
                                                cb.RecordSet(deferred[op.Slot], op.A);
                                                break;
                                            case 1:
                                                cb.RecordSet(deferred[op.Slot], op.B);
                                                break;
                                            default:
                                                cb.RecordSet(deferred[op.Slot], op.C);
                                                break;
                                        }
                                        break;
                                    default: // Remove
                                        switch (op.CompType)
                                        {
                                            case 0:
                                                cb.RecordRemove<CompA>(deferred[op.Slot]);
                                                break;
                                            case 1:
                                                cb.RecordRemove<CompB>(deferred[op.Slot]);
                                                break;
                                            default:
                                                cb.RecordRemove<CompC>(deferred[op.Slot]);
                                                break;
                                        }
                                        break;
                                }
                            }
                        });
                        recordingThread.Start();
                        recordingThread.Join();

                        // The recording must actually have run on a different thread for the
                        // off-thread guarantee to be meaningful (Requirement 10.1).
                        if (recordThreadId == flushThreadId)
                        {
                            return false;
                        }

                        // --- Direct application on the test thread --------------------
                        foreach (var op in program)
                        {
                            switch (op.Kind)
                            {
                                case OpKind.Create:
                                    direct.Add(directWorld.CreateEntity());
                                    break;
                                case OpKind.Set:
                                    switch (op.CompType)
                                    {
                                        case 0:
                                            direct[op.Slot].Set(op.A);
                                            break;
                                        case 1:
                                            direct[op.Slot].Set(op.B);
                                            break;
                                        default:
                                            direct[op.Slot].Set(op.C);
                                            break;
                                    }
                                    break;
                                default: // Remove
                                    switch (op.CompType)
                                    {
                                        case 0:
                                            direct[op.Slot].Remove<CompA>();
                                            break;
                                        case 1:
                                            direct[op.Slot].Remove<CompB>();
                                            break;
                                        default:
                                            direct[op.Slot].Remove<CompC>();
                                            break;
                                    }
                                    break;
                            }
                        }

                        // Flush the off-thread-recorded program onto world A on the test thread.
                        var flush = cb.Flush(bufferWorld);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        // Same entity count as the direct application.
                        if (bufferWorld.Count != directWorld.Count)
                        {
                            return false;
                        }

                        // For each created entity (by creation order), the buffer-path entity
                        // exists with the same component presence and values as the direct
                        // entity. Two fresh worlds assign sequential ids in creation order, so
                        // the i-th created entity has the same local id in both worlds.
                        for (var i = 0; i < direct.Count; i++)
                        {
                            var directEntity = direct[i];
                            var bufferEntity = bufferWorld.GetEntity(directEntity.Id);

                            if (!bufferWorld.HasEntity(bufferEntity))
                            {
                                return false;
                            }

                            if (!ComponentsMatch<CompA>(bufferEntity, directEntity, CompAEquals))
                            {
                                return false;
                            }
                            if (!ComponentsMatch<CompB>(bufferEntity, directEntity, CompBEquals))
                            {
                                return false;
                            }
                            if (!ComponentsMatch<CompC>(bufferEntity, directEntity, CompCEquals))
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        bufferWorld.Dispose();
                        directWorld.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // Derives a deterministic program from (n, seed). Each op references only entities the
    // program has already created; a create is forced whenever there is no entity yet.
    private static List<Op> BuildProgram(int n, int seed)
    {
        var program = new List<Op>(n);
        var created = 0;
        var rng = new Random(seed);

        for (var i = 0; i < n; i++)
        {
            var kind = rng.Next(0, 3);

            if (kind == 0 || created == 0)
            {
                program.Add(new Op { Kind = OpKind.Create });
                created++;
                continue;
            }

            var slot = rng.Next(created);
            var compType = rng.Next(0, 3);

            if (kind == 1)
            {
                var op = compType switch
                {
                    0 => new Op
                    {
                        Kind = OpKind.Set,
                        Slot = slot,
                        CompType = 0,
                        A = new CompA { X = rng.Next(-100000, 100000) },
                    },
                    1 => new Op
                    {
                        Kind = OpKind.Set,
                        Slot = slot,
                        CompType = 1,
                        B = new CompB { Y = rng.Next(-100000, 100000), Z = rng.Next(-100000, 100000) },
                    },
                    _ => new Op
                    {
                        Kind = OpKind.Set,
                        Slot = slot,
                        CompType = 2,
                        C = new CompC { W = rng.Next(-100000, 100000), V = rng.Next(-100000, 100000) },
                    },
                };
                program.Add(op);
            }
            else
            {
                program.Add(new Op { Kind = OpKind.Remove, Slot = slot, CompType = compType });
            }
        }

        return program;
    }

    // Compares presence and (when present) value of component T on two entities.
    private static bool ComponentsMatch<T>(Entity bufferEntity, Entity directEntity, Func<T, T, bool> valueEquals)
    {
        var hasBuffer = bufferEntity.Has<T>();
        var hasDirect = directEntity.Has<T>();
        if (hasBuffer != hasDirect)
        {
            return false;
        }
        if (!hasBuffer)
        {
            return true;
        }
        return valueEquals(bufferEntity.Get<T>(), directEntity.Get<T>());
    }

    private static bool CompAEquals(CompA a, CompA b) => a.X == b.X;

    private static bool CompBEquals(CompB a, CompB b) => a.Y == b.Y && a.Z == b.Z;

    private static bool CompCEquals(CompC a, CompC b) => a.W == b.W && a.V == b.V;
}
