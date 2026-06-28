using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: engine-node-command-buffer, Property 8: Record-then-flush of a custom node equals
/// direct construction.
///
/// For any custom node recorded through a factory (with optional recorded name, local transform,
/// and renderable flag) and then flushed, the materialized node is identical to one produced by
/// Direct_Construction of the same custom node on the owning thread: identical concrete runtime
/// type, identical set of component types, field-for-field equal component values (excluding
/// entity-identity / world-assigned identifier fields), identical Renderable presence/absence,
/// identical tag components, and identical applied name and local transform.
///
/// Validates: Requirements 3.1, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 7.2
/// </summary>
[TestClass]
public class SceneCommandBufferDirectConstructionEquivalenceTest
{
    // The number of distinct custom-node "kinds" the generator can produce. Each kind exercises a
    // real Engine subtype through its convenience record method and the matching direct constructor.
    private const int KindCount = 12;

    [TestMethod]
    public void Property8_RecordThenFlush_EqualsDirectConstruction()
    {
        // A test case is a custom-node kind plus a seed. The seed deterministically derives the
        // node name, an optional recorded-name override, an optional recorded local transform, an
        // optional recorded renderable flag, and the type-specific component values, so the case
        // space spans every kind with and without each optional recording operation.
        var gen =
            from kind in Gen.Choose(0, KindCount - 1)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (kind, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int kind, int seed) t) =>
                {
                    var bufferWorld = World.CreateWorld();
                    var directWorld = World.CreateWorld();
                    try
                    {
                        var spec = NodeCaseSpec.Derive(t.kind, t.seed);

                        // ---- Path A: record the custom node through its convenience factory,
                        // apply the optional recorded properties, then flush onto world A.
                        var scb = new SceneCommandBuffer();
                        var handle = spec.Record(scb);

                        if (spec.RecordedName is not null
                            && scb.RecordName(handle, spec.RecordedName) != ResultCode.Ok)
                        {
                            return false;
                        }
                        if (spec.LocalTransform is { } tf
                            && scb.RecordLocalTransform(handle, in tf) != ResultCode.Ok)
                        {
                            return false;
                        }
                        if (spec.Renderable is { } r
                            && scb.RecordRenderable(handle, r) != ResultCode.Ok)
                        {
                            return false;
                        }

                        var flush = scb.Flush(bufferWorld);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        var recorded = scb.MaterializedNodes[handle];

                        // ---- Path B: construct the same custom node directly on world B and apply
                        // the same optional properties on the owning thread.
                        var direct = spec.DirectConstruct(directWorld);
                        if (spec.RecordedName is not null)
                        {
                            direct.Name = spec.RecordedName;
                        }
                        if (spec.LocalTransform is { } dtf)
                        {
                            direct.Transform = dtf;
                        }
                        if (spec.Renderable is { } dr)
                        {
                            direct.IsRenderable = dr;
                        }

                        // ---- Compare the two materialized nodes for full equivalence.
                        return NodeEquivalence.AreEquivalent(recorded, direct);
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
}

/// <summary>
/// A deterministically-derived test case describing how to build one custom node both through a
/// recorded factory and through direct construction, plus the optional recorded properties.
/// </summary>
internal sealed class NodeCaseSpec
{
    public required Func<SceneCommandBuffer, DeferredNode> Record { get; init; }
    public required Func<World, Node> DirectConstruct { get; init; }
    public string? RecordedName { get; init; }
    public Transform? LocalTransform { get; init; }
    public bool? Renderable { get; init; }

    public static NodeCaseSpec Derive(int kind, int seed)
    {
        var rng = new Random(seed);
        var name = RandomName(rng);
        var recordedName = rng.Next(0, 2) == 0 ? null : RandomName(rng);
        Transform? transform = rng.Next(0, 2) == 0 ? null : RandomTransform(rng);
        bool? renderable = rng.Next(0, 3) switch
        {
            0 => null,
            1 => true,
            _ => false,
        };

        var (record, direct) = BuildBuilders(kind, name, rng);

        return new NodeCaseSpec
        {
            Record = record,
            DirectConstruct = direct,
            RecordedName = recordedName,
            LocalTransform = transform,
            Renderable = renderable,
        };
    }

    // Builds the matched (record-factory, direct-constructor) pair for a kind. The same captured
    // component value flows into both paths so any difference must come from the buffer mechanism.
    private static (Func<SceneCommandBuffer, DeferredNode> record, Func<World, Node> direct) BuildBuilders(
        int kind,
        string name,
        Random rng
    )
    {
        switch (kind)
        {
            case 0:
                return (scb => scb.RecordCreateMeshNode(name), w => new MeshNode(w, name));
            case 1:
            {
                var info = MakeMeshInfo(rng);
                return (scb => scb.RecordCreateMeshNode(name, info), w => new MeshNode(w, name, info));
            }
            case 2:
                return (scb => scb.RecordCreateLineNode(name), w => new LineNode(w, name));
            case 3:
            {
                var info = MakeLineInfo(rng);
                return (scb => scb.RecordCreateLineNode(name, info), w => new LineNode(w, name, info));
            }
            case 4:
                return (scb => scb.RecordCreateDirectionalLight(name), w => new DirectionalLightNode(w, name));
            case 5:
            {
                var info = MakeDirectionalLightInfo(rng);
                return (
                    scb => scb.RecordCreateDirectionalLight(name, info),
                    w => new DirectionalLightNode(w, name, info)
                );
            }
            case 6:
                return (scb => scb.RecordCreatePointLight(name), w => new PointLightNode(w, name));
            case 7:
                return (scb => scb.RecordCreateSpotLight(name), w => new SpotLightNode(w, name));
            case 8:
                return (scb => scb.RecordCreateBillboardNode(name), w => new BillboardNode(w, name));
            case 9:
            {
                var info = MakeBillboardInfo(rng);
                return (
                    scb => scb.RecordCreateBillboardNode(name, info),
                    w => new BillboardNode(w, name, info)
                );
            }
            case 10:
                return (scb => scb.RecordCreatePointCloudNode(name), w => new PointCloudNode(w, name));
            default:
            {
                var info = MakePointInfo(rng);
                return (
                    scb => scb.RecordCreatePointCloudNode(name, info),
                    w => new PointCloudNode(w, name, info)
                );
            }
        }
    }

    private static MeshDrawInfo MakeMeshInfo(Random rng) =>
        new(geometry: null, materialProperties: null, instancing: null, cullable: rng.Next(0, 2) == 0, hitable: rng.Next(0, 2) == 0);

    private static LineDrawInfo MakeLineInfo(Random rng) =>
        new(geometry: null, materialTypeName: RandomMaterialName(rng), cullable: rng.Next(0, 2) == 0, hitable: rng.Next(0, 2) == 0)
        {
            LineThickness = RandomFloat(rng),
        };

    private static PointDrawInfo MakePointInfo(Random rng) =>
        new(geometry: null, materialTypeName: RandomMaterialName(rng), cullable: rng.Next(0, 2) == 0, hitable: rng.Next(0, 2) == 0)
        {
            PointSize = RandomFloat(rng),
            FixedSize = rng.Next(0, 2) == 0,
            TextureIndex = (uint)rng.Next(0, 16),
            SamplerIndex = (uint)rng.Next(0, 16),
        };

    private static BillboardDrawInfo MakeBillboardInfo(Random rng) =>
        new()
        {
            FixedSize = rng.Next(0, 2) == 0,
            AxisConstrained = rng.Next(0, 2) == 0,
            Hitable = rng.Next(0, 2) == 0,
            CullDistance = RandomFloat(rng),
        };

    private static DirectionalLightInfo MakeDirectionalLightInfo(Random rng) =>
        new()
        {
            Direction = RandomVector3(rng),
            Intensity = RandomFloat(rng),
        };

    private static string RandomName(Random rng) =>
        rng.Next(0, 3) switch
        {
            0 => string.Empty,
            1 => "Node",
            _ => $"Node_{rng.Next(0, 100000)}",
        };

    private static string RandomMaterialName(Random rng) =>
        rng.Next(0, 3) switch
        {
            0 => "Default",
            1 => "Custom",
            _ => $"Mat_{rng.Next(0, 1000)}",
        };

    private static float RandomFloat(Random rng) => (float)(rng.NextDouble() * 200.0 - 100.0);

    private static Vector3 RandomVector3(Random rng) =>
        new(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng));

    private static Transform RandomTransform(Random rng) =>
        new()
        {
            Scale = RandomVector3(rng),
            Translation = RandomVector3(rng),
            Rotation = new Quaternion(RandomFloat(rng), RandomFloat(rng), RandomFloat(rng), RandomFloat(rng)),
        };
}

/// <summary>
/// Model-based equivalence comparison between a record-then-flush materialized node and a
/// directly-constructed node. Throws a descriptive exception on the first observed divergence so a
/// real implementation defect surfaces with full detail rather than an opaque false.
/// </summary>
internal static class NodeEquivalence
{
    public static bool AreEquivalent(Node recorded, Node direct)
    {
        // (6.1) Identical concrete runtime type.
        if (recorded.GetType() != direct.GetType())
        {
            throw new InvalidOperationException(
                $"Runtime type mismatch: recorded={recorded.GetType().Name}, direct={direct.GetType().Name}"
            );
        }

        // (6.2 / 6.4 / 6.5 / 6.6) Identical set of component types over the known universe,
        // including the Renderable marker and the tag components.
        ComparePresence<NodeInfo>(recorded, direct);
        ComparePresence<Transform>(recorded, direct);
        ComparePresence<WorldTransform>(recorded, direct);
        ComparePresence<Parent>(recorded, direct);
        ComparePresence<NodeName>(recorded, direct);
        ComparePresence<Children>(recorded, direct);
        ComparePresence<Renderable>(recorded, direct);
        ComparePresence<MeshDrawInfo>(recorded, direct);
        ComparePresence<TransparentComponent>(recorded, direct);
        ComparePresence<AlphaMaskComponent>(recorded, direct);
        ComparePresence<LineDrawInfo>(recorded, direct);
        ComparePresence<PointDrawInfo>(recorded, direct);
        ComparePresence<BillboardDrawInfo>(recorded, direct);
        ComparePresence<RangeLightInfo>(recorded, direct);
        ComparePresence<DirectionalLightInfo>(recorded, direct);

        // (7.2) Applied name equals.
        if (recorded.Name != direct.Name)
        {
            throw new InvalidOperationException(
                $"Name mismatch: recorded='{recorded.Name}', direct='{direct.Name}'"
            );
        }

        // (7.2) Applied local transform equals (Scale / Translation / Rotation; the volatile
        // dirty flags and timestamp are intentionally excluded).
        ref var rt = ref recorded.Transform;
        ref var dt = ref direct.Transform;
        if (rt.Scale != dt.Scale || rt.Translation != dt.Translation || rt.Rotation != dt.Rotation)
        {
            throw new InvalidOperationException(
                $"Local transform mismatch: recorded=({rt.Scale},{rt.Translation},{rt.Rotation}), "
                + $"direct=({dt.Scale},{dt.Translation},{dt.Rotation})"
            );
        }

        // NodeInfo.Level and Enabled equal (EntityId / Version are identity / housekeeping fields).
        if (recorded.Info.Level != direct.Info.Level || recorded.Info.Enabled != direct.Info.Enabled)
        {
            throw new InvalidOperationException(
                $"NodeInfo mismatch: recorded=(Level={recorded.Info.Level},Enabled={recorded.Info.Enabled}), "
                + $"direct=(Level={direct.Info.Level},Enabled={direct.Info.Enabled})"
            );
        }

        // WorldTransform equal element-by-element (both identity prior to UpdateTransforms).
        if (recorded.WorldTransform.Value != direct.WorldTransform.Value)
        {
            throw new InvalidOperationException("WorldTransform value mismatch.");
        }

        // (6.3) Field-for-field equal values for every type-specific data component present.
        CompareValue<MeshDrawInfo>(recorded, direct);
        CompareValue<LineDrawInfo>(recorded, direct);
        CompareValue<PointDrawInfo>(recorded, direct);
        CompareValue<BillboardDrawInfo>(recorded, direct);
        CompareValue<RangeLightInfo>(recorded, direct);
        CompareValue<DirectionalLightInfo>(recorded, direct);
        CompareValue<NodeName>(recorded, direct);
        CompareValue<Renderable>(recorded, direct);

        return true;
    }

