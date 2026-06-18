using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using GltfNode = glTFLoader.Schema.Node;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 2: Transform round-trip (TRS → Matrix → TRS)

/// <summary>
/// Property-based tests for transform round-trip (Property 2).
/// Validates that composing TRS into a matrix and decomposing back via SceneBuilder
/// produces equivalent values within floating-point tolerance.
/// </summary>
[TestClass]
public class TransformPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);
    private const float Tolerance = 1e-5f;

    #region Mock Infrastructure

    /// <summary>
    /// Minimal IGeometryManager stub (not exercised since test nodes have no mesh).
    /// </summary>
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

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer)
        {
            return ResultCode.Ok;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Minimal IPBRMaterialPropertyManager stub that delegates to the real implementation.
    /// Not exercised since test nodes have no mesh.
    /// </summary>
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

    /// <summary>
    /// Minimal ITextureRepository stub (not exercised since test nodes have no mesh).
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

    /// <summary>
    /// Minimal ISamplerRepository stub using MockContext.
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

    #region Helpers

    /// <summary>
    /// Converts a row-major Matrix4x4 to a column-major float[16] array (glTF format).
    /// This is the inverse of SceneBuilder.ConvertColumnMajorToMatrix4x4.
    /// </summary>
    private static float[] MatrixToColumnMajor(Matrix4x4 m)
    {
        return
        [
            m.M11,
            m.M21,
            m.M31,
            m.M41, // Column 0
            m.M12,
            m.M22,
            m.M32,
            m.M42, // Column 1
            m.M13,
            m.M23,
            m.M33,
            m.M43, // Column 2
            m.M14,
            m.M24,
            m.M34,
            m.M44, // Column 3
        ];
    }

    /// <summary>
    /// Checks if two quaternions represent the same rotation (q and -q are equivalent).
    /// </summary>
    private static bool QuaternionsEquivalent(Quaternion a, Quaternion b, float tolerance)
    {
        bool directMatch =
            MathF.Abs(a.X - b.X) <= tolerance
            && MathF.Abs(a.Y - b.Y) <= tolerance
            && MathF.Abs(a.Z - b.Z) <= tolerance
            && MathF.Abs(a.W - b.W) <= tolerance;

        bool negatedMatch =
            MathF.Abs(a.X + b.X) <= tolerance
            && MathF.Abs(a.Y + b.Y) <= tolerance
            && MathF.Abs(a.Z + b.Z) <= tolerance
            && MathF.Abs(a.W + b.W) <= tolerance;

        return directMatch || negatedMatch;
    }

    #endregion

    /// <summary>
    /// Property 2: For any valid translation (Vector3), rotation (normalized Quaternion),
    /// and scale (Vector3 with positive components), composing them into a 4×4 matrix and
    /// then decomposing back SHALL produce translation, rotation, and scale values equivalent
    /// to the originals within floating-point tolerance (±1e-5).
    /// **Validates: Requirements 2.2**
    /// </summary>
    [TestMethod]
    public void TransformRoundTrip_TRS_Matrix_TRS_PreservesValues()
    {
        // Generator for translation: any float values (reasonable range)
        var translationGen =
            from x in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            from y in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            from z in Gen.Choose(-10000, 10000).Select(i => i / 100.0f)
            select new Vector3(x, y, z);

        // Generator for rotation: random quaternion components, then normalize
        var rotationGen =
            from x in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from y in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from z in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            from w in Gen.Choose(-1000, 1000).Select(i => i / 1000.0f)
            let raw = new Quaternion(x, y, z, w)
            let length = raw.Length()
            where length > 0.001f // Avoid degenerate zero-length quaternions
            select Quaternion.Normalize(raw);

        // Generator for scale: positive components only (0.01 to 100)
        var scaleGen =
            from x in Gen.Choose(1, 10000).Select(i => i / 100.0f)
            from y in Gen.Choose(1, 10000).Select(i => i / 100.0f)
            from z in Gen.Choose(1, 10000).Select(i => i / 100.0f)
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
                    // Step 1: Compose TRS into a 4x4 matrix (Scale × Rotation × Translation)
                    var matrix =
                        Matrix4x4.CreateScale(input.scale)
                        * Matrix4x4.CreateFromQuaternion(input.rotation)
                        * Matrix4x4.CreateTranslation(input.translation);

                    // Step 2: Convert to column-major float[16] (glTF format)
                    var columnMajor = MatrixToColumnMajor(matrix);

                    // Step 3: Create a glTF node with that matrix
                    var gltfNode = new GltfNode { Matrix = columnMajor };

                    var model = new Gltf { Nodes = [gltfNode] };

                    // Step 4: Create SceneBuilder and call BuildNode
                    using var world = World.CreateWorld();
                    var diagnostics = new List<ImportDiagnostic>();

                    var accessorReader = new AccessorReader(model, []);
                    using var geoManager = new StubGeometryManager();
                    var meshConverter = new MeshConverter(
                        geoManager,
                        accessorReader,
                        diagnostics,
                        new ResourceManifest()
                    );

                    using var textureRepo = new StubTextureRepository();
                    using var samplerRepo = new StubSamplerRepository();
                    var manifest = new ResourceManifest();
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

                    var resultNode = sceneBuilder.BuildNode(model, 0, null, Matrix4x4.Identity);

                    // Step 5: Verify the resulting node's Transform
                    var resultTransform = resultNode.Transform;

                    // Translation within ±1e-5
                    bool translationMatch =
                        MathF.Abs(resultTransform.Translation.X - input.translation.X) <= Tolerance
                        && MathF.Abs(resultTransform.Translation.Y - input.translation.Y)
                            <= Tolerance
                        && MathF.Abs(resultTransform.Translation.Z - input.translation.Z)
                            <= Tolerance;

                    // Rotation within ±1e-5 (or negated quaternion, since -q == q for rotations)
                    bool rotationMatch = QuaternionsEquivalent(
                        resultTransform.Rotation,
                        input.rotation,
                        Tolerance
                    );

                    // Scale within ±1e-5
                    bool scaleMatch =
                        MathF.Abs(resultTransform.Scale.X - input.scale.X) <= Tolerance
                        && MathF.Abs(resultTransform.Scale.Y - input.scale.Y) <= Tolerance
                        && MathF.Abs(resultTransform.Scale.Z - input.scale.Z) <= Tolerance;

                    return translationMatch && rotationMatch && scaleMatch;
                }
            )
            .Check(FsCheckConfig);
    }
}
