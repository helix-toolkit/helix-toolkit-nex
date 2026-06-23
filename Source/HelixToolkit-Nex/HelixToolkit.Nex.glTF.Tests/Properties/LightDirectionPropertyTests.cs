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

// Feature: gltf-light-import-cr-fixes — directional light placement is driven by the node transform.

/// <summary>
/// Property-based test for the node-transform-driven directional light model of the
/// gltf-light-import-cr-fixes feature. The importer does NOT derive the light direction from a world
/// matrix; instead it attaches the <see cref="DirectionalLightInfo"/> to the referencing node's
/// own entity, leaving <c>Direction</c> at the component default <c>-Vector3.UnitZ</c>, and the
/// engine's node transform drives the light's effective orientation. For any node TRS, this test
/// asserts the attached directional light keeps the default <c>-Vector3.UnitZ</c> direction (no
/// importer-set/ world-derived direction) and that the node's engine-observable world transform
/// equals the authored TRS composition.
/// Build an in-memory <see cref="Gltf"/> model carrying a <c>KHR_lights_punctual</c> directional
/// light, run the import against a <see cref="World"/> with mock managers, and read back the
/// component and node transform.
/// </summary>
[TestClass]
public class LightDirectionPropertyTests
{
    // Use QuickThrowOnFailure so a falsified property actually throws (and fails the test).
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure;
    private const float Tolerance = 1e-5f;

    // Looser tolerance for the engine-observable world transform (TRS composition accumulation).
    private const float MatrixTolerance = 1e-3f;

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c>
    /// directional light (index 0) and a single node (index 0) referencing it via the node-level
    /// extension, carrying the given TRS, then imports it against the supplied (live)
    /// <paramref name="world"/> and returns the engine node the directional light was attached to.
    /// The transform hierarchy is updated so the node's engine-observable world transform reflects
    /// the authored TRS. The <paramref name="world"/> must outlive the returned node.
    /// </summary>
    private static Node BuildAndReadLightNode(
        Vector3 translation,
        Quaternion rotation,
        Vector3 scale,
        World world
    )
    {
        // Document-level KHR_lights_punctual extension carrying one directional light.
        var lightDefinition = new JObject { ["type"] = "directional" };
        var documentExtension = new JObject { ["lights"] = new JArray { lightDefinition } };

        // Node-level KHR_lights_punctual extension referencing light index 0.
        var nodeExtension = new JObject { ["light"] = 0 };

        var gltfNode = new GltfNode
        {
            Name = "DirectionalLightNode",
            // Use the TRS path (no explicit matrix) so ApplyTransform reads TRS.
            Translation = [translation.X, translation.Y, translation.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Scale = [scale.X, scale.Y, scale.Z],
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

        // Update the transform hierarchy so the node's engine-observable world transform is current.
        world.SortSceneNodes();
        world.UpdateTransforms();

        // Robust node mapping: find the node carrying the directional light component.
        var lightNode = FindNodeWithDirectionalLight(root);
        Assert.IsNotNull(
            lightNode,
            "Expected a DirectionalLightComponent on the referencing node's own entity."
        );
        return lightNode!;
    }

    private static Node? FindNodeWithDirectionalLight(Node node)
    {
        if (node.Entity.TryGet<DirectionalLightInfo>(out _))
        {
            return node;
        }
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindNodeWithDirectionalLight(child);
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
    /// Directional light placement is driven by the node transform. For any node TRS, the attached
    /// directional light keeps the component default <c>Direction == -Vector3.UnitZ</c> (the importer
    /// sets no world-derived direction), and the node's engine-observable world transform equals the
    /// authored TRS composition (Scale * Rotation * Translation), so orientation is delegated to the
    /// node transform.
    /// **Validates: Requirements 2.1, 2.3**
    /// </summary>
    [TestMethod]
    public void DirectionalLight_KeepsDefaultDirection_AndNodeTransformDrivesPlacement()
    {
        // Translation: arbitrary, bounded range.
        var translationGen =
            from x in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            from y in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            from z in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            select new Vector3(x, y, z);

        // Rotation: random non-degenerate quaternion, normalized to a unit rotation.
        var rotationGen =
            from x in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from y in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from z in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from w in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            let raw = new Quaternion(x, y, z, w)
            where raw.Length() > 0.001f
            select Quaternion.Normalize(raw);

        // Scale: positive components (0.1 .. 10) so the transform is non-degenerate.
        var scaleGen =
            from x in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            from y in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            from z in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            select new Vector3(x, y, z);

        var inputGen =
            from translation in translationGen
            from rotation in rotationGen
            from scale in scaleGen
            select (translation, rotation, scale);

        Prop.ForAll(
                Arb.From(inputGen),
                ((Vector3 translation, Quaternion rotation, Vector3 scale) input) =>
                {
                    // Keep the ECS world alive while reading the component and node transform.
                    using var world = World.CreateWorld();
                    var node = BuildAndReadLightNode(
                        input.translation,
                        input.rotation,
                        input.scale,
                        world
                    );

                    Assert.IsTrue(
                        node.Entity.TryGet<DirectionalLightInfo>(out var dirLight),
                        "Expected a DirectionalLightComponent on the referencing node."
                    );

                    // (a) The importer sets no per-light direction: Direction stays at the component
                    // default -Vector3.UnitZ regardless of the node's rotation/scale.
                    bool keepsDefaultDirection = ApproxEqual(
                        dirLight.Direction,
                        -Vector3.UnitZ,
                        Tolerance
                    );

                    // (b) The node's engine-observable world transform equals the authored TRS
                    // composition, so the node transform drives the light's placement/orientation.
                    var expectedWorld =
                        Matrix4x4.CreateScale(input.scale)
                        * Matrix4x4.CreateFromQuaternion(input.rotation)
                        * Matrix4x4.CreateTranslation(input.translation);
                    bool nodeTransformDrivesPlacement = MatrixApproxEqual(
                        node.WorldTransform.Value,
                        expectedWorld,
                        MatrixTolerance
                    );

                    return keepsDefaultDirection && nodeTransformDrivesPlacement;
                }
            )
            .Check(FsCheckConfig);
    }
}
