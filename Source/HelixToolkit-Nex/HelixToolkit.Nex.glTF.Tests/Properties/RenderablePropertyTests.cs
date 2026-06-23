using glTFLoader.Schema;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Tests.Mocks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using GltfScene = glTFLoader.Schema.Scene;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 13: Renderable invariant

/// <summary>
/// Property-based tests for the renderable invariant (Property 13).
/// Verifies that MeshNodes produced by the SceneBuilder have IsRenderable == true
/// only when both geometry handle is valid AND material is valid.
/// </summary>
[TestClass]
public class RenderablePropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock IGeometryManager that returns a valid handle for any geometry added.
    /// </summary>
    private sealed class MockGeometryManager : IGeometryManager
    {
        private uint _nextIndex = 1;

        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => 0;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry)
        {
            return new Handle<GeometryResourceType>(_nextIndex++, 1);
        }

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry)
        {
            var handle = Add(geometry);
            return Task.FromResult((true, handle));
        }

        public bool Remove(Geometry geometry) => true;

        public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

        public void Clear() { }

        public Geometry? GetGeometryById(uint index) => null;

        public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

        public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
            throw new NotImplementedException();

        public int GetDirtyCount() => 0;

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer)
        {
            return ResultCode.Ok;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A mock IGeometryManager that always returns an invalid handle (simulates geometry registration failure).
    /// </summary>
    private sealed class FailingGeometryManager : IGeometryManager
    {
        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => 0;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry) =>
            Handle<GeometryResourceType>.Null;

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry) =>
            Task.FromResult((false, Handle<GeometryResourceType>.Null));

        public bool Remove(Geometry geometry) => false;

        public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

        public void Clear() { }

        public Geometry? GetGeometryById(uint index) => null;

        public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

        public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
            throw new NotImplementedException();

        public int GetDirtyCount() => 0;

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer)
        {
            return ResultCode.Ok;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A mock IPBRMaterialPropertyManager that delegates to the real PBRMaterialPropertyManager
    /// using the built-in "PBR" registered material type.
    /// </summary>
    private sealed class MockPBRMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public PBRMaterialProperties Create(string materialName) => _inner.Create(materialName);

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties) =>
            _inner.Create(materialName, ref properties);

        public void Clear() => _inner.Clear();

        public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
            _inner.Objects;

        public ref PBRProperties At(int index) => ref _inner.At(index);

        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer)
        {
            return ResultCode.Ok;
        }

        public ResultCode UploadDynamic(
            ElementBuffer<PBRProperties> buffer,
            IEnumerable<uint> indices
        )
        {
            return ResultCode.Ok;
        }

        public void Dispose() => _inner.Dispose();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a glTF model with a simple triangle mesh and the specified number of primitives.
    /// Each primitive has valid POSITION data.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateGltfModelWithMesh(
        int primitiveCount,
        bool hasMaterial = true
    )
    {
        // 3 vertices forming a triangle
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        var byteBuffer = new byte[positions.Length * sizeof(float)];
        System.Buffer.BlockCopy(positions, 0, byteBuffer, 0, byteBuffer.Length);

        var primitives = new MeshPrimitive[primitiveCount];
        for (int i = 0; i < primitiveCount; i++)
        {
            primitives[i] = new MeshPrimitive
            {
                Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
                Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                Material = hasMaterial ? 0 : null,
            };
        }

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
            Meshes = [new Mesh { Name = "TestMesh", Primitives = primitives }],
            Nodes = [new glTFLoader.Schema.Node { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Materials = hasMaterial
                ?
                [
                    new glTFLoader.Schema.Material
                    {
                        Name = "TestMaterial",
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
    /// Collects all MeshNode instances from the scene graph by traversing the node tree.
    /// </summary>
    private static List<MeshNode> CollectMeshNodes(Node root)
    {
        var meshNodes = new List<MeshNode>();
        CollectMeshNodesRecursive(root, meshNodes);
        return meshNodes;
    }

    private static void CollectMeshNodesRecursive(Node node, List<MeshNode> meshNodes)
    {
        if (node is MeshNode meshNode)
        {
            meshNodes.Add(meshNode);
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CollectMeshNodesRecursive(child, meshNodes);
            }
        }
    }

    #endregion

    /// <summary>
    /// Property 13: For any MeshNode produced by the importer that has both a valid Geometry handle
    /// and a valid PBRMaterialProperties instance, the MeshNode's IsRenderable property SHALL be true.
    ///
    /// Test strategy:
    /// - Generate meshes with varying primitive counts (1 to 5)
    /// - Use a valid geometry manager (returns valid handles)
    /// - Use a valid material manager (returns valid materials)
    /// - Verify all resulting MeshNodes have IsRenderable == true
    /// **Validates: Requirements 6.7**
    /// </summary>
    [TestMethod]
    public void MeshNodes_WithValidGeometryAndMaterial_HaveIsRenderableTrue()
    {
        // Generator: number of primitives per mesh (1 to 5)
        var primitiveCountGen = Gen.Choose(1, 5);

        Prop.ForAll(
                Arb.From(primitiveCountGen),
                (int primitiveCount) =>
                {
                    using var world = World.CreateWorld();
                    using var geoManager = new MockGeometryManager();
                    using var materialManager = new MockPBRMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository(
                        StubSamplerRepositoryMode.MockContextBacked
                    );

                    var (model, buffer) = CreateGltfModelWithMesh(
                        primitiveCount,
                        hasMaterial: true
                    );

                    var diagnostics = new List<ImportDiagnostic>();
                    var accessorReader = new AccessorReader(model, [buffer]);
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

                    // Act
                    var rootNode = sceneBuilder.BuildScene(model, 0);

                    // Collect all MeshNodes from the scene graph
                    var meshNodes = CollectMeshNodes(rootNode);

                    // Assert: we should have exactly primitiveCount MeshNodes
                    if (meshNodes.Count != primitiveCount)
                        return false;

                    // Assert: all MeshNodes should have IsRenderable == true
                    // because both geometry handle is valid and material is valid
                    foreach (var meshNode in meshNodes)
                    {
                        if (!meshNode.IsRenderable)
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 13 (contrapositive): When the geometry manager returns invalid handles,
    /// no MeshNodes should be created at all (the SceneBuilder skips MeshNode creation
    /// when geometry handle is invalid).
    /// **Validates: Requirements 6.7**
    /// </summary>
    [TestMethod]
    public void MeshNodes_WithInvalidGeometry_AreNotCreated()
    {
        // Generator: number of primitives per mesh (1 to 5)
        var primitiveCountGen = Gen.Choose(1, 5);

        Prop.ForAll(
                Arb.From(primitiveCountGen),
                (int primitiveCount) =>
                {
                    using var world = World.CreateWorld();
                    using var geoManager = new FailingGeometryManager();
                    using var materialManager = new MockPBRMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository(
                        StubSamplerRepositoryMode.MockContextBacked
                    );

                    var (model, buffer) = CreateGltfModelWithMesh(
                        primitiveCount,
                        hasMaterial: true
                    );

                    var diagnostics = new List<ImportDiagnostic>();
                    var accessorReader = new AccessorReader(model, [buffer]);
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

                    // Act
                    var rootNode = sceneBuilder.BuildScene(model, 0);

                    // Collect all MeshNodes from the scene graph
                    var meshNodes = CollectMeshNodes(rootNode);

                    // Assert: no MeshNodes should exist because geometry handles are all invalid
                    return meshNodes.Count == 0;
                }
            )
            .Check(FsCheckConfig);
    }
}