    private static void ComparePresence<T>(Node recorded, Node direct)
    {
        var ra = recorded.Entity.Has<T>();
        var db = direct.Entity.Has<T>();
        if (ra != db)
        {
            throw new InvalidOperationException(
                $"Component-type presence mismatch for {typeof(T).Name}: recorded={ra}, direct={db}"
            );
        }
    }

    private static void CompareValue<T>(Node recorded, Node direct)
    {
        if (!recorded.Entity.Has<T>() || !direct.Entity.Has<T>())
        {
            return;
        }

        object a = recorded.Entity.Get<T>()!;
        object b = direct.Entity.Get<T>()!;
        if (!DeepEquals(a, b, new HashSet<object>(ReferenceEqualityComparer.Instance)))
        {
            throw new InvalidOperationException(
                $"Component value mismatch for {typeof(T).Name}: recorded={a}, direct={b}"
            );
        }
    }

    // Structural value comparison that masks entity-identity fields and treats shared reference
    // instances as equal, while deep-comparing distinct reference instances (e.g. the light info
    // class is allocated fresh by each constructor) field-by-field.
    private static bool DeepEquals(object? a, object? b, HashSet<object> visited)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }

        var type = a.GetType();
        if (b.GetType() != type)
        {
            return false;
        }

        // Mask entity-identity values.
        if (type == typeof(Entity))
        {
            return true;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return a.Equals(b);
        }

        // Known numeric value types: compare by value (exact).
        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
            || type == typeof(Quaternion) || type == typeof(Matrix4x4))
        {
            return a.Equals(b);
        }

        if (!type.IsValueType && !visited.Add(a))
        {
            return true; // already visiting this reference; avoid cycles
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(Entity) || field.Name.Contains("EntityId"))
            {
                continue; // mask entity-identity / world-assigned identifier fields
            }
            if (!DeepEquals(field.GetValue(a), field.GetValue(b), visited))
            {
                return false;
            }
        }
        return true;
    }
}
