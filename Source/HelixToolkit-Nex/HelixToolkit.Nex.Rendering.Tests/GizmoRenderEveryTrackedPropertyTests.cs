using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 10: All tracked gizmos are rendered each frame.
/// <para>
/// For any set of two or more tracked gizmos, the per-frame gather yields every tracked gizmo and its
/// complete (non-degenerate) handle set, so no tracked gizmo's handles are omitted from the frame.
/// </para>
/// <para>
/// The per-frame gather is exercised through the same ECS query mechanism the engine-layer
/// <c>GizmoDataProvider</c> uses — an entity collection over <see cref="GizmoDrawInfo"/> — so this
/// rendering-layer test asserts the publishing contract the gather consumes without depending on the
/// engine assembly. Each gizmo is created via <see cref="GizmoManager.TryCreateGizmo"/> and set on a
/// distinct entity via <see cref="GizmoManager.TrySetGizmo"/>; the gather must then surface every one
/// of those entities exactly once, each carrying a <see cref="GizmoDrawInfo.Valid"/> component whose
/// handle set matches — in count and per-handle geometry — the reference
/// <see cref="GizmoModelBuilder"/> build for that gizmo's mode.
/// </para>
/// **Validates: Requirements 7.1**
/// </summary>
[TestClass]
public class GizmoRenderEveryTrackedPropertyTests
{
    /// <summary>The three gizmo modes; each distinct mode is one distinct handle-set shape.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; space does not affect the published handle geometry.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect the published handle geometry.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>
    /// Generates a well-formed <see cref="GizmoDefinition"/> whose mode/space/target/pixel-size/
    /// occlusion vary independently. Every generated definition is valid, so each
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
    /// Generates a set of two or more gizmo definitions (2..6). The lower bound of two satisfies the
    /// "two or more tracked gizmos" precondition of Property 10.
    /// </summary>
    private static Arbitrary<GizmoDefinition[]> MultiGizmoSets() =>
        (from count in Gen.Choose(2, 6)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         select defs).ToArbitrary();

    /// <summary>
    /// Builds the reference handle set the same way the per-frame path does: a fresh list populated by
    /// the matching <see cref="GizmoModelBuilder"/> method for the mode.
    /// </summary>
    private static List<GizmoHandle> BuildReference(GizmoMode mode)
    {
        List<GizmoHandle> handles = [];
        switch (mode)
        {
            case GizmoMode.Translate:
                GizmoModelBuilder.BuildTranslate(handles);
                break;
            case GizmoMode.Rotate:
                GizmoModelBuilder.BuildRotate(handles);
                break;
            case GizmoMode.Scale:
                GizmoModelBuilder.BuildScale(handles);
                break;
        }

        return handles;
    }

    /// <summary>
    /// Verifies the published handle set is complete: same count and identical per-handle geometry
    /// (id, shape, axis, local transform) as the reference builder for <paramref name="mode"/>, so no
    /// handle is omitted from the gizmo's contribution to the frame.
    /// </summary>
    private static bool HandleSetIsComplete(IReadOnlyList<GizmoHandle> published, GizmoMode mode)
    {
        List<GizmoHandle> reference = BuildReference(mode);

        if (published.Count != reference.Count)
        {
            return false;
        }

        for (int i = 0; i < reference.Count; i++)
        {
            GizmoHandle p = published[i];
            GizmoHandle r = reference[i];

            if (p.Id != r.Id
                || p.Shape != r.Shape
                || p.Axis != r.Axis
                || p.LocalTransform != r.LocalTransform)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 10: All tracked gizmos are rendered each frame.
    /// For any set of two or more gizmos created and set on distinct entities through one
    /// <see cref="GizmoManager"/>, the per-frame gather (an ECS query over
    /// <see cref="GizmoDrawInfo"/>) yields every one of those entities exactly once, each carrying a
    /// valid component whose handle set is the complete set for that gizmo's mode. No tracked gizmo is
    /// omitted.
    ///
    /// **Validates: Requirements 7.1**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void PerFrameGather_YieldsEveryTrackedGizmo_WithCompleteHandleSet()
    {
        Prop.ForAll(MultiGizmoSets(), definitions =>
        {
            // A fresh world per run so the gather observes only this run's gizmos.
            using World world = World.CreateWorld();

            var manager = new GizmoManager();

            // The mode expected for each tracked gizmo's carrier entity.
            var expectedModeByEntityId = new Dictionary<int, GizmoMode>();

            foreach (GizmoDefinition definition in definitions)
            {
                if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
                {
                    return false;
                }

                // Each gizmo is tracked on its own distinct entity.
                Entity entity = world.CreateEntity();
                if (!manager.TrySetGizmo(entity, handle))
                {
                    return false;
                }

                expectedModeByEntityId[entity.Id] = definition.Mode;
            }

            // Every gizmo was set on a distinct entity, so the tracked count equals the input count.
            if (expectedModeByEntityId.Count != definitions.Length)
            {
                return false;
            }

            // Per-frame gather: enumerate every entity carrying a GizmoDrawInfo, mirroring the
            // engine-layer GizmoDataProvider gather over the same component.
            using EntityCollection gathered = world.CreateCollection().Has<GizmoDrawInfo>().Build();

            var gatheredEntityIds = new HashSet<int>();
            foreach (Entity entity in gathered)
            {
                ref readonly GizmoDrawInfo info = ref entity.Get<GizmoDrawInfo>();

                // The gather must only surface valid gizmos with a complete handle set (Req 7.1).
                if (!info.Valid)
                {
                    return false;
                }

                if (!expectedModeByEntityId.TryGetValue(entity.Id, out GizmoMode expectedMode))
                {
                    // An entity the gather surfaced that we never tracked — unexpected.
                    return false;
                }

                if (!HandleSetIsComplete(info.Handles, expectedMode))
                {
                    return false;
                }

                // Each tracked entity must appear exactly once in the gather.
                if (!gatheredEntityIds.Add(entity.Id))
                {
                    return false;
                }
            }

            // No tracked gizmo is omitted: the gather yields exactly the set of tracked entities.
            return gatheredEntityIds.SetEquals(expectedModeByEntityId.Keys);
        }).QuickCheckThrowOnFailure();
    }
}
