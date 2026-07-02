using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 7: Removal clears the component and tracking;
/// unknown removal is a no-op.
/// <para>
/// For any tracked gizmo, removing it removes the <see cref="GizmoDrawInfo"/> component from its
/// entity and drops its tracking entry; and for any handle that is not tracked (never created,
/// already removed, or <see cref="GizmoInstanceHandle.None"/>), removal is a no-op that leaves all
/// tracking state unchanged.
/// </para>
/// <para>
/// The observable signal for "tracked" is the presence of a <see cref="GizmoDrawInfo"/> component on
/// the carrier entity: a set gizmo publishes one, a removed gizmo no longer has one, and every gizmo
/// that was not the removal target keeps its component. No-op removals (None, a never-created handle,
/// or an already-removed handle) return <see langword="false"/> and leave every other tracked gizmo's
/// component intact.
/// </para>
/// **Validates: Requirements 2.5, 2.7, 2.8**
/// </summary>
[TestClass]
public class GizmoFactoryRemovalPropertyTests
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

    /// <summary>Generates a well-formed <see cref="GizmoDefinition"/>; all generated definitions are valid.</summary>
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
    /// Generates a non-empty (1..8) sequence of definitions plus the index of the gizmo to remove.
    /// The sequence is non-empty so there is always a valid tracked gizmo to remove.
    /// </summary>
    private static Arbitrary<(GizmoDefinition[] Definitions, int RemoveIndex)> ScenarioArb() =>
        Gen.Choose(1, 8)
            .SelectMany(n =>
                from defs in Gen.ArrayOf(DefinitionGen(), n)
                from removeIndex in Gen.Choose(0, n - 1)
                select (defs, removeIndex))
            .ToArbitrary();

    /// <summary>
    /// Property 7 (removal clears): creating and setting N gizmos on distinct entities, then removing
    /// exactly one, returns <see langword="true"/>, drops that entity's <see cref="GizmoDrawInfo"/>
    /// component, and leaves every other gizmo's component in place.
    ///
    /// **Validates: Requirements 2.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void RemovingTrackedGizmo_ClearsItsComponent_AndLeavesOthersIntact()
    {
        using World world = World.CreateWorld();

        Prop.ForAll(ScenarioArb(), scenario =>
        {
            (GizmoDefinition[] definitions, int removeIndex) = scenario;

            var manager = new GizmoManager();
            var handles = new GizmoInstanceHandle[definitions.Length];
            var entities = new Entity[definitions.Length];

            // Create + set each gizmo on its own fresh entity so every gizmo is independently tracked.
            for (int i = 0; i < definitions.Length; i++)
            {
                if (!manager.TryCreateGizmo(definitions[i], out handles[i]))
                {
                    return false;
                }

                entities[i] = world.CreateEntity();
                if (!manager.TrySetGizmo(entities[i], handles[i]))
                {
                    return false;
                }

                // Precondition: a set gizmo publishes its component.
                if (!entities[i].TryGet(out GizmoDrawInfo _))
                {
                    return false;
                }
            }

            // Removing a tracked gizmo returns true (Requirement 2.5).
            if (!manager.RemoveGizmo(handles[removeIndex]))
            {
                return false;
            }

            // The removed gizmo's entity no longer carries a GizmoDrawInfo component (Requirement 2.5).
            if (entities[removeIndex].TryGet(out GizmoDrawInfo _))
            {
                return false;
            }

            // Every other gizmo's component is left intact (tracking of others unchanged).
            for (int i = 0; i < definitions.Length; i++)
            {
                if (i == removeIndex)
                {
                    continue;
                }

                if (!entities[i].TryGet(out GizmoDrawInfo _))
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 7 (no-op removal): removing <see cref="GizmoInstanceHandle.None"/>, a never-created
    /// handle, or an already-removed handle returns <see langword="false"/> and leaves every tracked
    /// gizmo's component intact.
    ///
    /// **Validates: Requirements 2.7, 2.8**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void RemovingUntrackedHandle_IsNoOp_AndLeavesAllTrackingUnchanged()
    {
        using World world = World.CreateWorld();

        Prop.ForAll(ScenarioArb(), scenario =>
        {
            (GizmoDefinition[] definitions, int removeIndex) = scenario;

            var manager = new GizmoManager();
            var handles = new GizmoInstanceHandle[definitions.Length];
            var entities = new Entity[definitions.Length];

            for (int i = 0; i < definitions.Length; i++)
            {
                if (!manager.TryCreateGizmo(definitions[i], out handles[i]))
                {
                    return false;
                }

                entities[i] = world.CreateEntity();
                if (!manager.TrySetGizmo(entities[i], handles[i]))
                {
                    return false;
                }
            }

            // Remove one gizmo so we can then exercise the "already removed" no-op case with its handle.
            GizmoInstanceHandle removedHandle = handles[removeIndex];
            if (!manager.RemoveGizmo(removedHandle))
            {
                return false;
            }

            // None, a never-created handle, and the already-removed handle are all no-op removals
            // that must return false (Requirements 2.7, 2.8).
            var untracked = new[]
            {
                GizmoInstanceHandle.None,
                new GizmoInstanceHandle(99999),
                removedHandle,
            };

            foreach (GizmoInstanceHandle handle in untracked)
            {
                if (manager.RemoveGizmo(handle))
                {
                    return false;
                }
            }

            // Every gizmo other than the one legitimately removed keeps its component: the no-op
            // removals changed no tracking state (Requirement 2.8).
            for (int i = 0; i < definitions.Length; i++)
            {
                bool shouldHaveComponent = i != removeIndex;
                bool hasComponent = entities[i].TryGet(out GizmoDrawInfo _);
                if (hasComponent != shouldHaveComponent)
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }
}
