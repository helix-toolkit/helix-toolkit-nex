using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferCopySemanticsTest
{
    /// <summary>
    /// Blittable test component with several fields, used to verify that a recorded
    /// set-component command captures a copy of the value rather than aliasing the
    /// caller's local variable.
    /// </summary>
    internal struct CopyProbe
    {
        public int A;
        public int B;
        public long C;
    }

    [TestMethod]
    public void Property6_SetComponent_StoresCopy()
    {
        // Feature: ecs-command-buffer, Property 6: Set-component stores a copy
        // For any component value recorded with set-component and then mutated in the
        // caller's local variable before flush, the value applied during flush equals the
        // value as it was at the moment of recording.
        // Validates: Requirements 2.1
        var gen =
            from a in Gen.Choose(-100000, 100000)
            from b in Gen.Choose(-100000, 100000)
            from c in Gen.Choose(-100000, 100000)
            from da in Gen.Choose(1, 1000)
            from db in Gen.Choose(1, 1000)
            from dc in Gen.Choose(1, 1000)
            select (a, b, (long)c, da, db, (long)dc);

        Prop.ForAll(
                Arb.From(gen),
                ((int a, int b, long c, int da, int db, long dc) t) =>
                {
                    var cb = new CommandBuffer();
                    var deferred = cb.RecordCreateEntity();

                    var value = new CopyProbe { A = t.a, B = t.b, C = t.c };

                    // Snapshot of the value as it is at the moment of recording.
                    var recorded = value;

                    var setResult = cb.RecordSet(deferred, value);

                    // Mutate the caller's local variable AFTER recording. With copy
                    // semantics this must have no effect on the value applied at flush.
                    value.A += t.da;
                    value.B += t.db;
                    value.C += t.dc;

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
                        foreach (var e in world.GetComponentEntities<CopyProbe>())
                        {
                            entity = e;
                            found = true;
                            break;
                        }

                        if (!found)
                        {
                            return false;
                        }

                        ref var applied = ref entity.Get<CopyProbe>();

                        // The applied value equals the record-time snapshot, not the
                        // mutated local variable.
                        return setResult == ResultCode.Ok
                            && applied.A == recorded.A
                            && applied.B == recorded.B
                            && applied.C == recorded.C;
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
