using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
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
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>An invalid <c>type</c> string that <see cref="LightConverter"/> cannot convert,
    /// yielding a <c>null</c> parsed slot at that index.</summary>
    private const string UnconvertibleLightType = "__unknown_light_type__";

    /// <summary>The glTF node index of the single referencing node built by the helper.</summary>
    private const int NodeIndex = 0;

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
        int referencedLightIndex
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

        using var world = World.CreateWorld();
        var diagnostics = new List<ImportDiagnostic>();

        var accessorReader = new AccessorReader(model, []);
        using var geoManager = new StubGeometryManager();
        var manifest = new ResourceManifest();
        var meshConverter = new MeshConverter(geoManager, accessorReader, diagnostics, manifest);

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

        // BuildScene populates the parsed-lights list (BuildNode alone does not), so reference
        // resolution/validation is exercised. The scene's single root-level node is the light node.
        var root = sceneBuilder.BuildScene(model, 0);
        var lightNode = root!.Children![0]!;

        return (lightNode, diagnostics);
    }

    /// <summary>
    /// Returns whether the node carries any punctual light component (directional, point, or spot).
    /// </summary>
    private static bool HasAnyLightComponent(Node node) =>
        node.Entity.TryGet<DirectionalLightComponent>(out _)
        || node.Entity.TryGet<RangeLightComponent>(out _);

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
                    var (node, diagnostics) = BuildAndImport(input.slots, input.index);

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
