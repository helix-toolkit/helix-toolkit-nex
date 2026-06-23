using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using Newtonsoft.Json.Linq;
using Gltf = glTFLoader.Schema.Gltf;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-light-import-cr-fixes
// Property 4 (Preservation): Non-light and invalid-reference inputs unchanged.
//
// PRESERVATION TEST — this test captures the BASELINE behavior that must NOT change across the fix.
// It is written observation-first against the UNFIXED code and MUST PASS on the unfixed code. The
// assertions are deliberately scoped to behaviors that hold on the unfixed code too:
//   * For "no light attached" cases (3.1, 3.2) the bug condition does NOT hold, so the importer
//     never calls AttachLightComponent — the referencing node carries no light component on the
//     unfixed code just as on the fixed code. We therefore assert no light on the referencing node.
//   * For value-parsing / cone-angle preservation (3.3, 3.4) we query the pure LightConverter
//     outputs directly, which are independent of the attachment-target defect.
// We do NOT assert the fixed attachment-target behavior here (that belongs to the exploration test
// LightAttachmentTargetExplorationTests).
//
// DO NOT change production code while writing/maintaining this test.

/// <summary>
/// Property-based preservation tests for Property 4 of the gltf-light-import-cr-fixes feature.
/// For any input where the bug condition does NOT hold — a node without the
/// <c>KHR_lights_punctual</c> extension or without a valid integer <c>light</c> reference (3.1), an
/// out-of-range reference or an in-range reference to a <c>null</c> slot (3.2), and any valid
/// <c>color</c>/<c>intensity</c>/<c>range</c>/valid-cone-angle value (3.3, 3.4) — the importer
/// continues to produce the baseline result: no light attached for non-references, the existing
/// Warning/Error diagnostics for invalid references, the same parsed values and documented defaults,
/// and valid cone angles preserved verbatim.
/// Mirrors the <c>LightAttachmentValuePropertyTests</c> / <c>InvalidLightReferencePropertyTests</c>
/// build harness (in-memory glTF + mock managers) for the attachment cases, and the
/// <c>SpotConeAngleRevalidationExplorationTests</c> LightConverter-direct harness for the
/// value-parsing cases.
/// </summary>
[TestClass]
public class NonLightInvalidReferencePreservationTests
{
    // QuickThrowOnFailure throws (failing the MSTest) when a property is violated, so a surfaced
    // counterexample fails the test instead of only being printed to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure.WithMaxTest(100);

    private const float Tolerance = 1e-5f;

    /// <summary>An invalid <c>type</c> string that <see cref="LightConverter"/> cannot convert,
    /// yielding a <c>null</c> parsed slot at that index.</summary>
    private const string UnconvertibleLightType = "__unknown_light_type__";

    /// <summary>The glTF node index of the single referencing node built by the helpers.</summary>
    private const int NodeIndex = 0;

    #region Attachment harness (3.1, 3.2)

    /// <summary>
    /// Imports an in-memory glTF model carrying the given document-level lights array and a single
    /// node (index 0) whose <c>KHR_lights_punctual</c> node-extension is set by
    /// <paramref name="configureNodeExtension"/> (or omitted entirely when it is <c>null</c>), then
    /// returns the engine node created for that glTF node together with the diagnostics produced.
    /// </summary>
    private static (Node node, List<ImportDiagnostic> Diagnostics) BuildAndImport(
        JArray documentLights,
        Action<Dictionary<string, object>>? configureNodeExtension
    )
    {
        var documentExtension = new JObject { ["lights"] = documentLights };

        var extensions = new Dictionary<string, object>();
        configureNodeExtension?.Invoke(extensions);

        var gltfNode = new GltfNode
        {
            Name = "LightReferenceNode",
            Translation = [0f, 0f, 0f],
            Rotation = [0f, 0f, 0f, 1f],
            Scale = [1f, 1f, 1f],
            Extensions = extensions.Count > 0 ? extensions : null,
        };

        var model = new Gltf
        {
            Nodes = [gltfNode],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
            Extensions = new Dictionary<string, object>
            {
                [LightConverter.ExtensionName] = documentExtension,
            },
        };

        using var world = World.CreateWorld();
        var diagnostics = new List<ImportDiagnostic>();

        var accessorReader = new AccessorReader(model, []);
        using var geoManager = new StubGeometryManager();
        var manifest = new ResourceManifest();
        var meshConverter = new MeshConverter(
            geoManager,
            accessorReader,
            diagnostics,
            manifest,
            MeshConverterTestDefaults.Config,
            MeshConverterTestDefaults.Decoder,
            false
        );

        using var textureRepo = new StubTextureRepository();
        using var samplerRepo = new StubSamplerRepository(
            StubSamplerRepositoryMode.MockContextBacked
        );
        var textureLoader = new TextureLoader(
            textureRepo,
            samplerRepo,
            "C:\\test",
            model,
            [],
            diagnostics,
            manifest,
            Guid.NewGuid().ToString("D")
        );
        using var materialManager = new StubMaterialPropertyManager();
        var materialConverter = new MaterialConverter(
            materialManager,
            textureLoader,
            diagnostics,
            manifest
        );

        var sceneBuilder = new SceneBuilder(
            world,
            meshConverter,
            materialConverter,
            new LightConverter(diagnostics, ImporterConfig.Default),
            diagnostics,
            ImporterConfig.Default
        );

        var root = sceneBuilder.BuildScene(model, 0);
        var lightNode = root!.Children![0]!;
        return (lightNode, diagnostics);
    }

