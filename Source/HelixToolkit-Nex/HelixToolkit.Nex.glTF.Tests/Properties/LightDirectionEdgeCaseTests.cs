using System.Numerics;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
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

// Feature: gltf-directionallight-render-fix — unit/example tests for light direction edge cases.

/// <summary>
/// Unit/example tests for the light-direction derivation edge cases in
/// <see cref="SceneBuilder"/> (task 1.3). Mirrors the <c>TransformPropertyTests</c> build
/// pattern: an in-memory <see cref="Gltf"/> model carrying a <c>KHR_lights_punctual</c>
/// directional light is built via <see cref="SceneBuilder.BuildNode"/> against a
/// <see cref="World"/> with mock managers, then the attached
/// <see cref="DirectionalLightComponent"/> and emitted diagnostics are asserted.
/// <para>
/// Covered cases:
/// <list type="bullet">
/// <item>Identity rotation yields direction ≈ <c>(0,0,-1)</c> (Requirement 2.2 anchor).</item>
/// <item>Degenerate (zero-scale) transform leaves direction at the component default with no
/// <c>NaN</c> and emits an error diagnostic naming the node and light index (Requirement 2.5).</item>
/// </list>
/// </para>
/// **Validates: Requirements 2.2, 2.5**
/// </summary>
[TestClass]
public class LightDirectionEdgeCaseTests
{
    private const float Tolerance = 1e-5f;

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

        // BuildScene (not BuildNode) is used so the document-level KHR_lights_punctual lights are
        // parsed into the builder before per-node attachment runs. The light node (glTF index 0)
        // carries no mesh, so it is attached directly as the first child of the scene root.
        var root = sceneBuilder.BuildScene(model, -1);
        var node = root.Children![0];
        return (node, diagnostics);
    }

    #endregion

    /// <summary>
    /// Requirement 2.2 anchor: a directional light on a node with identity rotation produces a
    /// Light_Direction equal to the world <c>-Z</c> axis <c>(0, 0, -1)</c> within <c>1e-5</c> per
    /// component, and no error diagnostic is emitted.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [TestMethod]
    public void IdentityRotation_ProducesNegativeZDirection()
    {
        using var world = World.CreateWorld();

        // A default glTF node has identity TRS (Translation 0, Rotation identity, Scale 1).
        var gltfNode = new GltfNode { Name = "Sun" };

        var (node, diagnostics) = BuildSingleLightNode(gltfNode, world);

        Assert.IsTrue(
            node.Entity.TryGet<DirectionalLightComponent>(out var light),
            "Expected a DirectionalLightComponent to be attached to the node."
        );

        var direction = light.Direction;

        Assert.IsFalse(
            float.IsNaN(direction.X) || float.IsNaN(direction.Y) || float.IsNaN(direction.Z),
            "Direction must not contain NaN."
        );
        Assert.AreEqual(0.0f, direction.X, Tolerance, "Direction.X should be 0.");
        Assert.AreEqual(0.0f, direction.Y, Tolerance, "Direction.Y should be 0.");
        Assert.AreEqual(-1.0f, direction.Z, Tolerance, "Direction.Z should be -1 (world -Z axis).");

        // The light is well-formed; no degenerate-direction error should be emitted.
        Assert.IsFalse(
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "No error diagnostic should be emitted for a valid identity-rotation light."
        );
    }

    /// <summary>
    /// Requirement 2.5: when the node world transform is degenerate (zero/near-zero scale) so the
    /// transformed local <c>-Z</c> axis has length below the normalize epsilon, the attached
    /// directional light's Direction is left unchanged at the component default (no <c>NaN</c>
    /// written), the component is still attached, and a single error diagnostic naming the node
    /// index and the offending light index is emitted.
    /// **Validates: Requirements 2.5**
    /// </summary>
    [TestMethod]
    public void DegenerateTransform_LeavesDirectionAtDefault_NoNaN_EmitsErrorDiagnostic()
    {
        using var world = World.CreateWorld();

        // Zero scale makes the upper-left 3x3 of the world matrix zero, so TransformNormal(-Z)
        // yields a zero-length vector that cannot be normalized (the degenerate path).
        var gltfNode = new GltfNode
        {
            Name = "DegenerateSun",
            Translation = [0f, 0f, 0f],
            Rotation = [0f, 0f, 0f, 1f],
            Scale = [0f, 0f, 0f],
        };

        var (node, diagnostics) = BuildSingleLightNode(gltfNode, world);

        // The component is still attached despite the degenerate transform.
        Assert.IsTrue(
            node.Entity.TryGet<DirectionalLightComponent>(out var light),
            "The directional light component should still be attached for a degenerate transform."
        );

        var direction = light.Direction;

        // Direction must be left at the component default (Vector3.Zero), never NaN.
        Assert.IsFalse(
            float.IsNaN(direction.X) || float.IsNaN(direction.Y) || float.IsNaN(direction.Z),
            "Direction must not contain NaN for a degenerate transform."
        );
        Assert.AreEqual(
            Vector3.Zero,
            direction,
            "Direction should be left unchanged at the component default (0,0,0)."
        );

        // Exactly one error diagnostic, naming the node index (0) and the offending light index (0).
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.AreEqual(
            1,
            errors.Count,
            "Exactly one error diagnostic should be emitted for the degenerate transform."
        );

        var error = errors[0];
        Assert.AreEqual("Node", error.ElementType, "Diagnostic should reference the node element.");
        Assert.AreEqual(0, error.ElementIndex, "Diagnostic should name node index 0.");
        StringAssert.Contains(
            error.Message,
            "light index 0",
            "Diagnostic message should name the offending light index (0)."
        );
    }
}
