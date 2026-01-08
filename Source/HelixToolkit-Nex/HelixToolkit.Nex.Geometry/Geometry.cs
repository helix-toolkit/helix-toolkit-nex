using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Geometries;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 color)
{
    public static readonly uint SizeInBytes = NativeHelper.SizeOf<Vertex>();

    static Vertex()
    {
        Debug.Assert(SizeInBytes == 48);
    }

    public Vector3 Position = position;
    public Vector3 Normal = normal;
    public Vector2 TexCoord = texCoord;
    public Vector4 Color = color;

    public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        : this(position, normal, texCoord, new Vector4(1, 1, 1, 1)) { }

    public Vertex(Vector3 position, Vector3 normal)
        : this(position, normal, new Vector2(0, 0), new Vector4(1, 1, 1, 1)) { }

    public Vertex(Vector3 position)
        : this(position, new Vector3(0, 0, 0), new Vector2(0, 0), new Vector4(1, 1, 1, 1)) { }

    public static readonly Vertex Empty = new(
        new Vector3(0, 0, 0),
        new Vector3(0, 0, 0),
        new Vector2(0, 0),
        new Vector4(0, 0, 0, 0)
    );
}

[StructLayout(LayoutKind.Sequential)]
public struct BiNormal(Vector3 bitangent, Vector3 tangent)
{
    public static readonly uint SizeInBytes = NativeHelper.SizeOf<BiNormal>();

    static BiNormal()
    {
        Debug.Assert(SizeInBytes == 24);
    }

    public Vector3 Bitangent = bitangent;
    public Vector3 Tangent = tangent;

    public static readonly BiNormal Empty = new(new Vector3(0, 0, 0), new Vector3(0, 0, 0));
}

[Flags]
public enum GeometryBufferType
{
    Vertex = 1,
    Index = 1 << 1,
    BiNormal = 1 << 2,
    All = Vertex | Index | BiNormal,
}

public partial class Geometry(Topology topology = Topology.Triangle) : ObservableObject, IDisposable
{
    private static readonly ILogger logger = LogManager.Create<Geometry>();
    public Guid Id { set; get; } = Guid.NewGuid();
    public Topology Topology { get; } = topology;

    [Observable]
    private FastList<Vertex> _vertices = [];

    [Observable]
    private FastList<uint> _indices = [];

    [Observable]
    private FastList<BiNormal> _biNormals = [];

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<uint> indices,
        Topology topology = Topology.Triangle
    )
        : this(topology)
    {
        _vertices.AddRange(vertices);
        _indices.AddRange(indices);
    }

    public Geometry(IEnumerable<Vertex> vertices, Topology topology = Topology.Point)
        : this(topology)
    {
        _vertices.AddRange(vertices);
    }

    public Geometry(Geometry other)
        : this(other.Topology)
    {
        _vertices.AddRange(other._vertices);
        _indices.AddRange(other._indices);
    }

    private BufferResource _vertexBuffer = BufferResource.Null;
    private BufferResource _indexBuffer = BufferResource.Null;
    private BufferResource _binormalBuffer = BufferResource.Null;

    public ResultCode UpdateBuffers(IContext context, GeometryBufferType types)
    {
        if (types.HasFlag(GeometryBufferType.Vertex))
        {
            _vertexBuffer?.Dispose();
            unsafe
            {
                using var ptr = _vertices.GetInternalArray().Pin();
                var result = context.CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Vertex,
                        StorageType.Device,
                        (nint)ptr.Pointer,
                        (uint)(_vertices.Count * Vertex.SizeInBytes)
                    ),
                    out _vertexBuffer,
                    debugName: GraphicsSettings.EnableDebug
                        ? $"{nameof(Geometry)}_{Id}_VertexBuffer"
                        : null
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError($"Failed to create vertex buffer for Geometry {Id}: {result}");
                    return result;
                }
            }
        }
        if (types.HasFlag(GeometryBufferType.Index))
        {
            _indexBuffer?.Dispose();
            unsafe
            {
                using var ptr = _indices.GetInternalArray().Pin();
                var result = context.CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Index,
                        StorageType.Device,
                        (nint)ptr.Pointer,
                        (uint)(_indices.Count * sizeof(uint))
                    ),
                    out _indexBuffer,
                    debugName: GraphicsSettings.EnableDebug
                        ? $"{nameof(Geometry)}_{Id}_IndexBuffer"
                        : null
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError($"Failed to create index buffer for Geometry {Id}: {result}");
                    return result;
                }
            }
        }
        if (types.HasFlag(GeometryBufferType.BiNormal))
        {
            _binormalBuffer?.Dispose();
            unsafe
            {
                using var ptr = _biNormals.GetInternalArray().Pin();
                var result = context.CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Vertex,
                        StorageType.Device,
                        (nint)ptr.Pointer,
                        (uint)(_biNormals.Count * BiNormal.SizeInBytes)
                    ),
                    out _binormalBuffer,
                    debugName: GraphicsSettings.EnableDebug
                        ? $"{nameof(Geometry)}_{Id}_BiNormalBuffer"
                        : null
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError(
                        $"Failed to create bi-normal buffer for Geometry {Id}: {result}"
                    );
                    return result;
                }
            }
        }
        return ResultCode.Ok;
    }

    #region Dispose Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _vertexBuffer?.Dispose();
                _indexBuffer?.Dispose();
                _binormalBuffer?.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Geometry()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