    /// <summary>
    /// Returns whether the node carries any punctual light component (directional, point, or spot)
    /// on its own entity.
    /// </summary>
    private static bool HasAnyLightComponent(Node node) =>
        node.Entity.TryGet<DirectionalLightInfo>(out _)
        || node.Entity.TryGet<RangeLightInfo>(out _);

    /// <summary>
    /// Finds the single reference-resolution diagnostic the SceneBuilder adds for the referencing
    /// node (identified by <c>ElementType == "Node"</c>; converter-level "Light" diagnostics are
    /// excluded).
    /// </summary>
    private static ImportDiagnostic? FindNodeDiagnostic(
        List<ImportDiagnostic> diagnostics,
        int nodeIndex
    ) => diagnostics.SingleOrDefault(d => d.ElementType == "Node" && d.ElementIndex == nodeIndex);

    #endregion

    #region LightConverter-direct harness (3.3, 3.4)

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c> light
    /// (index 0) carrying the given definition, runs the pure <see cref="LightConverter"/>, and
    /// returns the parsed light at index 0.
    /// </summary>
    private static ParsedLight ParseSingleLight(JObject lightDefinition)
    {
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

        // Directional/point/spot definitions are always convertible, so slot 0 is non-null.
        return parsed[0]!.Value;
    }

    private static bool ApproxEqual(Vector3 a, Vector3 b, float tolerance) =>
        MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

    private static bool ApproxEqual(float a, float b, float tolerance) =>
        MathF.Abs(a - b) <= tolerance;

    #endregion

    #region 3.1 — no extension / no valid integer light reference

    /// <summary>The shape of the node-level <c>KHR_lights_punctual</c> reference under test.</summary>
    private enum RefVariant
    {
        /// <summary>The node has no <c>KHR_lights_punctual</c> extension at all.</summary>
        NoExtension,

        /// <summary>The extension object is present but carries no <c>light</c> property.</summary>
        NoLightProperty,

        /// <summary>The <c>light</c> reference is a string (not an integer).</summary>
        LightString,

        /// <summary>The <c>light</c> reference is a floating-point value (not an integer).</summary>
        LightFloat,

        /// <summary>The <c>light</c> reference is explicit JSON <c>null</c> (not an integer).</summary>
        LightNull,
    }

    private static Gen<RefVariant> RefVariantGen() =>
        Gen.Elements(
            RefVariant.NoExtension,
            RefVariant.NoLightProperty,
            RefVariant.LightString,
            RefVariant.LightFloat,
            RefVariant.LightNull
        );

    /// <summary>
    /// Property 4 (3.1): a node with no <c>KHR_lights_punctual</c> extension, or whose extension
    /// lacks a valid integer <c>light</c> reference, gets no light attached to the referencing
    /// node's own entity. A valid directional light occupies document slot 0, so the only reason no
    /// light is attached is the absent/invalid node reference.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [TestMethod]
    public void NoExtensionOrInvalidReference_AttachesNoLight()
    {
        Prop.ForAll(
                Arb.From(RefVariantGen()),
                (RefVariant variant) =>
                {
                    // Document-level lights: a single, convertible directional light at index 0.
                    var documentLights = new JArray { new JObject { ["type"] = "directional" } };

                    Action<Dictionary<string, object>>? configure = variant switch
                    {
                        RefVariant.NoExtension => null,
                        RefVariant.NoLightProperty => ext =>
                            ext[LightConverter.ExtensionName] = new JObject(),
                        RefVariant.LightString => ext =>
                            ext[LightConverter.ExtensionName] = new JObject { ["light"] = "0" },
                        RefVariant.LightFloat => ext =>
                            ext[LightConverter.ExtensionName] = new JObject { ["light"] = 0.5f },
                        RefVariant.LightNull => ext =>
                            ext[LightConverter.ExtensionName] = new JObject
                            {
                                ["light"] = JValue.CreateNull(),
                            },
                        _ => null,
                    };

                    var (node, _) = BuildAndImport(documentLights, configure);

                    // No punctual light component on the referencing node's own entity.
                    return !HasAnyLightComponent(node);
                }
            )
            .Check(FsCheckConfig);
    }

