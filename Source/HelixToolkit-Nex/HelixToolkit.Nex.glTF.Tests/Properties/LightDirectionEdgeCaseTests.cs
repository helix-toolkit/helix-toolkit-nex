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

// Feature: gltf-light-import-cr-fixes — unit/example tests for node-transform-driven light direction.

/// <summary>
/// Unit/example tests for the node-transform-driven directional light model in
/// <see cref="SceneBuilder"/>. The importer attaches the <see cref="DirectionalLightInfo"/> to
/// the referencing node's own entity and leaves <c>Direction</c> at the component default
/// <c>-Vector3.UnitZ</c>; orientation is delegated to the node transform. Mirrors the
/// <c>TransformPropertyTests</c> build pattern: an in-memory <see cref="Gltf"/> model carrying a
/// <c>KHR_lights_punctual</c> directional light is built via <see cref="SceneBuilder.BuildScene"/>
/// against a <see cref="World"/> with mock managers, then the attached
/// <see cref="DirectionalLightInfo"/> and emitted diagnostics are asserted.
/// <para>
/// Covered cases:
/// <list type="bullet">
/// <item>An identity-transform directional light keeps the component default direction
/// <c>(0,0,-1)</c> and emits no error diagnostic.</item>
/// <item>A rotated/degenerate transform does not cause the importer to derive or write a
/// per-light direction (no NaN, no epsilon-normalization, no error diagnostic): the component keeps
/// the default <c>-Vector3.UnitZ</c> and the node transform drives orientation.</item>
/// </list>
/// </para>
/// **Validates: Requirements 2.1, 2.3**
/// </summary>
[TestClass]
public class LightDirectionEdgeCaseTests
{
    private const float Tolerance = 1e-5f;

    #region Helpers

    /// <summary>
    /// Builds the engine node for <paramref name="gltfNode"/> (placed at glTF node index 0), which
    /// references the single document-level directional light at light index 0, and returns the
    /// resulting engine <see cref="Node"/> together with the diagnostics produced during the build.
    /// </summary>
    private static (Node Node, List<ImportDiagnostic> Diagnostics) BuildSingleLightNode(
        GltfNode gltfNode,
        World world
    )
    {
        // Document-level KHR_lights_punctual with one directional light (index 0).
        var model = new Gltf
        {
            Nodes = [gltfNode],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
            Extensions = new Dictionary<string, object>
            {
                [LightConverter.ExtensionName] = new JObject
                {
                    ["lights"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "directional",
                            ["color"] = new JArray { 0.9f, 0.8f, 0.1f },
                            ["intensity"] = 1.0f,
                        },
                    },
                },
            },
        };

        // The node references light index 0 via its per-node KHR_lights_punctual extension.
        gltfNode.Extensions = new Dictionary<string, object>
        {
            [LightConverter.ExtensionName] = new JObject { ["light"] = 0 },
        };

        var diagnostics = new List<ImportDiagnostic>();
        var manifest = new ResourceManifest();
        var accessorReader = new AccessorReader(model, []);
        using var geoManager = new StubGeometryManager();
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

        // BuildScene (not BuildNode) is used so the document-level KHR_lights_punctual lights are
        // parsed into the builder before per-node attachment runs.
        var root = sceneBuilder.BuildScene(model, -1);

        // Update the transform hierarchy so the node's engine-observable world transform is current.
        world.SortSceneNodes();
        world.UpdateTransforms();

        // Robust node mapping: find the node carrying the directional light component rather than
        // assuming a fixed child index.
        var node = FindNodeWithDirectionalLight(root);
        Assert.IsNotNull(
            node,
            "Expected a DirectionalLightComponent on the referencing node's own entity."
        );
        return (node!, diagnostics);
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

    #endregion

