using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration, Property 4: Invalid definitions are rejected and
/// leave the cache unchanged.
/// <para>
/// For any invalid <see cref="GizmoDefinition"/> — an undefined <see cref="GizmoMode"/>, an undefined
/// <see cref="GizmoSpace"/>, or a non-finite <see cref="GizmoHandleConfiguration.DesiredPixelSize"/>
/// (NaN, +∞, -∞) — <see cref="GizmoManager.TryCreateGizmo"/> returns <see langword="false"/>, yields
/// <see cref="GizmoInstanceHandle.None"/>, and leaves the handle-set cache unchanged.
/// </para>
/// <para>
/// The handle-set cache is private, so <see cref="GizmoManager.HandleSetBuildCount"/> is used as the
/// observation seam: an invalid request must never trigger a build, so the count stays flat across a
/// batch of invalid requests (0), and is left unchanged by invalid requests interleaved with valid
/// creates. <see cref="GizmoDefinition"/> is a value type, so the "null definition" case of the
/// requirement is subsumed by the undefined/non-finite invalid cases modelled here.
/// </para>
/// **Validates: Requirements 1.5**
/// </summary>
[TestClass]
public class GizmoFactoryInvalidDefinitionPropertyTests
{
    /// <summary>Which single attribute of a generated definition is made invalid.</summary>
    private enum InvalidKind
    {
        /// <summary>The mode is an undefined <see cref="GizmoMode"/> value.</summary>
        Mode,

        /// <summary>The space is an undefined <see cref="GizmoSpace"/> value.</summary>
        Space,

        /// <summary>The desired pixel size is non-finite (NaN or ±∞).</summary>
        Pixel,
    }

    /// <summary>The three defined gizmo modes.</summary>
    private static Gen<GizmoMode> ValidModeGen() =>
        Gen.Elements(GizmoMode.Translate, GizmoMode.Rotate, GizmoMode.Scale);

    /// <summary>The two defined reference spaces.</summary>
    private static Gen<GizmoSpace> ValidSpaceGen() =>
        Gen.Elements(GizmoSpace.World, GizmoSpace.Local);

    /// <summary>The two defined occlusion modes; occlusion never affects validity.</summary>
    private static Gen<GizmoOcclusionMode> OcclusionGen() =>
        Gen.Elements(GizmoOcclusionMode.AlwaysOnTop, GizmoOcclusionMode.DepthTested);

    /// <summary>An out-of-range (undefined) <see cref="GizmoMode"/> value, above or below the range.</summary>
    private static Gen<GizmoMode> InvalidModeGen() =>
        Gen.OneOf(Gen.Choose(3, 1000), Gen.Choose(-1000, -1)).Select(i => (GizmoMode)i);

    /// <summary>An out-of-range (undefined) <see cref="GizmoSpace"/> value, above or below the range.</summary>
    private static Gen<GizmoSpace> InvalidSpaceGen() =>
        Gen.OneOf(Gen.Choose(2, 1000), Gen.Choose(-1000, -1)).Select(i => (GizmoSpace)i);

    /// <summary>A finite, positive desired pixel size.</summary>
    private static Gen<float> FinitePixelGen() =>
        Gen.Choose(1, 200).Select(i => (float)i);

    /// <summary>A non-finite desired pixel size: NaN, +∞, or -∞.</summary>
    private static Gen<float> NonFinitePixelGen() =>
        Gen.Elements(float.NaN, float.PositiveInfinity, float.NegativeInfinity);

    /// <summary>
    /// Generates an invalid <see cref="GizmoDefinition"/> by making exactly one attribute invalid
    /// (chosen by <see cref="InvalidKind"/>) while keeping the others well-formed. Every produced
    /// definition therefore has <see cref="GizmoDefinition.IsValid"/> == <see langword="false"/>.
    /// </summary>
    private static Gen<GizmoDefinition> InvalidDefinitionGen() =>
        from kind in Gen.Elements(InvalidKind.Mode, InvalidKind.Space, InvalidKind.Pixel)
        from validMode in ValidModeGen()
        from invalidMode in InvalidModeGen()
        from validSpace in ValidSpaceGen()
        from invalidSpace in InvalidSpaceGen()
        from validPixel in FinitePixelGen()
        from invalidPixel in NonFinitePixelGen()
        from occlusion in OcclusionGen()
        select new GizmoDefinition(
            kind == InvalidKind.Mode ? invalidMode : validMode,
            kind == InvalidKind.Space ? invalidSpace : validSpace,
            new GizmoHandleConfiguration(
                kind == InvalidKind.Pixel ? invalidPixel : validPixel,
                occlusion));

