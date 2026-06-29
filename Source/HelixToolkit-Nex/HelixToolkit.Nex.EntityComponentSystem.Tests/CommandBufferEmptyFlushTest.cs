using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferEmptyFlushTest
{
    // Small set of blittable test component value types used to populate an arbitrary
    // world state before an empty flush is performed.
    internal struct EHealth
    {
        public int Value;
    }

    internal struct EPosition
    {
        public float X;
        public float Y;
    }

    internal struct ESpeed
    {
        public float Velocity;
        public float Acceleration;
    }

    /// <summary>
    /// Captures the observable contents of a world: its entity count and, per component
    /// type, the value held by each entity that carries that component. Two snapshots are
    /// equal when the world's entity count and every component's contents match exactly.
    /// </summary>
    private readonly struct WorldSnapshot
    {
        public readonly int Count;
        public readonly Dictionary<int, EHealth> Health;
        public readonly Dictionary<int, EPosition> Position;
        public readonly Dictionary<int, ESpeed> Speed;

        private WorldSnapshot(
            int count,
            Dictionary<int, EHealth> health,
            Dictionary<int, EPosition> position,
            Dictionary<int, ESpeed> speed
        )
        {
            Count = count;
            Health = health;
            Position = position;
            Speed = speed;
        }

        public static WorldSnapshot Capture(World world)
        {
            var health = new Dictionary<int, EHealth>();
            foreach (var e in world.GetComponentEntities<EHealth>())
            {
                health[e.Id] = e.Get<EHealth>();
            }

            var position = new Dictionary<int, EPosition>();
            foreach (var e in world.GetComponentEntities<EPosition>())
            {
                position[e.Id] = e.Get<EPosition>();
            }

            var speed = new Dictionary<int, ESpeed>();
            foreach (var e in world.GetComponentEntities<ESpeed>())
            {
                speed[e.Id] = e.Get<ESpeed>();
            }

            return new WorldSnapshot(world.Count, health, position, speed);
        }

        public bool Matches(WorldSnapshot other)
        {
            return Count == other.Count
                && SameHealth(Health, other.Health)
                && SamePosition(Position, other.Position)
                && SameSpeed(Speed, other.Speed);
        }

        private static bool SameHealth(Dictionary<int, EHealth> a, Dictionary<int, EHealth> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var v) || v.Value != kv.Value.Value)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool NearlyEqual(float left, float right, float epsilon = 1e-6f)
        {
            return MathF.Abs(left - right) <= epsilon;
        }

        private static bool SamePosition(Dictionary<int, EPosition> a, Dictionary<int, EPosition> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            foreach (var kv in a)
            {
                if (
                    !b.TryGetValue(kv.Key, out var v)
                    || !NearlyEqual(v.X, kv.Value.X)
                    || !NearlyEqual(v.Y, kv.Value.Y)
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameSpeed(Dictionary<int, ESpeed> a, Dictionary<int, ESpeed> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            foreach (var kv in a)
            {
                if (
                    !b.TryGetValue(kv.Key, out var v)
                    || !NearlyEqual(v.Velocity, kv.Value.Velocity)
                    || !NearlyEqual(v.Acceleration, kv.Value.Acceleration)
                )
                {
                    return false;
                }
            }
            return true;
        }
    }

    [TestMethod]
    public void Property8_EmptyFlush_LeavesWorldUnchanged()
    {
        // Feature: ecs-command-buffer, Property 8: Empty flush leaves the world unchanged
        // For any world state and any command buffer holding no recorded commands, flushing
        // the buffer leaves the world's entity count and component contents unchanged.
        // Validates: Requirements 6.1

        // A world state is described by a number of pre-populated entities and a seed; the
        // seed deterministically derives, for each entity, which components it carries and
        // their values. Entities are created directly on the world (not through the buffer)
        // so the buffer genuinely holds no recorded commands at flush time.
        var gen =
            from n in Gen.Choose(0, 40)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        var rng = new Random(t.seed);

                        // Pre-populate the world with an arbitrary state via direct creation.
                        for (var i = 0; i < t.n; i++)
                        {
                            var entity = world.CreateEntity();

                            if (rng.Next(0, 2) == 0)
                            {
                                entity.Set(new EHealth { Value = rng.Next() });
                            }
                            if (rng.Next(0, 2) == 0)
                            {
                                entity.Set(
                                    new EPosition
                                    {
                                        X = rng.Next(-1000, 1000),
                                        Y = rng.Next(-1000, 1000),
                                    }
                                );
                            }
                            if (rng.Next(0, 2) == 0)
                            {
                                entity.Set(
                                    new ESpeed
                                    {
                                        Velocity = rng.Next(-1000, 1000),
                                        Acceleration = rng.Next(-1000, 1000),
                                    }
                                );
                            }
                        }

                        // Snapshot the arbitrary world state before the empty flush.
                        var before = WorldSnapshot.Capture(world);

                        // An empty command buffer: no commands recorded.
                        var cb = new CommandBuffer();
                        if (cb.PendingCount != 0)
                        {
                            return false;
                        }

                        var flush = cb.Flush(world);

                        // Flush of an empty buffer succeeds and leaves nothing pending.
                        if (!flush.Success || cb.PendingCount != 0)
                        {
                            return false;
                        }

                        // Entity count and component contents are unchanged.
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
