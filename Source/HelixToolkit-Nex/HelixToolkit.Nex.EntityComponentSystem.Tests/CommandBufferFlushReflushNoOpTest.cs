using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferFlushReflushNoOpTest
{
    // Small set of blittable component value types referenced by a generated command
    // program. They carry varied fields so the snapshot captures real component contents.
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

    /// <summary>
    /// An immutable snapshot of a world's observable state: total entity count plus, for
    /// each tracked component type, the per-entity component values keyed by entity id and
    /// ordered for a stable comparison.
    /// </summary>
    private sealed class WorldSnapshot
    {
        public int EntityCount { get; init; }
        public List<(int Id, int X)> A { get; init; } = [];
        public List<(int Id, float Y, long Z)> B { get; init; } = [];
        public List<(int Id, double W)> C { get; init; } = [];

        public static WorldSnapshot Capture(World world)
        {
            var a = new List<(int, int)>();
            foreach (var e in world.GetComponentEntities<CompA>())
            {
                a.Add((e.Id, e.Get<CompA>().X));
            }

            var b = new List<(int, float, long)>();
            foreach (var e in world.GetComponentEntities<CompB>())
            {
                ref var v = ref e.Get<CompB>();
                b.Add((e.Id, v.Y, v.Z));
            }

            var c = new List<(int, double)>();
            foreach (var e in world.GetComponentEntities<CompC>())
            {
                c.Add((e.Id, e.Get<CompC>().W));
            }

            a.Sort((l, r) => l.Item1.CompareTo(r.Item1));
            b.Sort((l, r) => l.Item1.CompareTo(r.Item1));
            c.Sort((l, r) => l.Item1.CompareTo(r.Item1));

            return new WorldSnapshot
            {
                EntityCount = world.Count,
                A = a,
                B = b,
                C = c,
            };
        }

        public bool Matches(WorldSnapshot other)
        {
            if (EntityCount != other.EntityCount)
            {
                return false;
            }

            if (A.Count != other.A.Count || B.Count != other.B.Count || C.Count != other.C.Count)
            {
                return false;
            }

            for (var i = 0; i < A.Count; i++)
            {
                if (A[i].Id != other.A[i].Id || A[i].X != other.A[i].X)
                {
                    return false;
                }
            }

            for (var i = 0; i < B.Count; i++)
            {
                if (B[i].Id != other.B[i].Id || B[i].Y != other.B[i].Y || B[i].Z != other.B[i].Z)
                {
                    return false;
                }
            }

            for (var i = 0; i < C.Count; i++)
            {
                if (C[i].Id != other.C[i].Id || Math.Abs(C[i].W - other.C[i].W) > 1e-12)
                {
                    return false;
                }
            }

            return true;
        }
    }

    [TestMethod]
    public void Property7_Flush_EmptiesBuffer_AndReflushIsNoOp()
    {
        // Feature: ecs-command-buffer, Property 7: Flush empties the buffer and re-flush is a no-op
        // For any command buffer that is flushed successfully, PendingCount is zero
        // afterward, and an immediate second flush (with no commands recorded in between)
        // leaves the target world's entity count and component contents unchanged.
        // Validates: Requirements 4.4, 6.2

        // A program is described by a length and a seed; the seed deterministically derives
        // a non-trivial sequence of create/set/remove operations over the blittable test
        // component types, referencing only deferred entities already created. The length
        // range is biased away from zero so the generated programs are non-trivial.
        var gen =
            from n in Gen.Choose(1, 80)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var cb = new CommandBuffer();
                    var deferred = new List<DeferredEntity>();

                    // Tracks, per created deferred entity (by list index), which component
                    // types are currently present. This lets the generator emit removes only
                    // for components that were actually set, so that every recorded command
                    // applies successfully during flush (Property 7 concerns the
                    // successfully-flushed case).
                    var present = new List<HashSet<int>>();
                    var rng = new Random(t.seed);

                    for (var i = 0; i < t.n; i++)
                    {
                        var kind = rng.Next(0, 3);

                        // Force a create when there is no deferred entity to reference yet.
                        if (kind == 0 || deferred.Count == 0)
                        {
                            deferred.Add(cb.RecordCreateEntity());
                            present.Add([]);
                            continue;
                        }

                        var targetIdx = rng.Next(deferred.Count);
                        var target = deferred[targetIdx];

                        if (kind == 1)
                        {
                            // set-component: records a value and marks the type present.
                            var compType = rng.Next(0, 3);
                            switch (compType)
                            {
                                case 0:
                                    cb.RecordSet(target, new CompA { X = rng.Next() });
                                    break;
                                case 1:
                                    cb.RecordSet(
                                        target,
                                        new CompB { Y = rng.Next(), Z = rng.Next() }
                                    );
                                    break;
                                default:
                                    cb.RecordSet(target, new CompC { W = rng.Next() });
                                    break;
                            }
                            present[targetIdx].Add(compType);
                        }
                        else
                        {
                            // remove-component: only remove a type currently present on the
                            // target so the flushed removal succeeds.
                            var owned = present[targetIdx];
                            if (owned.Count == 0)
                            {
                                continue;
                            }

                            var choices = owned.ToArray();
                            var compType = choices[rng.Next(choices.Length)];
                            switch (compType)
                            {
                                case 0:
                                    cb.RecordRemove<CompA>(target);
                                    break;
                                case 1:
                                    cb.RecordRemove<CompB>(target);
                                    break;
                                default:
                                    cb.RecordRemove<CompC>(target);
                                    break;
                            }
                            owned.Remove(compType);
                        }
                    }

                    var world = World.CreateWorld();
                    try
                    {
                        // First flush applies the recorded program.
                        var firstFlush = cb.Flush(world);
                        if (!firstFlush.Success)
                        {
                            return false;
                        }

                        // The buffer is empty after a successful flush (Req 4.4).
                        if (cb.PendingCount != 0)
                        {
                            return false;
                        }

                        // Snapshot the world after the first flush.
                        var before = WorldSnapshot.Capture(world);

                        // Second flush with no commands recorded in between is a no-op.
                        var secondFlush = cb.Flush(world);
                        if (!secondFlush.Success)
                        {
                            return false;
                        }

                        if (cb.PendingCount != 0)
                        {
                            return false;
                        }

                        // The world's entity count and component contents are unchanged (Req 6.2).
                        var after = WorldSnapshot.Capture(world);
                        return before.Matches(after);
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
