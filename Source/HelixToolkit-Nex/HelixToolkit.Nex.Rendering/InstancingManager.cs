namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Manages a pool of <see cref="Instancing"/> objects, tracking membership and ownership by object
/// reference (no manager-assigned handle). Mirrors the lifecycle, eventing, GPU-upload, deferred-removal,
/// and thread-safety guarantees provided by the geometry manager.
/// </summary>
public sealed class InstancingManager : IInstancingManager
{
    private static readonly ILogger _logger = LogManager.Create<InstancingManager>();
    private static readonly EventBus _eventBus = EventBus.Instance;
    private readonly IContext _context;
    private readonly object _lock = new();

    // Reference-keyed membership for O(1) Contains, plus an insertion-ordered list for the read-only
    // Objects view and deterministic enumeration. An Instancing is in _set/_objects iff its
    // Owning_Manager equals this manager.
    private readonly HashSet<Instancing> _set = new(ReferenceEqualityComparer.Instance);

    // Deferred-removal queue, deduplicated by reference.
    private readonly HashSet<Instancing> _pendingRemovals = new(ReferenceEqualityComparer.Instance);

    private bool _disposed;

    private bool _dirty = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstancingManager"/> class bound to the supplied
    /// graphics context.
    /// </summary>
    /// <param name="context">The graphics context used for GPU buffer creation and uploads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    public InstancingManager(IContext context)
    {
        // Requirement 1.3 — reject null context so no instance is created.
        _context = context ?? throw new ArgumentNullException(nameof(context));
        // Requirement 1.1 — retain the supplied context; 1.2 — collections start empty (Count == 0).
    }

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _set.Count;
            }
        }
    }

    /// <inheritdoc/>
    public bool Contains(Instancing instancing)
    {
        if (instancing is null)
        {
            return false;
        }
        lock (_lock)
        {
            return _set.Contains(instancing);
        }
    }

    /// <inheritdoc/>
    public int GetDirtyCount()
    {
        lock (_lock)
        {
            int count = 0;
            foreach (var instancing in _set.AsValueEnumerable())
            {
                if (instancing.IsDirty)
                {
                    ++count;
                }
            }
            return count;
        }
    }

    /// <inheritdoc/>
    public bool Add(Instancing instancing)
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed.
        if (_disposed)
        {
            _logger.LogError("Cannot add an Instancing: the InstancingManager has been disposed.");
            return false;
        }

        // Requirement 2.7 — reject a null instancing: log and return false, no state change.
        if (instancing is null)
        {
            _logger.LogError("Cannot add a null Instancing.");
            return false;
        }

        // Requirement 2.5 / 2.6 — ownership pre-check via the Owning_Manager back-reference.
        var owner = instancing.Manager;
        if (owner is not null)
        {
            if (ReferenceEquals(owner, this))
            {
                // Requirement 2.5 — already owned by this manager: idempotent no-op
                // (no re-add, no re-subscribe, no re-upload, no event).
                return true;
            }
            // Requirement 2.6 / 13.4 — owned by another manager: reject and leave Owning_Manager unchanged.
            _logger.LogError("Instancing is already owned by another InstancingManager.");
            return false;
        }

        lock (_lock)
        {
            // Re-check ownership inside the lock to guard against concurrent adds.
            owner = instancing.Manager;
            if (owner is not null)
            {
                if (ReferenceEquals(owner, this))
                {
                    return true;
                }
                _logger.LogError("Instancing is already owned by another InstancingManager.");
                return false;
            }

            // Requirement 2.1 / 13.1 — establish membership and ownership.
            _set.Add(instancing);
            instancing.Manager = this;

            // Requirement 2.2 — subscribe to OnDirty.
            instancing.OnDirty += InstancingDirty;

            _dirty |= instancing.IsDirty;
            // Requirement 2.4 — publish the Added notification carrying the instancing reference.
            _eventBus.PublishAsync(
                new InstancingUpdatedEvent(instancing, InstancingChangeOp.Added)
            );
            return true;
        }
    }

    private void InstancingDirty(object? sender, EventArgs e)
    {
        if (sender is not Instancing instancing)
        {
            return;
        }

        _dirty = true;
        _eventBus.PublishAsync(new InstancingUpdatedEvent(instancing, InstancingChangeOp.Updated));
    }

    /// <inheritdoc/>
    public bool Remove(Instancing instancing)
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed. During
        // Dispose itself this guard is not yet active (_disposed is set only after the disposal loop
        // completes), so Dispose's own removals proceed normally.
        if (_disposed)
        {
            _logger.LogError(
                "Cannot remove an Instancing: the InstancingManager has been disposed."
            );
            return false;
        }

        // Requirement 4.5 — reject a null instancing: log one Error-level message, publish no event,
        // make no state change, and return false.
        if (instancing is null)
        {
            _logger.LogError("Cannot remove a null Instancing.");
            return false;
        }

        lock (_lock)
        {
            // Requirement 4.4 — reject an instancing whose Owning_Manager is not this manager or that
            // is not currently contained in the pool: log one Error-level message identifying the
            // rejected instancing, publish no event, make no state change, and return false.
            // Note: instancing.Manager may already be null when Remove is reached via the disposal
            // path; such an instancing is correctly rejected here as not managed by this manager.
            if (!ReferenceEquals(instancing.Manager, this) || !_set.Contains(instancing))
            {
                _logger.LogError(
                    "Cannot remove Instancing '{Name}': it is not managed by this InstancingManager.",
                    instancing.Name
                );
                return false;
            }
            instancing.OnDirty -= InstancingDirty;
            _set.Remove(instancing);

            // Requirement 13.3 — clear the Owning_Manager reference. This is done BEFORE disposing the
            // GPU resources so that routing Instancing.Dispose through the owning manager's deferred
            // removal (task 14.1) observes a null Manager and does not re-enter this removal path.
            instancing.Manager = null;

            // Requirement 4.1 — dispose the instancing's GPU resources (Buffer / CulledIndicesBuffer).
            instancing.Dispose();

            // Requirement 4.2 — publish the Removed notification carrying the instancing reference
            // exactly once.
            _eventBus.PublishAsync(
                new InstancingUpdatedEvent(instancing, InstancingChangeOp.Removed)
            );

            // Drop any pending-removal entry so a later ProcessPendingRemovals does not re-process it.
            _pendingRemovals.Remove(instancing);

            // Requirement 4.3 — report that the removal succeeded.
            return true;
        }
    }

    /// <inheritdoc/>
    public void RemoveDeferred(Instancing instancing)
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed.
        if (_disposed)
        {
            _logger.LogError(
                "Cannot defer removal of an Instancing: the InstancingManager has been disposed."
            );
            return;
        }

        // Requirement 5.4 — reject an instancing not managed by this manager: log an error, leave the
        // pending-removals queue and the managed pool unchanged. instancing.Manager may already be null
        // if called from Instancing.Dispose after the manager reference was cleared; in that case there
        // is nothing pooled to defer.
        if (instancing is null)
        {
            _logger.LogError("Cannot defer removal of a null Instancing.");
            return;
        }

        lock (_lock)
        {
            if (!ReferenceEquals(instancing.Manager, this) || !_set.Contains(instancing))
            {
                _logger.LogError(
                    "Cannot defer removal of Instancing '{Name}': it is not managed by this InstancingManager.",
                    instancing.Name
                );
                return;
            }

            // Requirements 5.1 / 5.2 / 5.3 — enqueue exactly once (the HashSet coalesces duplicate
            // enqueues) without altering membership or ownership: the instancing stays managed with its
            // Owning_Manager unchanged until ProcessPendingRemovals runs.
            _pendingRemovals.Add(instancing);
        }
    }

    /// <inheritdoc/>
    public void ProcessPendingRemovals()
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed.
        if (_disposed)
        {
            _logger.LogError(
                "Cannot process pending removals: the InstancingManager has been disposed."
            );
            return;
        }

        Instancing[] toRemove;
        lock (_lock)
        {
            // Requirement 5.6 — empty queue: no-op without modifying the pool or raising an error.
            if (_pendingRemovals.Count == 0)
            {
                return;
            }

            // Snapshot the queued entries and clear the queue under the lock, mirroring the geometry
            // manager. Remove takes the lock itself per call, so the removals run after the snapshot.
            toRemove = [.. _pendingRemovals];
            _pendingRemovals.Clear();
        }

        // Requirement 5.5 — remove exactly the queued subset (clearing each removed instancing's
        // Owning_Manager) and leave the queue empty. Remove already drops any pending-removal entry, so
        // double-processing is safe.
        foreach (var instancing in toRemove)
        {
            Remove(instancing);
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed.
        if (_disposed)
        {
            _logger.LogError("Cannot clear: the InstancingManager has been disposed.");
            return;
        }

        // Collected disposal failures so the loop can continue past an individual failure and then
        // surface an aggregate error (Requirement 6.6).
        List<Exception>? failures = null;

        lock (_lock)
        {
            // Requirement 6.3 — discard all deferred removals; the instancings are still pooled and
            // are removed/disposed by the snapshot loop below.
            _pendingRemovals.Clear();

            // Requirement 6.1 — snapshot every managed instancing so the collection can be mutated
            // safely while iterating. Empty pool → empty snapshot → no-op (Requirement 6.5).
            var snapshot = new List<Instancing>(_set);
            foreach (var instancing in snapshot)
            {
                try
                {
                    // Mirror GeometryManager.Clear: route each removal through Remove, which
                    // unsubscribes PropertyChanged, removes from _set/_objects, clears Owning_Manager
                    // before disposing GPU resources (avoiding re-entrancy), disposes the GPU
                    // resources exactly once (Requirement 6.2), and publishes the Removed event.
                    Remove(instancing);
                }
                catch (Exception ex)
                {
                    // Requirement 6.6 — a disposal failure must not stop the loop. Membership for this
                    // instancing has already been cleared inside Remove before the failing Dispose, so
                    // Count still reaches 0; record the error and continue with the remaining entries.
                    _logger.LogError(
                        ex,
                        "Failed to dispose Instancing '{Name}' during Clear.",
                        instancing.Name
                    );
                    failures ??= [];
                    failures.Add(ex);
                }
            }

            // Requirement 6.1 / 6.4 — the pool is empty and Count reports 0 after Clear.
            Debug.Assert(_set.Count == 0, "Managed set should be empty after Clear.");
            Debug.Assert(
                _pendingRemovals.Count == 0,
                "Pending removals should be empty after Clear."
            );
        }

        // Requirement 6.6 — surface an aggregate error indicating that one or more disposals failed,
        // after every instancing has been removed and the pool emptied.
        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more Instancing disposals failed during Clear.",
                failures
            );
        }
    }

    /// <inheritdoc/>
    public ResultCode UploadInstanceBuffers()
    {
        // Requirement 12.4 — reject managed operations after the manager has been disposed.
        if (_disposed)
        {
            _logger.LogError(
                "Cannot upload instance buffers: the InstancingManager has been disposed."
            );
            return ResultCode.InvalidState;
        }
        if (!_dirty)
        {
            return ResultCode.Ok;
        }
        lock (_lock)
        {
            _dirty = false;
            // Requirement 9.4 — track the first (most specific) failure so a success result is only
            // returned when every processed dirty instancing uploaded successfully.
            var result = ResultCode.Ok;

            // Requirements 9.1 — process each managed instancing whose state is Dirty. Clean
            // instancings are skipped, so an all-clean pool performs no GPU work and returns Ok.
            foreach (var instancing in _set.AsValueEnumerable())
            {
                if (!instancing.IsDirty)
                {
                    continue;
                }

                // Requirement 9.6 — explicit oversize pre-check: an instancing whose transform count
                // exceeds MaxInstanceCount is skipped (no GPU upload), an out-of-range error is
                // signalled identifying the affected instancing, and a failure is recorded. Its prior
                // buffer contents are left unchanged because UpdateBuffer is never invoked.
                if (instancing.Transforms.Count > Limits.MaxInstanceCount)
                {
                    _logger.LogError(
                        "Cannot upload instance-transform buffer for '{Name}': instance count {Count} "
                            + "exceeds the maximum allowed {Max}.",
                        instancing.Name,
                        instancing.Transforms.Count,
                        Limits.MaxInstanceCount
                    );
                    if (result == ResultCode.Ok)
                    {
                        result = ResultCode.ArgumentOutOfRange;
                    }
                    continue;
                }

                // Requirements 9.1 / 9.2 — upload the transform data; UpdateBuffer clears the dirty
                // state on success so the instancing is not re-uploaded on the next pass.
                try
                {
                    var uploadResult = instancing.UpdateBuffer(_context);
                    if (uploadResult != ResultCode.Ok)
                    {
                        // Requirement 9.5 — log the failure identifying the affected instancing and
                        // cause, leave its prior buffer contents unchanged, and record the failure.
                        _logger.LogError(
                            "Failed to upload instance-transform buffer for '{Name}': {ResultCode}.",
                            instancing.Name,
                            uploadResult
                        );
                        if (result == ResultCode.Ok)
                        {
                            result = uploadResult;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Requirement 9.5 — an upload that throws is reported with the affected instancing
                    // and cause; the prior buffer contents are untouched and a failure is recorded.
                    _logger.LogError(
                        ex,
                        "Failed to upload instance-transform buffer for '{Name}'.",
                        instancing.Name
                    );
                    if (result == ResultCode.Ok)
                    {
                        result = ResultCode.RuntimeError;
                    }
                }
            }

            // Requirements 9.3 / 9.4 — Ok only if all processed dirty instancings succeeded (or none
            // were dirty); otherwise the first/most specific failure code.
            return result;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _pendingRemovals.Clear();

        Clear();

        Debug.Assert(_set.Count == 0, "Managed set should be empty after Dispose.");
        Debug.Assert(
            _pendingRemovals.Count == 0,
            "Pending removals should be empty after Dispose."
        );
        _disposed = true;
    }
}

public static class InstancingManagerExtensions
{
    /// <summary>
    /// Creates a new <see cref="Instancing"/> object, adds it to the specified <see cref="IInstancingManager"/>,
    /// and initializes it with the provided instance transforms.
    /// </summary>
    /// <param name="manager"></param>
    /// <param name="isDynamic"></param>
    /// <param name="name"></param>
    /// <param name="instanceTransforms"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Instancing Create(
        this IInstancingManager manager,
        bool isDynamic,
        string? name = null,
        IEnumerable<InstanceTransform>? instanceTransforms = null
    )
    {
        ArgumentNullException.ThrowIfNull(manager);
        var instancing = new Instancing(isDynamic, name)
        {
            Transforms = [.. instanceTransforms ?? Enumerable.Empty<InstanceTransform>()],
        };
        if (!manager.Add(instancing))
        {
            throw new InvalidOperationException(
                "Failed to add the newly created Instancing to the manager."
            );
        }
        return instancing;
    }
}
