using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

[TestClass]
public class SceneCommandBufferMaterializedPropertiesTest
{
    [TestMethod]
    public void Property12_MaterializedNodes_CarryRecordedComponentsAndProperties()
    {
        // Feature: ecs-command-buffer, Property 12: Materialized nodes carry recorded components and properties
        // For any deferred node recorded with an optional name, an optional local transform, and an
        // optional renderable flag, the materialized Node carries the NodeInfo, Transform,
        // WorldTransform, and Parent components, and its name, local transform value, and Renderable
        // presence equal the recorded values.
        // Validates: Requirements 7.4, 8.1, 8.2, 8.3

        // A test case is a node count plus a seed. The seed deterministically derives, for each
        // node, whether a name / local transform / renderable flag is recorded and what value is
        // used. This covers all combinations of "recorded vs. not recorded" by construction:
        //   - name: not recorded | recorded empty | recorded non-empty
        //   - transform: not recorded (identity default) | recorded random transform
        //   - renderable: not recorded | recorded true | recorded false
        var gen =
            from n in Gen.Choose(0, 30)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        var rng = new Random(t.seed);

                        // Derive the recorded specification for each node deterministically.
                        var specs = new NodeSpec[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            specs[i] = MakeSpec(rng, i);
                        }

                        // ---- Record into a SceneCommandBuffer, then flush onto the world.
                        var scb = new SceneCommandBuffer();
                        var deferred = new DeferredNode[t.n];
                        for (var i = 0; i < t.n; i++)
                        {
                            deferred[i] = scb.RecordCreateNode();

                            if (specs[i].Name is not null)
                            {
                                if (scb.RecordName(deferred[i], specs[i].Name!) != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }

                            if (specs[i].Transform is { } tf)
                            {
                                if (scb.RecordLocalTransform(deferred[i], in tf) != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }

                            if (specs[i].Renderable is { } r)
                            {
                                if (scb.RecordRenderable(deferred[i], r) != ResultCode.Ok)
                                {
                                    return false;
                                }
                            }
                        }

                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        for (var i = 0; i < t.n; i++)
                        {
                            var node = scb.MaterializedNodes[deferred[i]];
                            var entity = node.Entity;

                            // Required components are present (set by the Node constructor).
                            if (!entity.Has<NodeInfo>()
                                || !entity.Has<Transform>()
                                || !entity.Has<WorldTransform>()
                                || !entity.Has<Parent>())
                            {
                                return false;
                            }

                            // Name equals the recorded name, or the default empty string when none recorded.
                            var expectedName = specs[i].Name ?? string.Empty;
                            if (node.Name != expectedName)
                            {
                                return false;
                            }

                            // Local transform value equals the recorded transform, or the identity
                            // default (scale=1, translation=0, rotation=identity) when none recorded.
                            var expectedTransform = specs[i].Transform ?? new Transform();
                            ref var actualTransform = ref node.Transform;
                            if (actualTransform.Scale != expectedTransform.Scale
                                || actualTransform.Translation != expectedTransform.Translation
                                || actualTransform.Rotation != expectedTransform.Rotation)
                            {
                                return false;
                            }

                            // Renderable presence matches whether renderable was recorded true.
                            var expectedRenderable = specs[i].Renderable == true;
                            if (node.IsRenderable != expectedRenderable
                                || entity.Has<Renderable>() != expectedRenderable)
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // The recorded specification for a single node: a null field means "not recorded".
    private readonly record struct NodeSpec(string? Name, Transform? Transform, bool? Renderable);

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
        var transform = new Transform
        {
            Scale = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Translation = new Vector3(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
            Rotation = new Quaternion(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
        };
        return transform;
    }

    private static float RandomFloat(Random rng) => (float)(rng.NextDouble() * 200.0 - 100.0);
}
