using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Trace;

namespace HelixToolkit.Nex.Geometries;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
[JsonConverter(typeof(Serialization.VertexPropsJsonConverter))]
public struct VertexProperties(Vector3 normal, Vector2 texCoord, Vector3 tangent)
{
    public static readonly uint SizeInBytes = NativeHelper.SizeOf<VertexProperties>();

    static VertexProperties()
    {
        Debug.Assert(
            SizeInBytes == 48,
            $"Size of VertexProperties struct must be 64 but is {SizeInBytes}"
        );
        // Verify alignment
        unsafe
        {
            var v = new VertexProperties();
            var basePtr = (byte*)&v;
            var normalOffset = (byte*)&v.Normal - basePtr;
            var texCoordOffset = (byte*)&v.TexCoord - basePtr;
            var tangentOffset = (byte*)&v.Tangent - basePtr;

            Debug.Assert(normalOffset == 0, "Normal must be at offset 0");
            Debug.Assert(texCoordOffset == 16, "TexCoord must be at offset 16");
            Debug.Assert(tangentOffset == 32, "Tangent must be at offset 32");
        }
    }

    public Vector3 Normal = normal;
    private readonly float _padding1 = 0;
    public Vector2 TexCoord = texCoord;
    private readonly Vector2 _padding2 = Vector2.Zero;
    public Vector3 Tangent = tangent;
    private readonly float _padding3 = 0;

    public VertexProperties(Vector3 normal, Vector2 texCoord)
        : this(normal, texCoord, Vector3.Zero) { }

    public VertexProperties(Vector3 normal)
        : this(normal, Vector2.Zero, Vector3.Zero) { }

    public VertexProperties()
        : this(Vector3.Zero, Vector2.Zero, Vector3.Zero) { }

    public static readonly VertexProperties Empty = new(Vector3.Zero, Vector2.Zero, Vector3.Zero);
}

[Flags]
public enum GeometryBufferType
{
    None = 0,
    Vertex = 1,
    VertexProp = 1 << 2,
    Index = 1 << 3,
    VertexColor = 1 << 4,
    All = Vertex | VertexProp | Index | VertexColor,
}

public readonly struct GeometryResourceType { }

