using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Tests for Property 4 (feature: engine-node-command-buffer): Captured value-type component
/// data is independent of post-record mutation.
///
/// For any value-type component captured through a convenience recording method (the Engine
/// <see cref="SceneCommandBufferEngineExtensions"/> methods, e.g. <c>RecordCreateLineNode</c>,
/// <c>RecordCreateMeshNode</c>), mutating the caller's local variable after recording and before
/// flush has no effect on the flushed result: the materialized node's component equals the value
/// as it was at record time.
///
/// Validates: Requirements 3.3, 3.4
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferValueCaptureTest
{
    [TestMethod]
    public void Property4_LineDrawInfo_CapturedValueIsIndependentOfPostRecordMutation()
    {
        // Feature: engine-node-command-buffer, Property 4
        // Record a LineNode through the convenience method passing a LineDrawInfo value, then
        // mutate the caller's local variable, then flush, then assert the materialized node carries
        // the component value as it was AT RECORD TIME (not the mutated value).
        // Validates: Requirements 3.3, 3.4

        // A scenario is described by a single seed that deterministically derives the recorded
        // component values and a set of distinct "mutated" values applied to the caller's local
        // variable after recording. Many seeds exercise many record-time/mutated value pairs.
        var gen = Gen.Choose(int.MinValue, int.MaxValue);

        Prop.ForAll(
                Arb.From(gen),
                (int seed) =>
                {
                    var rng = new Random(seed);

                    // ---- Record-time component values (what the flushed node MUST carry).
                    var recThickness = (float)(rng.NextDouble() * 63.0 + 1.0); // [1, 64)
                    var recColor = new Color4(
                        (float)rng.NextDouble(),
                        (float)rng.NextDouble(),
                        (float)rng.NextDouble(),
                        (float)rng.NextDouble());
                    var recCullable = rng.Next(2) == 0;
                    var recHitable = rng.Next(2) == 0;
                    var recMaterialName = "mat" + rng.Next();

                    var world = World.CreateWorld();
                    try
                    {
                        var scb = new SceneCommandBuffer();

                        // Build the value-type component and record the node with it.
                        var info = new LineDrawInfo
                        {
                            LineThickness = recThickness,
                            LineColor = recColor,
                            Cullable = recCullable,
                            Hitable = recHitable,
                            LineMaterialTypeName = recMaterialName,
                        };

                        var handle = scb.RecordCreateLineNode("line", info);
                        if (!handle.IsValid)
                        {
                            return false;
                        }

                        // ---- Mutate the caller's local variable to DISTINCT values after recording
                        // and before flush. None of these may reach the flushed result.
                        info.LineThickness = recThickness + 100.0f;
                        info.LineColor = new Color4(
                            recColor.Red + 0.5f,
                            recColor.Green + 0.5f,
                            recColor.Blue + 0.5f,
                            recColor.Alpha + 0.5f);
                        info.Cullable = !recCullable;
                        info.Hitable = !recHitable;
                        info.LineMaterialTypeName = recMaterialName + "_mutated";

                        // ---- Flush on the owning thread, then retrieve the materialized node.
                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        if (scb.TryGetMaterializedNode(handle, out var node)
                            != ResultCode.Ok
                            || node is null)
                        {
                            return false;
                        }

                        // The materialized node must carry the values as captured AT RECORD TIME.
                        return node.LineThickness == recThickness
                            && node.LineColor.Equals(recColor)
                            && node.Cullable == recCullable
                            && node.Hitable == recHitable
                            && node.LineMaterialName == recMaterialName;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property4_MeshDrawInfo_CapturedValueIsIndependentOfPostRecordMutation()
    {
        // Feature: engine-node-command-buffer, Property 4 (second value-type component)
        // The same independence guarantee holds for a MeshDrawInfo captured through
        // RecordCreateMeshNode: mutating the caller's local variable after recording has no effect
        // on the flushed MeshNode's component values.
        // Validates: Requirements 3.3, 3.4
        var gen = Gen.Choose(int.MinValue, int.MaxValue);

        Prop.ForAll(
                Arb.From(gen),
                (int seed) =>
                {
                    var rng = new Random(seed);

                    var recCullable = rng.Next(2) == 0;
                    var recHitable = rng.Next(2) == 0;

                    var world = World.CreateWorld();
                    try
                    {
                        var scb = new SceneCommandBuffer();

                        var info = new MeshDrawInfo
                        {
                            Cullable = recCullable,
                            Hitable = recHitable,
                        };

                        var handle = scb.RecordCreateMeshNode("mesh", info);
                        if (!handle.IsValid)
                        {
                            return false;
                        }

                        // Mutate the caller's local variable after recording.
                        info.Cullable = !recCullable;
                        info.Hitable = !recHitable;

                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        if (scb.TryGetMaterializedNode(handle, out var node)
                            != ResultCode.Ok
                            || node is null)
                        {
                            return false;
                        }

                        return node.Cullable == recCullable && node.Hitable == recHitable;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void ValueCapture_LineDrawInfo_ConcreteExample()
    {
        // Feature: engine-node-command-buffer, Property 4 (concrete example)
        // A single, readable demonstration: record with one set of values, mutate the local
        // variable, flush, and observe the record-time values on the materialized node.
        // Validates: Requirements 3.3, 3.4
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();

            var info = new LineDrawInfo
            {
                LineThickness = 4.0f,
                LineColor = new Color4(0.1f, 0.2f, 0.3f, 0.4f),
                Cullable = false,
                Hitable = true,
                LineMaterialTypeName = "RecordTime",
            };

            var handle = scb.RecordCreateLineNode("line", info);

            // Mutate the caller's local variable after recording, before flush.
            info.LineThickness = 99.0f;
            info.LineColor = new Color4(0.9f, 0.9f, 0.9f, 0.9f);
            info.Cullable = true;
            info.Hitable = false;
            info.LineMaterialTypeName = "Mutated";

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);

            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode(handle, out var node));
            Assert.IsNotNull(node);

            // The materialized node reflects the record-time values, not the mutated ones.
            Assert.AreEqual(4.0f, node.LineThickness);
            Assert.AreEqual(new Color4(0.1f, 0.2f, 0.3f, 0.4f), node.LineColor);
            Assert.IsFalse(node.Cullable);
            Assert.IsTrue(node.Hitable);
            Assert.AreEqual("RecordTime", node.LineMaterialName);
        }
        finally
        {
            world.Dispose();
        }
    }
}
