using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferRecordThenFlushEquivalenceTest
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

    [TestMethod]
    public void Property4_RecordThenFlush_IsEquivalentToDirectApplication()
    {
        // Feature: ecs-command-buffer, Property 4: Record-then-flush is equivalent to direct application
        // For any valid sequence of entity-creation, set-component, and remove-component
        // operations, recording the sequence into a command buffer and flushing it onto a
        // world produces a world state in which every entity created by a successfully
        // applied command exists with the same set of components and the same component
        // values as performing the equivalent sequence directly on the owning thread, in
        // the same order.
        // Validates: Requirements 2.1, 2.2, 3.1, 3.2, 4.1, 4.2, 4.3, 5.1

        // A program is described by a length and a seed; the seed deterministically derives
        // a sequence of create/set/remove operations that reference only deferred entities
        // the program has already created. The SAME derived program is applied two ways:
        //   - buffer path: recorded into a CommandBuffer, then flushed onto world A;
        //   - direct path: applied directly onto world B in the same order.
        // Edge cases are covered by construction: empty programs (n == 0), removes of
        // never-set components, and repeated sets of the same type on one entity all arise
        // naturally from the random op stream.
        var gen =
            from n in Gen.Choose(0, 120)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var bufferWorld = World.CreateWorld();
                    var directWorld = World.CreateWorld();
                    try
                    {
                        var cb = new CommandBuffer();

                        // Parallel mapping: the i-th created entity in each path corresponds.
                        var deferred = new List<DeferredEntity>();
                        var direct = new List<Entity>();

                        var rng = new Random(t.seed);

                        for (var i = 0; i < t.n; i++)
                        {
                            var kind = rng.Next(0, 3);

                            // Force a create when there is no entity to reference yet.
                            if (kind == 0 || deferred.Count == 0)
                            {
                                deferred.Add(cb.RecordCreateEntity());
                                direct.Add(directWorld.CreateEntity());
                                continue;
                            }

                            var slot = rng.Next(deferred.Count);
                            var deferredTarget = deferred[slot];
                            var directTarget = direct[slot];
                            var compType = rng.Next(0, 3);

                            if (kind == 1)
                            {
                                // set-component: generate the value once, apply identically
                                // to both the buffer (record) and the direct world.
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
                                        var v = new CompB
                                        {
                                            Y = rng.Next(-100000, 100000),
                                            Z = rng.Next(-100000, 100000),
                                        };
                                        cb.RecordSet(deferredTarget, v);
                                        directTarget.Set(v);
                                        break;
                                    }
                                    default:
                                    {
                                        var v = new CompC
                                        {
                                            W = rng.Next(-100000, 100000),
                                            V = rng.Next(-100000, 100000),
                                        };
                                        cb.RecordSet(deferredTarget, v);
                                        directTarget.Set(v);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                // remove-component
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

                        // Apply the recorded program onto world A.
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
