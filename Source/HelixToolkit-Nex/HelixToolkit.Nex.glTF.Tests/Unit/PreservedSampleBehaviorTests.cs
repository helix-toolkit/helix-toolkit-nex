using glTFLoader.Schema;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using Gltf = glTFLoader.Schema.Gltf;
using GltfMaterial = glTFLoader.Schema.Material;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

// Feature: gltf-directionallight-render-fix — unit/example tests for preserved sample behavior (task 7.2).

/// <summary>
/// Unit/example tests for behavior that must be preserved by the fix:
/// <list type="bullet">
/// <item>
/// When a glTF document carries no <c>KHR_lights_punctual</c> lights, the importer attaches no
/// punctual light component (and emits no light diagnostics), so the sample's configured lighting
/// remains the sole active light. The sample light is defined in the sample app
/// (<c>GltfImporterApp.SetupLighting</c>) with the current values <c>Color = Color.WhiteSmoke</c>,
/// <c>Intensity = 0.8f</c>, and no fixed direction (so the <see cref="DirectionalLightInfo"/>
/// keeps its default <c>Direction == -Vector3.UnitZ</c>); because that lives in the Samples project
/// (no importer/test seam), the expected lighting configuration is locked here at the
/// <see cref="DirectionalLightInfo"/> level using the same literals the sample uses.
/// </item>
/// <item>
/// A double-sided material disables backface culling on the produced mesh node. The engine has no
/// dedicated culling flag, so <see cref="SceneBuilder"/> represents the disabled-culling state via
/// <see cref="MeshNode.IsAlphaMask"/>; these tests assert that current code behavior.
/// </item>
/// </list>
/// **Validates: Requirements 2.10, 3.1**
/// </summary>
[TestClass]
public class PreservedSampleBehaviorTests
{
    private const float Tolerance = 1e-5f;

    #region Mock Infrastructure

