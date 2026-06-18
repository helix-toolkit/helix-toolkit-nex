using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using Accessor = glTFLoader.Schema.Accessor;
using BufferView = glTFLoader.Schema.BufferView;
using Gltf = glTFLoader.Schema.Gltf;
using GltfBuffer = glTFLoader.Schema.Buffer;
using GltfMaterial = glTFLoader.Schema.Material;
using GltfNode = glTFLoader.Schema.Node;
using GltfScene = glTFLoader.Schema.Scene;
using MaterialPbrMetallicRoughness = glTFLoader.Schema.MaterialPbrMetallicRoughness;
using Mesh = glTFLoader.Schema.Mesh;
using MeshPrimitive = glTFLoader.Schema.MeshPrimitive;
using NexImage = HelixToolkit.Nex.Textures.Image;
using TextureInfo = glTFLoader.Schema.TextureInfo;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 14: Diagnostic completeness

/// <summary>
/// Property-based tests for diagnostic completeness (Property 14).
/// Verifies that for any import encountering an error condition (unsupported topology,
/// missing texture, invalid transform, missing POSITION attribute), the ImportResult.Diagnostics
/// list contains at least one entry with a non-empty Message, a valid ElementType string,
/// and a non-negative ElementIndex.
/// </summary>
[TestClass]
public class DiagnosticPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock IGeometryManager that returns valid handles.
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
    /// A mock IPBRMaterialPropertyManager that creates valid materials.
    /// </summary>
    private sealed class MockMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public IReadOnlyList<Pool<
            MaterialPropertyResource,
            HelixToolkit.Nex.Shaders.PBRProperties
        >.PoolEntry> Objects => _inner.Objects;

        public PBRMaterialProperties Create(string materialName) =>
            _inner.Create(HelixToolkit.Nex.Shaders.Frag.PBRShadingMode.PBR);

        public PBRMaterialProperties Create(
            string materialName,
            ref HelixToolkit.Nex.Shaders.PBRProperties properties
        ) => _inner.Create(HelixToolkit.Nex.Shaders.Frag.PBRShadingMode.PBR, ref properties);

        public PBRMaterialProperties Create(MaterialTypeId materialTypeId) =>
            _inner.Create(materialTypeId);

        public PBRMaterialProperties Create(
            MaterialTypeId materialTypeId,
            ref HelixToolkit.Nex.Shaders.PBRProperties properties
        ) => _inner.Create(materialTypeId, ref properties);

        public ref HelixToolkit.Nex.Shaders.PBRProperties At(int index) => ref _inner.At(index);

        public void Clear() => _inner.Clear();

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
    /// A stub ITextureRepository that always returns TextureRef.Null (simulates texture loading failure).
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
    /// A stub ISamplerRepository.
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

    #region Error Condition Enum

    /// <summary>
    /// Represents the different error conditions that should produce diagnostics.
    /// </summary>
    private enum ErrorCondition
    {
        /// <summary>Mesh primitive uses an unsupported topology mode (e.g., LINE_LOOP = 2).</summary>
        UnsupportedTopology,

        /// <summary>Mesh primitive is missing the required POSITION attribute.</summary>
        MissingPositionAttribute,

        /// <summary>Material references a texture index that does not exist.</summary>
        MissingTextureReference,

        /// <summary>Node has a matrix that cannot be decomposed into valid TRS.</summary>
        InvalidTransformMatrix,
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates valid position buffer data for a triangle (3 vertices).
    /// </summary>
    private static byte[] CreateTrianglePositionBuffer()
    {
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        var buffer = new byte[positions.Length * sizeof(float)];
        System.Buffer.BlockCopy(positions, 0, buffer, 0, buffer.Length);
        return buffer;
    }

    /// <summary>
    /// Creates a glTF model with an unsupported topology mode on a mesh primitive.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithUnsupportedTopology(int meshIndex)
    {
        var buffer = CreateTrianglePositionBuffer();

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
                    ByteLength = buffer.Length,
                },
            ],
            Buffers = [new GltfBuffer { ByteLength = buffer.Length }],
            Meshes =
            [
                new Mesh
                {
                    Name = "TestMesh",
                    Primitives =
                    [
                        new MeshPrimitive
                        {
                            Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
                            // LINE_LOOP (2) is unsupported
                            Mode = (MeshPrimitive.ModeEnum)2,
                        },
                    ],
                },
            ],
            Nodes = [new GltfNode { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
        };

        return (model, buffer);
    }

    /// <summary>
    /// Creates a glTF model with a mesh primitive missing the POSITION attribute.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithMissingPosition()
    {
        var buffer = CreateTrianglePositionBuffer();

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
                    ByteLength = buffer.Length,
                },
            ],
            Buffers = [new GltfBuffer { ByteLength = buffer.Length }],
            Meshes =
            [
                new Mesh
                {
                    Name = "TestMesh",
                    Primitives =
                    [
                        new MeshPrimitive
                        {
                            // Only NORMAL, no POSITION
                            Attributes = new Dictionary<string, int> { ["NORMAL"] = 0 },
                            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                        },
                    ],
                },
            ],
            Nodes = [new GltfNode { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
        };

        return (model, buffer);
    }

    /// <summary>
    /// Creates a glTF model with a material referencing a non-existent texture index.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithMissingTexture(int textureIndex)
    {
        var buffer = CreateTrianglePositionBuffer();

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
                    ByteLength = buffer.Length,
                },
            ],
            Buffers = [new GltfBuffer { ByteLength = buffer.Length }],
            Materials =
            [
                new GltfMaterial
                {
                    Name = "TestMaterial",
                    PbrMetallicRoughness = new MaterialPbrMetallicRoughness
                    {
                        BaseColorFactor = [1.0f, 1.0f, 1.0f, 1.0f],
                        MetallicFactor = 1.0f,
                        RoughnessFactor = 1.0f,
                        // Reference a texture index that doesn't exist
                        BaseColorTexture = new TextureInfo { Index = textureIndex },
                    },
                },
            ],
            // No Textures array defined, so any texture index is out of range
            Textures = [],
            Meshes =
            [
                new Mesh
                {
                    Name = "TestMesh",
                    Primitives =
                    [
                        new MeshPrimitive
                        {
                            Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
                            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                            Material = 0,
                        },
                    ],
                },
            ],
            Nodes = [new GltfNode { Name = "TestNode", Mesh = 0 }],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
        };

        return (model, buffer);
    }

    /// <summary>
    /// Creates a glTF model with a node that has a non-decomposable matrix (contains skew).
    /// A matrix with skew cannot be cleanly decomposed into TRS.
    /// </summary>
    private static (Gltf model, byte[] buffer) CreateModelWithInvalidTransform()
    {
        var buffer = CreateTrianglePositionBuffer();

        // Create a matrix with skew that cannot be decomposed into valid TRS
        // A singular or near-singular matrix will fail Matrix4x4.Decompose
        // Using a matrix with zero scale on one axis makes it non-decomposable
        float[] skewMatrix =
        [
            1f,
            0f,
            0f,
            0f, // column 0
            0f,
            0f,
            0f,
            0f, // column 1 (zero scale on Y makes it non-decomposable)
            0f,
            0f,
            1f,
            0f, // column 2
            0f,
            0f,
            0f,
            1f, // column 3
        ];

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
                    ByteLength = buffer.Length,
                },
            ],
            Buffers = [new GltfBuffer { ByteLength = buffer.Length }],
            Meshes =
            [
                new Mesh
                {
                    Name = "TestMesh",
                    Primitives =
                    [
                        new MeshPrimitive
                        {
                            Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
                            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                        },
                    ],
                },
            ],
            Nodes =
            [
                new GltfNode
                {
                    Name = "SkewedNode",
                    Matrix = skewMatrix,
                    Mesh = 0,
                },
            ],
            Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
            Scene = 0,
        };

        return (model, buffer);
    }

    /// <summary>
    /// Validates that a diagnostics list contains at least one entry with the required properties:
    /// non-empty Message, valid ElementType string, and non-negative ElementIndex.
    /// </summary>
    private static bool HasValidDiagnostic(List<ImportDiagnostic> diagnostics)
    {
        return diagnostics.Any(d =>
            !string.IsNullOrEmpty(d.Message)
            && !string.IsNullOrEmpty(d.ElementType)
            && d.ElementIndex >= 0
        );
    }

    /// <summary>
    /// Builds a scene using the SceneBuilder with the given model and buffer, returning the diagnostics.
    /// </summary>
    private static List<ImportDiagnostic> BuildSceneAndGetDiagnostics(Gltf model, byte[] buffer)
    {
        using var world = World.CreateWorld();
        var diagnostics = new List<ImportDiagnostic>();

        var geoManager = new MockGeometryManager();
        var accessorReader = new AccessorReader(model, [buffer]);
        var manifest = new ResourceManifest();
        var meshConverter = new MeshConverter(geoManager, accessorReader, diagnostics, manifest);

        using var materialManager = new MockMaterialPropertyManager();
        using var textureRepo = new StubTextureRepository();
        using var samplerRepo = new StubSamplerRepository();
        var textureLoader = new TextureLoader(
            textureRepo,
            samplerRepo,
            "C:\\test",
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

        // Build the scene — this triggers all the error conditions
        var rootNode = sceneBuilder.BuildScene(model, 0);

        return diagnostics;
    }

    #endregion

    /// <summary>
    /// Property 14: For any import that encounters an unsupported topology mode,
    /// the ImportResult.Diagnostics list SHALL contain at least one entry with a non-empty Message,
    /// a valid ElementType string, and a non-negative ElementIndex.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [TestMethod]
    public void Import_WithUnsupportedTopology_ProducesDiagnostic()
    {
        // Generator: mesh index (0..4) — the mesh index doesn't change the error condition
        // but varies the diagnostic content
        var meshIndexGen = Gen.Choose(0, 4);

        Prop.ForAll(
                Arb.From(meshIndexGen),
                (int meshIndex) =>
                {
                    var (model, buffer) = CreateModelWithUnsupportedTopology(meshIndex);
                    var diagnostics = BuildSceneAndGetDiagnostics(model, buffer);

                    // Must have at least one diagnostic
                    if (diagnostics.Count == 0)
                        return false;

                    // Must have a valid diagnostic entry
                    return HasValidDiagnostic(diagnostics);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 14: For any import that encounters a missing POSITION attribute,
    /// the ImportResult.Diagnostics list SHALL contain at least one entry with a non-empty Message,
    /// a valid ElementType string, and a non-negative ElementIndex.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [TestMethod]
    public void Import_WithMissingPositionAttribute_ProducesDiagnostic()
    {
        // This test uses a fixed model structure but we run it through the property framework
        // to verify the invariant holds consistently
        var gen = Gen.Constant(0);

        Prop.ForAll(
                Arb.From(gen),
                (_) =>
                {
                    var (model, buffer) = CreateModelWithMissingPosition();
                    var diagnostics = BuildSceneAndGetDiagnostics(model, buffer);

                    // Must have at least one diagnostic
                    if (diagnostics.Count == 0)
                        return false;

                    // Must have a valid diagnostic entry
                    return HasValidDiagnostic(diagnostics);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 14: For any import that encounters a missing texture reference (texture index
    /// out of range), the ImportResult.Diagnostics list SHALL contain at least one entry with
    /// a non-empty Message, a valid ElementType string, and a non-negative ElementIndex.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [TestMethod]
    public void Import_WithMissingTextureReference_ProducesDiagnostic()
    {
        // Generator: texture index (0..10) — all are out of range since Textures array is null
        var textureIndexGen = Gen.Choose(0, 10);

        Prop.ForAll(
                Arb.From(textureIndexGen),
                (int textureIndex) =>
                {
                    var (model, buffer) = CreateModelWithMissingTexture(textureIndex);
                    var diagnostics = BuildSceneAndGetDiagnostics(model, buffer);

                    // Must have at least one diagnostic
                    if (diagnostics.Count == 0)
                        return false;

                    // Must have a valid diagnostic entry
                    return HasValidDiagnostic(diagnostics);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 14: For any import that encounters an invalid transform matrix (non-decomposable),
    /// the ImportResult.Diagnostics list SHALL contain at least one entry with a non-empty Message,
    /// a valid ElementType string, and a non-negative ElementIndex.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [TestMethod]
    public void Import_WithInvalidTransformMatrix_ProducesDiagnostic()
    {
        // Generator: produce non-decomposable matrices by zeroing out different columns
        // This creates matrices that Matrix4x4.Decompose will fail on
        var columnToZeroGen = Gen.Choose(0, 2); // Zero column 0, 1, or 2

        Prop.ForAll(
                Arb.From(columnToZeroGen),
                (int columnToZero) =>
                {
                    // Start with identity in column-major order
                    float[] matrix =
                    [
                        1f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                        0f,
                        0f,
                        0f,
                        0f,
                        1f,
                    ];

                    // Zero out the specified column to make it non-decomposable
                    int colStart = columnToZero * 4;
                    matrix[colStart] = 0f;
                    matrix[colStart + 1] = 0f;
                    matrix[colStart + 2] = 0f;
                    matrix[colStart + 3] = 0f;

                    var buffer = CreateTrianglePositionBuffer();

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
                                ByteLength = buffer.Length,
                            },
                        ],
                        Buffers = [new GltfBuffer { ByteLength = buffer.Length }],
                        Meshes =
                        [
                            new Mesh
                            {
                                Name = "TestMesh",
                                Primitives =
                                [
                                    new MeshPrimitive
                                    {
                                        Attributes = new Dictionary<string, int>
                                        {
                                            ["POSITION"] = 0,
                                        },
                                        Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                                    },
                                ],
                            },
                        ],
                        Nodes =
                        [
                            new GltfNode
                            {
                                Name = "BadTransformNode",
                                Matrix = matrix,
                                Mesh = 0,
                            },
                        ],
                        Scenes = [new GltfScene { Name = "TestScene", Nodes = [0] }],
                        Scene = 0,
                    };

                    var diagnostics = BuildSceneAndGetDiagnostics(model, buffer);

                    // Must have at least one diagnostic about the transform
                    if (diagnostics.Count == 0)
                        return false;

                    // Must have a valid diagnostic entry
                    return HasValidDiagnostic(diagnostics);
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 14 (combined): For any randomly selected error condition from the set
    /// {unsupported topology, missing POSITION, missing texture, invalid transform},
    /// the diagnostics SHALL contain at least one valid entry.
    /// This tests the property across all error types with random selection.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [TestMethod]
    public void Import_WithAnyErrorCondition_ProducesDiagnosticWithValidFields()
    {
        // Generator: randomly select an error condition
        var errorConditionGen = Gen.Elements(
            ErrorCondition.UnsupportedTopology,
            ErrorCondition.MissingPositionAttribute,
            ErrorCondition.MissingTextureReference,
            ErrorCondition.InvalidTransformMatrix
        );

        Prop.ForAll(
                Arb.From(errorConditionGen),
                (ErrorCondition errorCondition) =>
                {
                    Gltf model;
                    byte[] buffer;

                    switch (errorCondition)
                    {
                        case ErrorCondition.UnsupportedTopology:
                            (model, buffer) = CreateModelWithUnsupportedTopology(0);
                            break;
                        case ErrorCondition.MissingPositionAttribute:
                            (model, buffer) = CreateModelWithMissingPosition();
                            break;
                        case ErrorCondition.MissingTextureReference:
                            (model, buffer) = CreateModelWithMissingTexture(5);
                            break;
                        case ErrorCondition.InvalidTransformMatrix:
                            (model, buffer) = CreateModelWithInvalidTransform();
                            break;
                        default:
                            return false;
                    }

                    var diagnostics = BuildSceneAndGetDiagnostics(model, buffer);

                    // Property: diagnostics must be non-empty
                    if (diagnostics.Count == 0)
                        return false;

                    // Property: at least one diagnostic must have:
                    // - non-empty Message
                    // - valid (non-empty) ElementType string
                    // - non-negative ElementIndex
                    return diagnostics.Any(d =>
                        !string.IsNullOrEmpty(d.Message)
                        && !string.IsNullOrEmpty(d.ElementType)
                        && d.ElementIndex >= 0
                    );
                }
            )
            .Check(FsCheckConfig);
    }
}
