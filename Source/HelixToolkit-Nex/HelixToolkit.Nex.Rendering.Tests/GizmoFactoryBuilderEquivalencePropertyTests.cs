using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 3: Factory geometry equals the reference builder.
/// <para>
/// For any gizmo mode, the handle set the factory builds (created via
/// <see cref="GizmoManager.TryCreateGizmo"/>, associated with an entity via
/// <see cref="GizmoManager.TrySetGizmo"/>, and read back from the published
/// <see cref="GizmoDrawInfo.Handles"/>) has the same handle count and identical per-handle geometry
/// (<see cref="GizmoHandle.Id"/>, <see cref="GizmoHandle.Shape"/>, <see cref="GizmoHandle.Axis"/>,
/// <see cref="GizmoHandle.LocalTransform"/>) as the reference
/// <see cref="GizmoModelBuilder.BuildTranslate"/>/<see cref="GizmoModelBuilder.BuildRotate"/>/<see cref="GizmoModelBuilder.BuildScale"/>
/// produce for that mode.
/// </para>
/// **Validates: Requirements 1.4, 7.6**
/// </summary>
[TestClass]
public class GizmoFactoryBuilderEquivalencePropertyTests
{
    /// <summary>
    /// Random gizmo definition: a mode in {Translate, Rotate, Scale} plus the non-geometry inputs
    /// (space, target association, handle configuration) varied to confirm they never perturb the
    /// generated geometry. The desired pixel size is kept finite so every definition is valid.
    /// </summary>
    private static Arbitrary<GizmoDefinition> DefinitionArb() =>
        Arb.From(
            from modeIndex in Gen.Choose(0, 2)
            from spaceIndex in Gen.Choose(0, 1)
            from pixels in Gen.Choose(1, 4096)
            from occlusionIndex in Gen.Choose(0, 1)
            select new GizmoDefinition(
                ModeFor(modeIndex),
                SpaceFor(spaceIndex),
                new GizmoHandleConfiguration(pixels, OcclusionFor(occlusionIndex))));

    private static GizmoMode ModeFor(int index) => index switch
    {
        0 => GizmoMode.Translate,
        1 => GizmoMode.Rotate,
        _ => GizmoMode.Scale,
    };

    private static GizmoSpace SpaceFor(int index) => index == 0 ? GizmoSpace.World : GizmoSpace.Local;

    private static GizmoOcclusionMode OcclusionFor(int index) =>
        index == 0 ? GizmoOcclusionMode.AlwaysOnTop : GizmoOcclusionMode.DepthTested;

    /// <summary>
    /// Builds the reference handle set the same way the per-frame path does: a fresh list populated
    /// by the matching <see cref="GizmoModelBuilder"/> method for the mode.
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
    /// Property 3: Factory geometry equals the reference builder.
    /// For any valid definition, the factory-built handle set published on the entity matches the
    /// reference builder for that mode in count and per-handle id/shape/axis/local-transform.
    ///
    /// **Validates: Requirements 1.4, 7.6**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void FactoryHandleSet_MatchesReferenceBuilder_ForEveryMode()
    {
        Prop.ForAll(DefinitionArb(), definition =>
        {
            using World world = World.CreateWorld();
            Entity entity = world.CreateEntity();

            var manager = new GizmoManager();

            // Factory: build + cache the handle set and associate it with the entity.
            if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
            {
                return false;
            }

            if (!manager.TrySetGizmo(entity, handle))
            {
                return false;
            }

            // Read back the factory-published handle set (references the cached set directly).
            if (!entity.TryGet(out GizmoDrawInfo drawInfo))
            {
                return false;
            }

            IReadOnlyList<GizmoHandle> factory = drawInfo.Handles;

            // Reference: independently built via the matching GizmoModelBuilder method.
            List<GizmoHandle> reference = BuildReference(definition.Mode);

            if (factory is null || factory.Count != reference.Count)
            {
                return false;
            }

            for (int i = 0; i < reference.Count; i++)
            {
                GizmoHandle f = factory[i];
                GizmoHandle r = reference[i];

                if (f.Id != r.Id
                    || f.Shape != r.Shape
                    || f.Axis != r.Axis
                    || f.LocalTransform != r.LocalTransform)
                {
                    return false;
                }
            }

            return true;
        }).QuickCheckThrowOnFailure();
    }
}
