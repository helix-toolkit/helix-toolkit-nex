using HelixToolkit.Nex.glTF.Internal;
using Newtonsoft.Json.Linq;
using Gltf = glTFLoader.Schema.Gltf;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-light-import-cr-fixes
// Property 2 (Bug Condition): Spot cone angle re-validation yields inner < outer.
//
// EXPLORATION TEST — this test encodes the EXPECTED (fixed) behavior and is intended to FAIL on the
// UNFIXED code. In LightConverter.ParseSpotAngles the inner and outer cone angles are validated
// independently against the counterpart's value-or-default. When one angle is provided and the
// other omitted (cross-default), or when a provided outer angle is itself invalid (e.g. > PI/2) and
// is reset to the default PI/4 AFTER a provided inner angle was accepted against that larger outer,
// the resulting (inner, outer) pair can end up with inner >= outer. There is no final re-validation
// of the pair, so the invariant inner < outer is not guaranteed.
//
// After the fix (task 7.5), ParseSpotAngles re-validates the resulting pair so the returned
// (inner, outer) always satisfies inner < outer, and this test passes.
//
// DO NOT "fix" this test or the code when it fails — the failure is the documented counterexample.

/// <summary>
/// Property-based exploration test for Property 2 of the gltf-light-import-cr-fixes feature.
/// For any authored pair of spot inner/outer cone angles — including the cross-default cases where
/// one angle is provided and the other omitted — the <see cref="LightConverter"/> SHALL produce a
/// final <c>(inner, outer)</c> pair satisfying <c>inner &lt; outer</c>.
///
/// The test drives the pure parsing/validation layer directly: it builds an in-memory
/// <see cref="Gltf"/> model carrying a single document-level <c>KHR_lights_punctual</c> spot light
/// with the authored cone angles, runs <see cref="LightConverter.ParseLights"/>, and inspects the
/// resulting <see cref="ParsedLight"/>'s inner/outer cone angles.
/// </summary>
[TestClass]
public class SpotConeAngleRevalidationExplorationTests
{
    // QuickThrowOnFailure throws (failing the MSTest) when the property is violated, so a surfaced
    // counterexample fails the test instead of only being printed to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure.WithMaxTest(100);

    #region Authored cone-angle case model + generator

    /// <summary>
    /// An authored spot <c>spot.innerConeAngle</c> / <c>spot.outerConeAngle</c> pair. Either angle
    /// may be present or omitted (cross-default cases). Present values span negative, valid, and
    /// above-<c>PI/2</c> ranges to exercise the independent-validation gap.
    /// </summary>
    private readonly record struct SpotConeCase(
        bool InnerPresent,
        float InnerValue,
        bool OuterPresent,
        float OuterValue
    );

    private static Gen<SpotConeCase> SpotConeCaseGen() =>
        from selector in Gen.Choose(0, 9)
        from innerPresent in Gen.Elements(true, false)
        from outerPresent in Gen.Elements(true, false)
            // Angle values in radians spanning [-0.5, 3.0]: negative (invalid), valid (< PI/2), and
            // above PI/2 (invalid outer that is reset to the default PI/4 after the fact).
        from innerMilli in Gen.Choose(-500, 3000)
        from outerMilli in Gen.Choose(-500, 3000)
        select selector switch
        {
            // The concrete cross-default case called out by the task: inner provided (1.2),
            // outer omitted (defaults to PI/4 ≈ 0.785).
            0 => new SpotConeCase(true, 1.2f, false, 0.0f),
            // A known cross-validation case: inner (1.0) is accepted against the provided outer
            // (2.0), but the provided outer is > PI/2 and is reset to the default PI/4 ≈ 0.785,
            // leaving inner (1.0) >= outer (0.785).
            1 => new SpotConeCase(true, 1.0f, true, 2.0f),
            // Wide generated domain across present/omitted and value ranges.
            _ => new SpotConeCase(
                innerPresent,
                innerMilli / 1000.0f,
                outerPresent,
                outerMilli / 1000.0f
            ),
        };

    #endregion

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c> spot
    /// light (index 0) carrying the authored cone angles, runs the pure
    /// <see cref="LightConverter"/>, and returns the parsed light at index 0.
    /// </summary>
    private static ParsedLight ParseSpotLight(SpotConeCase c)
    {
        var spot = new JObject();
        if (c.InnerPresent)
        {
            spot["innerConeAngle"] = c.InnerValue;
        }
        if (c.OuterPresent)
        {
            spot["outerConeAngle"] = c.OuterValue;
        }

        var lightDefinition = new JObject { ["type"] = "spot", ["spot"] = spot };

        var documentExtension = new JObject { ["lights"] = new JArray { lightDefinition } };

        var model = new Gltf
        {
            Extensions = new Dictionary<string, object>
            {
                [LightConverter.ExtensionName] = documentExtension,
            },
        };

        var diagnostics = new List<ImportDiagnostic>();
        var converter = new LightConverter(diagnostics, ImporterConfig.Default);

        var parsed = converter.ParseLights(model);

        // A "spot" light is always convertible, so slot 0 is non-null.
        return parsed[0]!.Value;
    }

    #endregion

    /// <summary>
    /// Property 2 (Bug Condition): the final parsed spot cone-angle pair satisfies
    /// <c>inner &lt; outer</c> for every authored pair, including cross-default cases.
    /// **Validates: Requirements 1.7, 2.7**
    /// </summary>
    [TestMethod]
    public void SpotConeAngles_FinalPair_SatisfiesInnerLessThanOuter()
    {
        Prop.ForAll(
                Arb.From(SpotConeCaseGen()),
                (SpotConeCase c) =>
                {
                    var light = ParseSpotLight(c);
                    return light.InnerConeAngle < light.OuterConeAngle;
                }
            )
            .Check(FsCheckConfig);
    }
}
