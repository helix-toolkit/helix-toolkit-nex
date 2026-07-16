using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 5: Steady-state updates preserve the cached
/// handle-set reference (zero re-allocation).
/// <para>
/// For any tracked gizmo whose definition is unchanged, any number of per-frame steady-state updates
/// — gizmo origin changes and active-camera changes via <see cref="GizmoManager.UpdateInstance"/>,
/// and highlight set/clear changes via <see cref="GizmoManager.SetHighlight"/> — leaves
/// <see cref="GizmoManager.HandleSetBuildCount"/> unchanged and keeps the published
/// <see cref="GizmoDrawInfo.Handles"/> reference-equal to the same cached instance across all those
/// frames. Only the <see cref="GizmoDrawInfo.Origin"/> (screen sizing is applied later at draw time)
/// and <see cref="GizmoDrawInfo.HighlightedHandle"/> fields change.
/// </para>
/// **Validates: Requirements 2.2, 2.4, 3.1, 3.2, 3.3, 3.4**
/// </summary>
[TestClass]
public class GizmoFactorySteadyStateReferenceIdentityPropertyTests
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

    /// <summary>Generates a single well-formed (and therefore unchanged-across-frames) definition.</summary>
    private static Gen<GizmoDefinition> DefinitionGen() =>
        from mode in ModeGen()
        from space in SpaceGen()
        from pixel in Gen.Choose(1, 200)
        from occlusion in OcclusionGen()
        select new GizmoDefinition(
            mode,
            space,
            new GizmoHandleConfiguration(pixel, occlusion));

    /// <summary>The kind of steady-state update applied on a given frame.</summary>
    private enum SteadyOpKind
    {
        /// <summary>Change the gizmo origin (target transform) and the active camera.</summary>
        UpdateOriginCamera,

        /// <summary>Set the highlight to one of the gizmo's own handles.</summary>
        SetHighlight,

        /// <summary>Clear the highlight.</summary>
        ClearHighlight,
    }

    /// <summary>One generated per-frame steady-state update against an unchanged definition.</summary>
    private readonly record struct SteadyOp(SteadyOpKind Kind, Vector3 Origin, Vector3 CameraPosition, int HandlePick);

    private static Gen<SteadyOp> OpGen() =>
        from kind in Gen.Elements(SteadyOpKind.UpdateOriginCamera, SteadyOpKind.SetHighlight, SteadyOpKind.ClearHighlight)
        from ox in Gen.Choose(-500, 500)
        from oy in Gen.Choose(-500, 500)
        from oz in Gen.Choose(-500, 500)
        from cx in Gen.Choose(-500, 500)
        from cy in Gen.Choose(-500, 500)
        from cz in Gen.Choose(-500, 500)
        from pick in Gen.Choose(0, 1000)
        select new SteadyOp(kind, new Vector3(ox, oy, oz), new Vector3(cx, cy, cz), pick);

    /// <summary>
    /// Generates a bounded (0..15) sequence of steady-state updates. The definition never changes
    /// across the sequence, so every update is a steady-state frame.
    /// </summary>
    private static Arbitrary<(GizmoDefinition Definition, SteadyOp[] Ops)> ScenarioArb() =>
        Arb.From(
            from definition in DefinitionGen()
            from n in Gen.Choose(0, 15)
            from ops in Gen.ArrayOf(OpGen(), n)
            select (definition, ops));

    /// <summary>
    /// Builds a valid, varying <see cref="CameraParams"/> from a camera position. The exact camera
    /// content is irrelevant to reference identity (sizing is applied later at draw time); it only
    /// needs to change so the property exercises "active-camera changes".
    /// </summary>
    private static CameraParams MakeCamera(Vector3 position)
    {
        Matrix4x4 view = Matrix4x4.CreateTranslation(-position);
        Matrix4x4.Invert(view, out Matrix4x4 invView);
        return new CameraParams(
            view,
            Matrix4x4.Identity,
            invView,
            Matrix4x4.Identity,
            position,
            Vector3.Zero,
            Vector3.UnitY,
            0.1f,
            1000f);
    }

    /// <summary>
    /// Property 5: Steady-state updates preserve the cached handle-set reference (zero re-allocation).
    /// For a single gizmo with an unchanged definition, apply an arbitrary sequence of origin/camera
    /// updates and highlight set/clear changes; after each one assert the published handle-set
    /// reference is identical and <see cref="GizmoManager.HandleSetBuildCount"/> is unchanged, while
    /// the origin and highlight overlay fields correctly reflect the update.
    ///
    /// **Validates: Requirements 2.2, 2.4, 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void SteadyStateUpdates_PreserveCachedHandleSetReference_AndBuildCount()
    {
        Prop.ForAll(ScenarioArb(), scenario =>
        {
            var (definition, ops) = scenario;

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

            // Bind a fixed-transform manipulator so UpdateInstance reads the origin from it; the
            // per-frame origin is driven by updating the manipulator's transform before each update.
            var manipulator = new FixedTransformManipulator(Matrix4x4.Identity);
            if (!manager.BindTarget(handle, manipulator))
            {
                return false;
            }

            if (!entity.TryGet(out GizmoDrawInfo initial) || initial.Handles is null)
            {
                return false;
            }

            // Capture the cached handle-set reference and the build count after the initial publish.
            IReadOnlyList<GizmoHandle> cached = initial.Handles;
            int buildCountAfterSet = manager.HandleSetBuildCount;

            // The set of valid handle ids we may highlight (the gizmo's own handles).
            var handleIds = cached.Select(h => h.Id).ToArray();

            foreach (SteadyOp op in ops)
            {
                GizmoHandleId? expectedHighlight = null;
                bool checkHighlight = false;
                bool checkOrigin = false;
                Vector3 expectedOrigin = default;

                switch (op.Kind)
                {
                    case SteadyOpKind.UpdateOriginCamera:
                        manipulator.Transform = Matrix4x4.CreateTranslation(op.Origin);
                        manager.UpdateInstance(
                            handle,
                            MakeCamera(op.CameraPosition),
                            new Size(1920, 1080));
                        checkOrigin = true;
                        expectedOrigin = op.Origin;
                        break;

                    case SteadyOpKind.SetHighlight:
                        GizmoHandleId picked = handleIds[op.HandlePick % handleIds.Length];
                        manager.SetHighlight(handle, picked);
                        checkHighlight = true;
                        expectedHighlight = picked;
                        break;

                    case SteadyOpKind.ClearHighlight:
                        manager.SetHighlight(handle, null);
                        checkHighlight = true;
                        expectedHighlight = null;
                        break;
                }

                if (!entity.TryGet(out GizmoDrawInfo info))
                {
                    return false;
                }

                // (a) The published handle set stays reference-equal to the cached instance — no
                // rebuild, no per-frame array snapshot (Requirements 2.2, 3.1, 3.2).
                if (!ReferenceEquals(cached, info.Handles))
                {
                    return false;
                }

                // (b) No handle-set build ran during steady-state updates (Requirements 2.4, 3.3, 3.4).
                if (manager.HandleSetBuildCount != buildCountAfterSet)
                {
                    return false;
                }

                // (c) The live fields still reflect the update: origin follows UpdateInstance, and the
                // highlight overlay follows SetHighlight/clear.
                if (checkOrigin && info.Origin != expectedOrigin)
                {
                    return false;
                }

                if (checkHighlight && !Nullable.Equals(info.HighlightedHandle, expectedHighlight))
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }
}
