using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 6: Definition change rebuilds and republishes.
/// <para>
/// For any tracked gizmo whose definition changes to one with a different shape key, the manager
/// rebuilds (or reuses the cache for) the new shape and, within the same
/// <see cref="GizmoManager.TryUpdateDefinition"/> call, publishes a <see cref="GizmoDrawInfo"/> whose
/// <see cref="GizmoDrawInfo.Handles"/> references the new shape's cached set.
/// </para>
/// <para>
/// The shape key is derived from <see cref="GizmoMode"/> alone, so a definition change that changes
/// the mode is a shape-key change; changing only space/target/pixel-size/occlusion is not. The new
/// shape's cached set is obtained independently by creating a second gizmo of the target mode on a
/// second entity, whose published <see cref="GizmoDrawInfo.Handles"/> must be reference-equal to the
/// updated gizmo's republished set.
/// </para>
/// **Validates: Requirements 2.3**
/// </summary>
[TestClass]
public class GizmoFactoryDefinitionChangeRebuildPropertyTests
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

    /// <summary>Generates a well-formed definition for an explicitly chosen mode.</summary>
    private static Gen<GizmoDefinition> DefinitionGenForMode(GizmoMode mode) =>
        from space in SpaceGen()
        from target in Gen.Choose(0, 100_000)
        from pixel in Gen.Choose(1, 4096)
        from occlusion in OcclusionGen()
        select new GizmoDefinition(
            mode,
            space,
            (uint)target,
            new GizmoHandleConfiguration(pixel, occlusion));

    /// <summary>
    /// Generates a pair of valid definitions whose modes differ, so applying the second to a gizmo
    /// created from the first is guaranteed to be a shape-key change.
    /// </summary>
    private static Arbitrary<(GizmoDefinition First, GizmoDefinition Second)> DistinctModePairArb() =>
        Arb.From(
            from modeA in ModeGen()
            from modeB in ModeGen().Where(m => m != modeA)
            from defA in DefinitionGenForMode(modeA)
            from defB in DefinitionGenForMode(modeB)
            select (defA, defB));

    /// <summary>
    /// Generates a definition together with a same-shape-key variant: same mode (hence same shape
    /// key) but differing space/target/pixel-size/occlusion, so applying the variant must reuse the
    /// cached set and trigger no rebuild.
    /// </summary>
    private static Arbitrary<(GizmoDefinition First, GizmoDefinition Variant)> SameShapePairArb() =>
        Arb.From(
            from mode in ModeGen()
            from space in SpaceGen()
            from target in Gen.Choose(0, 100_000)
            from pixel in Gen.Choose(1, 2048)
            from occlusion in OcclusionGen()
            let first = new GizmoDefinition(mode, space, (uint)target, new GizmoHandleConfiguration(pixel, occlusion))
            let variant = new GizmoDefinition(
                mode,
                space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World,
                (uint)target + 1,
                new GizmoHandleConfiguration(pixel + 1, occlusion))
            select (first, variant));

    /// <summary>
    /// Property 6: Definition change rebuilds and republishes.
    /// For any tracked gizmo whose definition changes to a different shape key (different mode),
    /// <see cref="GizmoManager.TryUpdateDefinition"/> republishes — within the same call — a
    /// <see cref="GizmoDrawInfo"/> whose <see cref="GizmoDrawInfo.Handles"/> references the NEW shape's
    /// cached set (reference-equal to an independently-obtained set for the new mode), whose
    /// <see cref="GizmoDrawInfo.Mode"/> is the new mode, and which is NOT reference-equal to the old set.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void DefinitionChange_RepublishesNewShapeCachedSet_WithinSameCall()
    {
        Prop.ForAll(DistinctModePairArb(), pair =>
        {
            (GizmoDefinition defA, GizmoDefinition defB) = pair;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            // Create + set the gizmo with mode A; capture its published (old) cached set reference.
            if (!manager.TryCreateGizmo(defA, out GizmoInstanceHandle handle))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!manager.TrySetGizmo(entity, handle) || !entity.TryGet(out GizmoDrawInfo before))
            {
                return false;
            }

            IReadOnlyList<GizmoHandle> oldSet = before.Handles;
            if (oldSet is null || before.Mode != defA.Mode)
            {
                return false;
            }

            // Independently obtain the NEW shape's cached set via a sibling gizmo of mode B.
            if (!manager.TryCreateGizmo(defB, out GizmoInstanceHandle siblingHandle))
            {
                return false;
            }

            Entity siblingEntity = world.CreateEntity();
            if (!manager.TrySetGizmo(siblingEntity, siblingHandle) ||
                !siblingEntity.TryGet(out GizmoDrawInfo siblingInfo))
            {
                return false;
            }

            IReadOnlyList<GizmoHandle> newShapeCachedSet = siblingInfo.Handles;

            // Change the definition to mode B (a shape-key change).
            if (!manager.TryUpdateDefinition(handle, defB))
            {
                return false;
            }

            // Re-read the component immediately after the single TryUpdateDefinition call: the
            // republish happened within that same call.
            if (!entity.TryGet(out GizmoDrawInfo after))
            {
                return false;
            }

            // The republished Handles reference the NEW shape's cached set, carry the new mode, and
            // are no longer the old set (Requirement 2.3).
            return ReferenceEquals(after.Handles, newShapeCachedSet)
                && after.Mode == defB.Mode
                && !ReferenceEquals(after.Handles, oldSet);
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6 (complement): a same-shape-key definition change reuses the cached set.
    /// For any tracked gizmo whose definition changes only in space/target/pixel-size/occlusion (same
    /// mode, hence same shape key), <see cref="GizmoManager.TryUpdateDefinition"/> keeps the published
    /// <see cref="GizmoDrawInfo.Handles"/> reference-equal to the cached set and performs no rebuild
    /// (<see cref="GizmoManager.HandleSetBuildCount"/> unchanged), while the changed scalar fields
    /// still take effect.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void SameShapeKeyChange_KeepsCachedSet_AndDoesNotRebuild()
    {
        Prop.ForAll(SameShapePairArb(), pair =>
        {
            (GizmoDefinition first, GizmoDefinition variant) = pair;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            if (!manager.TryCreateGizmo(first, out GizmoInstanceHandle handle))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!manager.TrySetGizmo(entity, handle) || !entity.TryGet(out GizmoDrawInfo before))
            {
                return false;
            }

            IReadOnlyList<GizmoHandle> cachedSet = before.Handles;
            int buildsBefore = manager.HandleSetBuildCount;

            // Apply a same-shape-key change (only space/target/pixel/occlusion differ).
            if (!manager.TryUpdateDefinition(handle, variant))
            {
                return false;
            }

            if (!entity.TryGet(out GizmoDrawInfo after))
            {
                return false;
            }

            // No rebuild occurred and the cached set is preserved by reference; the changed scalar
            // field (desired pixel size) still took effect.
            return ReferenceEquals(after.Handles, cachedSet)
                && manager.HandleSetBuildCount == buildsBefore
                && after.DesiredPixelSize == variant.Handles.DesiredPixelSize
                && after.Space == variant.Space;
        }).QuickCheckThrowOnFailure();
    }
}
