using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 16 (feature: engine-node-command-buffer): Off-thread recording is
/// equivalent to on-thread recording.
///
/// For any sequence of custom-node operations recorded on a Recording_Thread that differs from
/// the flush thread and then flushed, the buffer produces a World state whose materialized nodes
/// have NodeInfo, Transform, WorldTransform, and Parent values and parent-child structure equal
/// to recording the same sequence on the owning (flush) thread.
///
/// Note: the flush itself runs on the owning thread in both cases. Only the recording thread
/// differs between the two paths; the buffer carries no thread affinity.
///
/// Validates: Requirements 10.1, 10.4
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferOffThreadEquivalenceTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to exercise the
    /// typed recording path through <see cref="TypedDeferredNode{T}"/> on a background thread.
    /// </summary>
    private sealed class OffThreadNode : Node
    {
        public OffThreadNode(World world)
            : base(world)
        {
        }
    }

    // The recorded specification for a single node: a null field means "not recorded".
    private readonly record struct NodeSpec(string? Name, Transform? Transform, bool? Renderable);

    [TestMethod]
    public void Property16_OffThreadRecording_IsEquivalentToOnThreadRecording()
    {
        // Feature: engine-node-command-buffer, Property 16
        // For any sequence of custom-node operations recorded on a Recording_Thread that differs
        // from the flush thread and then flushed, the buffer produces a World state whose
        // materialized nodes have NodeInfo, Transform, WorldTransform, and Parent values and
        // parent-child structure equal to recording the same sequence on the owning (flush)
        // thread.
        // Validates: Requirements 10.1, 10.4

        // A program is described by a node count n and a seed. The seed deterministically derives,
        // for each node i: a parent index in [-1, i - 1] (-1 means "root"), and the recorded
        // name / local-transform / renderable specification. Because every parent index is
        // strictly less than the child index, the structure is always an acyclic forest in which
        // each node has at most one parent, exercising forests of varying depth and breadth by
        // construction.
        var gen =
            from n in Gen.Choose(0, 40)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var offThreadWorld = World.CreateWorld();
                    var onThreadWorld = World.CreateWorld();
                    try
                    {
                        // Derive the program deterministically from the seed.
                        var parent = new int[t.n];
                        var specs = new NodeSpec[t.n];
                        var rng = new Random(t.seed);
                        for (var i = 0; i < t.n; i++)
                        {
                            parent[i] = rng.Next(-1, i); // [-1, i - 1]; -1 means root.
                            specs[i] = MakeSpec(rng, i);
                        }

                        // ---- Path A (off-thread recording): record the entire program on a
                        //      dedicated thread that differs from this (flush) thread, then flush
                        //      here on the owning thread.
                        var offScb = new SceneCommandBuffer();
                        var offHandles = new TypedDeferredNode<OffThreadNode>[t.n];
                        var recordOk = true;
                        var recordingThreadId = -1;
                        var recordThread = new Thread(() =>
                        {
                            recordingThreadId = Environment.CurrentManagedThreadId;
                            recordOk = RecordProgram(offScb, offHandles, parent, specs);
                        });
                        recordThread.Start();
                        recordThread.Join();

                        if (!recordOk)
                        {
                            return false;
                        }

                        var flushThreadId = Environment.CurrentManagedThreadId;
                        if (!offScb.Flush(offThreadWorld).Success)
                        {
                            return false;
                        }

                        // Sanity: when at least one node was recorded, recording really did happen
                        // on a thread distinct from the flush thread (off-thread isolation).
                        if (t.n > 0 && recordingThreadId == flushThreadId)
                        {
                            return false;
                        }

                        // ---- Path B (on-thread recording): record the identical program on this
                        //      (owning/flush) thread, then flush here.
                        var onScb = new SceneCommandBuffer();
                        var onHandles = new TypedDeferredNode<OffThreadNode>[t.n];
                        if (!RecordProgram(onScb, onHandles, parent, specs))
                        {
                            return false;
                        }

                        if (!onScb.Flush(onThreadWorld).Success)
                        {
                            return false;
                        }

                        // ---- Collect the materialized nodes from both paths by creation index.
                        var offNodes = new Node[t.n];
                        var onNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            offNodes[i] = offScb.MaterializedNodes[offHandles[i]];
                            onNodes[i] = onScb.MaterializedNodes[onHandles[i]];
                        }

                        // Run the downstream passes on both worlds on this (owning) thread so the
                        // WorldTransform comparison is meaningful.
                        offThreadWorld.SortSceneNodes();
                        offThreadWorld.UpdateTransforms();
                        onThreadWorld.SortSceneNodes();
                        onThreadWorld.UpdateTransforms();

                        var offIndex = BuildIndexMap(offNodes);
                        var onIndex = BuildIndexMap(onNodes);

                        // ---- Compare the materialized world state node-for-node.
                        for (var i = 0; i < t.n; i++)
                        {
                            // Same concrete runtime type.
                            if (offNodes[i].GetType() != onNodes[i].GetType())
                            {
                                return false;
                            }

                            // Same NodeInfo (including Level).
                            if (offNodes[i].Info.Level != onNodes[i].Info.Level)
                            {
                                return false;
                            }

                            // Same name.
                            if (offNodes[i].Name != onNodes[i].Name)
                            {
                                return false;
                            }

                            // Same parent (by structural creation position).
                            if (ParentIndex(offNodes[i], offIndex) != ParentIndex(onNodes[i], onIndex))
                            {
                                return false;
                            }

                            // Same set of children (by structural creation position).
                            if (!ChildIndices(offNodes[i], offIndex).SetEquals(ChildIndices(onNodes[i], onIndex)))
                            {
                                return false;
                            }

                            // Same local Transform (bitwise exact: identical operations).
                            ref var offTf = ref offNodes[i].Transform;
                            ref var onTf = ref onNodes[i].Transform;
                            if (!Vec3BitsEqual(offTf.Scale, onTf.Scale)
                                || !Vec3BitsEqual(offTf.Translation, onTf.Translation)
                                || !QuatBitsEqual(offTf.Rotation, onTf.Rotation))
                            {
                                return false;
                            }

                            // Same WorldTransform (bitwise exact after the identical two passes).
                            if (!MatrixBitsEqual(offNodes[i].WorldTransform.Value, onNodes[i].WorldTransform.Value))
                            {
                                return false;
                            }

                            // Same renderable presence.
                            if (offNodes[i].IsRenderable != onNodes[i].IsRenderable
                                || offNodes[i].Entity.Has<Renderable>() != onNodes[i].Entity.Has<Renderable>())
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        offThreadWorld.Dispose();
                        onThreadWorld.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void OffThreadAndOnThread_ProduceIdenticalState_ConcreteExample()
    {
        // Feature: engine-node-command-buffer, Property 16 (concrete example)
        // A small hierarchy with a name, a transform, and a renderable flag, recorded once on a
        // background thread and once on the test thread, materializes (and runs the downstream
        // passes) to identical world state.
        // Validates: Requirements 10.1, 10.4
        var offThreadWorld = World.CreateWorld();
        var onThreadWorld = World.CreateWorld();
        try
        {
            var tf = new Transform
            {
                Scale = new Vector3(2f, 3f, 4f),
                Translation = new Vector3(5f, 6f, 7f),
                Rotation = Quaternion.Identity,
            };

            // ---- Path A: record off-thread, flush on the test (owning) thread.
            var offScb = new SceneCommandBuffer();
            TypedDeferredNode<OffThreadNode> offRoot = default;
            TypedDeferredNode<OffThreadNode> offChild = default;
            var recordThread = new Thread(() =>
            {
                offRoot = offScb.RecordCreateNode<OffThreadNode>(w => new OffThreadNode(w));
                offChild = offScb.RecordCreateNode<OffThreadNode>(w => new OffThreadNode(w));
                offScb.RecordName(offRoot, "root");
                offScb.RecordLocalTransform(offChild, in tf);
                offScb.RecordRenderable(offChild, true);
                offScb.RecordAddChild(offRoot, offChild);
            });
            recordThread.Start();
            recordThread.Join();
            Assert.IsTrue(offScb.Flush(offThreadWorld).Success);

            // ---- Path B: record and flush on the test thread.
            var onScb = new SceneCommandBuffer();
            var onRoot = onScb.RecordCreateNode<OffThreadNode>(w => new OffThreadNode(w));
            var onChild = onScb.RecordCreateNode<OffThreadNode>(w => new OffThreadNode(w));
            Assert.AreEqual(ResultCode.Ok, onScb.RecordName(onRoot, "root"));
            Assert.AreEqual(ResultCode.Ok, onScb.RecordLocalTransform(onChild, in tf));
            Assert.AreEqual(ResultCode.Ok, onScb.RecordRenderable(onChild, true));
            Assert.AreEqual(ResultCode.Ok, onScb.RecordAddChild(onRoot, onChild));
            Assert.IsTrue(onScb.Flush(onThreadWorld).Success);

            offThreadWorld.SortSceneNodes();
            offThreadWorld.UpdateTransforms();
            onThreadWorld.SortSceneNodes();
            onThreadWorld.UpdateTransforms();

            var offRootNode = offScb.MaterializedNodes[offRoot];
            var offChildNode = offScb.MaterializedNodes[offChild];
            var onRootNode = onScb.MaterializedNodes[onRoot];
            var onChildNode = onScb.MaterializedNodes[onChild];

            Assert.AreEqual(onRootNode.GetType(), offRootNode.GetType());
            Assert.AreEqual(onRootNode.Name, offRootNode.Name);
            Assert.AreEqual(onRootNode.Info.Level, offRootNode.Info.Level);
            Assert.AreEqual(onChildNode.Info.Level, offChildNode.Info.Level);
            Assert.AreEqual(onChildNode.IsRenderable, offChildNode.IsRenderable);
            Assert.AreEqual(onChildNode.Transform.Translation, offChildNode.Transform.Translation);
            Assert.AreEqual(onChildNode.WorldTransform.Value, offChildNode.WorldTransform.Value);
            Assert.AreSame(offChildNode.Parent, offRootNode);
            Assert.AreSame(onChildNode.Parent, onRootNode);
        }
        finally
        {
            offThreadWorld.Dispose();
            onThreadWorld.Dispose();
        }
    }

    // Records the full program (create custom nodes, apply name/transform/renderable, wire
    // parent/child) into the given buffer. Returns false on the first non-Ok result.
    private static bool RecordProgram(
        SceneCommandBuffer scb,
        TypedDeferredNode<OffThreadNode>[] handles,
        int[] parent,
        NodeSpec[] specs)
    {
        var n = handles.Length;
        for (var i = 0; i < n; i++)
        {
            if (scb.TryRecordCreateNode<OffThreadNode>(w => new OffThreadNode(w), out handles[i])
                != ResultCode.Ok)
            {
                return false;
            }

            if (!ApplyProperties(scb, handles[i], specs[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < n; i++)
        {
            if (parent[i] >= 0
                && scb.RecordAddChild(handles[parent[i]], handles[i]) != ResultCode.Ok)
            {
                return false;
            }
        }

        return true;
    }

    // Applies the recorded name / local-transform / renderable specification to the given handle.
    private static bool ApplyProperties(SceneCommandBuffer scb, DeferredNode handle, NodeSpec spec)
    {
        if (spec.Name is not null
            && scb.RecordName(handle, spec.Name) != ResultCode.Ok)
        {
            return false;
        }

        if (spec.Transform is { } tf
            && scb.RecordLocalTransform(handle, in tf) != ResultCode.Ok)
        {
            return false;
        }

        if (spec.Renderable is { } r
            && scb.RecordRenderable(handle, r) != ResultCode.Ok)
        {
            return false;
        }

        return true;
    }

    private static NodeSpec MakeSpec(Random rng, int index)
    {
        // Name: 0 => not recorded, 1 => recorded empty, 2 => recorded non-empty.
        string? name = rng.Next(0, 3) switch
        {
            0 => null,
            1 => string.Empty,
            _ => $"Node_{index}_{rng.Next(0, 1000)}",
        };

        // Transform: ~50% chance of a recorded random local transform, otherwise none.
        Transform? transform = rng.Next(0, 2) == 0 ? null : MakeRandomTransform(rng);

        // Renderable: 0 => not recorded, 1 => recorded true, 2 => recorded false.
        bool? renderable = rng.Next(0, 3) switch
        {
            0 => null,
            1 => true,
            _ => false,
        };

        return new NodeSpec(name, transform, renderable);
    }

    private static Transform MakeRandomTransform(Random rng)
    {
        return new Transform
        {
            Scale = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Translation = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Rotation = new Quaternion(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
        };
    }

    private static float RandomFloat(Random rng) => (float)(rng.NextDouble() * 200.0 - 100.0);

    private static Dictionary<Node, int> BuildIndexMap(Node[] nodes)
    {
        var map = new Dictionary<Node, int>(nodes.Length);
        for (var i = 0; i < nodes.Length; i++)
        {
            map[nodes[i]] = i;
        }
        return map;
    }

    private static int ParentIndex(Node node, Dictionary<Node, int> index)
    {
        var parent = node.Parent;
        return parent is not null && index.TryGetValue(parent, out var i) ? i : -1;
    }

    private static HashSet<int> ChildIndices(Node node, Dictionary<Node, int> index)
    {
        var result = new HashSet<int>();
        var children = node.Children;
        if (children is not null)
        {
            foreach (var child in children)
            {
                if (index.TryGetValue(child, out var i))
                {
                    result.Add(i);
                }
            }
        }
        return result;
    }

    private static bool Vec3BitsEqual(Vector3 a, Vector3 b) =>
        FloatBitsEqual(a.X, b.X) && FloatBitsEqual(a.Y, b.Y) && FloatBitsEqual(a.Z, b.Z);

    private static bool QuatBitsEqual(Quaternion a, Quaternion b) =>
        FloatBitsEqual(a.X, b.X) && FloatBitsEqual(a.Y, b.Y)
        && FloatBitsEqual(a.Z, b.Z) && FloatBitsEqual(a.W, b.W);

    private static bool MatrixBitsEqual(Matrix4x4 a, Matrix4x4 b)
    {
        return FloatBitsEqual(a.M11, b.M11) && FloatBitsEqual(a.M12, b.M12)
            && FloatBitsEqual(a.M13, b.M13) && FloatBitsEqual(a.M14, b.M14)
            && FloatBitsEqual(a.M21, b.M21) && FloatBitsEqual(a.M22, b.M22)
            && FloatBitsEqual(a.M23, b.M23) && FloatBitsEqual(a.M24, b.M24)
            && FloatBitsEqual(a.M31, b.M31) && FloatBitsEqual(a.M32, b.M32)
            && FloatBitsEqual(a.M33, b.M33) && FloatBitsEqual(a.M34, b.M34)
            && FloatBitsEqual(a.M41, b.M41) && FloatBitsEqual(a.M42, b.M42)
            && FloatBitsEqual(a.M43, b.M43) && FloatBitsEqual(a.M44, b.M44);
    }

    private static bool FloatBitsEqual(float a, float b) =>
        BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
}
