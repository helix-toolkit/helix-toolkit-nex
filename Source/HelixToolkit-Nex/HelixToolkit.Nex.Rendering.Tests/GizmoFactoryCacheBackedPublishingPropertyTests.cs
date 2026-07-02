using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 2: Setting a gizmo publishes the cached handle set.
/// <para>
/// For any successfully created <see cref="GizmoInstanceHandle"/> set on an entity, the entity's
/// published <see cref="GizmoDrawInfo.Handles"/> is reference-equal to the cached handle set for that
/// definition's shape key, and exactly that one cached set is associated with the entity.
/// </para>
/// <para>
/// The cached set reference is obtained independently by creating a second gizmo with the same mode
/// (hence the same shape key) and setting it on a second entity: both entities must publish the
/// reference-equal cached list instance. "Exactly one cached set" is confirmed by asserting the
/// published set is non-null, <see cref="GizmoDrawInfo.Valid"/>, and matches — in count — the
/// reference <see cref="GizmoModelBuilder"/> output for the mode.
/// </para>
/// **Validates: Requirements 1.3, 2.1**
/// </summary>
[TestClass]
public class GizmoFactoryCacheBackedPublishingPropertyTests
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
    /// Generates a well-formed <see cref="GizmoDefinition"/>. All generated definitions are valid, so
    /// every <see cref="GizmoManager.TryCreateGizmo"/> / <see cref="GizmoManager.TrySetGizmo"/> call
    /// succeeds. The non-geometry inputs (space, target, pixel size, occlusion) vary independently to
    /// confirm they never affect which cached set is published.
    /// </summary>
    private static Arbitrary<GizmoDefinition> DefinitionArb() =>
        Arb.From(
            from mode in ModeGen()
            from space in SpaceGen()
            from target in Gen.Choose(0, 100_000)
            from pixel in Gen.Choose(1, 4096)
            from occlusion in OcclusionGen()
            select new GizmoDefinition(
                mode,
                space,
                (uint)target,
                new GizmoHandleConfiguration(pixel, occlusion)));

    /// <summary>
    /// Builds the reference handle set the same way the per-frame path does: a fresh list populated
    /// by the matching <see cref="GizmoModelBuilder"/> method for the mode. Used only to confirm the
    /// published set has the expected handle count for the mode.
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
    /// Property 2: Setting a gizmo publishes the cached handle set.
    /// For any valid definition, setting the created gizmo on an entity publishes a valid
    /// <see cref="GizmoDrawInfo"/> whose <see cref="GizmoDrawInfo.Handles"/> is reference-equal to the
    /// shape key's single cached set — observed independently via a second gizmo of the same mode set
    /// on a second entity — and whose handle count equals the reference builder's for that mode.
    ///
    /// **Validates: Requirements 1.3, 2.1**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void SettingGizmo_PublishesReferenceEqualCachedHandleSet()
    {
        Prop.ForAll(DefinitionArb(), definition =>
        {
            using World world = World.CreateWorld();

            var manager = new GizmoManager();

            // Create + set the primary gizmo on its own entity.
            if (!manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle))
            {
                return false;
            }

            Entity entity = world.CreateEntity();
            if (!manager.TrySetGizmo(entity, handle))
            {
                return false;
            }

            if (!entity.TryGet(out GizmoDrawInfo info))
            {
                return false;
            }

            IReadOnlyList<GizmoHandle> published = info.Handles;

            // The published component must be valid with a non-null handle set (Req 2.1).
            if (published is null || !info.Valid)
            {
                return false;
            }

            // Exactly one cached set is associated: the published set matches the reference builder's
            // handle count for the mode — no partial/duplicated set (Req 1.3).
            List<GizmoHandle> reference = BuildReference(definition.Mode);
            if (published.Count != reference.Count)
            {
                return false;
            }

            // Independently obtain the shape key's cached set: a second gizmo with the SAME mode
            // shares the shape key and must publish the reference-equal cached list instance. The
            // non-geometry inputs are varied to prove they do not change which set is cached/published.
            var sibling = new GizmoDefinition(
                definition.Mode,
                definition.Space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World,
                definition.TargetEntityId + 1,
                new GizmoHandleConfiguration(
                    definition.Handles.DesiredPixelSize + 1,
                    definition.Handles.OcclusionMode));

            if (!manager.TryCreateGizmo(sibling, out GizmoInstanceHandle siblingHandle))
            {
                return false;
            }

            Entity siblingEntity = world.CreateEntity();
            if (!manager.TrySetGizmo(siblingEntity, siblingHandle))
            {
                return false;
            }

            if (!siblingEntity.TryGet(out GizmoDrawInfo siblingInfo))
            {
                return false;
            }

            // The cached handle set for this shape key is published by reference to both entities:
            // the entity's Handles is reference-equal to that one cached set (Req 1.3, 2.1).
            return ReferenceEquals(published, siblingInfo.Handles);
        }).QuickCheckThrowOnFailure();
    }
}
