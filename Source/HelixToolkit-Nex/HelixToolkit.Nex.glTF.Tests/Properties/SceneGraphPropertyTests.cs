using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Shaders.Frag;
using HelixToolkit.Nex.Textures;
using Accessor = glTFLoader.Schema.Accessor;
using BufferView = glTFLoader.Schema.BufferView;
using Gltf = glTFLoader.Schema.Gltf;
using GltfBuffer = glTFLoader.Schema.Buffer;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using Mesh = glTFLoader.Schema.Mesh;
using MeshPrimitive = glTFLoader.Schema.MeshPrimitive;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 1: Scene graph topology preservation

/// <summary>
/// Property-based tests for scene graph topology preservation.
/// Validates that the imported engine scene graph has identical parent-child topology,
/// branching structure, and child ordering as the source glTF node hierarchy.
/// </summary>
[TestClass]
public class SceneGraphPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Represents a tree node structure for generating random glTF hierarchies.
    /// </summary>
    private sealed class TreeNode
    {
        public int Index { get; set; }
        public List<TreeNode> Children { get; } = [];
    }

    #region Mock Dependencies

    /// <summary>
    /// A simple mock IGeometryManager. Not actually called since nodes have no mesh references.
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

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer)
        {
            return ResultCode.Ok;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A mock IPBRMaterialPropertyManager that creates valid materials using PBRShadingMode.PBR.
    /// </summary>
    private sealed class MockMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
            _inner.Objects;

        public PBRMaterialProperties Create(string materialName) =>
            _inner.Create(PBRShadingMode.PBR);

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties) =>
            _inner.Create(PBRShadingMode.PBR, ref properties);

        public PBRMaterialProperties Create(MaterialTypeId materialTypeId) =>
            _inner.Create(materialTypeId);

        public PBRMaterialProperties Create(
            MaterialTypeId materialTypeId,
            ref PBRProperties properties
        ) => _inner.Create(materialTypeId, ref properties);

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

        public void Clear() => _inner.Clear();

        public void Dispose() => _inner.Dispose();
    }

    /// <summary>
    /// A minimal mock ITextureRepository. Not called in this test.
    /// </summary>
    private sealed class StubTextureRepository : ITextureRepository
    {
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

        public void Clear() { }

        public int CleanupExpired() => 0;

        public RepositoryStatistics GetStatistics() =>
            new()
            {
                TotalEntries = 0,
                MaxEntries = 0,
                TotalHits = 0,
                TotalMisses = 0,
            };

        public void Dispose() { }
    }

    /// <summary>
    /// A minimal mock ISamplerRepository. Not called in this test.
    /// </summary>
    private sealed class StubSamplerRepository : ISamplerRepository
    {
        private readonly MockContext _context = new();
        private readonly SamplerRepository _inner;

        public StubSamplerRepository()
        {
            _context.Initialize();
            _inner = new SamplerRepository(_context);
        }

        public int Count => _inner.Count;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
            _inner.GetOrCreate(key, desc);

        public bool Remove(string key) => _inner.Remove(key);

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry) =>
            _inner.TryGet(cacheKey, out entry);

        public void Clear() => _inner.Clear();

        public int CleanupExpired() => _inner.CleanupExpired();

        public RepositoryStatistics GetStatistics() => _inner.GetStatistics();

        public void Dispose()
        {
            _inner.Dispose();
            _context.Dispose();
        }
    }

    #endregion

    /// <summary>
    /// Property 1: For any valid glTF node hierarchy, the imported engine scene graph SHALL have
    /// identical parent-child topology — the same number of nodes at each level, the same branching
    /// structure, and the same child ordering within each parent.
    /// **Validates: Requirements 2.1, 2.7**
    /// </summary>
    [TestMethod]
    public void BuildScene_PreservesTopology_ForRandomNodeHierarchies()
    {
        // Generator: random tree structures with 1..20 nodes and random branching
        var treeGen = GenRandomTree();

        Prop.ForAll(
                Arb.From(treeGen),
                (List<TreeNode> roots) =>
                {
                    // Build a glTF model from the generated tree structure
                    var (model, expectedRoots) = BuildGltfModelFromTree(roots);

                    // Create SceneBuilder with mock dependencies
                    using var world = World.CreateWorld();
                    var diagnostics = new List<ImportDiagnostic>();

                    var geoManager = new MockGeometryManager();
                    var accessorReader = new AccessorReader(model, []);
                    var manifest = new ResourceManifest();
                    var meshConverter = new MeshConverter(
                        geoManager,
                        accessorReader,
                        diagnostics,
                        manifest
                    );

                    using var materialManager = new PBRMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository();
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

                    // Act: build the scene
                    var rootNode = sceneBuilder.BuildScene(model, 0);

                    // Assert: verify topology matches
                    // The root node is the scene root; its children correspond to the scene's root nodes
                    var engineChildren = rootNode.Children;
                    if (engineChildren == null || engineChildren.Count != expectedRoots.Count)
                        return false;

                    // Recursively verify topology
                    for (int i = 0; i < expectedRoots.Count; i++)
                    {
                        if (!VerifyTopology(expectedRoots[i], engineChildren[i]))
                            return false;
                    }

                    return true;
                }
            )
            .Check(Config.QuickThrowOnFailure.WithMaxTest(100));
    }

    /// <summary>
    /// Generates a random tree structure with 1..20 nodes and random branching.
    /// Returns a list of root TreeNodes (the scene's top-level nodes).
    /// </summary>
    private static Gen<List<TreeNode>> GenRandomTree()
    {
        return Gen.Choose(1, 20)
            .SelectMany(totalNodes =>
            {
                // Generate a random tree with the given number of nodes
                int numRoots = Math.Max(1, Math.Min(totalNodes, 5));
                return Gen.Choose(1, numRoots)
                    .SelectMany(actualRoots =>
                    {
                        return GenTreeWithNodes(totalNodes, actualRoots);
                    });
            });
    }

    /// <summary>
    /// Generates a tree with a specific total number of nodes and root count.
    /// Uses a random assignment strategy to build the tree.
    /// </summary>
    private static Gen<List<TreeNode>> GenTreeWithNodes(int totalNodes, int numRoots)
    {
        int nonRootNodes = totalNodes - numRoots;

        if (nonRootNodes <= 0)
        {
            // All nodes are roots
            return Gen.Constant(
                Enumerable.Range(0, totalNodes).Select(i => new TreeNode { Index = i }).ToList()
            );
        }

        // For each non-root node, generate which existing node is its parent
        return Gen.ArrayOf(Gen.Choose(0, int.MaxValue - 1), nonRootNodes)
            .Select(parentIndices =>
            {
                var allNodes = new List<TreeNode>(totalNodes);
                var roots = new List<TreeNode>();

                // Create root nodes
                for (int i = 0; i < numRoots; i++)
                {
                    var node = new TreeNode { Index = i };
                    allNodes.Add(node);
                    roots.Add(node);
                }

                // Create non-root nodes and assign parents
                for (int i = 0; i < nonRootNodes; i++)
                {
                    var node = new TreeNode { Index = numRoots + i };
                    // Parent must be one of the already-created nodes
                    int parentIdx = parentIndices[i] % allNodes.Count;
                    allNodes[parentIdx].Children.Add(node);
                    allNodes.Add(node);
                }

                return roots;
            });
    }

    /// <summary>
    /// Builds a glTF model from the generated tree structure.
    /// Returns the model and the list of root TreeNodes for verification.
    /// </summary>
    private static (Gltf model, List<TreeNode> roots) BuildGltfModelFromTree(List<TreeNode> roots)
    {
        // Flatten the tree to assign glTF node indices
        var allNodes = new List<TreeNode>();
        FlattenTree(roots, allNodes);

        // Create glTF nodes array
        var gltfNodes = new GltfNode[allNodes.Count];
        for (int i = 0; i < allNodes.Count; i++)
        {
            var treeNode = allNodes[i];
            var gltfNode = new GltfNode { Name = $"Node_{treeNode.Index}" };

            if (treeNode.Children.Count > 0)
            {
                var childIndices = new int[treeNode.Children.Count];
                for (int j = 0; j < treeNode.Children.Count; j++)
                {
                    int idx = allNodes.IndexOf(treeNode.Children[j]);
                    if (idx < 0)
                        throw new InvalidOperationException(
                            $"Child node not found in flattened list"
                        );
                    childIndices[j] = idx;
                }
                gltfNode.Children = childIndices;
            }

            gltfNodes[i] = gltfNode;
        }

        // Create scene with root node indices
        var rootIndices = roots.Select(r => allNodes.IndexOf(r)).ToArray();

        var model = new Gltf
        {
            Nodes = gltfNodes,
            Scenes = [new GltfScene { Name = "TestScene", Nodes = rootIndices }],
            Scene = 0,
        };

        return (model, roots);
    }

    /// <summary>
    /// Flattens the tree into a list using breadth-first traversal to assign indices.
    /// </summary>
    private static void FlattenTree(List<TreeNode> roots, List<TreeNode> output)
    {
        var queue = new Queue<TreeNode>();
        foreach (var root in roots)
        {
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            output.Add(node);
            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// Recursively verifies that the engine node's topology matches the expected tree node.
    /// Checks child count and ordering at each level.
    /// </summary>
    private static bool VerifyTopology(TreeNode expected, Node actual)
    {
        // Verify child count matches
        int expectedChildCount = expected.Children.Count;
        var actualChildren = actual.Children;
        int actualChildCount = actualChildren?.Count ?? 0;

        if (expectedChildCount != actualChildCount)
            return false;

        // Verify child ordering recursively
        if (expectedChildCount > 0)
        {
            for (int i = 0; i < expectedChildCount; i++)
            {
                if (!VerifyTopology(expected.Children[i], actualChildren![i]))
                    return false;
            }
        }

        return true;
    }

    // Feature: gltf-importer, Property 3: Node name preservation

    /// <summary>
    /// Property 3: For any glTF node with a non-null name string, the corresponding engine Node
    /// SHALL have its Name property equal to that string.
    /// **Validates: Requirements 2.6**
    /// </summary>
    [TestMethod]
    public void BuildScene_PreservesNodeNames_ForRandomNameStrings()
    {
        // Generator: produce 1..10 non-null, non-empty name strings
        // using alphanumeric + common characters (spaces, underscores, hyphens, dots)
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _-.";

        var nameGen =
            from length in Gen.Choose(1, 50)
            from chars in Gen.ArrayOf(
                Gen.Choose(0, validChars.Length - 1).Select(i => validChars[i]),
                length
            )
            select new string(chars);

        var namesGen =
            from count in Gen.Choose(1, 10)
            from names in Gen.ArrayOf(nameGen, count)
            select names;

        Prop.ForAll(
                Arb.From(namesGen),
                (string[] names) =>
                {
                    using var world = World.CreateWorld();
                    var diagnostics = new List<ImportDiagnostic>();

                    // Create minimal dependencies for SceneBuilder
                    var emptyModel = new Gltf();
                    var accessorReader = new AccessorReader(emptyModel, []);
                    var geoManager = new MockGeometryManager();
                    var manifest = new ResourceManifest();
                    var meshConverter = new MeshConverter(
                        geoManager,
                        accessorReader,
                        diagnostics,
                        manifest
                    );

                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository();
                    using var materialManager = new PBRMaterialPropertyManager();

                    var textureLoader = new TextureLoader(
                        textureRepo,
                        samplerRepo,
                        "C:\\test",
                        emptyModel,
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

                    var builder = new SceneBuilder(
                        world,
                        meshConverter,
                        materialConverter,
                        new LightConverter(diagnostics, ImporterConfig.Default),
                        diagnostics,
                        ImporterConfig.Default
                    );

                    // Build glTF nodes with the generated names as top-level scene nodes
                    var gltfNodes = new GltfNode[names.Length];
                    var sceneNodeIndices = new int[names.Length];

                    for (int i = 0; i < names.Length; i++)
                    {
                        gltfNodes[i] = new GltfNode { Name = names[i] };
                        sceneNodeIndices[i] = i;
                    }

                    var model = new Gltf
                    {
                        Nodes = gltfNodes,
                        Scenes = [new GltfScene { Name = "TestScene", Nodes = sceneNodeIndices }],
                        Scene = 0,
                    };

                    // Act: build the scene
                    var rootNode = builder.BuildScene(model, 0);

                    // Assert: each child of the root node should have the corresponding name
                    if (rootNode.ChildCount != names.Length)
                        return false;

                    for (int i = 0; i < names.Length; i++)
                    {
                        var childNode = rootNode.Children![i];
                        if (childNode.Name != names[i])
                            return false;
                    }

                    // Cleanup
                    rootNode.Dispose();
                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    // Feature: gltf-importer, Property 12: Mesh node structure

    /// <summary>
    /// Creates a glTF model with a single mesh containing N primitives, each with valid POSITION data.
    /// Returns the model and the buffer data needed for AccessorReader.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithNPrimitives(
        int primitiveCount,
        string meshName = "TestMesh"
    )
    {
        // Each primitive has 3 vertices (a triangle) with POSITION data
        int verticesPerPrimitive = 3;
        int bytesPerPrimitive = verticesPerPrimitive * 3 * sizeof(float); // 3 floats per vertex (VEC3)
        int totalBytes = primitiveCount * bytesPerPrimitive;

        var byteBuffer = new byte[totalBytes];

        // Fill buffer with valid position data for each primitive
        for (int p = 0; p < primitiveCount; p++)
        {
            float offset = p * 2.0f;
            float[] positions = [offset + 0f, 0f, 0f, offset + 1f, 0f, 0f, offset + 0f, 1f, 0f];
            System.Buffer.BlockCopy(
                positions,
                0,
                byteBuffer,
                p * bytesPerPrimitive,
                bytesPerPrimitive
            );
        }

        // Create accessors and buffer views for each primitive
        var accessors = new Accessor[primitiveCount];
        var bufferViews = new BufferView[primitiveCount];

        for (int p = 0; p < primitiveCount; p++)
        {
            bufferViews[p] = new BufferView
            {
                Buffer = 0,
                ByteOffset = p * bytesPerPrimitive,
                ByteLength = bytesPerPrimitive,
            };

            accessors[p] = new Accessor
            {
                BufferView = p,
                ByteOffset = 0,
                ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                Type = Accessor.TypeEnum.VEC3,
                Count = verticesPerPrimitive,
            };
        }

        // Create primitives referencing their respective accessors
        var primitives = new MeshPrimitive[primitiveCount];
        for (int p = 0; p < primitiveCount; p++)
        {
            primitives[p] = new MeshPrimitive
            {
                Attributes = new Dictionary<string, int> { ["POSITION"] = p },
                Mode = MeshPrimitive.ModeEnum.TRIANGLES,
            };
        }

        var model = new Gltf
        {
            Accessors = accessors,
            BufferViews = bufferViews,
            Buffers = [new GltfBuffer { ByteLength = totalBytes }],
            Meshes = [new Mesh { Name = meshName, Primitives = primitives }],
            Nodes = [new GltfNode { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
        };

        return (model, byteBuffer);
    }

    /// <summary>
    /// Property 12: For any glTF mesh with exactly 1 primitive, the importer SHALL produce
    /// a single MeshNode (no intermediate parent). For any glTF mesh with N > 1 primitives,
    /// the importer SHALL produce a parent Node with exactly N child MeshNodes.
    /// **Validates: Requirements 6.4, 6.5**
    /// </summary>
    [TestMethod]
    public void MeshNodeStructure_SinglePrimitive_ProducesSingleMeshNode_MultiplePrimitives_ProducesParentWithNChildren()
    {
        // Generate N (1..5) primitives per mesh
        var primCountGen = Gen.Choose(1, 5);

        Prop.ForAll(
                Arb.From(primCountGen),
                (int primitiveCount) =>
                {
                    // Arrange: Create a glTF model with a node referencing a mesh with N primitives
                    var (model, buffer) = CreateModelWithNPrimitives(primitiveCount);

                    using var world = World.CreateWorld();
                    var diagnostics = new List<ImportDiagnostic>();

                    var geoManager = new MockGeometryManager();
                    var accessorReader = new AccessorReader(model, [buffer]);
                    var manifest = new ResourceManifest();
                    var meshConverter = new MeshConverter(
                        geoManager,
                        accessorReader,
                        diagnostics,
                        manifest
                    );

                    using var materialManager = new MockMaterialPropertyManager();
                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository();
                    var textureLoader = new TextureLoader(
                        textureRepo,
                        samplerRepo,
                        "C:\\fake",
                        model,
                        [buffer],
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

                    // Act: Build the scene
                    var rootNode = sceneBuilder.BuildScene(model, -1);

                    // The root node ("TestScene") should have one child (the "TestNode")
                    var rootChildren = rootNode.Children;
                    if (rootChildren == null || rootChildren.Count != 1)
                        return false;

                    var testNode = rootChildren[0];

                    if (primitiveCount == 1)
                    {
                        // If N == 1: the node has exactly 1 child that is a MeshNode
                        var children = testNode.Children;
                        if (children == null || children.Count != 1)
                            return false;

                        if (children[0] is not MeshNode)
                            return false;
                    }
                    else
                    {
                        // If N > 1: the node has a child Node (named after mesh) with exactly N child MeshNodes
                        var children = testNode.Children;
                        if (children == null || children.Count != 1)
                            return false;

                        var meshParentNode = children[0];

                        // The parent node should be named after the mesh
                        if (meshParentNode.Name != "TestMesh")
                            return false;

                        // The parent node should have exactly N children
                        var meshChildren = meshParentNode.Children;
                        if (meshChildren == null || meshChildren.Count != primitiveCount)
                            return false;

                        // All children should be MeshNodes
                        for (int i = 0; i < meshChildren.Count; i++)
                        {
                            if (meshChildren[i] is not MeshNode)
                                return false;
                        }
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
