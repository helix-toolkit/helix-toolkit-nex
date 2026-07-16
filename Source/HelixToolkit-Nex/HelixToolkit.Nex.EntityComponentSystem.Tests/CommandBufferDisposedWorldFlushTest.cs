namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferDisposedWorldFlushTest
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
    public void Property9_FlushingDisposedWorld_IsRejected()
    {
        // Feature: ecs-command-buffer, Property 9: Flushing a disposed world is rejected
        // For any recorded command buffer, flushing onto a disposed world returns an error
        // result and performs no mutation observable in any world.
        // Validates: Requirements 4.5

        // Record some commands: an entity creation and a set-component on it.
        var cb = new CommandBuffer();
        var deferred = cb.RecordCreateEntity();
        Assert.AreEqual(ResultCode.Ok, cb.RecordSet(deferred, new Probe { X = 7, Y = 42L }));

        var pendingBeforeFlush = cb.PendingCount;
        Assert.AreEqual(2, pendingBeforeFlush, "Expected the create and set commands to be pending before flush.");

        // Create a world and immediately dispose it. Dispose resets World.Id to 0,
        // which the buffer treats as a disposed/invalid world.
        var world = World.CreateWorld();
        world.Dispose();
        Assert.AreEqual(0, world.Id, "Disposing a world should reset its Id to 0.");

        // Flush onto the disposed world.
        var result = cb.Flush(world);

        // The flush must be rejected with WorldNotValid.
        Assert.IsFalse(result.Success, "Flushing onto a disposed world must not succeed.");
        Assert.AreEqual(ResultCode.WorldNotValid, result.Code, "A disposed-world flush must report WorldNotValid.");

        // A rejected flush mutates nothing: the disposed-world check returns before clearing
        // the buffer, so the recorded commands remain pending.
        Assert.AreEqual(pendingBeforeFlush, cb.PendingCount,
            "A rejected flush must not clear or otherwise mutate the buffer's pending commands.");
    }
}
