using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-rendering, Property 7: Occlusion toggle.
/// <para>
/// Validates that <see cref="GizmoRenderNode.SelectDepthState"/> maps the occlusion mode to the
/// depth state exactly as the design requires under the reversed-Z convention:
/// <c>AlwaysOnTop ⟹ DepthState.Disabled</c>, <c>DepthTested ⟹ DepthState.ReadOnlyInvZ</c>, and the
/// selected depth state never writes scene depth. The mapping is exercised over random sequences of
/// occlusion modes to confirm it is stateless (each mode maps identically regardless of history),
/// which is what makes a runtime mode change take effect on the next rendered frame (Req 8.5).
/// </para>
/// **Validates: Requirements 8.1, 8.2, 8.3**
/// </summary>
[TestClass]
public class GizmoOcclusionTogglePropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>Generates random, non-empty sequences of <see cref="GizmoOcclusionMode"/> values.</summary>
    private static Arbitrary<GizmoOcclusionMode[]> OcclusionModeSequences() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested)
            .ArrayOf()
            .Where(seq => seq.Length > 0)
            .ToArbitrary();

    /// <summary>
    /// Property 7: for a random sequence of occlusion modes, each mode maps to the required depth
    /// state — <c>AlwaysOnTop ⟹ Disabled</c>, <c>DepthTested ⟹ ReadOnlyInvZ</c> — and the selected
    /// depth state never enables depth writes (Req 8.3: gizmos never write scene depth).
    /// **Validates: Requirements 8.1, 8.2, 8.3**
    /// </summary>
    [TestMethod]
    public void SelectDepthState_MapsEachModeCorrectly_AndNeverWritesDepth()
    {
        Prop.ForAll(
                OcclusionModeSequences(),
                (GizmoOcclusionMode[] modes) =>
                {
                    foreach (var mode in modes)
                    {
                        var depthState = GizmoRenderNode.SelectDepthState(mode);

                        // 8.1 / 8.2: exact mode -> depth-state mapping.
                        var expected =
                            mode == GizmoOcclusionMode.AlwaysOnTop
                                ? DepthState.Disabled
                                : DepthState.ReadOnlyInvZ;

                        bool mappingCorrect =
                            depthState.CompareOp == expected.CompareOp
                            && depthState.IsDepthWriteEnabled == expected.IsDepthWriteEnabled;

                        // 8.3: the selected depth store never writes depth, in either mode.
                        bool neverWritesDepth = !depthState.IsDepthWriteEnabled;

                        if (!mappingCorrect || !neverWritesDepth)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            )
            .Check(DefaultConfig);
    }

    /// <summary>
    /// AlwaysOnTop selects the X-ray (depth-test-disabled) state that never writes depth.
    /// **Validates: Requirements 8.1, 8.3**
    /// </summary>
    [TestMethod]
    public void SelectDepthState_AlwaysOnTop_IsDisabledAndReadOnly()
    {
        var depthState = GizmoRenderNode.SelectDepthState(GizmoOcclusionMode.AlwaysOnTop);

        Assert.AreEqual(
            DepthState.Disabled.CompareOp,
            depthState.CompareOp,
            "AlwaysOnTop must disable the depth test (CompareOp.AlwaysPass)."
        );
        Assert.IsFalse(
            depthState.IsDepthWriteEnabled,
            "AlwaysOnTop must never write scene depth."
        );
    }

    /// <summary>
    /// DepthTested selects the read-only reversed-Z state that respects scene depth without writing it.
    /// **Validates: Requirements 8.2, 8.3**
    /// </summary>
    [TestMethod]
    public void SelectDepthState_DepthTested_IsReadOnlyInvZAndReadOnly()
    {
        var depthState = GizmoRenderNode.SelectDepthState(GizmoOcclusionMode.DepthTested);

        Assert.AreEqual(
            DepthState.ReadOnlyInvZ.CompareOp,
            depthState.CompareOp,
            "DepthTested must use a reversed-Z (GreaterEqual) depth test."
        );
        Assert.IsFalse(
            depthState.IsDepthWriteEnabled,
            "DepthTested must respect but never write scene depth."
        );
    }
}
