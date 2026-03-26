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
    private BufferResource _vertexBuffer = BufferResource.Null;
    private BufferResource _vertexPropsBuffer = BufferResource.Null;

    /// <summary>
    /// Index buffer is only used for dynamic geometry. All static geometry shares single index buffer externally.
    /// </summary>
    private BufferResource _indexBuffer = BufferResource.Null;
    private BufferResource _vertColorsBuffer = BufferResource.Null;

    /// <summary>
    /// Pending buffer state for async uploads. Holds new buffers that are being uploaded
    /// while the old buffers remain active for rendering.
    /// </summary>
    private PendingBufferUpdate? _pendingBufferUpdate;

    public uint IndexCount => (uint)_indices.Count;

    public BufferResource VertexBuffer => _vertexBuffer;
    public BufferResource VertexPropsBuffer => _vertexPropsBuffer;
    public BufferResource IndexBuffer => _indexBuffer;
    public BufferResource VertexColorBuffer => _vertColorsBuffer;

    /// <summary>
    /// Gets a value indicating whether there is a pending async buffer update in progress.
    /// </summary>
    public bool HasPendingBufferUpdate => _pendingBufferUpdate is not null;
    #endregion

    public GeometryBufferType BufferDirty { set; get; } = GeometryBufferType.All;

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
                            (uint)(_vertices.Count * sizeof(Vertex))
                        ),
                        out _vertexBuffer,
                        debugName: GraphicsSettings.EnableDebug
                            ? $"{nameof(Geometry)}_{Id}_VertexBuffer"
                            : null
                    );
                    if (result != ResultCode.Ok)
                    {
                        logger.LogError(
                            $"Failed to create vertex property buffer for Geometry {Id}: {result}"
                        );
                        return result;
                    }
                }
            }
            BufferDirty &= ~GeometryBufferType.Vertex;
        }
        if (types.HasFlag(GeometryBufferType.VertexProp))
        {
            _vertexPropsBuffer?.Dispose();
            if (_vertexProps.Count > 0 && _vertexProps.Count == _vertices.Count)
            {
                unsafe
                {
                    using var ptr = _vertexProps.GetInternalArray().Pin();
                    var result = context.CreateBuffer(
                        new BufferDesc(
                            BufferUsageBits.Vertex | BufferUsageBits.Storage,
                            storageType,
                            (nint)ptr.Pointer,
                            (uint)(_vertexProps.Count * VertexProperties.SizeInBytes)
                        ),
                        out _vertexPropsBuffer,
                        debugName: GraphicsSettings.EnableDebug
                            ? $"{nameof(Geometry)}_{Id}_VertexPropsBuffer"
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
            BufferDirty &= ~GeometryBufferType.VertexProp;
        }
        if (types.HasFlag(GeometryBufferType.Index))
        {
            _indexBuffer?.Dispose();
            if (CanHaveIndexBuffer && IsDynamic && _indices.Count > 0)
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

        using var scope = _tracer.BeginScope(nameof(UpdateBuffersAsync), TRACE_BUFFER);

        // Cancel any previous pending update
        _pendingBufferUpdate?.Dispose();
        _pendingBufferUpdate = null;

        const StorageType storageType = StorageType.Device;

        var pending = new PendingBufferUpdate();
        var hasAnyUpload = false;

        if (types.HasFlag(GeometryBufferType.Vertex) && _vertices.Count > 0)
        {
            unsafe
            {
                uint dataSize = (uint)(_vertices.Count * sizeof(Vertex));
                string? debugName = GraphicsSettings.EnableDebug
                    ? $"{nameof(Geometry)}_{Id}_VertexBuffer"
                    : null;

                var result = context.CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        storageType,
                        nint.Zero,
                        dataSize
                    ),
                    out var newBuffer,
                    debugName: debugName
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError(
                        "Failed to create vertex buffer for Geometry {ID}: {RESULT}",
                        Id,
                        result
                    );
                    pending.Dispose();
                    return result;
                }

                using var ptr = _vertices.GetInternalArray().Pin();
                var upload = context.UploadAsync(newBuffer.Handle, 0, (nint)ptr.Pointer, dataSize);
                pending.VertexBuffer = newBuffer;
                pending.UploadHandles.Add(upload);
                hasAnyUpload = true;
            }
            pending.Types |= GeometryBufferType.Vertex;
        }
        else if (types.HasFlag(GeometryBufferType.Vertex))
        {
            // No vertices — mark for clearing on apply
            pending.Types |= GeometryBufferType.Vertex;
        }

        if (
            types.HasFlag(GeometryBufferType.VertexProp)
            && _vertexProps.Count > 0
            && _vertexProps.Count == _vertices.Count
        )
        {
            unsafe
            {
                uint dataSize = (uint)(_vertexProps.Count * VertexProperties.SizeInBytes);
                string? debugName = GraphicsSettings.EnableDebug
                    ? $"{nameof(Geometry)}_{Id}_VertexPropsBuffer"
                    : null;

                var result = context.CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Vertex | BufferUsageBits.Storage,
                        storageType,
                        nint.Zero,
                        dataSize
                    ),
                    out var newBuffer,
                    debugName: debugName
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError(
                        "Failed to create vertex props buffer for Geometry {ID}: {RESULT}",
                        Id,
                        result
                    );
                    pending.Dispose();
                    return result;
                }

                using var ptr = _vertexProps.GetInternalArray().Pin();
                var upload = context.UploadAsync(newBuffer.Handle, 0, (nint)ptr.Pointer, dataSize);
                pending.VertexPropsBuffer = newBuffer;
                pending.UploadHandles.Add(upload);
                hasAnyUpload = true;
            }
            pending.Types |= GeometryBufferType.VertexProp;
        }
        else if (types.HasFlag(GeometryBufferType.VertexProp))
        {
            pending.Types |= GeometryBufferType.VertexProp;
        }

        if (
            types.HasFlag(GeometryBufferType.Index)
            && CanHaveIndexBuffer
            && IsDynamic
            && _indices.Count > 0
        )
        {
            unsafe
            {
                uint dataSize = (uint)(_indices.Count * sizeof(uint));
                string? debugName = GraphicsSettings.EnableDebug
                    ? $"{nameof(Geometry)}_{Id}_IndexBuffer"
                    : null;

                var result = context.CreateBuffer(
                    new BufferDesc(BufferUsageBits.Index, storageType, nint.Zero, dataSize),
                    out var newBuffer,
                    debugName: debugName
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError(
                        "Failed to create index buffer for Geometry {ID}: {RESULT}",
                        Id,
                        result
                    );
                    pending.Dispose();
                    return result;
                }

                using var ptr = _indices.GetInternalArray().Pin();
                var upload = context.UploadAsync(newBuffer.Handle, 0, (nint)ptr.Pointer, dataSize);
                pending.IndexBuffer = newBuffer;
                pending.UploadHandles.Add(upload);
                hasAnyUpload = true;
            }
            pending.Types |= GeometryBufferType.Index;
        }
        else if (types.HasFlag(GeometryBufferType.Index))
        {
            pending.Types |= GeometryBufferType.Index;
        }

        if (
            types.HasFlag(GeometryBufferType.VertexColor)
            && _vertexColors.Count > 0
            && _vertexColors.Count == _vertices.Count
        )
        {
            unsafe
            {
                uint dataSize = (uint)(_vertexColors.Count * sizeof(Vector4));
                string? debugName = GraphicsSettings.EnableDebug
                    ? $"{nameof(Geometry)}_{Id}_VertexColorBuffer"
                    : null;

                var result = context.CreateBuffer(
                    new BufferDesc(BufferUsageBits.Vertex, StorageType.Device, nint.Zero, dataSize),
                    out var newBuffer,
                    debugName: debugName
                );
                if (result != ResultCode.Ok)
                {
                    logger.LogError(
                        "Failed to create vertex color buffer for Geometry {ID}: {RESULT}",
                        Id,
                        result
                    );
                    pending.Dispose();
                    return result;
                }

                using var ptr = _vertexColors.GetInternalArray().Pin();
                var upload = context.UploadAsync(newBuffer.Handle, 0, (nint)ptr.Pointer, dataSize);
                pending.VertexColorBuffer = newBuffer;
                pending.UploadHandles.Add(upload);
                hasAnyUpload = true;
            }
            pending.Types |= GeometryBufferType.VertexColor;
        }
        else if (types.HasFlag(GeometryBufferType.VertexColor))
        {
            pending.Types |= GeometryBufferType.VertexColor;
        }

        if (!hasAnyUpload)
        {
            // All requested buffer types had no data — apply immediately
            ApplyPendingBuffersInternal(pending);
            pending.Dispose();
            return ResultCode.Ok;
        }

        _pendingBufferUpdate = pending;
        BufferDirty = GeometryBufferType.None; // Clear dirty flag since we have a pending update that will apply the changes
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
            _vertexBuffer?.Dispose();
            _vertexBuffer = pending.VertexBuffer ?? BufferResource.Null;
            pending.VertexBuffer = null; // Ownership transferred
            BufferDirty &= ~GeometryBufferType.Vertex;
        }

        if (pending.Types.HasFlag(GeometryBufferType.VertexProp))
        {
            _vertexPropsBuffer?.Dispose();
            _vertexPropsBuffer = pending.VertexPropsBuffer ?? BufferResource.Null;
            pending.VertexPropsBuffer = null;
            BufferDirty &= ~GeometryBufferType.VertexProp;
        }

        if (pending.Types.HasFlag(GeometryBufferType.Index))
        {
            _indexBuffer?.Dispose();
            _indexBuffer = pending.IndexBuffer ?? BufferResource.Null;
            pending.IndexBuffer = null;
            BufferDirty &= ~GeometryBufferType.Index;
        }

        if (pending.Types.HasFlag(GeometryBufferType.VertexColor))
        {
            _vertColorsBuffer?.Dispose();
            _vertColorsBuffer = pending.VertexColorBuffer ?? BufferResource.Null;
            pending.VertexColorBuffer = null;
            BufferDirty &= ~GeometryBufferType.VertexColor;
        }
        EventBus.Instance.PublishAsync(new GeometryUpdatedEvent(Id, GeometryChangeOp.Updated));
    }

    /// <summary>
    /// Holds the state for a pending async buffer upload operation.
    /// New buffers are staged here until uploads complete, then swapped into the geometry.
    /// </summary>
    private sealed class PendingBufferUpdate : IDisposable
    {
        public GeometryBufferType Types;
        public BufferResource? VertexBuffer;
        public BufferResource? VertexPropsBuffer;
        public BufferResource? IndexBuffer;
        public BufferResource? VertexColorBuffer;
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

        /// <summary>
        /// Disposes any buffers that were not transferred to the geometry (e.g., on cancellation or error).
        /// </summary>
        public void Dispose()
        {
            VertexBuffer?.Dispose();
            VertexBuffer = null;
            VertexPropsBuffer?.Dispose();
            VertexPropsBuffer = null;
            IndexBuffer?.Dispose();
            IndexBuffer = null;
            VertexColorBuffer?.Dispose();
            VertexColorBuffer = null;
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
    #endregion

    #region Dispose Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _pendingBufferUpdate?.Dispose();
                _pendingBufferUpdate = null;
                _vertexBuffer?.Dispose();
                _vertexPropsBuffer?.Dispose();
                _indexBuffer?.Dispose();
                _vertColorsBuffer?.Dispose();
                _vertexBuffer = BufferResource.Null;
                _vertexPropsBuffer = BufferResource.Null;
                _indexBuffer = BufferResource.Null;
                _vertColorsBuffer = BufferResource.Null;
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
