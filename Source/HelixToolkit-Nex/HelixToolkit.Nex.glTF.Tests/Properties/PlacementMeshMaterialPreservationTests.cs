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
using HelixToolkit.Nex.Shaders.Frag;
using Newtonsoft.Json.Linq;
using Geometry = HelixToolkit.Nex.Geometries.Geometry;
using Gltf = glTFLoader.Schema.Gltf;
using GltfMaterial = glTFLoader.Schema.Material;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using MeshNode = HelixToolkit.Nex.Scene.MeshNode;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-light-import-cr-fixes
// Property 5 (Preservation): Node-transform-driven placement, mesh creation, and material conversion.
//
// PRESERVATION TEST — captures the BASELINE behavior that must NOT change across the fix. Written
// observation-first against the UNFIXED code and MUST PASS on the unfixed code. The assertions are
// deliberately scoped to behaviors that already hold on the unfixed code:
//   * (3.5) With CreatePointLightMeshes enabled for a point light, the importer creates a
//     visualization sphere mesh (a MeshNode named "PointLightMesh_{nodeIndex}" carrying the cached
//     sphere geometry) scaled by ImporterConfig.PointLightMeshSize. This is independent of where the
//     light COMPONENT is attached (child light node on unfixed code; node's own entity after the
//     fix), so the mesh-creation assertion survives the fix.
//   * (3.6) The referencing node's engine-observable world transform is driven by the node's glTF
//     transform — we assert it against an INDEPENDENT recomposition of the authored TRS (not a
//     tautological read-back), and that the importer bakes no per-light world position/direction
//     into the light component (it stays at the component defaults Position == Vector3.Zero,
//     Direction == -Vector3.UnitZ). Placement is therefore delegated to the node transform on both
//     unfixed and fixed code.
//   * (3.7) A valid material converted with a given shading mode carries the correct shading mode
//     (MaterialTypeId derived from the shading mode) and an appropriate display name. We query the
//     real ConvertMaterialWithMetadata path with the REAL PBRMaterialPropertyManager so the shading
//     mode is genuinely derived; this path is unaffected by the CreateMaterialProps API change.
//
// DO NOT change production code while writing/maintaining this test.

/// <summary>
/// Property-based preservation tests for Property 5 of the gltf-light-import-cr-fixes feature.
/// For any point light with <see cref="ImporterConfig.CreatePointLightMeshes"/> enabled, the
/// importer continues to create the visualization sphere mesh scaled by
/// <see cref="ImporterConfig.PointLightMeshSize"/> (3.5); for any node transform, the node's
/// engine-observable world transform continues to drive the light's effective position/direction
/// with no importer-set per-light position (3.6); and for any valid material converted with a given
/// shading mode, the result continues to carry the correct shading mode and an appropriate display
/// name (3.7). Mirrors the <c>LightAttachmentTargetExplorationTests</c> build harness (in-memory
/// glTF + mock managers) for the scene-graph cases, and the <c>MaterialPropsApiExplorationTests</c>
/// real-manager harness for the material-conversion case.
/// </summary>
[TestClass]
public class PlacementMeshMaterialPreservationTests
{
    // QuickThrowOnFailure throws (failing the MSTest) when a property is violated, so a surfaced
    // counterexample fails the test instead of only being printed to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure.WithMaxTest(100);

    private const float Tolerance = 1e-5f;

    // World/composition tolerance: the engine composes Value via the exact same formula as the
    // independent recomposition, so values match to near machine precision.
    private const float MatrixTolerance = 1e-3f;

    // Component defaults the importer must NOT override with world-derived values.
    private static readonly Vector3 DefaultDirection = -Vector3.UnitZ;
    private static readonly Vector3 DefaultPosition = Vector3.Zero;

    /// <summary>The single glTF node index built by the scene-graph helpers.</summary>
    private const int NodeIndex = 0;

    #region Light parameter model + generators (3.5, 3.6)

