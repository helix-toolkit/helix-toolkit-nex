using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferFailedCommandHandlingTest
{
    // Small set of blittable test component value types referenced by the generated
    // prefix/suffix programs. Fields are integer-valued so comparison is exact.
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

    /// <summary>
    /// A test-only command whose <see cref="Apply"/> always reports a fixed non-Ok
    /// <see cref="ResultCode"/> without touching the resolved-entity table. Injecting one
    /// of these into a command buffer deterministically forces exactly one mid-flush
    /// failure at a known position, which is the construction Property 10 requires.
    /// </summary>
    internal sealed class FailingCommand : ICommand
    {
        private readonly ResultCode _code;

        internal FailingCommand(ResultCode code) => _code = code;

        public ResultCode Apply(World world, Entity[] resolved) => _code;

        public string Describe() => $"{nameof(FailingCommand)}({_code})";
    }

    [TestMethod]
    public void Property10_FailedCommand_ClearsRemainingWorkAndReportsItself()
    {
        // Feature: ecs-command-buffer, Property 10: A failed command clears remaining work and reports itself
        // For any recorded sequence in which exactly one command is constructed to fail
        // during flush, the flush returns an error result that identifies the failed command
        // by position, leaves no pending commands in the buffer, and leaves the successfully
        // applied prefix equivalent to direct application of that same prefix.
        // Validates: Requirements 4.6, 5.2

        // Each case is described by:
        //   - prefixLen: number of successful create/set/remove operations recorded before
        //     the failing command (and applied directly onto the model world);
        //   - suffixLen: number of additional successful operations recorded AFTER the
        //     failing command; these must never be applied because flush aborts at the
        //     failing command;
        //   - failCodeSel: selects which non-Ok ResultCode the injected command reports;
        //   - seed: deterministically derives the prefix/suffix operation streams.
        var gen =
            from prefixLen in Gen.Choose(0, 60)
            from suffixLen in Gen.Choose(0, 20)
            from failCodeSel in Gen.Choose(0, 3)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (prefixLen, suffixLen, failCodeSel, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int prefixLen, int suffixLen, int failCodeSel, int seed) t) =>
                {
                    var bufferWorld = World.CreateWorld();
                    var directWorld = World.CreateWorld();
                    try
                    {
                        var cb = new CommandBuffer();

                        // The i-th created entity in each path corresponds.
                        var deferred = new List<DeferredEntity>();
                        var direct = new List<Entity>();
                        var rng = new Random(t.seed);

                        // --- Prefix: record into the buffer AND apply directly onto the
                        // model world. These commands all succeed during flush. ---
                        for (var i = 0; i < t.prefixLen; i++)
                        {
                            RecordAndApply(cb, deferred, direct, directWorld, rng);
                        }

                        // The failing command is appended at the current end of the list, so
                        // its index equals the number of prefix commands recorded so far.
                        var failedIndex = cb.PendingCount;

                        // --- Inject exactly one failing command. ---
                        var failCode = SelectFailCode(t.failCodeSel);
                        cb.AppendCommandForTest(new FailingCommand(failCode));

                        // --- Suffix: record into the buffer ONLY (not the model world). These
                        // commands come after the failing command and must never be applied. ---
                        for (var i = 0; i < t.suffixLen; i++)
                        {
                            RecordOnly(cb, deferred, rng);
                        }

                        // Flush onto the buffer world. The failing command aborts the flush.
                        var result = cb.Flush(bufferWorld);

                        // The flush must report failure with the injected command's code.
                        if (result.Success)
                        {
                            return false;
                        }
                        if (result.Code != failCode)
                        {
                            return false;
                        }

                        // The failed command is identified by its recorded position.
                        if (result.FailedCommandIndex != failedIndex)
                        {
                            return false;
                        }
                        if (string.IsNullOrEmpty(result.Message))
                        {
                            return false;
                        }

                        // No pending commands remain: the buffer cleared all remaining work
                        // (the failing command plus the entire un-applied suffix).
                        if (cb.PendingCount != 0)
                        {
                            return false;
                        }

                        // The successfully applied prefix is equivalent to direct application
                        // of the same prefix: same entity count and same per-entity components.
                        if (bufferWorld.Count != directWorld.Count)
                        {
                            return false;
                        }

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

    // Maps a selector to one of the non-Ok ResultCodes. NotFound is intentionally excluded
    // because RemoveComponentCommand treats it as a no-op success; every code here is a
    // genuine flush-aborting failure.
    private static ResultCode SelectFailCode(int sel) => sel switch
    {
        0 => ResultCode.Unknown,
        1 => ResultCode.InvalidState,
        2 => ResultCode.NotThisWorld,
        _ => ResultCode.NotTheSameWorld,
    };

    // Records one operation into the buffer and applies the identical operation directly
    // onto the model world, keeping the deferred/direct entity lists in parallel.
    private static void RecordAndApply(
        CommandBuffer cb,
        List<DeferredEntity> deferred,
        List<Entity> direct,
        World directWorld,
        Random rng)
    {
        var kind = rng.Next(0, 3);

        // Force a create when there is no entity to reference yet.
        if (kind == 0 || deferred.Count == 0)
        {
            deferred.Add(cb.RecordCreateEntity());
            direct.Add(directWorld.CreateEntity());
            return;
        }

        var slot = rng.Next(deferred.Count);
        var deferredTarget = deferred[slot];
        var directTarget = direct[slot];
        var compType = rng.Next(0, 3);

        if (kind == 1)
        {
            switch (compType)
            {
                case 0:
                {
                    var v = new CompA { X = rng.Next(-100000, 100000) };
                    cb.RecordSet(deferredTarget, v);
                    directTarget.Set(v);
                    break;
                }
                case 1:
                {
                    var v = new CompB { Y = rng.Next(-100000, 100000), Z = rng.Next(-100000, 100000) };
                    cb.RecordSet(deferredTarget, v);
                    directTarget.Set(v);
                    break;
                }
                default:
                {
                    var v = new CompC { W = rng.Next(-100000, 100000), V = rng.Next(-100000, 100000) };
                    cb.RecordSet(deferredTarget, v);
                    directTarget.Set(v);
                    break;
                }
            }
        }
        else
        {
            switch (compType)
            {
                case 0:
                    cb.RecordRemove<CompA>(deferredTarget);
                    directTarget.Remove<CompA>();
                    break;
                case 1:
                    cb.RecordRemove<CompB>(deferredTarget);
                    directTarget.Remove<CompB>();
                    break;
                default:
                    cb.RecordRemove<CompC>(deferredTarget);
                    directTarget.Remove<CompC>();
                    break;
            }
        }
    }

    // Records one operation into the buffer ONLY (no direct application). Used for the
    // suffix that must never be applied because flush aborts at the failing command.
    private static void RecordOnly(CommandBuffer cb, List<DeferredEntity> deferred, Random rng)
    {
        var kind = rng.Next(0, 3);

        if (kind == 0 || deferred.Count == 0)
        {
            deferred.Add(cb.RecordCreateEntity());
            return;
        }

        var deferredTarget = deferred[rng.Next(deferred.Count)];
        var compType = rng.Next(0, 3);

        if (kind == 1)
        {
            switch (compType)
            {
                case 0:
                    cb.RecordSet(deferredTarget, new CompA { X = rng.Next(-100000, 100000) });
                    break;
                case 1:
                    cb.RecordSet(deferredTarget, new CompB { Y = rng.Next(-100000, 100000), Z = rng.Next(-100000, 100000) });
                    break;
                default:
                    cb.RecordSet(deferredTarget, new CompC { W = rng.Next(-100000, 100000), V = rng.Next(-100000, 100000) });
                    break;
            }
        }
        else
        {
            switch (compType)
            {
                case 0:
                    cb.RecordRemove<CompA>(deferredTarget);
                    break;
                case 1:
                    cb.RecordRemove<CompB>(deferredTarget);
                    break;
                default:
                    cb.RecordRemove<CompC>(deferredTarget);
                    break;
            }
        }
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
