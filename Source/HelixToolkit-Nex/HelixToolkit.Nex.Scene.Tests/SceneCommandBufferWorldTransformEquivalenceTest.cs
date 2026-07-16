using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

[TestClass]
public class SceneCommandBufferWorldTransformEquivalenceTest
{
    [TestMethod]
    public void Property14_FlushedHierarchy_YieldsEquivalentWorldTransforms()
    {
        // Feature: ecs-command-buffer, Property 14: Flushed hierarchy yields equivalent world transforms
        // For any recorded node hierarchy with arbitrary local transforms, after flush followed by
        // SortSceneNodes and UpdateTransforms on the owning thread, each materialized Node has a
        // WorldTransform equal to the world transform produced by directly constructing the
        // equivalent hierarchy and running the same two passes.
        // Validates: Requirements 9.2

        // A test case is a node count plus a seed. The seed deterministically derives, for each
        // node i, a parent index in [-1, i - 1] (-1 means "root"), and a random local Transform.
        // Because every parent index is strictly less than the child index the structure is always
        // an acyclic forest in which each node has at most one parent, and varying parent choices
        // produces forests of varying depth and breadth (flat to deeply nested) by construction.
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
                        // Derive parent assignments and local transforms deterministically.
                        var rng = new Random(t.seed);
                        var parent = new int[t.n];
                        var transforms = new Transform[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            parent[i] = rng.Next(-1, i); // [-1, i - 1]; -1 means root.
                            transforms[i] = MakeRandomTransform(rng);
                        }

                        // ---- Path A: record into a SceneCommandBuffer, then flush onto world A.
                        var scb = new SceneCommandBuffer();
                        var deferred = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            deferred[i] = scb.RecordCreateNode();
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
                                if (scb.RecordAddChild(deferred[parent[i]], deferred[i]) != ResultCode.Ok)
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

                        // Run the two passes on world A on this (owning) thread.
                        bufferWorld.SortSceneNodes();
                        bufferWorld.UpdateTransforms();

                        // ---- Path B: construct the equivalent hierarchy directly on world B,
                        //      applying the same local transforms and the same parent wiring.
                        var directNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            directNodes[i] = new Node(directWorld);
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

                        // ---- Compare: each corresponding node (by creation index) must have an
                        // equal WorldTransform. Both paths apply identical local transforms in the
                        // same way over the identical hierarchy and run the identical two passes,
                        // so the IEEE-754 results are produced by the same operations in the same
                        // order and must match bit-for-bit. Bitwise comparison (rather than an
                        // epsilon) is used deliberately: it is exact when the operations are
                        // identical, and it remains correct in the presence of non-finite values
                        // (a generated transform can overflow to Infinity or produce NaN at deep
                        // nesting, where an epsilon difference would itself be NaN and mis-report).
                        for (var i = 0; i < t.n; i++)
                        {
                            if (!MatrixBitsEqual(bufferNodes[i].WorldTransform.Value, directNodes[i].WorldTransform.Value))
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

    // Exact, NaN/Infinity-safe matrix equality: compares the raw IEEE-754 bit patterns of all
    // sixteen elements. Identical deterministic operations produce identical bits, including for
    // non-finite results where value equality (NaN != NaN) would be unreliable.
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