    private readonly record struct PointLightCase(
        Vector3 Color,
        float Intensity,
        float Range,
        float MeshSize,
        Vector3 Translation,
        Quaternion Rotation,
        Vector3 Scale
    );

    private static Gen<Vector3> ColorGen() =>
        from r in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from g in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from b in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        select new Vector3(r, g, b);

    private static Gen<float> IntensityGen() => Gen.Choose(0, 100000).Select(i => i / 100.0f);

    private static Gen<float> RangeGen() => Gen.Choose(1, 100000).Select(i => i / 100.0f);

    // Point-light visualization mesh scale: 0.001 .. 10.0 (world-space scale applied to the sphere).
    private static Gen<float> MeshSizeGen() => Gen.Choose(1, 10000).Select(i => i / 1000.0f);

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

    private static Gen<Vector3> ScaleGen() =>
        from x in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from y in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from z in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        select new Vector3(x, y, z);

    private static Gen<PointLightCase> PointLightCaseGen() =>
        from color in ColorGen()
        from intensity in IntensityGen()
        from range in RangeGen()
        from meshSize in MeshSizeGen()
        from translation in TranslationGen()
        from rotation in RotationGen()
        from scale in ScaleGen()
        select new PointLightCase(color, intensity, range, meshSize, translation, rotation, scale);

    #endregion

    #region Scene-graph harness (3.5, 3.6)

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c> point
    /// light (index 0) and a single node (index 0) referencing it via the node-level extension
    /// carrying the given TRS, imports it with the supplied <paramref name="config"/>, and returns
    /// the (still-live) ECS world, the referencing engine node, and the cached sphere geometry the
    /// mesh converter uses for point-light visualization meshes.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="World"/> is intentionally left undisposed so the caller can read node
    /// transforms and components while the entities are still valid; the caller MUST dispose it.
    /// </remarks>
    private static (World World, Node Node, Geometry SphereMesh) BuildPointLightNode(
        PointLightCase c,
        ImporterConfig config
    )
    {
        var lightDefinition = new JObject
        {
            ["type"] = "point",
            ["color"] = new JArray { c.Color.X, c.Color.Y, c.Color.Z },
            ["intensity"] = c.Intensity,
            ["range"] = c.Range,
        };

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

        // The world is intentionally NOT disposed here — the caller reads node transforms and
        // components after this returns, which requires the entities to remain valid.
        var world = World.CreateWorld();
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
            new LightConverter(diagnostics, config),
            diagnostics,
            config
        );

        var root = sceneBuilder.BuildScene(model, 0);

        // GetSphereMesh caches its result, so this returns the exact instance the importer assigned
        // to the visualization mesh node during the build.
        var sphereMesh = meshConverter.GetSphereMesh();