[JsonConverter(typeof(Serialization.GeometryJsonConverter))]
public partial class Geometry : ObservableObject, IDisposable
{
    private static readonly ILogger logger = LogManager.Create<Geometry>();
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(Geometry));
    private const string TRACE_BUFFER = "Buffer";
    private const string TRACE_BOUNDS = "Bounds";
    internal Handle<GeometryResourceType> Handle { set; get; } = Handle<GeometryResourceType>.Null;
    internal GeometryManager? Manager { set; get; } = null;

    public bool Attached => Handle.Valid && Manager is not null;

    public uint Id => Handle.Index;

    public string Name { set; get; } = string.Empty;

    /// <summary>
    /// Gets the topology configuration of the geometry.
    /// </summary>
    public Topology Topology { get; }

    [Observable]
    private FastList<Vertex> _vertices = [];

    [Observable]
    private FastList<VertexProperties> _vertexProps = [];

    [Observable]
    private FastList<uint> _indices = [];

    [Observable]
    private FastList<Vector4> _vertexColors = [];

    #region Buffers
    private ElementBuffer<Vector4>? _vertexBuffer;
    private ElementBuffer<VertexProperties>? _vertexPropsBuffer;

    /// <summary>
    /// Index buffer is only used for dynamic geometry. All static geometry shares single index buffer externally.
    /// </summary>
    private ElementBuffer<uint>? _indexBuffer;
    private ElementBuffer<Vector4>? _vertColorsBuffer;

    /// <summary>
    /// Pending buffer state for async uploads. Holds new buffers that are being uploaded
    /// while the old buffers remain active for rendering.
    /// </summary>
    private PendingBufferUpdate? _pendingBufferUpdate;

    public uint IndexCount => (uint)_indices.Count;

    public BufferResource VertexBuffer => _vertexBuffer?.Buffer ?? BufferResource.Null;
    public BufferResource VertexPropsBuffer => _vertexPropsBuffer?.Buffer ?? BufferResource.Null;
    public BufferResource IndexBuffer => _indexBuffer?.Buffer ?? BufferResource.Null;
    public BufferResource VertexColorBuffer => _vertColorsBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets a value indicating whether there is a pending async buffer update in progress.
    /// </summary>
    public bool HasPendingBufferUpdate => _pendingBufferUpdate is not null;
    #endregion

    private GeometryBufferType _bufferDirty = GeometryBufferType.All;
    public GeometryBufferType BufferDirty
    {
        set
        {
            _bufferDirty = value;
            if (value.HasFlag(GeometryBufferType.Vertex))
            {
                IsBoundDirty = true;
            }
        }
        get => _bufferDirty;
    }

    public bool CanHaveIndexBuffer =>
        Topology is not Topology.Point and not Topology.TriangleStrip and not Topology.LineStrip;

    /// <summary>
    /// Gets or sets a value indicating whether the object is dynamic.
    /// If true, the geometry is expected to change frequently.
    /// If false, the geometry is static and does not change after creation. This can allow for certain optimizations such as mesh batching and shared buffers.
    /// Especially for index buffer, dynamic geometry will have its own index buffer, while all static geometries share a single index buffer externally.
    /// Gpu culling only supports static geometry with a single shared index buffer for batched indirect draw.
    /// </summary>
    public bool IsDynamic { set; get; } = false;

    public bool IsBoundDirty { set; get; } = true;

    /// <summary>
    /// Used to indicate the index offset in shared index buffer. For dynamic geometry, it should always be 0.
    /// </summary>
    public uint IndexOffset { set; get; } = 0;

    /// <summary>
    /// Gets or sets the bounding box of the object in local space coordinates.
    /// </summary>
    public BoundingBox BoundingBoxLocal { get; set; } = BoundingBox.Empty;

    /// <summary>
    /// Gets or sets the bounding sphere of the object in local space.
    /// </summary>
    public BoundingSphere BoundingSphereLocal { get; set; } = BoundingSphere.Empty;

    public Geometry(Topology topology = Topology.Triangle, bool isDynamic = false)
    {
        Topology = topology;
        IsDynamic = isDynamic;
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(Vertices))
            {
                BufferDirty |= GeometryBufferType.Vertex;
                IsBoundDirty = true;
            }
            else if (e.PropertyName is nameof(VertexProps))
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
            if (BufferDirty != GeometryBufferType.None && Attached)
            {
                EventBus.Instance.PublishAsync(
                    new GeometryUpdatedEvent(Id, GeometryChangeOp.Updated)
                );
            }
        };
    }

    public Geometry(bool isDynamic)
        : this(Topology.Triangle, isDynamic) { }

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<uint> indices,
        IEnumerable<Vector4>? colors = null,
        Topology topology = Topology.Triangle,
        bool isDynamic = false
    )
        : this(vertices, null, indices, colors, topology, isDynamic) { }

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<VertexProperties>? vertexProps,
        IEnumerable<uint> indices,
        IEnumerable<Vector4>? colors = null,
        Topology topology = Topology.Triangle,
        bool isDynamic = false
    )
        : this(topology, isDynamic)
    {
        _vertices.AddRange(vertices);
        if (vertexProps is not null)
        {
            _vertexProps.AddRange(vertexProps);
        }
        _indices.AddRange(indices);
        if (colors is not null)
        {
            _vertexColors.AddRange(colors);
            Debug.Assert(_vertices.Count == _vertexColors.Count);
        }
    }

    public Geometry(
        IEnumerable<Vertex> vertices,
        IEnumerable<VertexProperties> vertexProps,
        Topology topology = Topology.Point
    )
        : this(topology)
    {
        _vertices.AddRange(vertices);
        _vertexProps.AddRange(vertexProps);
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
        _vertexProps.AddRange(other._vertexProps);
        _indices.AddRange(other._indices);
    }

    #region Buffer Creation
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
        using var scope = _tracer.BeginScope(nameof(UpdateBuffers), TRACE_BUFFER);
        var storageType = IsDynamic ? StorageType.HostVisible : StorageType.Device;
        if (types.HasFlag(GeometryBufferType.Vertex))
        {
            if (_vertices.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertexBuffer);
            }
            else
            {
                _vertexBuffer ??= new ElementBuffer<Vector4>(
                    context,
                    _vertices.Count,
                    BufferUsageBits.Vertex | BufferUsageBits.Storage,
                    IsDynamic,
                    debugName: $"Geo_{Id}_Vert"
                );
                _vertexBuffer.Upload(_vertices);
            }

            BufferDirty &= ~GeometryBufferType.Vertex;
        }
        if (types.HasFlag(GeometryBufferType.VertexProp))
        {
            if (_vertexProps.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
            }
            else
            {
                if (_vertexProps.Count != _vertices.Count)
                {
                    HxDebug.Assert(false, "Vertex properties count must match vertex count");
                }
                else
                {
                    _vertexPropsBuffer ??= new ElementBuffer<VertexProperties>(
                        context,
                        _vertexProps.Count,
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        IsDynamic,
                        debugName: $"Geo_{Id}_VertProps"
                    );
                    _vertexPropsBuffer.Upload(_vertexProps);
                }
            }

            BufferDirty &= ~GeometryBufferType.VertexProp;
        }
        if (types.HasFlag(GeometryBufferType.Index))
        {
            if (_indices.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _indexBuffer);
            }
            else
            {
                if (CanHaveIndexBuffer && IsDynamic && _indices.Count > 0)
                {
                    _indexBuffer ??= new ElementBuffer<uint>(
                        context,
                        _indices.Count,
                        BufferUsageBits.Index | BufferUsageBits.Storage,
                        IsDynamic,
                        debugName: $"Geo_{Id}_Index"
                    );
                    _indexBuffer.Upload(_indices);
                }
            }

            BufferDirty &= ~GeometryBufferType.Index;
        }
        if (types.HasFlag(GeometryBufferType.VertexColor))
        {
            if (_vertexColors.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertColorsBuffer);
            }
            else
            {
                if (_vertexColors.Count != _vertices.Count)
                {
                    HxDebug.Assert(false, "Vertex colors count must match vertex count");
                }
                else
                {
                    _vertColorsBuffer ??= new ElementBuffer<Vector4>(
                        context,
                        _vertexColors.Count,
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        IsDynamic,
                        debugName: $"Geo_{Id}_VertColor"
                    );
                    _vertColorsBuffer.Upload(_vertexColors);
                }
            }

            BufferDirty &= ~GeometryBufferType.VertexColor;
        }
        return ResultCode.Ok;
    }

    /// <summary>
    /// Asynchronously updates the geometry buffers using the transfer queue for GPU uploads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method uses a double-buffering strategy: new GPU buffers are created and data uploads
    /// are scheduled via <see cref="IContext.UploadAsync"/>, while the existing buffers remain
    /// active for rendering. Once all uploads are complete, call <see cref="TryCompletePendingBufferUpdate"/>
    /// or <see cref="ApplyPendingBuffers"/> to atomically swap the new buffers in and dispose the old ones.
    /// </para>
    /// <para>
    /// For dynamic (HostVisible) geometry, this method falls back to the synchronous <see cref="UpdateBuffers"/>
    /// path since mapped memory writes don't benefit from async transfer.
    /// </para>
    /// <para>
    /// If a previous async update is still pending, it will be cancelled (its buffers disposed) and replaced
    /// by this new update.
    /// </para>
    /// </remarks>
    /// <param name="context">The graphics context used to create and manage the buffers. Must not be <c>null</c>.</param>
    /// <param name="types">A bitwise combination of <see cref="GeometryBufferType"/> values indicating which buffers to update.</param>
    /// <returns>A <see cref="ResultCode"/> indicating whether buffer creation and upload scheduling succeeded.</returns>
    public ResultCode UpdateBuffersAsync(IContext context, GeometryBufferType types)
    {
        // Dynamic buffers use mapped memory — no benefit from async transfer
        if (IsDynamic)
        {
            return UpdateBuffers(context, types);
        }
        if (_pendingBufferUpdate != null)
        {
            return ResultCode.InvalidState;
        }

        using var scope = _tracer.BeginScope(nameof(UpdateBuffersAsync), TRACE_BUFFER);

        var pending = new PendingBufferUpdate();
        var hasAnyUpload = false;

        if (types.HasFlag(GeometryBufferType.Vertex))
        {
            if (_vertices.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertexBuffer);
            }
            else
            {
                _vertexBuffer ??= new ElementBuffer<Vector4>(
                    context,
                    _vertices.Count,
                    BufferUsageBits.Vertex | BufferUsageBits.Storage,
                    IsDynamic,
                    debugName: $"Geo_{Id}_Vert"
                );
                pending.UploadHandles.Add(_vertexBuffer.UploadAsync(_vertices));
                hasAnyUpload = true;

                pending.Types |= GeometryBufferType.Vertex;
            }
            BufferDirty &= ~GeometryBufferType.Vertex;
        }

        if (types.HasFlag(GeometryBufferType.VertexProp))
        {
            if (_vertexProps.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
            }
            else
            {
                if (_vertexProps.Count != _vertices.Count)
                {
                    HxDebug.Assert(false, "Vertex properties count must match vertex count");
                }
                else
                {
                    _vertexPropsBuffer ??= new ElementBuffer<VertexProperties>(
                        context,
                        _vertexProps.Count,
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        IsDynamic,
                        debugName: $"Geo_{Id}_VertProps"
                    );
                    pending.UploadHandles.Add(_vertexPropsBuffer.UploadAsync(_vertexProps));
                    hasAnyUpload = true;
                }

                pending.Types |= GeometryBufferType.VertexProp;
            }
            BufferDirty &= ~GeometryBufferType.VertexProp;
        }

        if (types.HasFlag(GeometryBufferType.Index) && CanHaveIndexBuffer && IsDynamic)
        {
            if (_indices.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _indexBuffer);
            }
            else
            {
                _indexBuffer ??= new ElementBuffer<uint>(
                    context,
                    _indices.Count,
                    BufferUsageBits.Index | BufferUsageBits.Storage,
                    IsDynamic,
                    debugName: $"Geo_{Id}_Index"
                );
                pending.UploadHandles.Add(_indexBuffer.UploadAsync(_indices));
                hasAnyUpload = true;
                pending.Types |= GeometryBufferType.Index;
            }
            BufferDirty &= ~GeometryBufferType.Index;
        }

        if (types.HasFlag(GeometryBufferType.VertexColor))
        {
            if (_vertexColors.Count == 0)
            {
                Disposer.DisposeAndRemove(ref _vertColorsBuffer);
            }
            else
            {
                if (_vertexColors.Count != _vertices.Count)
                {
                    HxDebug.Assert(
                        false,
                        $"Vertex colors count {_vertexColors.Count} must match vertex count {_vertices.Count}"
                    );
                }
                else
                {
                    _vertColorsBuffer ??= new ElementBuffer<Vector4>(
                        context,
                        _vertexColors.Count,
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        IsDynamic,
                        debugName: $"Geo_{Id}_VertColor"
                    );
                    pending.UploadHandles.Add(_vertColorsBuffer.UploadAsync(_vertexColors));
                    hasAnyUpload = true;
                }

                pending.Types |= GeometryBufferType.VertexColor;
            }
            BufferDirty &= ~GeometryBufferType.VertexColor;
        }

        if (!hasAnyUpload)
        {
            // All requested buffer types had no data — apply immediately
            ApplyPendingBuffersInternal(pending);
            return ResultCode.Ok;
        }

        _pendingBufferUpdate = pending;
        return ResultCode.Ok;
    }

    /// <summary>
    /// Asynchronously updates all dirty geometry buffers using the transfer queue.
    /// </summary>
    /// <param name="context">The graphics context used to create and manage the buffers.</param>
    /// <returns>A <see cref="ResultCode"/> indicating whether buffer creation and upload scheduling succeeded.</returns>
    public ResultCode UpdateBuffersAsync(IContext context)
    {
        return UpdateBuffersAsync(context, BufferDirty);
    }

    /// <summary>
    /// Checks if the pending async buffer update has completed, and if so, swaps in the new buffers.
    /// </summary>
    /// <remarks>
    /// Call this method each frame (or at a suitable point) to check if the async uploads have finished.
    /// When complete, the old buffers are disposed and the new buffers become active.
    /// This is a non-blocking operation.
    /// </remarks>
    /// <returns><c>true</c> if there was a pending update and it has been applied;
    /// <c>false</c> if there is no pending update or it is still in progress.</returns>
    public bool TryCompletePendingBufferUpdate()
    {
        if (_pendingBufferUpdate is null)
        {
            return false;
        }

        if (!_pendingBufferUpdate.IsCompleted)
        {
            return false;
        }

        ApplyPendingBuffers();
        return true;
    }

    /// <summary>
    /// Applies the pending buffer update, swapping in new buffers and disposing old ones.
    /// </summary>
    /// <remarks>
    /// This method should only be called after verifying that all uploads are complete
    /// (e.g., via <see cref="TryCompletePendingBufferUpdate"/> or by awaiting the upload tasks).
    /// If called while uploads are still in progress, the buffers may contain incomplete data.
    /// </remarks>
    public void ApplyPendingBuffers()
    {
        if (_pendingBufferUpdate is null)
        {
            return;
        }

        var pending = _pendingBufferUpdate;
        _pendingBufferUpdate = null;

        // Log any upload failures
        foreach (var handle in pending.UploadHandles)
        {
            if (handle.Result != ResultCode.Ok)
            {
                logger.LogError(
                    "Async buffer upload failed for Geometry {ID}: {RESULT}",
                    Id,
                    handle.Result
                );
            }
        }

        ApplyPendingBuffersInternal(pending);
    }

    private void ApplyPendingBuffersInternal(PendingBufferUpdate pending)
    {
        if (pending.Types.HasFlag(GeometryBufferType.Vertex))
        {
            BufferDirty &= ~GeometryBufferType.Vertex;
        }

        if (pending.Types.HasFlag(GeometryBufferType.VertexProp))
        {
            BufferDirty &= ~GeometryBufferType.VertexProp;
        }

        if (pending.Types.HasFlag(GeometryBufferType.Index))
        {
            BufferDirty &= ~GeometryBufferType.Index;
        }

        if (pending.Types.HasFlag(GeometryBufferType.VertexColor))
        {
            BufferDirty &= ~GeometryBufferType.VertexColor;
        }
        EventBus.Instance.PublishAsync(new GeometryUpdatedEvent(Id, GeometryChangeOp.Updated));
    }

    /// <summary>
    /// Holds the state for a pending async buffer upload operation.
    /// New buffers are staged here until uploads complete, then swapped into the geometry.
    /// </summary>
    private sealed class PendingBufferUpdate
    {
        public GeometryBufferType Types;
        public readonly List<AsyncUploadHandle> UploadHandles = [];

        /// <summary>
        /// Gets a value indicating whether all async uploads have completed.
        /// </summary>
        public bool IsCompleted
        {
            get
            {
                foreach (var handle in UploadHandles)
                {
                    if (!handle.IsCompleted)
                        return false;
                }
                return true;
            }
        }
    }
    #endregion

    #region Create Bounding Box
    /// <summary>
    /// Creates a bounding box that encompasses all vertices in the current object.
    /// </summary>
    /// <remarks>If no vertices are present, the bounding box is set to <see cref="BoundingBox.Empty"/>.
    /// Otherwise, the bounding box is calculated to enclose all vertices.</remarks>
    public void CreateBoundingBox()
    {
        if (_vertices.Count == 0)
        {
            BoundingBoxLocal = BoundingBox.Empty;
            return;
        }
        BoundingBoxLocal = BoundingBox.FromPoints(_vertices);
    }

    /// <summary>
    /// Creates a bounding sphere that encompasses all vertices in the current object.
    /// </summary>
    /// <remarks>If no vertices are present, the bounding sphere is set to an empty state.</remarks>
    public void CreateBoundingSphere()
    {
        if (_vertices.Count == 0)
        {
            BoundingSphereLocal = BoundingSphere.Empty;
            return;
        }
        BoundingSphereLocal = BoundingSphere.FromPoints(_vertices);
    }

    /// <summary>
    /// Updates the bounding box and bounding sphere for the object if the bounds are marked as dirty.
    /// </summary>
    /// <remarks>This method recalculates the bounding box and bounding sphere only when the bounds are
    /// flagged as dirty.  After the update, the dirty flag is cleared. Call this method to ensure the bounding data is
    /// up-to-date  before performing operations that depend on accurate bounds.</remarks>
    public void UpdateBounds()
    {
        if (!IsBoundDirty)
        {
            return;
        }
        using var scope = _tracer.BeginScope(nameof(CreateBoundingBox), TRACE_BOUNDS);
        CreateBoundingBox();
        CreateBoundingSphere();
        IsBoundDirty = false;
    }

    /// <summary>
    /// Marks the specified geometry buffer type as dirty, indicating that it requires updating.
    /// </summary>
    /// <remarks>If the specified buffer type is <see cref="GeometryBufferType.Vertex"/>, the binding state is
    /// also marked as dirty.</remarks>
    /// <param name="type">The type of geometry buffer to mark as dirty.</param>
    public void MarkDirty(GeometryBufferType type)
    {
        BufferDirty |= type;
    }
    #endregion

    #region Dispose Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _pendingBufferUpdate = null;
                Disposer.DisposeAndRemove(ref _vertexBuffer);
                Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
                Disposer.DisposeAndRemove(ref _indexBuffer);
                Disposer.DisposeAndRemove(ref _vertColorsBuffer);
                Manager?.Remove(this);
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
