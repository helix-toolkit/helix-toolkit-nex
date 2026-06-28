using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferLastWriteWinsTest
{
    /// <summary>
    /// Blittable test component with several fields, used to verify last-write-wins
    /// semantics when the same component type is set twice on one deferred entity.
    /// </summary>
    internal struct LwwProbe
    {
        public int A;
        public int B;
        public long C;
    }

    [TestMethod]
    public void Property5_RepeatedSet_LastWriteWins()
    {
        // Feature: ecs-command-buffer, Property 5: Last write wins for repeated set-component
        // For any deferred entity and any two values v1, v2 of component type T recorded as
        // set-component commands in that order, after flush the entity's T component equals v2.
        // Validates: Requirements 3.3
        var gen =
            from a1 in Gen.Choose(-100000, 100000)
            from b1 in Gen.Choose(-100000, 100000)
            from c1 in Gen.Choose(-100000, 100000)
            from a2 in Gen.Choose(-100000, 100000)
            from b2 in Gen.Choose(-100000, 100000)
            from c2 in Gen.Choose(-100000, 100000)
            select (a1, b1, (long)c1, a2, b2, (long)c2);

        Prop.ForAll(
                Arb.From(gen),
                ((int a1, int b1, long c1, int a2, int b2, long c2) t) =>
                {
                    var cb = new CommandBuffer();
                    var deferred = cb.RecordCreateEntity();

                    var v1 = new LwwProbe { A = t.a1, B = t.b1, C = t.c1 };
                    var v2 = new LwwProbe { A = t.a2, B = t.b2, C = t.c2 };

                    // Record two set-component commands of the same type T in order: v1 then v2.
                    var set1 = cb.RecordSet(deferred, v1);
                    var set2 = cb.RecordSet(deferred, v2);

                    var world = World.CreateWorld();
                    try
                    {
                        var flush = cb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        // The world holds exactly one entity; read its component back.
                        Entity entity = default;
                        var found = false;
                        foreach (var e in world.GetComponentEntities<LwwProbe>())
                        {
                            entity = e;
                            found = true;
                            break;
                        }

                        if (!found)
                        {
                            return false;
                        }

                        ref var applied = ref entity.Get<LwwProbe>();

                        // After flush the component equals the value recorded later (v2).
                        return set1 == ResultCode.Ok
                            && set2 == ResultCode.Ok
                            && applied.A == v2.A
                            && applied.B == v2.B
                            && applied.C == v2.C;
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
