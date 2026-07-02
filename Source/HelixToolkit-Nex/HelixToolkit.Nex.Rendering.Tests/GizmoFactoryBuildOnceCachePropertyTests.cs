using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 1: Build-once caching.
/// <para>
/// For any sequence of gizmo creation requests, the underlying handle-set build runs exactly once
/// per distinct shape key, and every request whose definition is equal (or shares the shape key)
/// returns a <see cref="GizmoInstanceHandle"/> referencing the identical cached
/// <see cref="IReadOnlyList{GizmoHandle}"/> instance without triggering an additional build.
/// </para>
/// <para>
/// The shape key is derived from <see cref="GizmoMode"/> alone, so the number of distinct modes in a
/// generated sequence equals the expected <see cref="GizmoManager.HandleSetBuildCount"/>. Reference
/// identity of the cached handle set is observed by setting each created gizmo on a fresh entity and
/// reading back its published <see cref="GizmoDrawInfo.Handles"/>: every gizmo sharing a mode must
/// publish the reference-equal cached list instance.
/// </para>
/// **Validates: Requirements 1.1, 1.2, 2.6**
/// </summary>
[TestClass]
public class GizmoFactoryBuildOnceCachePropertyTests
{
    /// <summary>The three gizmo modes; each distinct mode is one distinct shape key.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; space does not affect the shape key.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect the shape key.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>
    /// Generates a well-formed <see cref="GizmoDefinition"/> whose mode/space/target/pixel-size/
    /// occlusion vary independently. All generated definitions are valid, so every
    /// <see cref="GizmoManager.TryCreateGizmo"/> call succeeds.
    /// </summary>
    private static Gen<GizmoDefinition> DefinitionGen() =>
        from mode in ModeGen()
        from space in SpaceGen()
        from target in Gen.Choose(0, 1000)
        from pixel in Gen.Choose(1, 200)
        from occlusion in OcclusionGen()
        select new GizmoDefinition(
            mode,
            space,
            (uint)target,
            new GizmoHandleConfiguration(pixel, occlusion));

    /// <summary>
    /// Generates a bounded (0..12) random sequence of gizmo creation requests. The bound keeps the
    /// entity count modest while still exercising many repeated shape keys per run.
    /// </summary>
    private static Arbitrary<GizmoDefinition[]> DefinitionSequences() =>
        Gen.Choose(0, 12)
            .SelectMany(n => Gen.ArrayOf(DefinitionGen(), n))
            .ToArbitrary();

    /// <summary>
    /// Property 1: Build-once caching.
    /// For any generated sequence of definitions, after creating and setting them all through one
    /// <see cref="GizmoManager"/>: (1) <see cref="GizmoManager.HandleSetBuildCount"/> equals the
    /// number of distinct shape keys (distinct modes) requested, and (2) every gizmo sharing a mode
    /// publishes the reference-equal cached handle-set instance.
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.6**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void CreatingGizmos_BuildsOncePerDistinctShapeKey_AndSharesCachedHandleSet()
    {
        // A single world is shared across all runs; entities are created per request and the world
        // is disposed at the end so the global world registry slot is released.
        using World world = World.CreateWorld();

        Prop.ForAll(DefinitionSequences(), definitions =>
        {
            var manager = new GizmoManager();

            // The first cached handle-set instance observed for each mode.
            var cachedByMode = new Dictionary<GizmoMode, IReadOnlyList<GizmoHandle>>();
            var distinctModes = new HashSet<GizmoMode>();

            foreach (GizmoDefinition definition in definitions)
            {
                // Every generated definition is valid, so creation must succeed (Req 1.1).
                if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
                {
                    return false;
                }

                distinctModes.Add(definition.Mode);

                // Publish onto a fresh entity so the cached handle set becomes observable.
                Entity entity = world.CreateEntity();
                if (!manager.TrySetGizmo(entity, handle))
                {
                    return false;
                }

                if (!entity.TryGet(out GizmoDrawInfo info) || info.Handles is null)
                {
                    return false;
                }

                IReadOnlyList<GizmoHandle> published = info.Handles;

                // Every request sharing a shape key must reference the identical cached instance
                // (Req 1.2) — no rebuild, no copy.
                if (cachedByMode.TryGetValue(definition.Mode, out IReadOnlyList<GizmoHandle>? first))
                {
                    if (!ReferenceEquals(first, published))
                    {
                        return false;
                    }
                }
                else
                {
                    cachedByMode[definition.Mode] = published;
                }
            }

            // The build ran exactly once per distinct shape key (Req 1.1, 1.2, 2.6).
            return manager.HandleSetBuildCount == distinctModes.Count;
        }).QuickCheckThrowOnFailure();
    }
}
