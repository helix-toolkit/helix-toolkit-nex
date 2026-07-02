using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for handle-id distinctness within a single gizmo.
/// </summary>
[TestClass]
public class GizmoHandleIdDistinctnessPropertyTests
{
    /// <summary>The three gizmo modes produced by the model builder.</summary>
    private static Arbitrary<GizmoMode> GizmoModeArb() =>
        Arb.From(Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale));

    /// <summary>
    /// Builds the handle set for the requested mode via the corresponding
    /// <see cref="GizmoModelBuilder"/> entry point.
    /// </summary>
    private static List<GizmoHandle> Build(GizmoMode mode)
    {
        var handles = new List<GizmoHandle>();
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
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return handles;
    }

    /// <summary>
    /// Property 10: Handle ids are distinct within a gizmo.
    /// For any gizmo produced by the model builder
    /// (<see cref="GizmoModelBuilder.BuildTranslate"/> / <c>BuildRotate</c> / <c>BuildScale</c>),
    /// the <see cref="GizmoHandleId"/> values of its handles are pairwise distinct.
    ///
    /// **Validates: Requirements 9.3**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void BuiltGizmo_HandleIds_ArePairwiseDistinct()
    {
        Prop.ForAll(GizmoModeArb(), mode =>
        {
            var handles = Build(mode);
            var ids = handles.Select(h => h.Id).ToList();
            var distinct = new HashSet<GizmoHandleId>(ids);
            return distinct.Count == ids.Count;
        }).QuickCheckThrowOnFailure();
    }
}