        return (world, root!.Children![0]!, sphereMesh);
    }

    /// <summary>
    /// Collects every <see cref="RangeLightInfo"/> attached to <paramref name="node"/>'s own
    /// entity or any of its descendant node entities. This is robust to whether the importer places
    /// the light component on the referencing node (fixed code) or on an added child light node
    /// (unfixed code).
    /// </summary>
    private static List<RangeLightInfo> CollectRangeLights(Node node)
    {
        var result = new List<RangeLightInfo>();
        CollectRangeLightsRecursive(node, result);
        return result;
    }

    private static void CollectRangeLightsRecursive(Node node, List<RangeLightInfo> acc)
    {
        if (node.Entity.TryGet<RangeLightInfo>(out var light) && light is not null)
        {
            acc.Add(light);
        }

        var children = node.Children;
        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            CollectRangeLightsRecursive(child, acc);
        }
    }

    /// <summary>
    /// Finds the single point-light visualization mesh node ("PointLightMesh_{nodeIndex}") in the
    /// referencing node's subtree, or <c>null</c> when none is present.
    /// </summary>
    private static MeshNode? FindVisualizationMesh(Node node, int nodeIndex)
    {
        var expectedName = $"PointLightMesh_{nodeIndex}";
        return FindMeshNodeRecursive(node, expectedName);
    }

    private static MeshNode? FindMeshNodeRecursive(Node node, string name)
    {
        if (node is MeshNode meshNode && meshNode.Name == name)
        {
            return meshNode;
        }

        var children = node.Children;
        if (children is null)
        {
            return null;
        }

        foreach (var child in children)
        {
            var found = FindMeshNodeRecursive(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    #endregion

    #region Helpers

    private static bool ApproxEqual(Vector3 a, Vector3 b, float tolerance) =>
        MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

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
    /// Property 5 (3.5): with <see cref="ImporterConfig.CreatePointLightMeshes"/> enabled for a
    /// point light, the importer creates a visualization sphere mesh node
    /// ("PointLightMesh_{nodeIndex}") carrying the cached sphere geometry and scaled uniformly by
    /// <see cref="ImporterConfig.PointLightMeshSize"/>.
    /// **Validates: Requirements 3.5**
    /// </summary>
    [TestMethod]
    public void PointLightMesh_CreatedAndScaledByPointLightMeshSize_WhenEnabled()
    {
        Prop.ForAll(
                Arb.From(PointLightCaseGen()),
                (PointLightCase c) =>
                {
                    var config = new ImporterConfig
                    {
                        CreatePointLightMeshes = true,
                        PointLightMeshSize = c.MeshSize,
                    };

                    var (world, node, sphereMesh) = BuildPointLightNode(c, config);
                    try
                    {
                        var meshNode = FindVisualizationMesh(node, NodeIndex);
                        if (meshNode is null)
                        {
                            return false;
                        }

                        // The visualization mesh uses the cached sphere geometry ...
                        if (!ReferenceEquals(meshNode.Geometry, sphereMesh) || sphereMesh is null)
                        {
                            return false;
                        }

                        // ... and is scaled uniformly by the configured PointLightMeshSize.
                        return ApproxEqual(
                            meshNode.Transform.Scale,
                            new Vector3(c.MeshSize),
                            Tolerance
                        );
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 5 (3.6): the referencing node's engine-observable world transform is driven by the
    /// node's authored glTF transform (asserted against an independent recomposition of the TRS, not
    /// a tautological read-back), and the importer bakes no per-light world position/direction — the
    /// light component remains at its defaults (Position == Vector3.Zero, Direction ==
    /// -Vector3.UnitZ). Placement is delegated to the node transform.
    /// **Validates: Requirements 3.6**
    /// </summary>
    [TestMethod]
    public void NodeWorldTransform_DrivesPlacement_WithNoImporterSetPerLightPosition()
    {
        // CreatePointLightMeshes disabled so the only RangeLightComponent in the subtree is the
        // point light itself (no incidental components from visualization meshes).
        var config = new ImporterConfig { CreatePointLightMeshes = false };

        Prop.ForAll(
                Arb.From(PointLightCaseGen()),
                (PointLightCase c) =>
                {
                    var (world, node, _) = BuildPointLightNode(c, config);
                    try
                    {
                        // Engine-observable world transform: the referencing node is a direct child
                        // of the identity-transform scene root, so its world transform equals its
                        // resolved local transform value.
                        var engineWorld = node.Transform.Value;

                        // Independent recomposition from the authored TRS (row-vector S * R * T) —
                        // this is NOT read back from the engine, so the comparison is
                        // non-tautological.
                        var expectedWorld =
                            Matrix4x4.CreateScale(c.Scale)
                            * Matrix4x4.CreateFromQuaternion(c.Rotation)
                            * Matrix4x4.CreateTranslation(c.Translation);

                        if (!MatrixApproxEqual(engineWorld, expectedWorld, MatrixTolerance))
                        {
                            return false;
                        }

                        // Exactly one point light exists in the subtree, and the importer set no
                        // per-light world position/direction (defaults preserved; placement comes
                        // from the node transform).
                        var lights = CollectRangeLights(node);
                        if (lights.Count != 1)
                        {
                            return false;
                        }

                        var light = lights[0];
                        return light.Type == RangeLightType.Point
                            && light.Position == DefaultPosition
                            && light.Direction == DefaultDirection;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .Check(FsCheckConfig);
    }

    #region Material conversion model + generators (3.7)

    private readonly record struct MaterialCase(PBRShadingMode ShadingMode, string? MaterialName);

    // Only the shading modes registered as built-in material types (PBRShadingMode.None is not
    // registered, so a material of that mode cannot be created via the manager).
    private static Gen<PBRShadingMode> ShadingModeGen() =>
        Gen.Elements(
            PBRShadingMode.PBR,
            PBRShadingMode.Unlit,
            PBRShadingMode.DebugTileLightCount,
            PBRShadingMode.Normal,
            PBRShadingMode.Flat,
            PBRShadingMode.CAD,
            PBRShadingMode.CADFlat
        );

    // Either an explicit display name or null (the importer falls back to "Material_{index}").
    private static Gen<string?> MaterialNameGen() =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            from index in Gen.Choose(0, 100000)
            select (string?)$"GoldTrim_{index}"
        );

    private static Gen<MaterialCase> MaterialCaseGen() =>
        from shadingMode in ShadingModeGen()
        from name in MaterialNameGen()
        select new MaterialCase(shadingMode, name);

    private static MaterialConverter CreateConverter(PBRShadingMode defaultShadingMode)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var manifest = new ResourceManifest();

        // Use the REAL material property manager so the shading mode is genuinely derived from the
        // registered material-type name (the stub used elsewhere always returns "PBR").
        var materialManager = new PBRMaterialPropertyManager();

        var textureRepo = new StubTextureRepository();
        var samplerRepo = new StubSamplerRepository(StubSamplerRepositoryMode.MockContextBacked);
        var textureLoader = new TextureLoader(
            textureRepo,
            samplerRepo,
            "C:\\test",
            new Gltf(),
            [],
            diagnostics,
            manifest,
            Guid.NewGuid().ToString("D")
        );

        return new MaterialConverter(
            materialManager,
            textureLoader,
            diagnostics,
            manifest,
            defaultShadingMode
        );
    }

    #endregion

    /// <summary>
    /// Property 5 (3.7): a valid material converted with a given shading mode carries the correct
    /// shading mode (its <c>MaterialTypeId</c> is derived from the shading mode) and an appropriate
    /// display name (the glTF material name, or the documented "Material_{index}" fallback when the
    /// glTF material has no name). Queries the real <see cref="MaterialConverter.ConvertMaterial"/>
    /// path with the real <see cref="PBRMaterialPropertyManager"/>, which is independent of the
    /// CreateMaterialProps API change.
    /// **Validates: Requirements 3.7**
    /// </summary>
    [TestMethod]
    public void ValidMaterial_ConvertedWithShadingMode_CarriesShadingModeAndDisplayName()
    {
        Prop.ForAll(
                Arb.From(MaterialCaseGen()),
                (MaterialCase c) =>
                {
                    var converter = CreateConverter(c.ShadingMode);

                    var gltfMaterial = new GltfMaterial { Name = c.MaterialName };
                    var model = new Gltf { Materials = [gltfMaterial] };

                    var result = converter.ConvertMaterialWithMetadata(model, 0);
                    var material = result.Material;

                    // Shading mode is derived from the configured shading mode.
                    bool shadingModeMatches =
                        material.MaterialTypeId == (MaterialTypeId)c.ShadingMode;

                    // Display name is the glTF material name, or the documented index-based fallback.
                    var expectedName = c.MaterialName ?? "Material_0";
                    bool nameMatches = material.Name == expectedName;

                    return shadingModeMatches && nameMatches;
                }
            )
            .Check(FsCheckConfig);
    }
}
