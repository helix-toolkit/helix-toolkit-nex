namespace HelixToolkit.Nex.Geometries;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
[JsonConverter(typeof(Serialization.VertexJsonConverter))]
public struct Vertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector4 tangent)
{
    public static readonly uint SizeInBytes = NativeHelper.SizeOf<Vertex>();

    static Vertex()
    {
        Debug.Assert(SizeInBytes == 64, $"Size of Vertex struct must be 64 but is {SizeInBytes}");
        // Verify alignment
        unsafe
        {
            var v = new Vertex();
            var basePtr = (byte*)&v;
            var normalOffset = (byte*)&v.Normal - basePtr;
            var texCoordOffset = (byte*)&v.TexCoord - basePtr;
            var tangentOffset = (byte*)&v.Tangent - basePtr;

            Debug.Assert(normalOffset == 16, "Normal must be at offset 16");
            Debug.Assert(texCoordOffset == 32, "TexCoord must be at offset 32");
            Debug.Assert(tangentOffset == 48, "Tangent must be at offset 48");
        }
    }

    public Vector3 Position = position;
    private readonly float _padding0 = 0;
    public Vector3 Normal = normal;
    private readonly float _padding1 = 0;
    public Vector2 TexCoord = texCoord;
    private readonly Vector2 _padding2 = Vector2.Zero;
    public Vector4 Tangent = tangent;

    public Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
        : this(position, normal, texCoord, Vector4.Zero) { }

    public Vertex(Vector3 position, Vector3 normal)
        : this(position, normal, Vector2.Zero, Vector4.Zero) { }

    public Vertex(Vector3 position)
        : this(position, Vector3.Zero, Vector2.Zero, Vector4.Zero) { }

    public static readonly Vertex Empty = new(
        Vector3.Zero,
        Vector3.Zero,
        Vector2.Zero,
        Vector4.Zero
    );
}

[Flags]
public enum GeometryBufferType
{
    Vertex = 1,
    Index = 1 << 1,
    VertexColor = 1 << 2,
    All = Vertex | Index | VertexColor,
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
    private FastList<Vector4> _vertexColors = [];

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
            else if (e.PropertyName is nameof(VertexColors))
            {
                BufferDirty |= GeometryBufferType.VertexColor;
            }
        };
    }

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<uint> indices,
        IEnumerable<Vector4>? colors = null,
        Topology topology = Topology.Triangle
    )
        : this(topology)
    {
        _vertices.AddRange(vertices);
        _indices.AddRange(indices);
        if (colors is not null)
        {
            _vertexColors.AddRange(colors);
            Debug.Assert(_vertices.Count == _vertexColors.Count);
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
    private BufferResource _vertColorsBuffer = BufferResource.Null;

    public BufferResource VertexBuffer => _vertexBuffer;
    public BufferResource IndexBuffer => _indexBuffer;
    public BufferResource BiNormalBuffer => _vertColorsBuffer;

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
        if (types.HasFlag(GeometryBufferType.VertexColor))
        {
            _vertColorsBuffer?.Dispose();
            if (_vertexColors.Count > 0 && _vertexColors.Count == _vertices.Count)
            {
                unsafe
                {
                    using var ptr = _vertexColors.GetInternalArray().Pin();
                    var result = context.CreateBuffer(
                        new BufferDesc(
                            BufferUsageBits.Vertex,
                            StorageType.Device,
                            (nint)ptr.Pointer,
                            (uint)(_vertexColors.Count * sizeof(Vector4))
                        ),
                        out _vertColorsBuffer,
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
            BufferDirty &= ~GeometryBufferType.VertexColor;
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
                _vertColorsBuffer?.Dispose();
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
