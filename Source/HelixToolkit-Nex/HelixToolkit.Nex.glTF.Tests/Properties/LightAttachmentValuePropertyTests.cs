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

// Feature: gltf-directionallight-render-fix, Property 1: Light attachment preserves parsed values for the matching kind

/// <summary>
/// Property-based test for Property 1 of the gltf-directionallight-render-fix feature.
/// For any resolvable, convertible <see cref="ParsedLight"/> and any node transform, attaching it
/// produces exactly one engine light component of the matching kind (directional →
/// <see cref="DirectionalLightComponent"/>; point/spot → <see cref="RangeLightComponent"/>) whose
/// color and intensity equal the parsed values within <c>1e-5</c> per component, whose range
/// (point/spot) equals the parsed range, whose position (point/spot, when finite) equals
/// <c>world.Translation</c>, and whose cone angles (spot) equal the parsed inner/outer angles in
/// radians with inner ≤ outer.
/// Mirrors the existing <see cref="LightDirectionPropertyTests"/> pattern: build an in-memory
/// <see cref="Gltf"/> model carrying a <c>KHR_lights_punctual</c> light, run the import against a
/// <see cref="World"/> with mock managers, and read back the attached component from the node entity.
/// </summary>
[TestClass]
public class LightAttachmentValuePropertyTests
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

    #region Light parameter model + generators

    private enum Kind
    {
        Directional,
        Point,
        Spot,
    }

    /// <summary>
    /// A fully-specified, converter-valid light definition plus the node transform it is attached
    /// to. All values are chosen inside the converter's "valid and preserved" ranges (color channels
    /// in [0,1], intensity &gt;= 0, range &gt; 0 for point/spot, inner &lt; outer &lt;= PI/2 for
    /// spot) so the parsed <see cref="ParsedLight"/> equals the authored values, letting the test
    /// assert the attached component preserves them.
    /// </summary>
    private readonly record struct LightCase(
        Kind Kind,
        Vector3 Color,
        float Intensity,
        float Range,
        float InnerConeAngle,
        float OuterConeAngle,
        Vector3 Translation,
        Quaternion Rotation,
        Vector3 Scale
    );

    private static Gen<Vector3> ColorGen() =>
        from r in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from g in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        from b in Gen.Choose(0, 1000).Select(i => i / 1000.0f)
        select new Vector3(r, g, b);

    // Intensity: numeric and >= 0 (preserved verbatim by the converter).
    private static Gen<float> IntensityGen() => Gen.Choose(0, 100000).Select(i => i / 100.0f);

    // Range for point/spot: strictly > 0 (preserved verbatim by the converter).
    private static Gen<float> RangeGen() => Gen.Choose(1, 100000).Select(i => i / 100.0f);

    // Spot cone angles: 0 <= inner < outer <= PI/2 (radians), preserved verbatim by the converter.
    private static Gen<(float Inner, float Outer)> ConeAnglesGen() =>
        // outer in (0.1, PI/2], inner in [0, outer) with a strict margin.
        from outerMilli in Gen.Choose(200, 1570) // 0.200 .. 1.570 rad (< PI/2 ≈ 1.5708)
        let outer = outerMilli / 1000.0f
        from innerFrac in Gen.Choose(0, 900).Select(i => i / 1000.0f) // 0 .. 0.9 of outer
        let inner = outer * innerFrac
        select (inner, outer);

    private static Gen<Quaternion> RotationGen() =>
        from x in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from y in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from z in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        from w in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
        let raw = new Quaternion(x, y, z, w)
        where raw.Length() > 0.001f
        select Quaternion.Normalize(raw);

    private static Gen<Vector3> TranslationGen() =>
        from x in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        from y in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        from z in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
        select new Vector3(x, y, z);

    // Positive, non-degenerate scale so the transform is well-formed.
    private static Gen<Vector3> ScaleGen() =>
        from x in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from y in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        from z in Gen.Choose(10, 1000).Select(i => i / 100.0f)
        select new Vector3(x, y, z);

    private static Gen<LightCase> LightCaseGen() =>
        from kind in Gen.Elements(Kind.Directional, Kind.Point, Kind.Spot)
        from color in ColorGen()
        from intensity in IntensityGen()
        from range in RangeGen()
        from cone in ConeAnglesGen()
        from translation in TranslationGen()
        from rotation in RotationGen()
        from scale in ScaleGen()
        select new LightCase(
            kind,
            color,
            intensity,
            range,
            cone.Inner,
            cone.Outer,
            translation,
            rotation,
            scale
        );

    #endregion

    #region Helpers

    /// <summary>
    /// Builds an in-memory glTF model with a single document-level <c>KHR_lights_punctual</c> light
    /// (index 0) of the given kind and authored values, and a single node (index 0) referencing it
    /// via the node-level extension carrying the given TRS, then imports it and returns the engine
    /// node created for that glTF node.
    /// </summary>
    private static Node BuildAndReadLightNode(LightCase c)
    {
        var lightDefinition = new JObject
        {
            ["type"] = c.Kind switch
            {
                Kind.Directional => "directional",
                Kind.Point => "point",
                Kind.Spot => "spot",
                _ => "directional",
            },
            ["color"] = new JArray { c.Color.X, c.Color.Y, c.Color.Z },
            ["intensity"] = c.Intensity,
        };

        // Range applies only to point/spot lights.
        if (c.Kind is Kind.Point or Kind.Spot)
        {
            lightDefinition["range"] = c.Range;
        }

        // Cone angles apply only to spot lights.
        if (c.Kind == Kind.Spot)
        {
            lightDefinition["spot"] = new JObject
            {
                ["innerConeAngle"] = c.InnerConeAngle,
                ["outerConeAngle"] = c.OuterConeAngle,
            };
        }

        var documentExtension = new JObject { ["lights"] = new JArray { lightDefinition } };
        var nodeExtension = new JObject { ["light"] = 0 };

        var gltfNode = new GltfNode
        {
            Name = "LightNode",
            Translation = [c.Translation.X, c.Translation.Y, c.Translation.Z],
            Rotation = [c.Rotation.X, c.Rotation.Y, c.Rotation.Z, c.Rotation.W],
            Scale = [c.Scale.X, c.Scale.Y, c.Scale.Z],
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
        return root!.Children![0]!;
    }

    private static bool ApproxEqual(Vector3 a, Vector3 b, float tolerance) =>
        MathF.Abs(a.X - b.X) <= tolerance
        && MathF.Abs(a.Y - b.Y) <= tolerance
        && MathF.Abs(a.Z - b.Z) <= tolerance;

    private static bool ApproxEqual(float a, float b, float tolerance) =>
        MathF.Abs(a - b) <= tolerance;

    /// <summary>
    /// Recomputes the world matrix the importer derives for a root-level node using the same
    /// row-vector TRS composition (Scale * Rotation * Translation) with parentWorld = Identity.
    /// </summary>
    private static Matrix4x4 ComposeWorld(LightCase c) =>
        Matrix4x4.CreateScale(c.Scale)
        * Matrix4x4.CreateFromQuaternion(c.Rotation)
        * Matrix4x4.CreateTranslation(c.Translation);

    #endregion

    /// <summary>
    /// Property 1: Light attachment preserves parsed values for the matching kind.
    /// **Validates: Requirements 1.1, 1.3, 1.4, 1.5**
    /// </summary>
    [TestMethod]
    public void LightAttachment_PreservesParsedValues_ForMatchingKind()
    {
        Prop.ForAll(
                Arb.From(LightCaseGen()),
                (LightCase c) =>
                {
                    var node = BuildAndReadLightNode(c);
                    var world = ComposeWorld(c);
                    var expectedPosition = world.Translation;

                    bool hasDirectional = node.Entity.TryGet<DirectionalLightComponent>(
                        out var dirLight
                    );
                    bool hasRange = node.Entity.TryGet<RangeLightComponent>(out var rangeLight);

                    switch (c.Kind)
                    {
                        case Kind.Directional:
                            // Exactly one component of the matching kind: directional present,
                            // no range component (Requirement 1.1, 1.3).
                            if (!hasDirectional || hasRange)
                            {
                                return false;
                            }

                            return ApproxEqual(dirLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(dirLight.Intensity, c.Intensity, Tolerance);

                        case Kind.Point:
                            // Exactly one component of the matching kind: a Point range light,
                            // no directional component (Requirement 1.1, 1.4).
                            if (!hasRange || hasDirectional || rangeLight is null)
                            {
                                return false;
                            }
                            if (rangeLight.Type != RangeLightType.Point)
                            {
                                return false;
                            }

                            return ApproxEqual(rangeLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(rangeLight.Intensity, c.Intensity, Tolerance)
                                && ApproxEqual(rangeLight.Range, c.Range, Tolerance)
                                && ApproxEqual(rangeLight.Position, expectedPosition, Tolerance);

                        case Kind.Spot:
                            // Exactly one component of the matching kind: a Spot range light,
                            // no directional component (Requirement 1.1, 1.5).
                            if (!hasRange || hasDirectional || rangeLight is null)
                            {
                                return false;
                            }
                            if (rangeLight.Type != RangeLightType.Spot)
                            {
                                return false;
                            }

                            var spotAngles = rangeLight.SpotAngles;

                            return ApproxEqual(rangeLight.Color.ToVector3(), c.Color, Tolerance)
                                && ApproxEqual(rangeLight.Intensity, c.Intensity, Tolerance)
                                && ApproxEqual(rangeLight.Range, c.Range, Tolerance)
                                && ApproxEqual(rangeLight.Position, expectedPosition, Tolerance)
                                // Cone angles in radians, inner ≤ outer, equal to parsed values.
                                && ApproxEqual(spotAngles.X, c.InnerConeAngle, Tolerance)
                                && ApproxEqual(spotAngles.Y, c.OuterConeAngle, Tolerance)
                                && spotAngles.X <= spotAngles.Y;

                        default:
                            return false;
                    }
                }
            )
            .Check(FsCheckConfig);
    }
}