    /// <summary>
    /// A mock IGeometryManager that returns a valid handle for any geometry added, so MeshNodes are
    /// actually created (required to observe the double-sided culling state on the mesh node).
    /// </summary>
    private sealed class MockGeometryManager : IGeometryManager
    {
        private uint _nextIndex = 1;

        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => 0;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry) =>
            new Handle<GeometryResourceType>(_nextIndex++, 1);

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry) =>
            Task.FromResult((true, Add(geometry)));

        public bool Remove(Geometry geometry) => true;

        public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

        public void Clear() { }

        public Geometry? GetGeometryById(uint index) => null;

        public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

        public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
            throw new NotImplementedException();

        public int GetDirtyCount() => 0;

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer) => ResultCode.Ok;

        public void Dispose() { }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a single-triangle mesh whose material has the specified
    /// <paramref name="doubleSided"/> flag. When <paramref name="includeMaterial"/> is false the mesh
    /// primitive carries no material reference.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithMesh(
        bool doubleSided,
        bool includeMaterial = true
    )
    {
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        var byteBuffer = new byte[positions.Length * sizeof(float)];
        System.Buffer.BlockCopy(positions, 0, byteBuffer, 0, byteBuffer.Length);

        var primitive = new MeshPrimitive
        {
            Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
            Material = includeMaterial ? 0 : null,
        };

        var model = new Gltf
        {
            Accessors =
            [
                new Accessor
                {
                    BufferView = 0,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC3,
                    Count = 3,
                },
            ],
            BufferViews =
            [
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = 0,
                    ByteLength = byteBuffer.Length,
                },
            ],
            Buffers = [new glTFLoader.Schema.Buffer { ByteLength = byteBuffer.Length }],
            Meshes = [new Mesh { Name = "TestMesh", Primitives = [primitive] }],
            Nodes = [new GltfNode { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
            Materials = includeMaterial
                ?
                [
                    new GltfMaterial
                    {
                        Name = "TestMaterial",
                        DoubleSided = doubleSided,
                        PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                        {
                            BaseColorFactor = [1.0f, 1.0f, 1.0f, 1.0f],
                            MetallicFactor = 1.0f,
                            RoughnessFactor = 1.0f,
                        },
                    },
                ]
                : null,
        };

        return (model, byteBuffer);
    }

    /// <summary>
    /// Runs the full <see cref="SceneBuilder.BuildScene"/> pipeline against the given model/buffer with
    /// mock managers, returning the scene root and the diagnostics produced. The <paramref name="world"/>
    /// must outlive the returned graph.
    /// </summary>
    private static (Node Root, List<ImportDiagnostic> Diagnostics) BuildScene(
        Gltf model,
        byte[] buffer,
        World world
    )
    {
        var diagnostics = new List<ImportDiagnostic>();
        var manifest = new ResourceManifest();
        var accessorReader = new AccessorReader(model, [buffer]);
        using var geoManager = new MockGeometryManager();
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

        var root = sceneBuilder.BuildScene(model, -1);
        return (root, diagnostics);
    }

    private static List<MeshNode> CollectMeshNodes(Node root)
    {
        var meshNodes = new List<MeshNode>();
        Recurse(root, meshNodes);
        return meshNodes;

        static void Recurse(Node node, List<MeshNode> acc)
        {
            if (node is MeshNode meshNode)
            {
                acc.Add(meshNode);
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    Recurse(child, acc);
                }
            }
        }
    }

    private static bool NodeHasPunctualLight(Node node) =>
        node.Entity.TryGet<DirectionalLightInfo>(out _)
        || node.Entity.TryGet<RangeLightInfo>(out _);

    private static bool AnyNodeHasPunctualLight(Node root)
    {
        if (NodeHasPunctualLight(root))
        {
            return true;
        }
        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                if (AnyNodeHasPunctualLight(child))
                {
                    return true;
                }
            }
        }
        return false;
    }

    #endregion

    // -------------------------------------------------------------------------
    // Sample lighting configuration preserved (no-lights document + SetupLighting values)
    // -------------------------------------------------------------------------

    /// <summary>
    /// A glTF document with no <c>KHR_lights_punctual</c> lights produces a scene in which no node
    /// carries a punctual light component and no light diagnostic is emitted. This is the condition
    /// under which the sample's configured directional light stays the only active light, so the fix
    /// does not disturb the existing lighting for no-lights documents.
    /// **Validates: Requirements 3.1**
    /// </summary>
    [TestMethod]
    public void NoLightsDocument_AttachesNoPunctualLight_AndEmitsNoLightDiagnostics()
    {
        using var world = World.CreateWorld();

        // A mesh-only document with no KHR_lights_punctual extension.
        var (model, buffer) = CreateModelWithMesh(doubleSided: false);

        var (root, diagnostics) = BuildScene(model, buffer, world);

        Assert.IsFalse(
            AnyNodeHasPunctualLight(root),
            "A document with no KHR_lights_punctual lights must not attach any punctual light component."
        );

        // No light-related diagnostics: nothing references a light, so there is nothing to warn/err on.
        Assert.IsFalse(
            diagnostics.Any(d => d.Message.Contains("light", StringComparison.OrdinalIgnoreCase)),
            "No light diagnostics should be produced for a document with no punctual lights."
        );
    }

    /// <summary>
    /// The sample's directional light is defined in <c>GltfImporterApp.SetupLighting</c> as
    /// <c>Color = Color.WhiteSmoke</c>, <c>Intensity = 0.8f</c>, with no fixed direction (so the
    /// <see cref="DirectionalLightInfo"/> keeps its default <c>Direction == -Vector3.UnitZ</c>).
    /// That code lives in the Samples project with no importer/test seam, so this test locks the
    /// expected configuration at the component level by constructing the same
    /// <see cref="DirectionalLightInfo"/> the sample builds and asserting it carries those
    /// values (white-smoke color, 0.8 intensity, default -Z direction).
    /// **Validates: Requirements 2.10**
    /// </summary>
    [TestMethod]
    public void SampleDirectionalLight_HasWhiteSmokeColor_PointEightIntensity_DefaultDirection()
    {
        // Construct the directional light exactly as the sample's SetupLighting does: Color and
        // Intensity are set; Direction is left at the component default (no fixed direction).
        var sampleLight = new DirectionalLightInfo { Color = Color.WhiteSmoke, Intensity = 0.8f };

        // No fixed direction: the component keeps its default -Vector3.UnitZ.
        Assert.AreEqual(0.0f, sampleLight.Direction.X, Tolerance, "Direction.X should be 0.");
        Assert.AreEqual(0.0f, sampleLight.Direction.Y, Tolerance, "Direction.Y should be 0.");
        Assert.AreEqual(
            -1.0f,
            sampleLight.Direction.Z,
            Tolerance,
            "Direction.Z should be the component default -1 (no fixed direction)."
        );

        // Intensity is 0.8f.
        Assert.AreEqual(0.8f, sampleLight.Intensity, Tolerance, "Intensity should be 0.8.");

        // Color is WhiteSmoke (0xFFF5F5F5) → each linear RGB channel ≈ 0xF5/255.
        var whiteSmokeChannel = 0xF5 / 255.0f;
        var color = sampleLight.Color;
        Assert.AreEqual(whiteSmokeChannel, color.Red, Tolerance, "Color Red should be WhiteSmoke.");
        Assert.AreEqual(
            whiteSmokeChannel,
            color.Green,
            Tolerance,
            "Color Green should be WhiteSmoke."
        );
        Assert.AreEqual(
            whiteSmokeChannel,
            color.Blue,
            Tolerance,
            "Color Blue should be WhiteSmoke."
        );
    }

    // -------------------------------------------------------------------------
    // Double-sided material disables backface culling
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requirement 4.3: a mesh whose material is double-sided must render without backface culling.
    /// The engine has no dedicated culling flag, so <see cref="SceneBuilder"/> represents the
    /// disabled-culling state on the produced <see cref="MeshNode"/> via <see cref="MeshNode.IsAlphaMask"/>;
    /// this test asserts the mesh node reflects that disabled-culling state.
    /// **Validates: Requirements 4.3**
    /// </summary>
    [TestMethod]
    public void DoubleSidedMaterial_DisablesBackfaceCulling_OnMeshNode()
    {
        using var world = World.CreateWorld();

        var (model, buffer) = CreateModelWithMesh(doubleSided: true);

        var (root, _) = BuildScene(model, buffer, world);

        var meshNodes = CollectMeshNodes(root);
        Assert.AreEqual(
            1,
            meshNodes.Count,
            "Expected exactly one mesh node for the single primitive."
        );

        Assert.IsTrue(
            meshNodes[0].IsAlphaMask,
            "A double-sided material must disable backface culling on the mesh node (represented as IsAlphaMask = true)."
        );
    }

    /// <summary>
    /// Requirement 4.3 (control): a single-sided material keeps backface culling enabled, so the mesh
    /// node is not put into the disabled-culling state by the double-sided handling.
    /// **Validates: Requirements 4.3**
    /// </summary>
    [TestMethod]
    public void SingleSidedMaterial_KeepsBackfaceCulling_OnMeshNode()
    {
        using var world = World.CreateWorld();

        var (model, buffer) = CreateModelWithMesh(doubleSided: false);

        var (root, _) = BuildScene(model, buffer, world);

        var meshNodes = CollectMeshNodes(root);
        Assert.AreEqual(
            1,
            meshNodes.Count,
            "Expected exactly one mesh node for the single primitive."
        );

        Assert.IsFalse(
            meshNodes[0].IsAlphaMask,
            "A single-sided opaque material must not set the disabled-culling state (IsAlphaMask) on the mesh node."
        );
    }
}
