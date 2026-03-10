namespace HelixToolkit.Nex.Geometries;

public enum GeometryChangeOp
{
    Added,
    Updated,
    Removed,
}

public readonly struct GeometryUpdatedEvent(uint geometryId, GeometryChangeOp changeType) : IEvent
{
    public uint GeometryId { get; } = geometryId;
    public GeometryChangeOp ChangeType { get; } = changeType;
}

/// <summary>
/// Defines the contract for a geometry pool that manages geometry resources with automatic ID assignment and lifecycle management.
/// </summary>
public interface IGeometryManager : IDisposable
{
    /// <summary>
    /// Gets a read-only list of pool entries containing geometry resources.
    /// </summary>
    IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects { get; }

    /// <summary>
    /// Gets the current number of active geometries in the pool.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the total number of indices in static geometries.
    /// </summary>
    int TotalStaticIndexCount { get; }

    /// <summary>
    /// Adds a new geometry to the Geometry Manager.
    /// </summary>
    /// <param name="geometry">The geometry to add.</param>
    /// <param name="id">Outputs the assigned geometry id</param>
    /// <returns>Success or failed.</returns>
    bool Add(Geometry geometry, out uint id);

    /// <summary>
    /// Remove geometry from Geometry Manager. You can also call geometry.Dispose() to remove geometry from Geometry Manager.
    /// </summary>
    /// <param name="geometry"></param>
    /// <returns>Whether geometry is removed successfully.</returns>
    bool Remove(Geometry geometry);

    /// <summary>
    /// Uploads the index data for all static mesh geometries in the pool to the specified write context.
    /// </summary>
    /// <remarks>Only geometries that are not marked as dynamic are processed. If any write operation fails,
    /// the method logs an error and returns <see langword="false"/>. The method is thread-safe.</remarks>
    /// <param name="ctx">The write context used to transfer index data. Must be a valid and writable context.</param>
    /// <returns><see langword="true"/> if all static mesh indices are successfully uploaded; otherwise, <see langword="false"/>.</returns>
    bool UploadStaticMeshIndices(ref SafeWriteContext ctx);

    /// <summary>
    /// Clears all geometries from the pool and disposes their resources.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets all active geometries in the pool.
    /// </summary>
    /// <returns>An enumerable of all active geometries.</returns>
    IEnumerable<Geometry> GetAll();

    /// <summary>
    /// Get geometry by its index. Note that the geometry may be null if it has been removed from the pool, so always check for null before using the returned geometry.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    Geometry? GetGeometryById(uint index);
}
