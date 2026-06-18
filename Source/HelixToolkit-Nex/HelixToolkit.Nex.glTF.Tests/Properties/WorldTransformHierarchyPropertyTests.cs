using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using Gltf = glTFLoader.Schema.Gltf;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-directionallight-render-fix, Property 9: World transform composition and hierarchy are preserved

/// <summary>
/// Property-based test for Property 9 of the gltf-directionallight-render-fix feature.
/// Validates that for any glTF node tree, each engine node's world transform equals
/// <c>localTransform * parentWorld</c> (row-vector composition), and that the parent-child
/// hierarchy and child ordering of the engine scene graph match the source glTF node tree.
/// Mirrors the existing <see cref="TransformPropertyTests"/> / <c>SceneGraphPropertyTests</c>
/// pattern: build an in-memory <see cref="Gltf"/> model, run <see cref="SceneBuilder.BuildNode"/>
/// against a <see cref="World"/> with mock managers, and assert.
/// </summary>
[TestClass]
public class WorldTransformHierarchyPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Engine local transform is computed by the exact same formula as the expected local
    // (Scale * Rotation * Translation) from the same TRS inputs, so per-node values match to
    // near machine precision. The tolerance accounts only for accumulation across hierarchy depth.
    private const float Tolerance = 1e-3f;

    /// <summary>
    /// A generated tree node carrying its TRS and ordered children.
    /// </summary>
    private sealed class TrsTreeNode
    {
        public Vector3 Translation { get; init; }
        public Quaternion Rotation { get; init; }
        public Vector3 Scale { get; init; }
        public List<TrsTreeNode> Children { get; } = [];
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
    /// Property 9: World transform composition and hierarchy are preserved.
    /// For any glTF node tree, each engine node's world transform equals
    /// <c>localTransform * parentWorld</c>, and the parent-child hierarchy and child ordering
    /// of the engine scene graph match the glTF node tree.
    /// **Validates: Requirements 6.4**
    /// </summary>
    [TestMethod]
    public void WorldTransformComposition_AndHierarchy_ArePreserved_ForRandomNodeTrees()
    {
        var treeGen = GenTrsTree(3);

        Prop.ForAll(
                Arb.From(treeGen),
                (TrsTreeNode root) =>
                {
                    // Flatten the generated tree into a glTF Nodes array (root at index 0),
                    // preserving child ordering.
                    var flattened = new List<TrsTreeNode>();
                    FlattenPreOrder(root, flattened);

                    var gltfNodes = new GltfNode[flattened.Count];
                    for (int i = 0; i < flattened.Count; i++)
                    {
                        var spec = flattened[i];
                        var gltfNode = new GltfNode
                        {
                            Name = $"Node_{i}",
                            // Use the TRS path (no explicit matrix) so ApplyTransform reads TRS.
                            Translation =
                            [
                                spec.Translation.X,
                                spec.Translation.Y,
                                spec.Translation.Z,
                            ],
                            Rotation =
                            [
                                spec.Rotation.X,
                                spec.Rotation.Y,
                                spec.Rotation.Z,
                                spec.Rotation.W,
                            ],
                            Scale = [spec.Scale.X, spec.Scale.Y, spec.Scale.Z],
                        };

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

                    var model = new Gltf
                    {
                        Nodes = gltfNodes,
                        Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
                        Scene = 0,
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

                    // Build the node tree directly from the root (parentWorld = Identity).
                    var engineRoot = sceneBuilder.BuildNode(model, 0, null, Matrix4x4.Identity);

                    // Verify hierarchy, child ordering, local transform, and world composition.
                    return Verify(engineRoot, root, Matrix4x4.Identity, Matrix4x4.Identity);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Recursively verifies, for a matched pair of engine node and spec node:
    /// (1) the engine node's local transform equals the spec TRS composition;
    /// (2) the world transform equals <c>local * parentWorld</c> (compared between the
    /// engine-derived accumulation and the spec-derived accumulation);
    /// (3) the engine node has the same number of children in the same order as the spec.
    /// </summary>
    private static bool Verify(
        Node engineNode,
        TrsTreeNode spec,
        Matrix4x4 engineParentWorld,
        Matrix4x4 expectedParentWorld
    )
    {
        // Expected local transform from the glTF TRS spec (row-vector S * R * T).
        var expectedLocal =
            Matrix4x4.CreateScale(spec.Scale)
            * Matrix4x4.CreateFromQuaternion(spec.Rotation)
            * Matrix4x4.CreateTranslation(spec.Translation);

        var engineLocal = engineNode.Transform.Value;

        if (!MatrixApproxEqual(engineLocal, expectedLocal, Tolerance))
        {
            return false;
        }

        // World transform composition: world = local * parentWorld.
        var engineWorld = engineLocal * engineParentWorld;
        var expectedWorld = expectedLocal * expectedParentWorld;

        if (!MatrixApproxEqual(engineWorld, expectedWorld, Tolerance))
        {
            return false;
        }

        // Engine node children correspond exactly to the spec children (no mesh nodes are
        // present since generated nodes carry no mesh reference), preserving count and order.
        var engineChildren = engineNode.Children;
        int engineChildCount = engineChildren?.Count ?? 0;
        if (engineChildCount != spec.Children.Count)
        {
            return false;
        }

        for (int i = 0; i < spec.Children.Count; i++)
        {
            if (!Verify(engineChildren![i], spec.Children[i], engineWorld, expectedWorld))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatrixApproxEqual(Matrix4x4 a, Matrix4x4 b, float tolerance)
    {
        return MathF.Abs(a.M11 - b.M11) <= tolerance
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
    }

    /// <summary>
    /// Flattens the tree in pre-order so the root is index 0 and children follow their parent,
    /// preserving sibling ordering.
    /// </summary>
    private static void FlattenPreOrder(TrsTreeNode node, List<TrsTreeNode> output)
    {
        output.Add(node);
        foreach (var child in node.Children)
        {
            FlattenPreOrder(child, output);
        }
    }

    /// <summary>
    /// Generates a random TRS tree with a single root and bounded depth/breadth.
    /// </summary>
    private static Gen<TrsTreeNode> GenTrsTree(int maxDepth)
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

        // Scale: positive components (0.1 .. 10) to keep transforms non-degenerate.
        var scaleGen =
            from x in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            from y in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            from z in Gen.Choose(10, 1000).Select(i => i / 100.0f)
            select new Vector3(x, y, z);

        Gen<TrsTreeNode> GenAtDepth(int depth)
        {
            var trsGen =
                from t in translationGen
                from r in rotationGen
                from s in scaleGen
                select (t, r, s);

            if (depth <= 0)
            {
                return trsGen.Select(x => new TrsTreeNode
                {
                    Translation = x.t,
                    Rotation = x.r,
                    Scale = x.s,
                });
            }

            return trsGen.SelectMany(x =>
                Gen.Choose(0, 3)
                    .SelectMany(childCount =>
                    {
                        if (childCount == 0)
                        {
                            return Gen.Constant(
                                new TrsTreeNode
                                {
                                    Translation = x.t,
                                    Rotation = x.r,
                                    Scale = x.s,
                                }
                            );
                        }

                        return Gen.ArrayOf(GenAtDepth(depth - 1), childCount)
                            .Select(children =>
                            {
                                var node = new TrsTreeNode
                                {
                                    Translation = x.t,
                                    Rotation = x.r,
                                    Scale = x.s,
                                };
                                node.Children.AddRange(children);
                                return node;
                            });
                    })
            );
        }

        return GenAtDepth(maxDepth);
    }
}