    #endregion

    #region 3.2 — out-of-range / null-slot reference

    /// <summary>
    /// Property 4 (3.2): an out-of-range reference, or an in-range reference to a <c>null</c> slot,
    /// emits the existing Warning/Error diagnostic identifying the node and the offending light
    /// index, and attaches no light to the referencing node.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [TestMethod]
    public void OutOfRangeOrNullSlotReference_EmitsDiagnostic_AndAttachesNoLight()
    {
        // A lights-array validity vector (each slot valid/invalid; may be empty) and a reference
        // index spanning negative, in-range, and past-the-end values; keep only invalid references.
        var inputGen =
            from slots in Gen.ArrayOf(Gen.Elements(true, false))
            from index in Gen.Choose(-3, slots.Length + 3)
            where index < 0 || index >= slots.Length || !slots[index]
            select (slots, index);

        Prop.ForAll(
                Arb.From(inputGen),
                ((bool[] slots, int index) input) =>
                {
                    var documentLights = new JArray();
                    foreach (var valid in input.slots)
                    {
                        documentLights.Add(
                            new JObject
                            {
                                ["type"] = valid ? "directional" : UnconvertibleLightType,
                            }
                        );
                    }

                    var (node, diagnostics) = BuildAndImport(
                        documentLights,
                        ext =>
                            ext[LightConverter.ExtensionName] = new JObject
                            {
                                ["light"] = input.index,
                            }
                    );

                    // (a) No punctual light component is attached to the referencing node.
                    if (HasAnyLightComponent(node))
                    {
                        return false;
                    }

                    bool outOfRange = input.index < 0 || input.index >= input.slots.Length;
                    var expectedSeverity = outOfRange
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Error;

                    var diagnostic = FindNodeDiagnostic(diagnostics, NodeIndex);

                    // (b) Exactly one node-scoped diagnostic of the correct severity is added.
                    if (diagnostic is null || diagnostic.Severity != expectedSeverity)
                    {
                        return false;
                    }

                    // (c) The diagnostic identifies the node by its glTF node index ...
                    if (diagnostic.ElementIndex != NodeIndex)
                    {
                        return false;
                    }

                    // ... and names both the node index and the offending light index.
                    bool namesNode = diagnostic.Message.Contains($"Node {NodeIndex}");
                    bool namesLightIndex = diagnostic.Message.Contains(
                        $"light index {input.index}"
                    );

                    return namesNode && namesLightIndex;
                }
            )
            .Check(FsCheckConfig);
    }

    #endregion

    #region 3.3 — value parsing / defaulting

    private enum Kind
    {
        Directional,
        Point,
        Spot,
    }

    private enum FieldState
    {
        Valid,
        Omitted,
        Invalid,
    }

    private readonly record struct ValueCase(
        Kind Kind,
        FieldState ColorState,
        Vector3 ColorValue,
        FieldState IntensityState,
        float IntensityValue,
        FieldState RangeState,
        float RangeValue
    );

    private static Gen<Vector3> ValidColorGen() =>
        from r in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from g in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from b in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        select new Vector3(r, g, b);

    private static Gen<float> ValidIntensityGen() => Gen.Choose(0, 100000).Select(i => i / 100.0f);

    private static Gen<float> ValidRangeGen() => Gen.Choose(1, 100000).Select(i => i / 100.0f);

    private static Gen<FieldState> FieldStateGen() =>
        Gen.Elements(FieldState.Valid, FieldState.Omitted, FieldState.Invalid);

    private static Gen<ValueCase> ValueCaseGen() =>
        from kind in Gen.Elements(Kind.Directional, Kind.Point, Kind.Spot)
        from colorState in FieldStateGen()
        from colorValue in ValidColorGen()
        from intensityState in FieldStateGen()
        from intensityValue in ValidIntensityGen()
        from rangeState in FieldStateGen()
        from rangeValue in ValidRangeGen()
        select new ValueCase(
            kind,
            colorState,
            colorValue,
            intensityState,
            intensityValue,
            rangeState,
            rangeValue
        );

