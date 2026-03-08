namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Manages a pool of geometry resources with automatic ID assignment and lifecycle management.
/// </summary>
/// <remarks>
/// The geometry pool provides:
/// <list type="bullet">
/// <item>Automatic ID generation and recycling using a free-list algorithm</item>
/// <item>Generation numbers to prevent the ABA problem</item>
/// <item>Automatic disposal of geometry GPU buffers when destroyed</item>
/// <item>Thread-safe operations</item>
/// </list>
/// </remarks>
public sealed class GeometryManager(IContext context) : IGeometryManager
{
    private static readonly ILogger _logger = LogManager.Create<GeometryManager>();
    private static readonly EventBus _eventBus = EventBus.Instance;
    private readonly IContext _context = context;
    private readonly Pool<GeometryResourceType, Geometry> _pool = new();
    private readonly Dictionary<Geometry, int> _indexCountDict = []; // Tracks index counts for static geometries to manage TotalStaticIndexCount
    private readonly object _lock = new();

    /// <summary>
    /// Gets the current number of active geometries in the pool.
    /// </summary>
    public int Count => _pool.Count;

    public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects => _pool.Objects;

    public int TotalStaticIndexCount { get; private set; } = 0;

    public bool Add(Geometry geometry, out uint id)
    {
        id = 0;
        if (geometry.Handle.Valid || geometry.Manager is not null)
        {
            _logger.LogError("Geometry already belongs to a GeometryManager.");
            return false;
        }
        lock (_lock)
        {
            var handle = _pool.Create(geometry);
            if (handle.Valid)
            {
                geometry.Handle = handle;
                geometry.Manager = this;
                geometry.PropertyChanged += Geometry_PropertyChanged;
                geometry.UpdateBuffers(_context);
                geometry.UpdateBounds();
                if (!geometry.IsDynamic)
                {
                    TotalStaticIndexCount += (int)geometry.IndexCount;
                    _indexCountDict[geometry] = (int)geometry.IndexCount;
                }
                _eventBus.Publish(new GeometryUpdatedEvent(geometry.Id, GeometryChangeOp.Added));
                id = geometry.Id;
            }
            return true;
        }
    }

    public Geometry? GetGeometryById(uint index)
    {
        if (_pool.Objects.Count <= index)
        {
            return null;
        }
        return _pool.Objects[(int)index].Obj;
    }

    private void Geometry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Geometry geometry)
        {
            // Handle property changes if needed, e.g., mark geometry as dirty for rendering
            _logger.LogInformation("Geometry property changed: {PropertyName}", e.PropertyName);
            _eventBus.Publish(new GeometryUpdatedEvent(geometry.Id, GeometryChangeOp.Updated));
        }
    }

    public bool UploadStaticMeshIndices(ref SafeWriteContext ctx)
    {
        lock (_lock)
        {
            uint currentOffset = 0;
            foreach (var entry in _pool.Objects)
            {
                var geometry = entry.Obj;
                if (geometry is not null && !geometry.IsDynamic)
                {
                    var result = ctx.Write(geometry.Indices);
                    if (result != ResultCode.Ok)
                    {
                        _logger.LogError(
                            "Failed to write indices for geometry ID {GeometryId}: {ResultCode}",
                            geometry.Id,
                            result
                        );
                        return false;
                    }
                    geometry.FirstIndex = currentOffset;
                    currentOffset += geometry.IndexCount;
                }
            }
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            // Take a snapshot of all geometries currently in the pool (static and dynamic).
            var list = _pool
                .Objects.Select(entry => entry.Obj)
                .Where(geometry => geometry is not null)
                .ToList();
            foreach (var geometry in list)
            {
                Remove(geometry!);
            }
            Debug.Assert(_pool.Count == 0, "Pool should be empty after Clear.");
            Debug.Assert(
                TotalStaticIndexCount == 0,
                "TotalStaticIndexCount should be zero after Clear."
            );
            Debug.Assert(
                _indexCountDict.Count == 0,
                "_indexCountDict should be empty after Clear."
            );
        }
    }

    public IEnumerable<Geometry> GetAll()
    {
        return _pool;
    }

    public bool Remove(Geometry geometry)
    {
        if (geometry.Manager != this || !geometry.Handle.Valid)
        {
            _logger.LogError("Geometry does not belong to this GeometryManager.");
            return false;
        }
        lock (_lock)
        {
            _pool.Destroy(geometry.Handle);
            var id = geometry.Id;
            geometry.PropertyChanged -= Geometry_PropertyChanged;
            geometry.Handle = Handle<GeometryResourceType>.Null;
            geometry.Manager = null;
            if (_indexCountDict.ContainsKey(geometry))
            {
                TotalStaticIndexCount -= _indexCountDict[geometry];
                _indexCountDict.Remove(geometry);
            }
            _eventBus.Publish(new GeometryUpdatedEvent(id, GeometryChangeOp.Removed));
            return true;
        }
    }

    #region IDisposable Support
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Clear();
        _disposed = true;
    }
    #endregion
}
