using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferRecordingNoWorldTouchTest
{
    // Small set of blittable test component value types referenced by recorded
    // set/remove commands. The recording API stores them without touching any World.
    internal struct CompA
    {
        public int X;
    }

    internal struct CompB
    {
        public float Y;
        public long Z;
    }

    internal struct CompC
    {
        public double W;
    }

    [TestMethod]
    public void Property2_Recording_NeverTouchesAWorld()
    {
        // Feature: ecs-command-buffer, Property 2: Recording never touches a World
        // For any sequence of record operations (create entity, set component, remove
        // component) performed on a command buffer that holds no World reference, every
        // recording completes successfully and PendingCount equals the number of recorded
        // commands, and no World instance is observed to change.
        // Validates: Requirements 1.1, 1.4, 2.4, 7.2, 10.2

        // A program is described by a length and a seed; the seed deterministically
        // derives a sequence of create/set/remove operations that reference only deferred
        // entities the program has already created. This keeps every set/remove on a valid,
        // buffer-owned handle so recording must return Ok.
        var gen =
            from n in Gen.Choose(0, 80)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    // A fresh world is created purely as an observation target. It is never
                    // passed to any recording method.
                    var world = World.CreateWorld();
                    try
                    {
                        // Snapshot the world state before any recording happens.
                        var initialCount = world.Count;
                        var initialHasA = world.HasAnyComponent<CompA>();
                        var initialHasB = world.HasAnyComponent<CompB>();
                        var initialHasC = world.HasAnyComponent<CompC>();

                        var cb = new CommandBuffer();
                        var deferred = new List<DeferredEntity>();
                        var recordedCount = 0;
                        var allRecordingsOk = true;

                        var rng = new Random(t.seed);

                        for (var i = 0; i < t.n; i++)
                        {
                            var kind = rng.Next(0, 3);

                            // Force a create when there is no deferred entity to reference.
                            if (kind == 0 || deferred.Count == 0)
                            {
                                var handle = cb.RecordCreateEntity();
                                deferred.Add(handle);
                                recordedCount++;
                                continue;
                            }

                            var target = deferred[rng.Next(deferred.Count)];
                            var compType = rng.Next(0, 3);

                            ResultCode result;
                            if (kind == 1)
                            {
                                // set-component
                                result = compType switch
                                {
                                    0 => cb.RecordSet(target, new CompA { X = rng.Next() }),
                                    1 => cb.RecordSet(
                                        target,
                                        new CompB { Y = rng.Next(), Z = rng.Next() }
                                    ),
                                    _ => cb.RecordSet(target, new CompC { W = rng.Next() }),
                                };
                            }
                            else
                            {
                                // remove-component
                                result = compType switch
                                {
                                    0 => cb.RecordRemove<CompA>(target),
                                    1 => cb.RecordRemove<CompB>(target),
                                    _ => cb.RecordRemove<CompC>(target),
                                };
                            }

                            if (result == ResultCode.Ok)
                            {
                                recordedCount++;
                            }
                            else
                            {
                                allRecordingsOk = false;
                            }
                        }

                        // Every set/remove on a valid owned handle must succeed.
                        if (!allRecordingsOk)
                        {
                            return false;
                        }

                        // PendingCount equals the number of recorded commands.
                        if (cb.PendingCount != recordedCount)
                        {
                            return false;
                        }

                        // The world is unchanged by recording (recording only, no flush):
                        // same entity count and same component contents as before.
                        return world.Count == initialCount
                            && world.HasAnyComponent<CompA>() == initialHasA
                            && world.HasAnyComponent<CompB>() == initialHasB
                            && world.HasAnyComponent<CompC>() == initialHasC
                            && initialCount == 0
                            && !initialHasA
                            && !initialHasB
                            && !initialHasC;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
