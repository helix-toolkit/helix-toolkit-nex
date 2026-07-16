using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Tests for Property 10 (feature: engine-node-command-buffer): Mixed custom/base hierarchy
/// structure equals direct construction.
///
/// For any recorded forest mixing custom-node (Engine subtypes such as <see cref="MeshNode"/>)
/// and base-<see cref="Node"/> handles with parent-child relationships, after flush each
/// materialized node has the same parent, the same set of children, and the same
/// <c>NodeInfo.Level</c> as the corresponding node produced by constructing the equivalent
/// hierarchy directly with the real constructors and <see cref="Node.AddChild"/> on the
/// owning thread.
///
/// Path A records the forest into a <see cref="SceneCommandBuffer"/> mixing the Engine
/// convenience method <c>RecordCreateMeshNode</c> (custom MeshNode) and the base
/// <c>RecordCreateNode</c> (base Node), then flushes onto world A. Path B constructs the
/// equivalent hierarchy directly (<c>new MeshNode</c> / <c>new Node</c> + <c>AddChild</c>)
/// onto world B. Correspondence is by structural creation index.
///
/// Validates: Requirements 7.3, 7.4
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferMixedHierarchyTest
{
    // Node kind for the generated forest: a base Scene Node, or a custom Engine MeshNode.
    private enum NodeKind
    {
        Base = 0,
        Mesh = 1,
    }

    [TestMethod]
    public void Property10_MixedCustomBaseHierarchy_EqualsDirectConstruction()
    {
        // Feature: engine-node-command-buffer, Property 10
        // For any recorded forest mixing custom-node and base-Node handles with parent-child
        // relationships, after flush each materialized node has the same parent, the same set
        // of children, and the same NodeInfo.Level as the corresponding node produced by
        // constructing the equivalent hierarchy directly with the real constructors and
        // Node.AddChild on the owning thread.
        // Validates: Requirements 7.3, 7.4

        // A program is described by a node count n and a seed. The seed deterministically
        // derives, for each node i: a node kind (base vs custom mesh) and a parent index in
        // [-1, i - 1] (-1 means "root"). Because every parent index is strictly less than the
        // child index, the structure is always an acyclic forest in which each node has at
        // most one parent, exercising forests of varying depth and breadth by construction.
        var gen =
            from n in Gen.Choose(0, 40)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var worldA = World.CreateWorld();
                    var worldB = World.CreateWorld();
                    try
                    {
                        // Derive the program deterministically from the seed.
                        var parent = new int[t.n];
                        var kind = new NodeKind[t.n];
                        var rng = new Random(t.seed);
                        for (var i = 0; i < t.n; i++)
                        {
                            kind[i] = rng.Next(0, 2) == 0 ? NodeKind.Base : NodeKind.Mesh;
                            parent[i] = rng.Next(-1, i); // [-1, i - 1]; -1 means root.
                        }

                        // ---- Path A: record the mixed forest into a command buffer and flush
                        //      onto world A on this (owning) thread.
                        var scb = new SceneCommandBuffer();
                        var handles = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            handles[i] = kind[i] == NodeKind.Mesh
                                ? scb.RecordCreateMeshNode($"mesh_{i}")
                                : scb.RecordCreateNode($"base_{i}");
                        }

                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0
                                && scb.RecordAddChild(handles[parent[i]], handles[i]) != ResultCode.Ok)
                            {
                                return false;
                            }
                        }

                        if (!scb.Flush(worldA).Success)
                        {
                            return false;
                        }

                        // ---- Path B: construct the equivalent hierarchy directly onto world B.
                        var direct = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            direct[i] = kind[i] == NodeKind.Mesh
                                ? new MeshNode(worldB, $"mesh_{i}")
                                : new Node(worldB, $"base_{i}");
                        }

                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                direct[parent[i]].AddChild(direct[i]);
                            }
                        }

                        // ---- Collect the materialized nodes from path A by creation index.
                        var matA = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            matA[i] = scb.MaterializedNodes[handles[i]];
                        }

                        var indexA = BuildIndexMap(matA);
                        var indexB = BuildIndexMap(direct);

                        // ---- Compare node-for-node by structural creation index.
                        for (var i = 0; i < t.n; i++)
                        {
                            // Same concrete runtime type (custom vs base materialized correctly).
                            if (matA[i].GetType() != direct[i].GetType())
                            {
                                return false;
                            }

                            // Same parent (by structural creation position).
                            if (ParentIndex(matA[i], indexA) != ParentIndex(direct[i], indexB))
                            {
                                return false;
                            }

                            // Same set of children (by structural creation position).
                            if (!ChildIndices(matA[i], indexA).SetEquals(ChildIndices(direct[i], indexB)))
                            {
                                return false;
                            }

                            // Same NodeInfo.Level.
                            if (matA[i].Info.Level != direct[i].Info.Level)
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        worldA.Dispose();
                        worldB.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void MixedHierarchy_EqualsDirectConstruction_ConcreteExample()
    {
        // Feature: engine-node-command-buffer, Property 10 (concrete example)
        // A small mixed hierarchy: a base root with a custom MeshNode child that itself has a
        // base grandchild. Recorded-then-flushed structure equals direct construction.
        // Validates: Requirements 7.3, 7.4
        var worldA = World.CreateWorld();
        var worldB = World.CreateWorld();
        try
        {
            // ---- Path A: record + flush.
            var scb = new SceneCommandBuffer();
            var root = scb.RecordCreateNode("root");                 // base Node
            var mesh = scb.RecordCreateMeshNode("mesh");             // custom MeshNode
            var leaf = scb.RecordCreateNode("leaf");                 // base Node
            Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(root, mesh));
            Assert.AreEqual(ResultCode.Ok, scb.RecordAddChild(mesh, leaf));
            Assert.IsTrue(scb.Flush(worldA).Success);

            var rootA = scb.MaterializedNodes[root];
            var meshA = scb.MaterializedNodes[mesh];
            var leafA = scb.MaterializedNodes[leaf];

            // ---- Path B: direct construction.
            var rootB = new Node(worldB, "root");
            var meshB = new MeshNode(worldB, "mesh");
            var leafB = new Node(worldB, "leaf");
            rootB.AddChild(meshB);
            meshB.AddChild(leafB);

            // Concrete runtime types match.
            Assert.AreEqual(rootB.GetType(), rootA.GetType());
            Assert.AreEqual(meshB.GetType(), meshA.GetType());
            Assert.AreEqual(leafB.GetType(), leafA.GetType());
            Assert.IsInstanceOfType(meshA, typeof(MeshNode));

            // Levels match.
            Assert.AreEqual(rootB.Info.Level, rootA.Info.Level);
            Assert.AreEqual(meshB.Info.Level, meshA.Info.Level);
            Assert.AreEqual(leafB.Info.Level, leafA.Info.Level);
            Assert.AreEqual(0, rootA.Info.Level);
            Assert.AreEqual(1, meshA.Info.Level);
            Assert.AreEqual(2, leafA.Info.Level);

            // Parent references match structurally.
            Assert.IsNull(rootA.Parent);
            Assert.AreSame(rootA, meshA.Parent);
            Assert.AreSame(meshA, leafA.Parent);

            // Child sets match structurally.
            Assert.AreEqual(1, rootA.ChildCount);
            Assert.AreSame(meshA, rootA.Children![0]);
            Assert.AreEqual(1, meshA.ChildCount);
            Assert.AreSame(leafA, meshA.Children![0]);
            Assert.AreEqual(0, leafA.ChildCount);
        }
        finally
        {
            worldA.Dispose();
            worldB.Dispose();
        }
    }

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
}
