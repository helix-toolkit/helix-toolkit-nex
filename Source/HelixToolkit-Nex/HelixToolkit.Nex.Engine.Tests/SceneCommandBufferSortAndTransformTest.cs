using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

[TestClass]
public class SceneCommandBufferSortAndTransformTest
{
    // Number of distinct node kinds mixed into the hierarchy: a base Node plus every Engine
    // custom subtype. Each kind is recorded through its convenience extension method on Path A
    // and built with its real constructor on Path B (Direct_Construction).
    private const int NodeKindCount = 8;

    [TestMethod]
    public void Property11_FlushedMixedHierarchy_YieldsEquivalentSortOrderAndWorldTransforms()
    {
        // Feature: engine-node-command-buffer, Property 11: Flushed custom/mixed hierarchy yields
        // equivalent sort order and world transforms.
        //
        // For any recorded hierarchy containing custom and/or base nodes with arbitrary local
        // transforms, after flush followed by SortSceneNodes and UpdateTransforms on the owning
        // thread, each materialized node occupies the same relative position in the sorted
        // sequence and has a WorldTransform whose every matrix element equals the corresponding
        // element produced by directly constructing the equivalent hierarchy and running the same
        // two passes.
        //
        // Validates: Requirements 8.1, 8.2, 8.3

        // A test case is a node count plus a seed. The seed deterministically derives, for each
        // node i, a node kind (base or one of the custom subtypes), a parent index in [-1, i - 1]
        // (-1 means "root"), and a random local Transform. Because every parent index is strictly
        // less than the child index, the structure is always an acyclic forest in which each node
        // has at most one parent; varying parent choices produces forests of varying depth and
        // breadth (flat to deeply nested) and varying type mixes by construction.
        var gen =
            from n in Gen.Choose(0, 40)
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
                        // Derive node kinds, parent assignments, and local transforms deterministically.
                        var rng = new Random(t.seed);
                        var kind = new int[t.n];
                        var parent = new int[t.n];
                        var transforms = new Transform[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            kind[i] = rng.Next(0, NodeKindCount);
                            parent[i] = rng.Next(-1, i); // [-1, i - 1]; -1 means root.
                            transforms[i] = MakeRandomTransform(rng);
                        }

                        // ---- Path A: record a mixed custom/base hierarchy into a SceneCommandBuffer,
                        //      then flush onto world A.
                        var scb = new SceneCommandBuffer();
                        var deferred = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            deferred[i] = RecordByKind(scb, kind[i], $"node{i}");
                            var tf = transforms[i];
                            if (scb.RecordLocalTransform(deferred[i], in tf) != ResultCode.Ok)
                            {
                                return false;
                            }
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                if (
                                    scb.RecordAddChild(deferred[parent[i]], deferred[i])
                                    != ResultCode.Ok
                                )
                                {
                                    return false;
                                }
                            }
                        }

                        if (!scb.Flush(bufferWorld).Success)
                        {
                            return false;
                        }

                        var bufferNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            bufferNodes[i] = scb.MaterializedNodes[deferred[i]];
                        }

                        // Run the two downstream passes on world A on this (owning) thread.
                        bufferWorld.SortSceneNodes();
                        bufferWorld.UpdateTransforms();

                        // ---- Path B: construct the equivalent hierarchy directly on world B with
                        //      the real constructors, applying the same local transforms and parent wiring.
                        var directNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            directNodes[i] = ConstructByKind(directWorld, kind[i], $"node{i}");
                            directNodes[i].Transform = transforms[i];
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                directNodes[parent[i]].AddChild(directNodes[i]);
                            }
                        }

                        directWorld.SortSceneNodes();
                        directWorld.UpdateTransforms();

                        // ---- Compare 1 (Req 8.1 / 8.3): the SortSceneNodes relative ordering must
                        // match node-for-node by logical creation command. Reading each world's
                        // sorted NodeInfo sequence and mapping every entity back to its creation
                        // index yields the order of creation indices; the two sequences must be equal.
                        var bufferOrder = SortedCreationOrder(bufferWorld, bufferNodes);
                        var directOrder = SortedCreationOrder(directWorld, directNodes);
                        if (!bufferOrder.SequenceEqual(directOrder))
                        {
                            return false;
                        }

                        // ---- Compare 2 (Req 8.2 / 8.3): each corresponding node (by creation index)
                        // must have an equal WorldTransform, element-by-element, within a
                        // floating-point tolerance.
                        for (var i = 0; i < t.n; i++)
                        {
                            if (
                                !MatrixClose(
                                    bufferNodes[i].WorldTransform.Value,
                                    directNodes[i].WorldTransform.Value
                                )
                            )
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

    // Records deferred creation of the node kind through the public/Engine recording API. Every
    // typed handle converts implicitly to the DeferredNode the hierarchy operations consume.
    private static DeferredNode RecordByKind(SceneCommandBuffer scb, int kind, string name) =>
        kind switch
        {
            0 => scb.RecordCreateNode(name),
            1 => scb.RecordCreateMeshNode(name),
            2 => scb.RecordCreateLineNode(name),
            3 => scb.RecordCreateDirectionalLight(name),
            4 => scb.RecordCreatePointLight(name),
            5 => scb.RecordCreateSpotLight(name),
            6 => scb.RecordCreateBillboardNode(name),
            _ => scb.RecordCreatePointCloudNode(name),
        };

    // Builds the same node kind directly on the owning thread with its real constructor.
    private static Node ConstructByKind(World world, int kind, string name) =>
        kind switch
        {
            0 => new Node(world, name),
            1 => new MeshNode(world, name),
            2 => new LineNode(world, name),
            3 => new DirectionalLightNode(world, name),
            4 => new PointLightNode(world, name),
            5 => new SpotLightNode(world, name),
            6 => new BillboardNode(world, name),
            _ => new PointCloudNode(world, name),
        };

    // Reads the world's post-sort NodeInfo order and maps each entry back to the creation index of
    // its node, producing the sequence of creation indices in sorted order.
    private static List<int> SortedCreationOrder(World world, Node[] nodes)
    {
        var entityToIndex = new Dictionary<int, int>(nodes.Length);
        for (var i = 0; i < nodes.Length; i++)
        {
            entityToIndex[nodes[i].Entity.Id] = i;
        }

        var nodeInfos = world.GetComponents<NodeInfo>();
        var order = new List<int>(nodeInfos.Count);
        for (var i = 0; i < nodeInfos.Count; i++)
        {
            ref readonly var info = ref nodeInfos[i];
            if (entityToIndex.TryGetValue(info.EntityId, out var index))
            {
                order.Add(index);
            }
        }
        return order;
    }

    private static Transform MakeRandomTransform(Random rng)
    {
        return new Transform
        {
            Scale = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Translation = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Rotation = new Quaternion(
                RandomFloat(rng),
                RandomFloat(rng),
                RandomFloat(rng),
                RandomFloat(rng)
            ),
        };
    }

    private static float RandomFloat(Random rng) => (float)(rng.NextDouble() * 200.0 - 100.0);

    private static bool MatrixClose(Matrix4x4 a, Matrix4x4 b)
    {
        return FloatClose(a.M11, b.M11)
            && FloatClose(a.M12, b.M12)
            && FloatClose(a.M13, b.M13)
            && FloatClose(a.M14, b.M14)
            && FloatClose(a.M21, b.M21)
            && FloatClose(a.M22, b.M22)
            && FloatClose(a.M23, b.M23)
            && FloatClose(a.M24, b.M24)
            && FloatClose(a.M31, b.M31)
            && FloatClose(a.M32, b.M32)
            && FloatClose(a.M33, b.M33)
            && FloatClose(a.M34, b.M34)
            && FloatClose(a.M41, b.M41)
            && FloatClose(a.M42, b.M42)
            && FloatClose(a.M43, b.M43)
            && FloatClose(a.M44, b.M44);
    }

    // Floating-point tolerance comparison that is also safe for non-finite values. Identical
    // deterministic operations on both paths produce identical bits (the fast path); the relative
    // tolerance covers any benign reordering, while exact-bit and NaN handling keep Infinity/NaN
    // results (possible at deep nesting with large random transforms) from mis-reporting.
    private static bool FloatClose(float a, float b)
    {
        if (BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b))
        {
            return true;
        }
        if (float.IsNaN(a) && float.IsNaN(b))
        {
            return true;
        }
        if (float.IsNaN(a) || float.IsNaN(b))
        {
            return false;
        }
        if (float.IsInfinity(a) || float.IsInfinity(b))
        {
            return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }
        var diff = MathF.Abs(a - b);
        var scale = MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
        return diff <= 1e-3f * scale;
    }
}
