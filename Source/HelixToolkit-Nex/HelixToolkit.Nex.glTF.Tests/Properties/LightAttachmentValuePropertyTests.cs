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
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using Newtonsoft.Json.Linq;
using Gltf = glTFLoader.Schema.Gltf;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-directionallight-render-fix, Property 1: Light attachment preserves parsed values for the matching kind

/// <summary>
/// Property-based test for Property 1 of the gltf-light-import-cr-fixes feature.
/// For any resolvable, convertible <see cref="ParsedLight"/> and any node transform, attaching it
/// produces exactly one engine light component of the matching kind (directional →
/// <see cref="DirectionalLightInfo"/>; point/spot → <see cref="RangeLightInfo"/>) on the
/// referencing node's own entity, whose color and intensity equal the parsed values within
/// <c>1e-5</c> per component, whose range (point/spot) equals the parsed range, and whose cone
/// angles (spot) equal the parsed inner/outer angles in radians with inner ≤ outer. The importer
/// does NOT set per-light position/direction: directional and range <c>Direction</c> remain at the
/// component default <c>-Vector3.UnitZ</c> and range <c>Position</c> remains at the component
/// default; instead the node's engine-observable world transform drives the light's placement.
/// Build an in-memory <see cref="Gltf"/> model carrying a <c>KHR_lights_punctual</c> light, run the
/// import against a <see cref="World"/> with mock managers, and read back the attached component
/// from the referencing node's entity.
/// </summary>
[TestClass]
public class LightAttachmentValuePropertyTests
{
    // Use QuickThrowOnFailure so a falsified property actually throws (and fails the test) rather
    // than only printing a counterexample to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure;
    private const float Tolerance = 1e-5f;

    // Looser tolerance for the engine-observable world transform: it accumulates a TRS composition
    // (Scale * Rotation * Translation) which can drift slightly more than a single component read.
    private const float MatrixTolerance = 1e-3f;

    #region Light parameter model + generators

    private enum Kind
    {
        Directional,
        Point,
        Spot,
    }

    /// <summary>
    /// A fully-specified, converter-valid light definition plus the node transform it is attached
    /// to. All values are chosen inside the converter's "valid and preserved" ranges (color channels
    /// in [0,1], intensity &gt;= 0, range &gt; 0 for point/spot, inner &lt; outer &lt;= PI/2 for
    /// spot) so the parsed <see cref="ParsedLight"/> equals the authored values, letting the test
    /// assert the attached component preserves them.
    /// </summary>
    private readonly record struct LightCase(
        Kind Kind,
        Vector3 Color,
        float Intensity,
        float Range,
        float InnerConeAngle,
        float OuterConeAngle,
        Vector3 Translation,
        Quaternion Rotation,
        Vector3 Scale
    );

    private static Gen<Vector3> ColorGen() =>
        from r in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from g in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from b in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        select new Vector3(r, g, b);

    // Intensity: numeric and >= 0 (preserved verbatim by the converter).
    private static Gen<float> IntensityGen() => Gen.Choose(0, 100000).Select(i => i / 100.0f);

    // Range for point/spot: strictly > 0 (preserved verbatim by the converter).
    private static Gen<float> RangeGen() => Gen.Choose(1, 100000).Select(i => i / 100.0f);

    // Spot cone angles: 0 <= inner < outer <= PI/2 (radians), preserved verbatim by the converter.
    private static Gen<(float Inner, float Outer)> ConeAnglesGen() =>
        // outer in (0.1, PI/2], inner in [0, outer) with a strict margin.
        from outerMilli in Gen.Choose(200, 1570) // 0.200 .. 1.570 rad (< PI/2 ≈ 1.5708)
        let outer = outerMilli / 1000.0f
        from innerFrac in Gen.Choose(0, 900).Select(i => i / 1000.0f) // 0 .. 0.9 of outer
        let inner = outer * innerFrac
        select (inner, outer);

    private static Gen<Quaternion> RotationGen() =>
        from x in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from y in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from z in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from w in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        let raw = new Quaternion(x, y, z, w)
        where raw.Length() > 0.001f
        select Quaternion.Normalize(raw);

    private static Gen<Vector3> TranslationGen() =>
        from x in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        from y in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        from z in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        select new Vector3(x, y, z);

    // Positive, non-degenerate scale so the transform is well-formed.
    private static Gen<Vector3> ScaleGen() =>
        from x in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from y in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from z in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        select new Vector3(x, y, z);

