using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 11: Picks and drags are isolated to a single
/// gizmo.
/// <para>
/// For any collection of tracked gizmos, a pick/drag that resolves to exactly one gizmo mutates only
/// that gizmo's state and leaves every other tracked gizmo's state unchanged; and a pick that
/// resolves to no tracked gizmo leaves every tracked gizmo's state unchanged.
/// </para>
/// <para>
/// Each gizmo's observable state is its published <see cref="GizmoDrawInfo"/> component — the
/// <see cref="GizmoDrawInfo.Origin"/>, the <see cref="GizmoDrawInfo.HighlightedHandle"/> overlay, and
/// the cached <see cref="GizmoDrawInfo.Handles"/> reference the pure-consumer render node reads. Three
/// entry points are exercised: routing a decoded pick that resolves to exactly one gizmo
/// (<see cref="GizmoManager.ResolveHighlight"/>, Requirement 7.2), routing a pick that resolves to no
/// tracked gizmo (Requirement 7.8), and beginning/continuing a drag on a single resolved gizmo
/// (<see cref="GizmoManager.BeginDrag(in GizmoPickResolution, in Ray)"/>, Requirement 7.2). In every
/// case only the resolved gizmo may change; all other tracked gizmos are byte-for-byte unchanged.
/// </para>
/// **Validates: Requirements 7.2, 7.8**
/// </summary>
[TestClass]
public class GizmoPickDragIsolationPropertyTests
{
    /// <summary>The three gizmo modes; each distinct mode is one distinct handle-set shape.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; space does not affect the handle geometry.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect the handle geometry.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>Generates a well-formed (always valid) <see cref="GizmoDefinition"/>.</summary>
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
    /// A set of two or more valid definitions (2..6) plus a selector for which tracked gizmo the pick
    /// resolves to and which of its handles it resolves. Two or more gizmos exercise isolation across
    /// instances (Requirement 7.2).
    /// </summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, int TargetPick, int HandlePick)> MultiGizmoArb() =>
        (from count in Gen.Choose(2, 6)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         from targetPick in Gen.Choose(0, 1000)
         from handlePick in Gen.Choose(0, 1000)
         select (defs, targetPick, handlePick)).ToArbitrary();

    /// <summary>
    /// The multi-gizmo scenario plus a flag selecting how a "resolves to nothing" pick is produced:
    /// either a non-gizmo decode, or a gizmo decode against an owning entity that no tracked gizmo
    /// carries. Both must leave every tracked gizmo unchanged (Requirement 7.8 / Property 11).
    /// </summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, int TargetPick, int HandlePick, bool UseNonGizmoDecode)> NoResolutionArb() =>
        (from count in Gen.Choose(2, 6)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         from targetPick in Gen.Choose(0, 1000)
         from handlePick in Gen.Choose(0, 1000)
         from useNonGizmo in Gen.Elements(true, false)
         select (defs, targetPick, handlePick, useNonGizmo)).ToArbitrary();

    /// <summary>The multi-gizmo scenario plus two random pointer-ray positions for a drag.</summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, int TargetPick, int HandlePick, Vector3 BeginPos, Vector3 UpdatePos)> DragScenarioArb() =>
        (from count in Gen.Choose(2, 6)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         from targetPick in Gen.Choose(0, 1000)
         from handlePick in Gen.Choose(0, 1000)
         from beginPos in PositionGen()
         from updatePos in PositionGen()
         select (defs, targetPick, handlePick, beginPos, updatePos)).ToArbitrary();

    private static Gen<Vector3> PositionGen() =>
        from x in Gen.Choose(-500, 500)
        from y in Gen.Choose(-500, 500)
        from z in Gen.Choose(-500, 500)
        select new Vector3(x, y, z);

