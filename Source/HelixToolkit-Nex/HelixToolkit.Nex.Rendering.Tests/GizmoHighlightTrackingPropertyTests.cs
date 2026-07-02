using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 12: Highlight tracks the resolved handle.
/// <para>
/// For any tracked gizmo, setting the highlight to a resolved handle makes that instance's
/// <see cref="GizmoDrawInfo.HighlightedHandle"/> equal that handle, and clearing the highlight
/// (pointer off any handle, or a pick that resolves to nothing) resets
/// <see cref="GizmoDrawInfo.HighlightedHandle"/> to none.
/// </para>
/// <para>
/// The highlight is observed on the published <see cref="GizmoDrawInfo"/> component, which is what the
/// pure-consumer render node reads. Two entry points are exercised: the direct per-instance
/// <see cref="GizmoManager.SetHighlight"/> (hover set/clear, Requirements 7.3, 7.4) and the decoded-pick
/// router <see cref="GizmoManager.ResolveHighlight"/> which highlights only the resolved gizmo's handle
/// and clears all highlights when a pick resolves to nothing (Requirement 7.8).
/// </para>
/// **Validates: Requirements 7.3, 7.4, 7.8**
/// </summary>
[TestClass]
public class GizmoHighlightTrackingPropertyTests
{
    /// <summary>The three gizmo modes; each distinct mode is one distinct handle-set shape.</summary>
    private static Gen<GizmoMode> ModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two reference spaces; space does not affect the handle geometry or highlight.</summary>
    private static Gen<GizmoSpace> SpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two occlusion modes; occlusion does not affect the handle geometry or highlight.</summary>
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
    /// A valid definition paired with a non-negative index used to select one of the gizmo's real
    /// published handles. The index is reduced modulo the handle count at use time so it always
    /// selects an existing handle.
    /// </summary>
    private static Arbitrary<(GizmoDefinition Definition, int HandlePick)> DefinitionWithHandlePickArb() =>
        (from definition in DefinitionGen()
         from pick in Gen.Choose(0, 1000)
         select (definition, pick)).ToArbitrary();

    /// <summary>
    /// A set of two or more valid definitions (2..6) plus a selector for which tracked gizmo the pick
    /// resolves to and which of its handles it resolves. Two or more gizmos exercise highlight
    /// isolation across instances (Requirement 7.8 / 7.2).
    /// </summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, int TargetPick, int HandlePick)> MultiGizmoArb() =>
        (from count in Gen.Choose(2, 6)
         from defs in Gen.ArrayOf(DefinitionGen(), count)
         from targetPick in Gen.Choose(0, 1000)
         from handlePick in Gen.Choose(0, 1000)
         select (defs, targetPick, handlePick)).ToArbitrary();

    /// <summary>Reads the published <see cref="GizmoDrawInfo.HighlightedHandle"/> for an entity.</summary>
    private static GizmoHandleId? PublishedHighlight(Entity entity) =>
        entity.TryGet(out GizmoDrawInfo info) ? info.HighlightedHandle : null;

    /// <summary>
    /// Property 12 (Req 7.3, 7.4): direct per-instance highlight set/clear.
    /// For any tracked gizmo, setting the highlight to one of its real handles publishes a component
    /// whose <see cref="GizmoDrawInfo.HighlightedHandle"/> equals that handle; clearing it publishes a
    /// component whose <see cref="GizmoDrawInfo.HighlightedHandle"/> is <see langword="null"/>.
    ///
    /// **Validates: Requirements 7.3, 7.4**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void SetHighlight_TracksResolvedHandle_AndClearsToNone()
    {
        Prop.ForAll(DefinitionWithHandlePickArb(), input =>
        {
            (GizmoDefinition definition, int handlePick) = input;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!manager.TrySetGizmo(entity, handle))
            {
                return false;
            }

            if (!entity.TryGet(out GizmoDrawInfo published) || published.Handles.Count == 0)
            {
                return false;
            }

            // A freshly set gizmo has no highlight.
            if (PublishedHighlight(entity) is not null)
            {
                return false;
            }

            // Pick a real handle id from the gizmo's published handle set (Req 7.3).
            GizmoHandleId highlightId = published.Handles[handlePick % published.Handles.Count].Id;

            // Setting the highlight makes the published HighlightedHandle equal that handle.
            manager.SetHighlight(handle, highlightId);
            if (PublishedHighlight(entity) is not GizmoHandleId set || !set.Equals(highlightId))
            {
                return false;
            }

            // Clearing the highlight (pointer off any handle) resets it to none (Req 7.4).
            manager.SetHighlight(handle, null);
            return PublishedHighlight(entity) is null;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 12 (Req 7.8): routed-pick highlight isolation and clear-on-no-resolution.
    /// For any set of two or more tracked gizmos, routing a decoded pick that resolves to exactly one
    /// gizmo's handle highlights only that instance (its published <see cref="GizmoDrawInfo.HighlightedHandle"/>
    /// equals the resolved handle) and leaves every other tracked instance's highlight cleared; and a
    /// subsequent pick that resolves to nothing clears every active highlight.
    ///
    /// **Validates: Requirements 7.8**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void ResolveHighlight_IsolatesResolvedGizmo_AndClearsWhenNothingResolves()
    {
        Prop.ForAll(MultiGizmoArb(), input =>
        {
            (GizmoDefinition[] definitions, int targetPick, int handlePick) = input;

            using World world = World.CreateWorld();
            var manager = new GizmoManager();

            var entities = new List<Entity>(definitions.Length);
            foreach (GizmoDefinition definition in definitions)
            {
                if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
                {
                    return false;
                }

                Entity entity = world.CreateEntity();
                if (!manager.TrySetGizmo(entity, handle))
                {
                    return false;
                }

                entities.Add(entity);
            }

            // Choose which tracked gizmo the pick resolves to, and a real handle it exposes.
            int targetIndex = targetPick % entities.Count;
            Entity targetEntity = entities[targetIndex];

            if (!targetEntity.TryGet(out GizmoDrawInfo targetInfo) || targetInfo.Handles.Count == 0)
            {
                return false;
            }

            GizmoHandleId resolvedHandle = targetInfo.Handles[handlePick % targetInfo.Handles.Count].Id;
            uint owningEntityId = (uint)targetEntity.Id;

            // Route a decoded gizmo pick that resolves to exactly one tracked gizmo's handle.
            manager.ResolveHighlight(isGizmo: true, owningEntityId, resolvedHandle);

            // Only the resolved gizmo is highlighted with the resolved handle; every other tracked
            // gizmo's highlight is cleared (isolation, Requirement 7.8 / 7.2).
            for (int i = 0; i < entities.Count; i++)
            {
                GizmoHandleId? highlight = PublishedHighlight(entities[i]);
                if (i == targetIndex)
                {
                    if (highlight is not GizmoHandleId h || !h.Equals(resolvedHandle))
                    {
                        return false;
                    }
                }
                else if (highlight is not null)
                {
                    return false;
                }
            }

            // A subsequent pick that resolves to nothing (non-gizmo decode) clears all highlights
            // (Requirement 7.8).
            manager.ResolveHighlight(isGizmo: false, owningEntityId, resolvedHandle);

            foreach (Entity entity in entities)
            {
                if (PublishedHighlight(entity) is not null)
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }
}