    /// <summary>
    /// A directional light on a node with identity rotation keeps the component default
    /// <c>Direction == (0, 0, -1)</c> (the importer sets no world-derived direction), with no
    /// <c>NaN</c> and no error diagnostic.
    /// **Validates: Requirements 2.1, 2.3**
    /// </summary>
    [TestMethod]
    public void IdentityRotation_KeepsDefaultNegativeZDirection()
    {
        using var world = World.CreateWorld();

        // A default glTF node has identity TRS (Translation 0, Rotation identity, Scale 1).
        var gltfNode = new GltfNode { Name = "Sun" };

        var (node, diagnostics) = BuildSingleLightNode(gltfNode, world);

        Assert.IsTrue(
            node.Entity.TryGet<DirectionalLightInfo>(out var light),
            "Expected a DirectionalLightComponent to be attached to the referencing node."
        );

        var direction = light.Direction;

        Assert.IsFalse(
            float.IsNaN(direction.X) || float.IsNaN(direction.Y) || float.IsNaN(direction.Z),
            "Direction must not contain NaN."
        );
        Assert.AreEqual(0.0f, direction.X, Tolerance, "Direction.X should be 0.");
        Assert.AreEqual(0.0f, direction.Y, Tolerance, "Direction.Y should be 0.");
        Assert.AreEqual(
            -1.0f,
            direction.Z,
            Tolerance,
            "Direction.Z should be -1 (component default -Z)."
        );

        // The light is well-formed; no diagnostic is emitted.
        Assert.IsFalse(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "No error diagnostic should be emitted for a valid identity-rotation light."
        );
    }

    /// <summary>
    /// A rotated node does NOT cause the importer to derive or write a per-light direction: the
    /// attached directional light keeps the component default <c>-Vector3.UnitZ</c> (no
    /// world-derivation, no NaN, no epsilon normalization, no error diagnostic), while the node's
    /// engine-observable world transform carries the rotation. Orientation is delegated to the node
    /// transform.
    /// **Validates: Requirements 2.1, 2.3**
    /// </summary>
    [TestMethod]
    public void RotatedNode_KeepsDefaultDirection_NoNaN_NoErrorDiagnostic_NodeTransformCarriesRotation()
    {
        using var world = World.CreateWorld();

        // A 90° rotation about Y. Under the abandoned world-derivation model this would have changed
        // the light's Direction; under the node-transform model the component keeps its default.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f);
        var gltfNode = new GltfNode
        {
            Name = "RotatedSun",
            Translation = [0f, 0f, 0f],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Scale = [1f, 1f, 1f],
        };

        var (node, diagnostics) = BuildSingleLightNode(gltfNode, world);

        Assert.IsTrue(
            node.Entity.TryGet<DirectionalLightInfo>(out var light),
            "The directional light component should be attached to the referencing node."
        );

        var direction = light.Direction;

        // No NaN and the direction stays at the component default (no world-derived value).
        Assert.IsFalse(
            float.IsNaN(direction.X) || float.IsNaN(direction.Y) || float.IsNaN(direction.Z),
            "Direction must not contain NaN."
        );
        Assert.AreEqual(0.0f, direction.X, Tolerance, "Direction.X should remain default 0.");
        Assert.AreEqual(0.0f, direction.Y, Tolerance, "Direction.Y should remain default 0.");
        Assert.AreEqual(
            -1.0f,
            direction.Z,
            Tolerance,
            "Direction.Z should remain the component default -1 (no world-derived direction)."
        );

        // The importer emits no error diagnostic: there is no degenerate-direction/NaN-guard path.
        Assert.IsFalse(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "No error diagnostic should be emitted; direction is delegated to the node transform."
        );

        // The node transform carries the rotation (engine-observable world transform), so orientation
        // is driven by the node, not by an importer-set component direction.
        var rotationRow = node.Transform.Rotation;
        Assert.AreEqual(rotation.X, rotationRow.X, Tolerance, "Node rotation X should match.");
        Assert.AreEqual(rotation.Y, rotationRow.Y, Tolerance, "Node rotation Y should match.");
        Assert.AreEqual(rotation.Z, rotationRow.Z, Tolerance, "Node rotation Z should match.");
        Assert.AreEqual(rotation.W, rotationRow.W, Tolerance, "Node rotation W should match.");
    }
}
