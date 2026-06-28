using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Shaders;
namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for MeshConverter.ConvertPrimitive.
/// </summary>
[TestClass]
public class MeshConverterTests
{
    /// <summary>
    /// A simple mock IGeometryManager that returns a valid handle for any geometry added.
    /// </summary>
    private sealed class MockGeometryManager : IGeometryManager
    {
        private uint _nextIndex = 1;
        public List<Geometry> AddedGeometries { get; } = [];

        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => AddedGeometries.Count;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry)
        {
            AddedGeometries.Add(geometry);
            return new Handle<GeometryResourceType>(_nextIndex++, 1);
        }

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry)
        {
            var handle = Add(geometry);
            return Task.FromResult((true, handle));
        }

        public bool Remove(Geometry geometry) => true;

        public void RemoveDeferred(Geometry geometry) => Remove(geometry);

        public void ProcessPendingRemovals() { }

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
    /// A mock IGeometryManager that always returns an invalid handle.
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

        public void RemoveDeferred(Geometry geometry) => Remove(geometry);

        public void ProcessPendingRemovals() { }

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

    private static (Gltf model, byte[] buffer) CreateSimpleTrianglePrimitive()
    {
        // 3 vertices forming a triangle
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        var byteBuffer = new byte[positions.Length * sizeof(float)];
        System.Buffer.BlockCopy(positions, 0, byteBuffer, 0, byteBuffer.Length);

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
        };

        return (model, byteBuffer);
    }

    private static MeshPrimitive CreatePrimitiveWithPosition(
        int positionAccessorIndex = 0,
        MeshPrimitive.ModeEnum mode = MeshPrimitive.ModeEnum.TRIANGLES
    )
    {
        return new MeshPrimitive
        {
            Attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessorIndex },
            Mode = mode,
        };
    }

    [TestMethod]
    public void ConvertPrimitive_WithValidTriangle_ReturnsGeometryAndValidHandle()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition();
        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        Assert.IsTrue(handle.Valid);
        Assert.AreEqual(3, geometry.Vertices.Count);
        Assert.AreEqual(Topology.Triangle, geometry.Topology);
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void ConvertPrimitive_MissingPosition_ReturnsNullAndAddsError()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        // Primitive with no POSITION attribute
        var primitive = new MeshPrimitive
        {
            Attributes = new Dictionary<string, int> { ["NORMAL"] = 0 },
            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
        };

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("POSITION"));
    }

    [TestMethod]
    public void ConvertPrimitive_UnsupportedTopology_ReturnsNullAndAddsError()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        // Use LINE_LOOP (mode 2) which is unsupported
        var primitive = CreatePrimitiveWithPosition(mode: MeshPrimitive.ModeEnum.LINE_LOOP);

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("unsupported"));
    }

    [TestMethod]
    public void ConvertPrimitive_TriangleFan_ReturnsNullAndAddsError()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        // Use TRIANGLE_FAN (mode 6) which is unsupported
        var primitive = CreatePrimitiveWithPosition(mode: MeshPrimitive.ModeEnum.TRIANGLE_FAN);

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [TestMethod]
    [DataRow(MeshPrimitive.ModeEnum.POINTS, Topology.Point)]
    [DataRow(MeshPrimitive.ModeEnum.LINES, Topology.Line)]
    [DataRow(MeshPrimitive.ModeEnum.TRIANGLES, Topology.Triangle)]
    [DataRow(MeshPrimitive.ModeEnum.TRIANGLE_STRIP, Topology.TriangleStrip)]
    public void ConvertPrimitive_MapsTopologyCorrectly(
        MeshPrimitive.ModeEnum mode,
        Topology expectedTopology
    )
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition(mode: mode);
        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        Assert.AreEqual(expectedTopology, geometry.Topology);
    }

    [TestMethod]
    public void ConvertPrimitive_WithAllAttributes_PopulatesAllFields()
    {
        int vertexCount = 3;
        int posBytes = vertexCount * 3 * sizeof(float);
        int normalBytes = vertexCount * 3 * sizeof(float);
        int texCoordBytes = vertexCount * 2 * sizeof(float);
        int tangentBytes = vertexCount * 4 * sizeof(float);
        int colorBytes = vertexCount * 4 * sizeof(float);
        int indexBytes = 3 * sizeof(ushort);
        int totalBytes =
            posBytes + normalBytes + texCoordBytes + tangentBytes + colorBytes + indexBytes;

        var byteBuffer = new byte[totalBytes];
        int offset = 0;

        // Positions: (0,0,0), (1,0,0), (0,1,0)
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        System.Buffer.BlockCopy(positions, 0, byteBuffer, offset, posBytes);
        offset += posBytes;

        // Normals: (0,0,1) for all
        float[] normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];
        System.Buffer.BlockCopy(normals, 0, byteBuffer, offset, normalBytes);
        offset += normalBytes;

        // TexCoords: (0,0), (1,0), (0,1)
        float[] texCoords = [0f, 0f, 1f, 0f, 0f, 1f];
        System.Buffer.BlockCopy(texCoords, 0, byteBuffer, offset, texCoordBytes);
        offset += texCoordBytes;

        // Tangents: (1,0,0,1) for all
        float[] tangents = [1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f];
        System.Buffer.BlockCopy(tangents, 0, byteBuffer, offset, tangentBytes);
        offset += tangentBytes;

        // Colors: (1,0,0,1), (0,1,0,1), (0,0,1,1)
        float[] colors = [1f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f, 1f];
        System.Buffer.BlockCopy(colors, 0, byteBuffer, offset, colorBytes);
        offset += colorBytes;

        // Indices: 0, 1, 2
        ushort[] indices = [0, 1, 2];
        System.Buffer.BlockCopy(indices, 0, byteBuffer, offset, indexBytes);

        int bvOffset = 0;
        var model = new Gltf
        {
            Accessors =
            [
                // 0: POSITION
                new Accessor
                {
                    BufferView = 0,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC3,
                    Count = 3,
                },
                // 1: NORMAL
                new Accessor
                {
                    BufferView = 1,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC3,
                    Count = 3,
                },
                // 2: TEXCOORD_0
                new Accessor
                {
                    BufferView = 2,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC2,
                    Count = 3,
                },
                // 3: TANGENT
                new Accessor
                {
                    BufferView = 3,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC4,
                    Count = 3,
                },
                // 4: COLOR_0
                new Accessor
                {
                    BufferView = 4,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.FLOAT,
                    Type = Accessor.TypeEnum.VEC4,
                    Count = 3,
                },
                // 5: Indices
                new Accessor
                {
                    BufferView = 5,
                    ByteOffset = 0,
                    ComponentType = Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
                    Type = Accessor.TypeEnum.SCALAR,
                    Count = 3,
                },
            ],
            BufferViews =
            [
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset),
                    ByteLength = posBytes,
                },
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset += posBytes),
                    ByteLength = normalBytes,
                },
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset += normalBytes),
                    ByteLength = texCoordBytes,
                },
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset += texCoordBytes),
                    ByteLength = tangentBytes,
                },
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset += tangentBytes),
                    ByteLength = colorBytes,
                },
                new BufferView
                {
                    Buffer = 0,
                    ByteOffset = (bvOffset += colorBytes),
                    ByteLength = indexBytes,
                },
            ],
            Buffers = [new glTFLoader.Schema.Buffer { ByteLength = totalBytes }],
        };

        var reader = new AccessorReader(model, [byteBuffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = new MeshPrimitive
        {
            Attributes = new Dictionary<string, int>
            {
                ["POSITION"] = 0,
                ["NORMAL"] = 1,
                ["TEXCOORD_0"] = 2,
                ["TANGENT"] = 3,
                ["COLOR_0"] = 4,
            },
            Indices = 5,
            Mode = MeshPrimitive.ModeEnum.TRIANGLES,
        };

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        Assert.IsTrue(handle.Valid);
        Assert.AreEqual(3, geometry.Vertices.Count);
        Assert.AreEqual(3, geometry.VertexProps.Count);
        Assert.AreEqual(3, geometry.VertexColors.Count);
        Assert.AreEqual(3, geometry.Indices.Count);
        Assert.AreEqual(Topology.Triangle, geometry.Topology);
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void ConvertPrimitive_ComputesBoundingVolumes()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition();
        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        // BoundingBox should contain all vertices
        var bb = geometry.BoundingBoxLocal;
        Assert.IsTrue(bb.Minimum.X <= 0f);
        Assert.IsTrue(bb.Minimum.Y <= 0f);
        Assert.IsTrue(bb.Maximum.X >= 1f);
        Assert.IsTrue(bb.Maximum.Y >= 1f);

        // BoundingSphere should have non-zero radius
        var bs = geometry.BoundingSphereLocal;
        Assert.IsTrue(bs.Radius > 0f);
    }

    [TestMethod]
    public void ConvertPrimitive_InvalidHandle_ReturnsNullGeometry()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new FailingGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition();
        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
    }

    [TestMethod]
    public async Task ConvertPrimitiveAsync_WithValidTriangle_ReturnsGeometryAndValidHandle()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition();
        var (geometry, handle) = await converter.ConvertPrimitiveAsync(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        Assert.IsTrue(handle.Valid);
        Assert.AreEqual(3, geometry.Vertices.Count);
        Assert.AreEqual(Topology.Triangle, geometry.Topology);
    }

    [TestMethod]
    public async Task ConvertPrimitiveAsync_InvalidHandle_ReturnsNullGeometry()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new FailingGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var primitive = CreatePrimitiveWithPosition();
        var (geometry, handle) = await converter.ConvertPrimitiveAsync(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
    }

    [TestMethod]
    public void ConvertPrimitive_NullAttributes_ReturnsNullAndAddsError()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        // Primitive with null Attributes
        var primitive = new MeshPrimitive { Mode = MeshPrimitive.ModeEnum.TRIANGLES };

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNull(geometry);
        Assert.IsFalse(handle.Valid);
        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [TestMethod]
    public void ConvertPrimitive_DefaultMode_UsesTriangleTopology()
    {
        var (model, buffer) = CreateSimpleTrianglePrimitive();
        var reader = new AccessorReader(model, [buffer]);
        var diagnostics = new List<ImportDiagnostic>();
        var geoManager = new MockGeometryManager();
        var converter = new MeshConverter(geoManager, reader, diagnostics, new ResourceManifest(), MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        // MeshPrimitive.Mode defaults to TRIANGLES (4) per glTF spec
        var primitive = new MeshPrimitive
        {
            Attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
        };

        var (geometry, handle) = converter.ConvertPrimitive(model, primitive, 0, 0);

        Assert.IsNotNull(geometry);
        Assert.AreEqual(Topology.Triangle, geometry.Topology);
    }
}
