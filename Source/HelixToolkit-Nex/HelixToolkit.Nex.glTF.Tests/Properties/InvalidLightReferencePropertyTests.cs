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

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-directionallight-render-fix, Property 3: Invalid or unresolvable light references attach nothing and report the correct diagnostic

/// <summary>
/// Property-based test for Property 3 of the gltf-directionallight-render-fix feature.
/// For any light reference index and lights-array length, if the index is negative or greater than
/// or equal to the length (including empty/absent arrays) the node receives no punctual light
/// component and a <see cref="DiagnosticSeverity.Warning"/> diagnostic identifying the node index
/// and the offending light index is added; if the index is in range but the parsed slot is
/// unconvertible (<c>null</c>) the node receives no punctual light component and a
/// <see cref="DiagnosticSeverity.Error"/> diagnostic identifying the node index and the offending
/// light index is added.
/// Mirrors the existing <see cref="LightDirectionPropertyTests"/> pattern: build an in-memory
/// <see cref="Gltf"/> model carrying a <c>KHR_lights_punctual</c> lights array of controlled
/// length and validity, run the import against a <see cref="World"/> with mock managers, and assert
/// over the attached components and the produced diagnostics.
/// </summary>
[TestClass]
public class InvalidLightReferencePropertyTests
{
    // Use QuickThrowOnFailure so a falsified property actually throws (and fails the test).
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure;

    /// <summary>An invalid <c>type</c> string that <see cref="LightConverter"/> cannot convert,
    /// yielding a <c>null</c> parsed slot at that index.</summary>
    private const string UnconvertibleLightType = "__unknown_light_type__";

    /// <summary>The glTF node index of the single referencing node built by the helper.</summary>
    private const int NodeIndex = 0;

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a document-level <c>KHR_lights_punctual</c> lights array
    /// of the given validity (one slot per entry; <c>true</c> = a convertible directional light,
    /// <c>false</c> = an unconvertible light yielding a <c>null</c> parsed slot) and a single node
    /// (index 0) referencing the given light index, imports it, and returns the engine node created
    /// for that glTF node together with the diagnostics produced during import.
    /// </summary>
    private static (Node node, List<ImportDiagnostic> Diagnostics) BuildAndImport(
        bool[] slotValidity,
        int referencedLightIndex,
        World world
    )
    {
        // Document-level KHR_lights_punctual extension: one light entry per slot. A valid slot is a
        // convertible directional light; an invalid slot carries an unknown type so the converter
        // produces a null parsed slot at that index.
        var lightsArray = new JArray();
        foreach (var valid in slotValidity)
        {
            lightsArray.Add(
                new JObject { ["type"] = valid ? "directional" : UnconvertibleLightType }
            );
        }
        var documentExtension = new JObject { ["lights"] = lightsArray };

        // Node-level KHR_lights_punctual extension referencing the (possibly out-of-range) index.
        var nodeExtension = new JObject { ["light"] = referencedLightIndex };

        var gltfNode = new GltfNode
        {
            Name = "LightReferenceNode",
            Translation = [0f, 0f, 0f],
            Rotation = [0f, 0f, 0f, 1f],
            Scale = [1f, 1f, 1f],
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

        // BuildScene populates the parsed-lights list (BuildNode alone does not), so reference
        // resolution/validation is exercised. Robust node mapping: find the referencing node by its
        // glTF name rather than assuming a fixed child index (no light is attached for invalid
        // references, so the node cannot be found by a light component).
        var root = sceneBuilder.BuildScene(model, 0);
        var lightNode = FindNodeByName(root, "LightReferenceNode");
        Assert.IsNotNull(lightNode, "Expected the referencing node to be present in the scene.");

        return (lightNode!, diagnostics);
    }

    /// <summary>
    /// Recursively finds the first node with the given name, or <c>null</c> if none matches.
    /// </summary>
    private static Node? FindNodeByName(Node node, string name)
    {
        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return node;
        }
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindNodeByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns whether the node carries any punctual light component (directional, point, or spot).
    /// </summary>
    private static bool HasAnyLightComponent(Node node) =>
        node.Entity.TryGet<DirectionalLightInfo>(out _)
        || node.Entity.TryGet<RangeLightInfo>(out _);

    /// <summary>
    /// Finds the single reference-resolution diagnostic the Scene_Builder adds for the referencing
    /// node. These are identified by <c>ElementType == "Node"</c>; converter-level diagnostics
    /// (unknown light type, etc.) use <c>ElementType == "Light"</c> and are excluded here.
    /// </summary>
    private static ImportDiagnostic? FindNodeDiagnostic(
        List<ImportDiagnostic> diagnostics,
        int nodeIndex
    ) => diagnostics.SingleOrDefault(d => d.ElementType == "Node" && d.ElementIndex == nodeIndex);

    #endregion

    /// <summary>
    /// Property 3: Invalid or unresolvable light references attach nothing and report the correct
    /// diagnostic. For any light reference index and lights-array length, an out-of-range index
    /// (negative or &gt;= length, including empty arrays) yields no light component and a Warning
    /// diagnostic naming the node and offending light index; an in-range index pointing at an
    /// unconvertible (<c>null</c>) slot yields no light component and an Error diagnostic naming the
    /// node and offending light index.
    /// **Validates: Requirements 1.2, 5.1, 5.2**
    /// </summary>
    [TestMethod]
    public void InvalidLightReference_AttachesNothing_AndReportsCorrectDiagnostic()
    {
        // Generator: a lights-array validity vector (each slot valid/invalid; may be empty) and an
        // arbitrary reference index spanning negative, in-range, and past-the-end values.
        var inputGen =
            from slots in Gen.ArrayOf(Gen.Elements(true, false))
            from index in Gen.Choose(-3, slots.Length + 3)
                // Keep only invalid references: out-of-range, or in-range pointing at a null slot.
                // In-range references to a valid slot are the (separate) successful-attach case.
            where index < 0 || index >= slots.Length || !slots[index]
            select (slots, index);

        Prop.ForAll(
                Arb.From(inputGen),
                ((bool[] slots, int index) input) =>
                {
                    // Keep the ECS world alive while reading components/diagnostics.
                    using var world = World.CreateWorld();
                    var (node, diagnostics) = BuildAndImport(input.slots, input.index, world);

                    // (a) No punctual light component is attached, regardless of failure mode.
                    if (HasAnyLightComponent(node))
                    {
                        return false;
                    }

                    bool outOfRange = input.index < 0 || input.index >= input.slots.Length;
                    var expectedSeverity = outOfRange
                        ? DiagnosticSeverity.Warning // Requirement 5.1
                        : DiagnosticSeverity.Error; // Requirement 5.2

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

                    // ... and names both the node index and the offending light index in its message.
                    bool namesNode = diagnostic.Message.Contains($"Node {NodeIndex}");
                    bool namesLightIndex = diagnostic.Message.Contains(
                        $"light index {input.index}"
                    );

                    return namesNode && namesLightIndex;
                }
            )
            .Check(FsCheckConfig);
    }
}