    /// <summary>
    /// Property 11 (Req 7.2): a routed pick that resolves to exactly one gizmo highlights only that
    /// gizmo's resolved handle and leaves every other tracked gizmo's published state (origin,
    /// highlight, cached handle-set reference) unchanged.
    ///
    /// **Validates: Requirements 7.2**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void ResolvedPick_MutatesOnlyResolvedGizmo_LeavesOthersUnchanged()
    {
        Prop.ForAll(MultiGizmoArb(), input =>
        {
            (GizmoDefinition[] definitions, int targetPick, int handlePick) = input;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            if (!TrySetupGizmos(world, manager, definitions, out List<Entity> entities))
            {
                return false;
            }

            // Snapshot every gizmo's published state before the pick (all start with no highlight).
            GizmoDrawInfo[] before = SnapshotAll(entities);

            int targetIndex = targetPick % entities.Count;
            Entity targetEntity = entities[targetIndex];
            GizmoHandleId resolvedHandle = before[targetIndex].Handles[handlePick % before[targetIndex].Handles.Count].Id;

            // Route a decoded gizmo pick that resolves to exactly one tracked gizmo's handle.
            manager.ResolveHighlight(isGizmo: true, (uint)targetEntity.Id, resolvedHandle);

            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities[i].TryGet(out GizmoDrawInfo after))
                {
                    return false;
                }

                if (i == targetIndex)
                {
                    // Only the resolved gizmo changes, and only its highlight overlay: the resolved
                    // handle is now highlighted while its origin and cached handle set are untouched.
                    bool highlightSet = after.HighlightedHandle is GizmoHandleId h && h.Equals(resolvedHandle);
                    if (!highlightSet
                        || after.Origin != before[i].Origin
                        || !ReferenceEquals(after.Handles, before[i].Handles))
                    {
                        return false;
                    }
                }
                else if (!StateUnchanged(before[i], after))
                {
                    // Every other tracked gizmo is left completely unchanged (isolation).
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 11 (Req 7.8): a routed pick that resolves to no tracked gizmo — whether a non-gizmo
    /// decode or a gizmo decode against an untracked owning entity — leaves every tracked gizmo's
    /// published state unchanged (with no highlight active, "clear any highlight" is observably a
    /// no-op on every gizmo).
    ///
    /// **Validates: Requirements 7.8**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void PickResolvingToNothing_LeavesAllTrackedGizmosUnchanged()
    {
        Prop.ForAll(NoResolutionArb(), input =>
        {
            (GizmoDefinition[] definitions, int targetPick, int handlePick, bool useNonGizmoDecode) = input;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            if (!TrySetupGizmos(world, manager, definitions, out List<Entity> entities))
            {
                return false;
            }

            GizmoDrawInfo[] before = SnapshotAll(entities);

            int targetIndex = targetPick % entities.Count;
            GizmoHandleId someHandle = before[targetIndex].Handles[handlePick % before[targetIndex].Handles.Count].Id;

            // An owning entity id no tracked gizmo carries: 1 beyond the max tracked id.
            uint untrackedOwningId = 1u;
            foreach (Entity e in entities)
            {
                untrackedOwningId = Math.Max(untrackedOwningId, (uint)e.Id + 1u);
            }

            // Both variants resolve to no tracked gizmo (Property 11 / Requirement 7.8).
            if (useNonGizmoDecode)
            {
                manager.ResolveHighlight(isGizmo: false, untrackedOwningId, someHandle);
            }
            else
            {
                manager.ResolveHighlight(isGizmo: true, untrackedOwningId, someHandle);
            }

            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities[i].TryGet(out GizmoDrawInfo after) || !StateUnchanged(before[i], after))
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 11 (Req 7.2): beginning and continuing a drag from a pick that resolves to exactly one
    /// gizmo binds the active drag to that single gizmo (its owning entity) and leaves every tracked
    /// gizmo's published state — including the dragged gizmo's own component — unchanged, since a drag
    /// produces only a transform delta and never mutates any gizmo's <see cref="GizmoDrawInfo"/>.
    ///
    /// **Validates: Requirements 7.2**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void DragOnResolvedGizmo_IsBoundToThatGizmo_LeavesAllComponentsUnchanged()
    {
        Prop.ForAll(DragScenarioArb(), input =>
        {
            (GizmoDefinition[] definitions, int targetPick, int handlePick, Vector3 beginPos, Vector3 updatePos) = input;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            if (!TrySetupGizmos(world, manager, definitions, out List<Entity> entities))
            {
                return false;
            }

            GizmoDrawInfo[] before = SnapshotAll(entities);

            int targetIndex = targetPick % entities.Count;
            Entity targetEntity = entities[targetIndex];
            uint owningEntityId = (uint)targetEntity.Id;

            // Every published handle is an axis handle (X/Y/Z), so any of them is draggable.
            GizmoHandleId dragHandle = before[targetIndex].Handles[handlePick % before[targetIndex].Handles.Count].Id;
            var resolution = new GizmoPickResolution(owningEntityId, dragHandle);

            // Instances were not update-tracked, so their captured target transform is the identity;
            // the constrained axis is therefore the canonical unit axis of the handle's axis.
            Vector3 axis = UnitAxis(dragHandle.Axis);
            var beginRay = new Ray(beginPos, DragRayDirection(axis));
            var updateRay = new Ray(updatePos, DragRayDirection(axis));

            bool began = manager.BeginDrag(resolution, beginRay);
            if (began)
            {
                // The active drag is bound to exactly the resolved gizmo (isolation, Requirement 7.2).
                if (!manager.IsDragging || manager.DragOwningEntityId != owningEntityId)
                {
                    return false;
                }

                manager.UpdateDrag(updateRay, out _);
                manager.EndDrag();
            }

            // A drag mutates no gizmo's published component: every tracked gizmo (including the
            // dragged one) is left unchanged.
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities[i].TryGet(out GizmoDrawInfo after) || !StateUnchanged(before[i], after))
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Creates and sets each definition on its own fresh entity so every gizmo is independently
    /// tracked and publishes a <see cref="GizmoDrawInfo"/> component with a non-empty handle set.
    /// </summary>
    private static bool TrySetupGizmos(World world, GizmoManager manager, GizmoDefinition[] definitions, out List<Entity> entities)
    {
        entities = new List<Entity>(definitions.Length);
        foreach (GizmoDefinition definition in definitions)
        {
            if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!manager.TrySetGizmo(entity, handle)
                || !entity.TryGet(out GizmoDrawInfo published)
                || published.Handles.Count == 0)
            {
                return false;
            }

            entities.Add(entity);
        }

        return true;
    }

    /// <summary>Snapshots the published <see cref="GizmoDrawInfo"/> of every entity by value.</summary>
    private static GizmoDrawInfo[] SnapshotAll(List<Entity> entities)
    {
        var snapshot = new GizmoDrawInfo[entities.Count];
        for (int i = 0; i < entities.Count; i++)
        {
            entities[i].TryGet(out snapshot[i]);
        }

        return snapshot;
    }

    /// <summary>
    /// Whether the published state (origin, highlight overlay, and cached handle-set reference) is
    /// identical to a previously captured snapshot.
    /// </summary>
    private static bool StateUnchanged(in GizmoDrawInfo before, in GizmoDrawInfo after) =>
        after.Origin == before.Origin
        && Nullable.Equals(after.HighlightedHandle, before.HighlightedHandle)
        && ReferenceEquals(after.Handles, before.Handles);

    private static Vector3 UnitAxis(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    /// <summary>
    /// A pointer-ray direction whose alignment with <paramref name="axis"/> is moderate (dot ≈ 0.45),
    /// so it is neither parallel to the axis (which would make the translate/scale closest-point solve
    /// degenerate) nor perpendicular to it (which would make the rotate in-plane solve degenerate),
    /// letting a drag begin for handles of any mode.
    /// </summary>
    private static Vector3 DragRayDirection(Vector3 axis)
    {
        // A fixed unit vector perpendicular to the given canonical axis.
        Vector3 perp = axis == Vector3.UnitX ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize((axis * 0.5f) + perp);
    }
}
