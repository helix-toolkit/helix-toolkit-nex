using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
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

// Feature: gltf-directionallight-render-fix, Property 5: Import continues across any mix of valid and invalid light references

/// <summary>
/// Property-based test for Property 5 of the gltf-directionallight-render-fix feature.
/// For any node tree containing an arbitrary mix of valid, out-of-range, and unconvertible
/// light references, the import completes without throwing or aborting and every node in the
/// tree is processed.
/// Mirrors the existing <see cref="LightDirectionPropertyTests"/> /
/// <see cref="WorldTransformHierarchyPropertyTests"/> pattern: build an in-memory
/// <see cref="Gltf"/> model whose nodes reference a mix of valid/invalid
/// <c>KHR_lights_punctual</c> lights, run <see cref="SceneBuilder.BuildScene"/> against a
/// <see cref="World"/> with mock managers, and assert the build completes and produces one
/// engine node per glTF node.
/// </summary>
[TestClass]
public class ImportContinuationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// The index, within the document-level lights array, of a valid convertible directional light.
    /// </summary>
    private const int ValidDirectionalLightIndex = 0;

    /// <summary>
    /// The index, within the document-level lights array, of an in-range-but-unconvertible light
    /// (an entry with an unknown <c>type</c>, parsed to a <c>null</c> slot).
    /// </summary>
    private const int UnconvertibleLightIndex = 1;

    /// <summary>
    /// The number of entries in the document-level lights array. Indices &gt;= this value (and any
    /// negative index) are out of range.
    /// </summary>
    private const int LightsArrayLength = 2;

    /// <summary>
    /// A generated tree node carrying its light-reference choice and ordered children.
    /// <see cref="LightRef"/> is <c>null</c> when the node carries no <c>KHR_lights_punctual</c>
    /// extension; otherwise it is the (possibly out-of-range or unconvertible) referenced index.
    /// </summary>
    private sealed class RefTreeNode
    {
        public int? LightRef { get; init; }
        public List<RefTreeNode> Children { get; } = [];
    }

    #region Mock Infrastructure

    private sealed class StubGeometryManager : IGeometryManager
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

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer) => ResultCode.Ok;

        public void Dispose() { }
    }

    private sealed class StubMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public PBRMaterialProperties Create(string materialName) => _inner.Create("PBR");

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties) =>
            _inner.Create("PBR", ref properties);

        public PBRMaterialProperties Create(MaterialTypeId materialTypeId) =>
            _inner.Create(materialTypeId);

        public PBRMaterialProperties Create(
            MaterialTypeId materialTypeId,
            ref PBRProperties properties
        ) => _inner.Create(materialTypeId, ref properties);

        public void Clear() => _inner.Clear();

        public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
            _inner.Objects;

        public ref PBRProperties At(int index) => ref _inner.At(index);

        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer) => ResultCode.Ok;

        public ResultCode UploadDynamic(
            ElementBuffer<PBRProperties> buffer,
            IEnumerable<uint> indices
        ) => ResultCode.Ok;

        public void Dispose() => _inner.Dispose();
    }

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
            NexImage image,
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
            NexImage image,
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
    /// Property 5: Import continues across any mix of valid and invalid light references.
    /// For any node tree containing an arbitrary mix of valid, out-of-range, and unconvertible
    /// light references, the import completes without throwing or aborting and every node in the
    /// tree is processed.
    /// **Validates: Requirements 5.5**
    /// </summary>
    [TestMethod]
    public void Import_ContinuesAcrossMixedLightReferences_ProcessingEveryNode()
    {
        var treeGen = GenRefTree(3);

        Prop.ForAll(
                Arb.From(treeGen),
                (RefTreeNode root) =>
                {
                    // Flatten the generated tree into a glTF Nodes array (root at index 0),
                    // preserving child ordering, so every node is represented.
                    var flattened = new List<RefTreeNode>();
                    FlattenPreOrder(root, flattened);

                    var gltfNodes = new GltfNode[flattened.Count];
                    for (int i = 0; i < flattened.Count; i++)
                    {
                        var spec = flattened[i];
                        var gltfNode = new GltfNode { Name = $"Node_{i}" };

                        // Attach a node-level KHR_lights_punctual extension referencing the chosen
                        // (possibly out-of-range/unconvertible) light index. A null LightRef means
                        // the node carries no extension at all.
                        if (spec.LightRef is int lightRef)
                        {
                            gltfNode.Extensions = new Dictionary<string, object>
                            {
                                [LightConverter.ExtensionName] = new JObject
                                {
                                    ["light"] = lightRef,
                                },
                            };
                        }

                        if (spec.Children.Count > 0)
                        {
                            var childIndices = new int[spec.Children.Count];
                            for (int j = 0; j < spec.Children.Count; j++)
                            {
                                childIndices[j] = flattened.IndexOf(spec.Children[j]);
                            }
                            gltfNode.Children = childIndices;
                        }

                        gltfNodes[i] = gltfNode;
                    }

                    // Document-level lights array exercising both diagnostic paths:
                    //   index 0 → a valid, convertible directional light;
                    //   index 1 → an unknown-type definition that parses to a null (unconvertible) slot.
                    var documentExtension = new JObject
                    {
                        ["lights"] = new JArray
                        {
                            new JObject { ["type"] = "directional" },
                            new JObject { ["type"] = "unknown-light-type" },
                        },
                    };

                    var model = new Gltf
                    {
                        Nodes = gltfNodes,
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
                        manifest
                    );

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

                    // (1) The import must complete without throwing or aborting.
                    Node sceneRoot;
                    try
                    {
                        sceneRoot = sceneBuilder.BuildScene(model, 0);
                    }
                    catch
                    {
                        return false;
                    }

                    // (2) The import returns a root node.
                    if (sceneRoot is null)
                    {
                        return false;
                    }

                    // (3) Every node in the tree is processed: because the generated nodes carry no
                    // mesh reference, each glTF node yields exactly one engine node, so the number of
                    // engine nodes under the synthetic scene root equals the number of glTF nodes.
                    int engineNodeCount = CountEngineNodes(sceneRoot);
                    return engineNodeCount == flattened.Count;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Counts the engine nodes produced for glTF nodes: all descendants of the synthetic scene
    /// root, excluding the synthetic root itself.
    /// </summary>
    private static int CountEngineNodes(Node sceneRoot)
    {
        int count = 0;
        var children = sceneRoot.Children;
        if (children != null)
        {
            foreach (var child in children)
            {
                count += CountSubtree(child);
            }
        }
        return count;
    }

    /// <summary>
    /// Counts a node and all of its descendants.
    /// </summary>
    private static int CountSubtree(Node node)
    {
        int count = 1;
        var children = node.Children;
        if (children != null)
        {
            foreach (var child in children)
            {
                count += CountSubtree(child);
            }
        }
        return count;
    }

    /// <summary>
    /// Flattens the tree in pre-order so the root is index 0 and children follow their parent,
    /// preserving sibling ordering.
    /// </summary>
    private static void FlattenPreOrder(RefTreeNode node, List<RefTreeNode> output)
    {
        output.Add(node);
        foreach (var child in node.Children)
        {
            FlattenPreOrder(child, output);
        }
    }

    /// <summary>
    /// Generates a random tree whose nodes reference an arbitrary mix of light references:
    /// a valid convertible directional light (index 0), an in-range-but-unconvertible light
    /// (index 1, a null slot), an out-of-range high index (&gt;= the lights length), an
    /// out-of-range negative index, and no extension at all (<c>null</c>).
    /// </summary>
    private static Gen<RefTreeNode> GenRefTree(int maxDepth)
    {
        // A mix of valid, out-of-range, and unconvertible references (and no-extension nodes).
        var lightRefGen = Gen.OneOf(
            Gen.Constant<int?>(ValidDirectionalLightIndex), // valid in-range convertible
            Gen.Constant<int?>(UnconvertibleLightIndex), // in-range but unconvertible (null slot)
            Gen.Choose(LightsArrayLength, LightsArrayLength + 5).Select(i => (int?)i), // out-of-range high
            Gen.Choose(-6, -1).Select(i => (int?)i), // out-of-range negative
            Gen.Constant<int?>(null) // no KHR_lights_punctual extension
        );

        Gen<RefTreeNode> GenAtDepth(int depth)
        {
            if (depth <= 0)
            {
                return lightRefGen.Select(r => new RefTreeNode { LightRef = r });
            }

            return lightRefGen.SelectMany(r =>
                Gen.Choose(0, 3)
                    .SelectMany(childCount =>
                    {
                        if (childCount == 0)
                        {
                            return Gen.Constant(new RefTreeNode { LightRef = r });
                        }

                        return Gen.ArrayOf(GenAtDepth(depth - 1), childCount)
                            .Select(children =>
                            {
                                var node = new RefTreeNode { LightRef = r };
                                node.Children.AddRange(children);
                                return node;
                            });
                    })
            );
        }

        return GenAtDepth(maxDepth);
    }
}