    /// <summary>
    /// Property 4 (3.3): valid <c>color</c>/<c>intensity</c>/<c>range</c> values are parsed
    /// verbatim, and omitted/invalid values fall back to the documented defaults
    /// (color <c>(1,1,1)</c>, intensity <c>1.0</c>, point/spot range to the config defaults, and
    /// <c>0</c> range for directional lights). Queries the pure <see cref="LightConverter"/>, which
    /// is independent of the attachment-target defect.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [TestMethod]
    public void ValidValuesParsed_AndDefaultsApplied_ForOmittedOrInvalid()
    {
        Prop.ForAll(
                Arb.From(ValueCaseGen()),
                (ValueCase c) =>
                {
                    var lightDefinition = new JObject
                    {
                        ["type"] = c.Kind switch
                        {
                            Kind.Directional => "directional",
                            Kind.Point => "point",
                            Kind.Spot => "spot",
                            _ => "directional",
                        },
                    };

                    // color
                    switch (c.ColorState)
                    {
                        case FieldState.Valid:
                            lightDefinition["color"] = new JArray
                            {
                                c.ColorValue.X,
                                c.ColorValue.Y,
                                c.ColorValue.Z,
                            };
                            break;
                        case FieldState.Invalid:
                            // Wrong-length array → invalid → default color.
                            lightDefinition["color"] = new JArray { 0.5f, 0.5f };
                            break;
                        case FieldState.Omitted:
                            break;
                    }

                    // intensity
                    switch (c.IntensityState)
                    {
                        case FieldState.Valid:
                            lightDefinition["intensity"] = c.IntensityValue;
                            break;
                        case FieldState.Invalid:
                            // Negative → invalid → default intensity.
                            lightDefinition["intensity"] = -1.0f;
                            break;
                        case FieldState.Omitted:
                            break;
                    }

                    // range applies only to point/spot lights
                    if (c.Kind is Kind.Point or Kind.Spot)
                    {
                        switch (c.RangeState)
                        {
                            case FieldState.Valid:
                                lightDefinition["range"] = c.RangeValue;
                                break;
                            case FieldState.Invalid:
                                // Non-positive → invalid → default range.
                                lightDefinition["range"] = 0.0f;
                                break;
                            case FieldState.Omitted:
                                break;
                        }
                    }

                    var parsed = ParseSingleLight(lightDefinition);

                    var expectedColor =
                        c.ColorState == FieldState.Valid ? c.ColorValue : ParsedLight.DefaultColor;
                    var expectedIntensity =
                        c.IntensityState == FieldState.Valid
                            ? c.IntensityValue
                            : ParsedLight.DefaultIntensity;

                    float expectedRange;
                    if (c.Kind == Kind.Directional)
                    {
                        expectedRange = 0f;
                    }
                    else if (c.RangeState == FieldState.Valid)
                    {
                        expectedRange = c.RangeValue;
                    }
                    else
                    {
                        expectedRange =
                            c.Kind == Kind.Point
                                ? ImporterConfig.Default.DefaultPointLightRange
                                : ImporterConfig.Default.DefaultSpotLightRange;
                    }

                    return ApproxEqual(parsed.Color, expectedColor, Tolerance)
                        && ApproxEqual(parsed.Intensity, expectedIntensity, Tolerance)
                        && ApproxEqual(parsed.Range, expectedRange, Tolerance);
                }
            )
            .Check(FsCheckConfig);
    }

    #endregion

    #region 3.4 — valid spot cone angles preserved verbatim

    // Valid spot cone angles: 0 <= inner < outer <= PI/2 (radians), both present.
    private static Gen<(float Inner, float Outer)> ValidConeAnglesGen() =>
        from outerMilli in Gen.Choose(200, 1570) // 0.200 .. 1.570 rad (< PI/2 ≈ 1.5708)
        let outer = outerMilli / 1000.0f
        from innerFrac in Gen.Choose(0, 900).Select(i => i / 1000.0f) // 0 .. 0.9 of outer
        let inner = outer * innerFrac
        select (inner, outer);

    /// <summary>
    /// Property 4 (3.4): valid spot cone angles with <c>inner &lt; outer &lt;= PI/2</c> are
    /// preserved verbatim by the <see cref="LightConverter"/>. Queries the pure converter, which is
    /// independent of the attachment-target defect.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [TestMethod]
    public void ValidSpotConeAngles_PreservedVerbatim()
    {
        Prop.ForAll(
                Arb.From(ValidConeAnglesGen()),
                ((float Inner, float Outer) cone) =>
                {
                    var lightDefinition = new JObject
                    {
                        ["type"] = "spot",
                        ["spot"] = new JObject
                        {
                            ["innerConeAngle"] = cone.Inner,
                            ["outerConeAngle"] = cone.Outer,
                        },
                    };

                    var parsed = ParseSingleLight(lightDefinition);

                    return ApproxEqual(parsed.InnerConeAngle, cone.Inner, Tolerance)
                        && ApproxEqual(parsed.OuterConeAngle, cone.Outer, Tolerance)
                        && parsed.InnerConeAngle < parsed.OuterConeAngle;
                }
            )
            .Check(FsCheckConfig);
    }

    #endregion
}
