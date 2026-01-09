using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Geometries;

[StructLayout(LayoutKind.Sequential)]
[JsonConverter(typeof(Serialization.VertexJsonConverter))]
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
[JsonConverter(typeof(Serialization.BiNormalJsonConverter))]
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

[JsonConverter(typeof(Serialization.GeometryJsonConverter))]
public partial class Geometry : ObservableObject, IDisposable
{
    private static readonly ILogger logger = LogManager.Create<Geometry>();
    public Guid Id { set; get; } = Guid.NewGuid();
    public Topology Topology { get; }

    [Observable]
    private FastList<Vertex> _vertices = [];

    [Observable]
    private FastList<uint> _indices = [];

    [Observable]
    private FastList<BiNormal> _biNormals = [];

    public Geometry(Topology topology = Topology.Triangle)
    {
        Topology = topology;

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(Vertices))
            {
                BufferDirty |= GeometryBufferType.Vertex;
            }
            else if (e.PropertyName is nameof(Indices))
            {
                BufferDirty |= GeometryBufferType.Index;
            }
            else if (e.PropertyName is nameof(BiNormals))
            {
                BufferDirty |= GeometryBufferType.BiNormal;
            }
        };
    }

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<uint> indices,
        IEnumerable<BiNormal>? biNormals = null,
        Topology topology = Topology.Triangle
    )
        : this(topology)
    {
        _vertices.AddRange(vertices);
        _indices.AddRange(indices);
        if (biNormals is not null)
        {
            _biNormals.AddRange(biNormals);
        }
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

    public BufferResource VertexBuffer => _vertexBuffer;
    public BufferResource IndexBuffer => _indexBuffer;
    public BufferResource BiNormalBuffer => _binormalBuffer;

    public GeometryBufferType BufferDirty { set; get; } = GeometryBufferType.All;

    public bool CanHaveIndexBuffer =>
        Topology is not Topology.Point and not Topology.TriangleStrip and not Topology.LineStrip;

    public bool IsDynamic { set; get; } = false;

    /// <summary>
    /// Updates the internal buffers using the specified graphics context.
    /// </summary>
    /// <param name="context">The graphics context to use for updating the buffers. Cannot be null.</param>
    /// <returns>A <see cref="ResultCode"/> value indicating the result of the buffer update operation.</returns>
    public ResultCode UpdateBuffers(IContext context)
    {
        return UpdateBuffers(context, BufferDirty);
    }

    /// <summary>
    /// Updates the geometry buffers of the current object for the specified buffer types.
    /// </summary>
    /// <remarks>This method disposes and recreates the specified geometry buffers (such as vertex, index, or
    /// bi-normal buffers) based on the provided <paramref name="types"/>. Buffers are only recreated if the
    /// corresponding data is present and valid. If a buffer cannot be created, the method returns the corresponding
    /// error code and stops further processing.</remarks>
    /// <param name="context">The graphics context used to create and manage the buffers. Must not be <c>null</c>.</param>
    /// <param name="types">A bitwise combination of <see cref="GeometryBufferType"/> values indicating which buffers to update.</param>
    /// <returns>A <see cref="ResultCode"/> value indicating the result of the buffer update operation. Returns <see
    /// cref="ResultCode.Ok"/> if all specified buffers are updated successfully; otherwise, returns an error code.</returns>
    public ResultCode UpdateBuffers(IContext context, GeometryBufferType types)
    {
        var storageType = IsDynamic ? StorageType.HostVisible : StorageType.Device;
        if (types.HasFlag(GeometryBufferType.Vertex))
        {
            _vertexBuffer?.Dispose();
            if (_vertices.Count > 0)
            {
                unsafe
                {
                    using var ptr = _vertices.GetInternalArray().Pin();
                    var result = context.CreateBuffer(
                        new BufferDesc(
                            BufferUsageBits.Vertex | BufferUsageBits.Storage,
                            storageType,
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
                        logger.LogError(
                            $"Failed to create vertex buffer for Geometry {Id}: {result}"
                        );
                        return result;
                    }
                }
            }
            BufferDirty &= ~GeometryBufferType.Vertex;
        }
        if (types.HasFlag(GeometryBufferType.Index))
        {
            _indexBuffer?.Dispose();
            if (CanHaveIndexBuffer && _indices.Count > 0)
            {
                unsafe
                {
                    using var ptr = _indices.GetInternalArray().Pin();
                    var result = context.CreateBuffer(
                        new BufferDesc(
                            BufferUsageBits.Index,
                            storageType,
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
                        logger.LogError(
                            $"Failed to create index buffer for Geometry {Id}: {result}"
                        );
                        return result;
                    }
                }
            }
            BufferDirty &= ~GeometryBufferType.Index;
        }
        if (types.HasFlag(GeometryBufferType.BiNormal))
        {
            _binormalBuffer?.Dispose();
            if (_biNormals.Count > 0 && _biNormals.Count == _vertices.Count)
            {
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
            BufferDirty &= ~GeometryBufferType.BiNormal;
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