    /// <summary>A well-formed <see cref="GizmoDefinition"/>; every such request succeeds.</summary>
    private static Gen<GizmoDefinition> ValidDefinitionGen() =>
        from mode in ValidModeGen()
        from space in ValidSpaceGen()
        from pixel in FinitePixelGen()
        from occlusion in OcclusionGen()
        select new GizmoDefinition(mode, space, new GizmoHandleConfiguration(pixel, occlusion));

    /// <summary>A bounded (1..12) sequence of invalid definitions.</summary>
    private static Arbitrary<GizmoDefinition[]> InvalidSequences() =>
        Gen.Choose(1, 12)
            .SelectMany(n => Gen.ArrayOf(InvalidDefinitionGen(), n))
            .ToArbitrary();

    /// <summary>One item in an interleaved request stream: either a valid or an invalid definition.</summary>
    private readonly record struct Request(GizmoDefinition Definition, bool Valid);

    private static Gen<Request> RequestGen() =>
        Gen.OneOf(
            ValidDefinitionGen().Select(d => new Request(d, true)),
            InvalidDefinitionGen().Select(d => new Request(d, false)));

    /// <summary>A bounded (0..16) interleaved sequence of valid and invalid requests.</summary>
    private static Arbitrary<Request[]> InterleavedSequences() =>
        Gen.Choose(0, 16)
            .SelectMany(n => Gen.ArrayOf(RequestGen(), n))
            .ToArbitrary();

    /// <summary>
    /// Property 4 (batch): every invalid definition is rejected — <see cref="GizmoManager.TryCreateGizmo"/>
    /// returns <see langword="false"/> and yields <see cref="GizmoInstanceHandle.None"/> — and no build
    /// ever runs, so <see cref="GizmoManager.HandleSetBuildCount"/> stays at 0 throughout and at the end.
    ///
    /// **Validates: Requirements 1.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void InvalidDefinitions_AreRejected_AndLeaveCacheUnchanged()
    {
        Prop.ForAll(InvalidSequences(), definitions =>
        {
            var manager = new GizmoManager();

            foreach (GizmoDefinition definition in definitions)
            {
                bool created = manager.TryCreateGizmo(definition, out GizmoInstanceHandle handle);

                // Rejected with no usable handle (Req 1.5).
                if (created || handle != GizmoInstanceHandle.None || handle.IsValid)
                {
                    return false;
                }

                // No build ran for any invalid request — cache left unchanged (Req 1.5).
                if (manager.HandleSetBuildCount != 0)
                {
                    return false;
                }
            }

            return manager.HandleSetBuildCount == 0;
        }).QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 4 (interleaved): invalid requests interleaved with valid creates never change the cache.
    /// Valid creates advance <see cref="GizmoManager.HandleSetBuildCount"/> by the number of distinct
    /// shape keys (modes) seen; each invalid request is rejected and leaves the count exactly as the
    /// preceding valid creates left it.
    ///
    /// **Validates: Requirements 1.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void InvalidDefinitions_DoNotChangeCache_WhenInterleavedWithValidCreates()
    {
        Prop.ForAll(InterleavedSequences(), requests =>
        {
            var manager = new GizmoManager();
            var distinctValidModes = new HashSet<GizmoMode>();

            foreach (Request request in requests)
            {
                int buildCountBefore = manager.HandleSetBuildCount;
                bool created = manager.TryCreateGizmo(request.Definition, out GizmoInstanceHandle handle);

                if (request.Valid)
                {
                    if (!created || !handle.IsValid)
                    {
                        return false;
                    }

                    distinctValidModes.Add(request.Definition.Mode);
                }
                else
                {
                    // Invalid: rejected, None, and the cache is untouched (Req 1.5).
                    if (created || handle != GizmoInstanceHandle.None
                        || manager.HandleSetBuildCount != buildCountBefore)
                    {
                        return false;
                    }
                }
            }

            // Only the distinct valid shape keys ever triggered a build.
            return manager.HandleSetBuildCount == distinctValidModes.Count;
        }).QuickCheckThrowOnFailure();
    }
}
