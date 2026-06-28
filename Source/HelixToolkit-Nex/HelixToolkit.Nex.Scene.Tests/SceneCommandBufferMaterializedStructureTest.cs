using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

[TestClass]
public class SceneCommandBufferMaterializedStructureTest
{
    [TestMethod]
    public void Property11_MaterializedSceneStructure_MatchesDirectConstruction()
    {
        // Feature: ecs-command-buffer, Property 11: Materialized scene structure matches direct construction
        // For any recorded forest of deferred nodes with parent-child relationships, after
        // flush each materialized Node has the same parent, the same set of children, and the
        // same NodeInfo.Level as the corresponding node produced by constructing the equivalent
        // hierarchy directly with new Node(world) and Node.AddChild on the owning thread.
        // Validates: Requirements 7.3, 7.4, 7.5, 9.1

        // A forest is described by a node count and a seed. The seed deterministically derives,
        // for each node i, a parent index in the range [-1, i - 1] where -1 means "root". Because
        // every parent index is strictly less than the child index, the structure is always an
        // acyclic forest in which each node has at most one parent (respecting the single-parent
        // rule that RecordAddChild / Node.AddChild enforce). Varying the parent choice produces
        // forests of varying depth and breadth, including flat (all roots) and deeply nested
        // (each node parented to the previous) shapes by construction.
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
                        // Derive parent assignments deterministically from the seed.
                        var rng = new Random(t.seed);
                        var parent = new int[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            // parent[i] in [-1, i - 1]; -1 means this node is a root.
                            parent[i] = rng.Next(-1, i);
                        }

                        // ---- Path A: record into a SceneCommandBuffer, then flush onto world A.
                        var scb = new SceneCommandBuffer();
                        var deferred = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            deferred[i] = scb.RecordCreateNode();
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                var code = scb.RecordAddChild(deferred[parent[i]], deferred[i]);
                                if (code != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }
                        }

                        var flush = scb.Flush(bufferWorld);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        var bufferNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            bufferNodes[i] = scb.MaterializedNodes[deferred[i]];
                        }

                        // ---- Path B: construct the equivalent hierarchy directly on world B.
                        var directNodes = new Node[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            directNodes[i] = new Node(directWorld);
                        }
                        for (var i = 0; i < t.n; i++)
                        {
                            if (parent[i] >= 0)
                            {
                                directNodes[parent[i]].AddChild(directNodes[i]);
                            }
                        }

                        // Reverse maps from Node -> creation index, used to compare parent and
                        // children by structural position rather than object identity.
                        var bufferIndex = BuildIndexMap(bufferNodes);
                        var directIndex = BuildIndexMap(directNodes);

                        for (var i = 0; i < t.n; i++)
                        {
                            // Same parent (by structural position).
                            if (ParentIndex(bufferNodes[i], bufferIndex) != ParentIndex(directNodes[i], directIndex))
                            {
                                return false;
                            }

                            // Same set of children (by structural position).
                            var bufferChildren = ChildIndices(bufferNodes[i], bufferIndex);
                            var directChildren = ChildIndices(directNodes[i], directIndex);
                            if (!bufferChildren.SetEquals(directChildren))
                            {
                                return false;
                            }

                            // Same NodeInfo.Level.
                            if (bufferNodes[i].Info.Level != directNodes[i].Info.Level)
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

    private static Dictionary<Node, int> BuildIndexMap(Node[] nodes)
    {
        var map = new Dictionary<Node, int>(nodes.Length);
        for (var i = 0; i < nodes.Length; i++)
        {
            map[nodes[i]] = i;
        }
        return map;
    }

    // Returns the creation index of the node's parent, or -1 when the node has no parent.
    private static int ParentIndex(Node node, Dictionary<Node, int> index)
    {
        var parent = node.Parent;
        return parent is not null && index.TryGetValue(parent, out var i) ? i : -1;
    }

    // Returns the set of creation indices of the node's children.
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
