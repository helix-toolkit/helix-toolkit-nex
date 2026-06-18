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

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-directionallight-render-fix, Property 2: Light direction is the normalized local −Z axis under rotation and scale

/// <summary>
/// Property-based test for Property 2 of the gltf-directionallight-render-fix feature.
/// For any node world transform whose local −Z image is normalizable, the attached directional
/// light's <c>Direction</c> equals <c>normalize(TransformNormal((0,0,-1), world))</c>, is a unit
/// vector within <c>1e-5</c>, equals the node rotation applied to <c>(0,0,-1)</c> (for identity
/// rotation, <c>(0,0,-1)</c>), and is invariant to the translation component of the transform.
/// Mirrors the existing <see cref="TransformPropertyTests"/> /
/// <see cref="WorldTransformHierarchyPropertyTests"/> pattern: build an in-memory <see cref="Gltf"/>
/// model carrying a <c>KHR_lights_punctual</c> directional light, run the import against a
/// <see cref="World"/> with mock managers, and read back the <see cref="DirectionalLightComponent"/>.
/// </summary>
[TestClass]
public class LightDirectionPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);
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
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c>
    /// directional light (index 0) and a single node (index 0) referencing it via the node-level
    /// extension, carrying the given TRS, then imports it and returns the attached directional
    /// light's world-space <c>Direction</c>.
    /// </summary>
    private static Vector3 BuildAndReadDirection(
        Vector3 translation,
        Quaternion rotation,
        Vector3 scale
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

        // BuildScene populates the parsed-lights list (BuildNode alone does not), so light
        // attachment is exercised. The scene's single root-level node is the light node.
        var root = sceneBuilder.BuildScene(model, 0);
        var lightNode = root!.Children![0]!;

        Assert.IsTrue(
            lightNode.Entity.TryGet<DirectionalLightComponent>(out var dirLight),
            "Expected a DirectionalLightComponent to be attached to the light node."
        );

        return dirLight.Direction;
    }

    private static bool ApproxEqual(Vector3 a, Vector3 b, float tolerance) =>
        MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

    #endregion

    /// <summary>
    /// Property 2: Light direction is the normalized local −Z axis under rotation and scale.
    /// For any node world transform whose local −Z image is normalizable, the computed
    /// <c>Light_Direction</c> equals <c>normalize(TransformNormal((0,0,-1), world))</c>, is a unit
    /// vector within <c>1e-5</c>, equals the node rotation applied to <c>(0,0,-1)</c>, and is
    /// invariant to the translation component of the transform.
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    /// </summary>
    [TestMethod]
    public void LightDirection_IsNormalizedLocalNegativeZ_UnderRotationAndScale()
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

        // Scale: positive components (0.1 .. 10) so the local −Z image is normalizable and the
        // transform is non-degenerate.
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
                    var direction = BuildAndReadDirection(
                        input.translation,
                        input.rotation,
                        input.scale
                    );

                    // The world matrix the importer derives uses the same row-vector TRS
                    // composition (Scale * Rotation * Translation) with parentWorld = Identity.
                    var world =
                        Matrix4x4.CreateScale(input.scale)
                        * Matrix4x4.CreateFromQuaternion(input.rotation)
                        * Matrix4x4.CreateTranslation(input.translation);

                    // (a) Direction equals normalize(TransformNormal((0,0,-1), world)).
                    var expectedFromTransform = Vector3.Normalize(
                        Vector3.TransformNormal(-Vector3.UnitZ, world)
                    );
                    bool matchesTransformNormal = ApproxEqual(
                        direction,
                        expectedFromTransform,
                        Tolerance
                    );

                    // (b) Direction is a unit vector within 1e-5.
                    bool isUnitLength = MathF.Abs(direction.Length() - 1.0f) <= Tolerance;

                    // (c) Direction equals the node rotation applied to (0,0,-1). Because a positive,
                    // possibly non-uniform scale applied to the pure −Z axis only scales the Z
                    // component (which normalization removes), the normalized direction equals the
                    // rotation applied to (0,0,-1).
                    var expectedFromRotation = Vector3.Transform(-Vector3.UnitZ, input.rotation);
                    bool matchesRotation = ApproxEqual(direction, expectedFromRotation, Tolerance);

                    // (d) Direction is invariant to the translation component of the transform:
                    // rebuilding with zero translation (same rotation/scale) yields the same
                    // direction.
                    var directionNoTranslation = BuildAndReadDirection(
                        Vector3.Zero,
                        input.rotation,
                        input.scale
                    );
                    bool translationInvariant = ApproxEqual(
                        direction,
                        directionNoTranslation,
                        Tolerance
                    );

                    return matchesTransformNormal
                        && isUnitLength
                        && matchesRotation
                        && translationInvariant;
                }
            )
            .Check(FsCheckConfig);
    }
}