    private static Gen<LightCase> LightCaseGen() =>
        from kind in Gen.Elements(Kind.Directional, Kind.Point, Kind.Spot)
        from color in ColorGen()
        from intensity in IntensityGen()
        from range in RangeGen()
        from cone in ConeAnglesGen()
        from translation in TranslationGen()
        from rotation in RotationGen()
        from scale in ScaleGen()
        select new LightCase(
            kind,
            color,
            intensity,
            range,
            cone.Inner,
            cone.Outer,
            translation,
            rotation,
            scale
        );

    #endregion

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c> light
    /// (index 0) of the given kind and authored values, and a single node (index 0) referencing it
    /// via the node-level extension carrying the given TRS, then imports it against the supplied
    /// (live) <paramref name="world"/> and returns the engine node the light component was attached
    /// to. The world's transform hierarchy is updated so the engine-observable world transform of
    /// the returned node is available to the caller. The <paramref name="world"/> must outlive the
    /// returned node (callers keep it alive while reading components/transforms).
    /// </summary>
    private static Node BuildAndReadLightNode(LightCase c, World world)
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
            ["color"] = new JArray { c.Color.X, c.Color.Y, c.Color.Z },
            ["intensity"] = c.Intensity,
        };

        // Range applies only to point/spot lights.
        if (c.Kind is Kind.Point or Kind.Spot)
        {
            lightDefinition["range"] = c.Range;
        }

        // Cone angles apply only to spot lights.
        if (c.Kind == Kind.Spot)
        {
            lightDefinition["spot"] = new JObject
            {
                ["innerConeAngle"] = c.InnerConeAngle,
                ["outerConeAngle"] = c.OuterConeAngle,
            };
        }

        var documentExtension = new JObject { ["lights"] = new JArray { lightDefinition } };
        var nodeExtension = new JObject { ["light"] = 0 };

        var gltfNode = new GltfNode
        {
            Name = "LightNode",
            Translation = [c.Translation.X, c.Translation.Y, c.Translation.Z],
            Rotation = [c.Rotation.X, c.Rotation.Y, c.Rotation.Z, c.Rotation.W],
            Scale = [c.Scale.X, c.Scale.Y, c.Scale.Z],
            Extensions = new Dictionary<string, object>
            {
                [LightConverter.ExtensionName] = nodeExtension,
            },
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

        // BuildScene populates the parsed-lights list (BuildNode alone does not), so light
        // attachment is exercised.
        var root = sceneBuilder.BuildScene(model, 0);

        // Update the transform hierarchy so the node's engine-observable world transform reflects
        // the authored TRS (placement is driven by the node transform, not an importer-set value).
        world.SortSceneNodes();
        world.UpdateTransforms();

        // Robust node mapping: locate the node that actually carries the light component rather than
        // assuming a fixed child index.
        var lightNode = FindNodeWithLight(root);
        Assert.IsNotNull(
            lightNode,
            "Expected a light component to be attached to the referencing node's own entity."
        );
        return lightNode!;
    }

    /// <summary>
    /// Recursively finds the first node carrying a punctual light component (directional or range),
    /// returning <c>null</c> if none is found.
    /// </summary>
    private static Node? FindNodeWithLight(Node node)
    {
        if (
            node.Entity.TryGet<DirectionalLightInfo>(out _)
            || node.Entity.TryGet<RangeLightInfo>(out _)
        )
        {
            return node;
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindNodeWithLight(child);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static bool ApproxEqual(Vector3 a, Vector3 b, float tolerance) =>
        MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

    private static bool ApproxEqual(float a, float b, float tolerance) =>
        MathF.Abs(a - b) <= tolerance;

    /// <summary>
    /// Recomputes the world matrix the engine derives for a root-level node using the same
    /// row-vector TRS composition (Scale * Rotation * Translation) with parentWorld = Identity.
    /// Used to validate the engine-observable world transform of the node (the engine computes this
    /// from the node hierarchy; this reconstruction is independent, from the authored TRS).
    /// </summary>
    private static Matrix4x4 ComposeWorld(LightCase c) =>
        Matrix4x4.CreateScale(c.Scale)
        * Matrix4x4.CreateFromQuaternion(c.Rotation)
        * Matrix4x4.CreateTranslation(c.Translation);

    private static bool MatrixApproxEqual(Matrix4x4 a, Matrix4x4 b, float tolerance) =>
        MathF.Abs(a.M11 - b.M11) <= tolerance
        && MathF.Abs(a.M12 - b.M12) <= tolerance
        && MathF.Abs(a.M13 - b.M13) <= tolerance
        && MathF.Abs(a.M14 - b.M14) <= tolerance
        && MathF.Abs(a.M21 - b.M21) <= tolerance
        && MathF.Abs(a.M22 - b.M22) <= tolerance
        && MathF.Abs(a.M23 - b.M23) <= tolerance
        && MathF.Abs(a.M24 - b.M24) <= tolerance
        && MathF.Abs(a.M31 - b.M31) <= tolerance
        && MathF.Abs(a.M32 - b.M32) <= tolerance
        && MathF.Abs(a.M33 - b.M33) <= tolerance
        && MathF.Abs(a.M34 - b.M34) <= tolerance
        && MathF.Abs(a.M41 - b.M41) <= tolerance
        && MathF.Abs(a.M42 - b.M42) <= tolerance
        && MathF.Abs(a.M43 - b.M43) <= tolerance
        && MathF.Abs(a.M44 - b.M44) <= tolerance;

    #endregion

    /// <summary>
    /// Property 1: Light attachment preserves parsed values for the matching kind on the referencing
    /// node's own entity; position/direction are left at the component defaults and the node's
    /// engine-observable world transform drives placement.
    /// **Validates: Requirements 2.1, 2.3**
    /// </summary>
    [TestMethod]
    public void LightAttachment_PreservesParsedValues_ForMatchingKind()
    {
        Prop.ForAll(
                Arb.From(LightCaseGen()),
                (LightCase c) =>
                {
                    // Keep the ECS world alive for the duration of the assertions: reading node
                    // transforms/components after the world is disposed would throw.
                    using var world = World.CreateWorld();
                    var node = BuildAndReadLightNode(c, world);

                    // Engine-observable world transform: the engine composes this from the node
                    // hierarchy; compare it to an independent reconstruction from the authored TRS
                    // (not a tautology — the importer no longer derives the placement itself).
                    var expectedWorld = ComposeWorld(c);
                    var actualWorld = node.WorldTransform.Value;
                    if (!MatrixApproxEqual(actualWorld, expectedWorld, MatrixTolerance))
                    {
                        return false;
                    }

                    bool hasDirectional = node.Entity.TryGet<DirectionalLightInfo>(
                        out var dirLight
                    );
                    bool hasRange = node.Entity.TryGet<RangeLightInfo>(out var rangeLight);

                    switch (c.Kind)
                    {
                        case Kind.Directional:
                            // Exactly one component of the matching kind: directional present,
                            // no range component.
                            if (!hasDirectional || hasRange)
                            {
                                return false;
                            }

                            return ApproxEqual(dirLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(dirLight.Intensity, c.Intensity, Tolerance)
                                // No importer-set direction: stays at the component default -Z.
                                && ApproxEqual(dirLight.Direction, -Vector3.UnitZ, Tolerance);

                        case Kind.Point:
                            // Exactly one component of the matching kind: a Point range light,
                            // no directional component.
                            if (!hasRange || hasDirectional || rangeLight is null)
                            {
                                return false;
                            }
                            if (rangeLight.Type != RangeLightType.Point)
                            {
                                return false;
                            }

                            return ApproxEqual(rangeLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(rangeLight.Intensity, c.Intensity, Tolerance)
                                && ApproxEqual(rangeLight.Range, c.Range, Tolerance)
                                // No importer-set position/direction: component defaults preserved.
                                && ApproxEqual(rangeLight.Position, Vector3.Zero, Tolerance)
                                && ApproxEqual(rangeLight.Direction, -Vector3.UnitZ, Tolerance);

                        case Kind.Spot:
                            // Exactly one component of the matching kind: a Spot range light,
                            // no directional component.
                            if (!hasRange || hasDirectional || rangeLight is null)
                            {
                                return false;
                            }
                            if (rangeLight.Type != RangeLightType.Spot)
                            {
                                return false;
                            }

                            var spotAngles = rangeLight.SpotAngles;

                            return ApproxEqual(rangeLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(rangeLight.Intensity, c.Intensity, Tolerance)
                                && ApproxEqual(rangeLight.Range, c.Range, Tolerance)
                                // No importer-set position/direction: component defaults preserved.
                                && ApproxEqual(rangeLight.Position, Vector3.Zero, Tolerance)
                                && ApproxEqual(rangeLight.Direction, -Vector3.UnitZ, Tolerance)
                                // Cone angles in radians, inner ≤ outer, equal to parsed values.
                                && ApproxEqual(spotAngles.X, c.InnerConeAngle, Tolerance)
                                && ApproxEqual(spotAngles.Y, c.OuterConeAngle, Tolerance)
                                && spotAngles.X <= spotAngles.Y;

                        default:
                            return false;
                    }
                }
            )
            .Check(FsCheckConfig);
    }
}
