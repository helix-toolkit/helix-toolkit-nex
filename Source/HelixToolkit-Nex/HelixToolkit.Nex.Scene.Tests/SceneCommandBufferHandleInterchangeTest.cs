using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 7 (feature: engine-node-command-buffer): A typed handle is
/// interchangeable with its untyped <see cref="DeferredNode"/>.
///
/// For any hierarchy/property program (add-child, name, local-transform, renderable) expressed
/// once with <see cref="TypedDeferredNode{T}"/> handles and once with their
/// implicitly-converted <see cref="DeferredNode"/> equivalents, flushing both produces identical
/// materialized world state.
///
/// Validates: Requirements 2.6, 7.1
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferHandleInterchangeTest
{
    /// <summary>
    /// A test-local <see cref="Node"/> subtype unknown to the Scene layer, used to exercise the
    /// typed recording path through <see cref="TypedDeferredNode{T}"/>.
    /// </summary>
    private sealed class InterchangeNode : Node
    {
        public InterchangeNode(World world)
            : base(world)
        {
        }
    }

    // The recorded specification for a single node: a null field means "not recorded".
    private readonly record struct NodeSpec(string? Name, Transform? Transform, bool? Renderable);

    [TestMethod]
    public void Property7_TypedHandle_IsInterchangeableWithUntypedDeferredNode()
    {
        // Feature: engine-node-command-buffer, Property 7
        // For any hierarchy/property program expressed once with TypedDeferredNode<T> handles and
        // once with their implicitly-converted DeferredNode equivalents, flushing both produces
        // identical materialized world state (hierarchy structure, parent/child, names,
        // transforms, renderable presence).
        // Validates: Requirements 2.6, 7.1

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
                    var typedWorld = World.CreateWorld();
                    var untypedWorld = World.CreateWorld();
                    try
                    {
                        // Derive the program deterministically from the seed.
                        var rng = new Random(t.seed);
                        var parent = new int[t.n];
                        var specs = new NodeSpec[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            parent[i] = rng.Next(-1, i); // [-1, i - 1]; -1 means root.
                            specs[i] = MakeSpec(rng, i);
                        }

                        // ---- Path A (typed): record every operation by passing the
                        //      TypedDeferredNode<InterchangeNode> handle directly to the
                        //      DeferredNode-accepting operations (implicit conversion at the call).
                        var typedScb = new SceneCommandBuffer();
                        var typedHandles = new TypedDeferredNode<InterchangeNode>[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            if (typedScb.TryRecordCreateNode<InterchangeNode>(
                                    w => new InterchangeNode(w), out typedHandles[i])
                                != ResultCode.Ok)
                            {
                                return false;
                            }

                            if (!ApplyProperties(typedScb, typedHandles[i], specs[i]))
                            {
                                return false;
                            }
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                // Pass typed handles directly; both convert implicitly.
                                if (typedScb.RecordAddChild(typedHandles[parent[i]], typedHandles[i])
                                    != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }
                        }

                        if (!typedScb.Flush(typedWorld).Success)
                        {
                            return false;
                        }

                        // ---- Path B (untyped): record the identical program, but first convert
                        //      each typed handle to its DeferredNode equivalent and pass that.
                        var untypedScb = new SceneCommandBuffer();
                        var untypedHandles = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            if (untypedScb.TryRecordCreateNode<InterchangeNode>(
                                    w => new InterchangeNode(w), out var typed)
                                != ResultCode.Ok)
                            {
                                return false;
                            }

                            // Implicit conversion: TypedDeferredNode<T> -> DeferredNode.
                            DeferredNode untyped = typed;
                            untypedHandles[i] = untyped;

                            if (!ApplyProperties(untypedScb, untyped, specs[i]))
                            {
                                return false;
                            }
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                if (untypedScb.RecordAddChild(untypedHandles[parent[i]], untypedHandles[i])
                                    != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }
                        }

                        if (!untypedScb.Flush(untypedWorld).Success)
                        {
                            return false;
                        }

                        // ---- Collect the materialized nodes from both paths by creation index.
                        var typedNodes = new Node[t.n];
                        var untypedNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            typedNodes[i] = typedScb.MaterializedNodes[typedHandles[i]];
                            untypedNodes[i] = untypedScb.MaterializedNodes[untypedHandles[i]];
                        }

                        var typedIndex = BuildIndexMap(typedNodes);
                        var untypedIndex = BuildIndexMap(untypedNodes);

                        // ---- Compare the materialized world state node-for-node.
                        for (var i = 0; i < t.n; i++)
                        {
                            // Same concrete runtime type (both materialized through the same path).
                            if (typedNodes[i].GetType() != untypedNodes[i].GetType())
                            {
                                return false;
                            }

                            // Same parent (by structural creation position).
                            if (ParentIndex(typedNodes[i], typedIndex) != ParentIndex(untypedNodes[i], untypedIndex))
                            {
                                return false;
                            }

                            // Same set of children (by structural creation position).
                            if (!ChildIndices(typedNodes[i], typedIndex).SetEquals(ChildIndices(untypedNodes[i], untypedIndex)))
                            {
                                return false;
                            }

                            // Same NodeInfo.Level.
                            if (typedNodes[i].Info.Level != untypedNodes[i].Info.Level)
                            {
                                return false;
                            }

                            // Same name.
                            if (typedNodes[i].Name != untypedNodes[i].Name)
                            {
                                return false;
                            }

                            // Same local transform (bitwise exact: identical operations).
                            ref var typedTf = ref typedNodes[i].Transform;
                            ref var untypedTf = ref untypedNodes[i].Transform;
                            if (!Vec3BitsEqual(typedTf.Scale, untypedTf.Scale)
                                || !Vec3BitsEqual(typedTf.Translation, untypedTf.Translation)
                                || !QuatBitsEqual(typedTf.Rotation, untypedTf.Rotation))
                            {
                                return false;
                            }

                            // Same renderable presence.
                            if (typedNodes[i].IsRenderable != untypedNodes[i].IsRenderable
                                || typedNodes[i].Entity.Has<Renderable>() != untypedNodes[i].Entity.Has<Renderable>())
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        typedWorld.Dispose();
                        untypedWorld.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void TypedAndUntyped_ProduceIdenticalStructure_ConcreteExample()
    {
        // Feature: engine-node-command-buffer, Property 7 (concrete example)
        // A small hierarchy with a name, a transform, and a renderable flag, recorded once via
        // typed handles and once via implicitly-converted DeferredNode handles, materializes to
        // identical world state.
        // Validates: Requirements 2.6, 7.1
        var typedWorld = World.CreateWorld();
        var untypedWorld = World.CreateWorld();
        try
        {
            var tf = new Transform
            {
                Scale = new Vector3(2f, 3f, 4f),
                Translation = new Vector3(5f, 6f, 7f),
                Rotation = Quaternion.Identity,
            };

            // ---- Path A: typed handles passed directly.
            var typedScb = new SceneCommandBuffer();
            var tRoot = typedScb.RecordCreateNode<InterchangeNode>(w => new InterchangeNode(w));
            var tChild = typedScb.RecordCreateNode<InterchangeNode>(w => new InterchangeNode(w));
            Assert.AreEqual(ResultCode.Ok, typedScb.RecordName(tRoot, "root"));
            Assert.AreEqual(ResultCode.Ok, typedScb.RecordLocalTransform(tChild, in tf));
            Assert.AreEqual(ResultCode.Ok, typedScb.RecordRenderable(tChild, true));
            Assert.AreEqual(ResultCode.Ok, typedScb.RecordAddChild(tRoot, tChild));
            Assert.IsTrue(typedScb.Flush(typedWorld).Success);

            // ---- Path B: same program via implicitly-converted DeferredNode handles.
            var untypedScb = new SceneCommandBuffer();
            DeferredNode uRoot = untypedScb.RecordCreateNode<InterchangeNode>(w => new InterchangeNode(w));
            DeferredNode uChild = untypedScb.RecordCreateNode<InterchangeNode>(w => new InterchangeNode(w));
            Assert.AreEqual(ResultCode.Ok, untypedScb.RecordName(uRoot, "root"));
            Assert.AreEqual(ResultCode.Ok, untypedScb.RecordLocalTransform(uChild, in tf));
            Assert.AreEqual(ResultCode.Ok, untypedScb.RecordRenderable(uChild, true));
            Assert.AreEqual(ResultCode.Ok, untypedScb.RecordAddChild(uRoot, uChild));
            Assert.IsTrue(untypedScb.Flush(untypedWorld).Success);

            var tRootNode = typedScb.MaterializedNodes[tRoot];
            var tChildNode = typedScb.MaterializedNodes[tChild];
            var uRootNode = untypedScb.MaterializedNodes[uRoot];
            var uChildNode = untypedScb.MaterializedNodes[uChild];

            Assert.AreEqual(tRootNode.GetType(), uRootNode.GetType());
            Assert.AreEqual(tRootNode.Name, uRootNode.Name);
            Assert.AreEqual(tRootNode.Info.Level, uRootNode.Info.Level);
            Assert.AreEqual(tChildNode.Info.Level, uChildNode.Info.Level);
            Assert.AreEqual(tChildNode.IsRenderable, uChildNode.IsRenderable);
            Assert.AreEqual(tChildNode.Transform.Translation, uChildNode.Transform.Translation);
            Assert.AreSame(tChildNode.Parent, tRootNode);
            Assert.AreSame(uChildNode.Parent, uRootNode);
        }
        finally
        {
            typedWorld.Dispose();
            untypedWorld.Dispose();
        }
    }

    // Applies the recorded name / local-transform / renderable specification to the given handle.
    // The handle is typed as DeferredNode so callers can pass either an explicit DeferredNode or a
    // TypedDeferredNode<T> (which converts implicitly).
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

    private static bool FloatBitsEqual(float a, float b) =>
        BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
}
