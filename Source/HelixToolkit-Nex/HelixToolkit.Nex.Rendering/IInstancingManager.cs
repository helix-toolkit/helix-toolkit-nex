namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Defines the contract for an instancing pool that manages <see cref="Instancing"/> resources by object
/// reference (no manager-assigned handle), mirroring the lifecycle, eventing, GPU-upload, deferred-removal,
/// and thread-safety guarantees provided by the geometry manager.
/// </summary>
public interface IInstancingManager : IDisposable
{
    /// <summary>
    /// Gets the current number of active instancings managed by this manager.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Adds an <see cref="Instancing"/> to the manager. GPU uploads are performed later via <see cref="UploadInstanceBuffers"/> during the render loop.
    /// The instancing must not already belong to another manager.
    /// </summary>
    /// <param name="instancing">The instancing to add. Must not already belong to another manager.</param>
    /// <returns><see langword="true"/> if the instancing was added (or is already owned by this manager); otherwise <see langword="false"/>.</returns>
    bool Add(Instancing instancing);

    /// <summary>
    /// Removes an <see cref="Instancing"/> from the manager and disposes its GPU resources immediately.
    /// </summary>
    /// <param name="instancing">The instancing to remove.</param>
    /// <returns><see langword="true"/> if the instancing was removed; otherwise <see langword="false"/>.</returns>
    bool Remove(Instancing instancing);

    /// <summary>
    /// Queues an <see cref="Instancing"/> for deferred removal. The instancing stays managed until
    /// <see cref="ProcessPendingRemovals"/> runs, so the removal happens at a single GPU-safe frame boundary.
    /// </summary>
    /// <param name="instancing">The instancing to remove on the next <see cref="ProcessPendingRemovals"/>.</param>
    void RemoveDeferred(Instancing instancing);

    /// <summary>
    /// Performs all removals queued by <see cref="RemoveDeferred"/>. Intended to be called once per frame by the
    /// render loop at a GPU-safe frame boundary.
    /// </summary>
    void ProcessPendingRemovals();

    /// <summary>
    /// Removes all managed instancings and disposes their GPU resources.
    /// </summary>
    void Clear();

    /// <summary>
    /// Determines whether the specified <see cref="Instancing"/> is currently managed by this manager.
    /// </summary>
    /// <param name="instancing">The instancing to test. A <see langword="null"/> reference returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the instancing is managed by this manager; otherwise <see langword="false"/>.</returns>
    bool Contains(Instancing instancing);

    /// <summary>
    /// Gets the number of managed instancings whose GPU buffers need to be re-uploaded.
    /// </summary>
    /// <returns>The number of managed instancings marked as dirty, always within the inclusive range <c>[0, Count]</c>.</returns>
    int GetDirtyCount();

    /// <summary>
    /// Uploads the instance-transform data of all dirty managed instancings to the GPU using the stored context.
    /// </summary>
    /// <returns>A <see cref="ResultCode"/> indicating success or the failure cause of the upload operation.</returns>
    ResultCode UploadInstanceBuffers();
}
