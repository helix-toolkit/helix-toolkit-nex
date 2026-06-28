using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Unit tests for self-parent rejection (feature: engine-node-command-buffer, task 8.1).
///
/// Requirement 7.6: IF a parent-child command names the same handle as both parent and child,
/// THEN the Scene_Command_Buffer SHALL reject the command and return an error result.
///
/// These tests assert that <see cref="SceneCommandBuffer.RecordAddChild"/> called with the same
/// handle as both parent and child returns <see cref="ResultCode.InvalidState"/>
/// and appends no command (<see cref="SceneCommandBuffer.PendingCount"/> is unchanged). Both a
/// base <see cref="DeferredNode"/> handle and a <see cref="TypedDeferredNode{T}"/> custom handle
/// are covered.
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferSelfParentTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to exercise the
    /// self-parent rejection path with a <see cref="TypedDeferredNode{T}"/> custom handle.
    /// </summary>
    private sealed class CustomTestNode : Node
    {
        public CustomTestNode(World world)
            : base(world)
        {
        }
    }

    [TestMethod]
    public void RecordAddChild_BaseHandleAsParentAndChild_ReturnsInvalid_AppendsNoCommand()
    {
        // Feature: engine-node-command-buffer, task 8.1
        // RecordAddChild(h, h) for a base DeferredNode handle returns Invalid and appends no command.
        // Validates: Requirements 7.6
        var scb = new SceneCommandBuffer();
        var handle = scb.RecordCreateNode();

        // One create command recorded so far.
        var pendingBefore = scb.PendingCount;
        Assert.AreEqual(1, pendingBefore);

        var result = scb.RecordAddChild(handle, handle);

        Assert.AreEqual(ResultCode.InvalidState, result,
            "Self-parent on a base handle must be rejected with Invalid.");
        Assert.AreEqual(pendingBefore, scb.PendingCount,
            "Rejected self-parent recording must not append a command.");
    }

    [TestMethod]
    public void RecordAddChild_TypedHandleAsParentAndChild_ReturnsInvalid_AppendsNoCommand()
    {
        // Feature: engine-node-command-buffer, task 8.1
        // RecordAddChild(h, h) for a TypedDeferredNode<T> custom handle returns Invalid and
        // appends no command. The typed handle converts implicitly to the equivalent DeferredNode.
        // Validates: Requirements 7.6
        var scb = new SceneCommandBuffer();
        TypedDeferredNode<CustomTestNode> handle =
            scb.RecordCreateNode<CustomTestNode>(world => new CustomTestNode(world));

        // One create command recorded so far.
        var pendingBefore = scb.PendingCount;
        Assert.AreEqual(1, pendingBefore);

        var result = scb.RecordAddChild(handle, handle);

        Assert.AreEqual(ResultCode.InvalidState, result,
            "Self-parent on a typed custom handle must be rejected with Invalid.");
        Assert.AreEqual(pendingBefore, scb.PendingCount,
            "Rejected self-parent recording must not append a command.");
    }
}
