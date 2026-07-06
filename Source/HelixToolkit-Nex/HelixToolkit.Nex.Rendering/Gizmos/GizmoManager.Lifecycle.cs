using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Disposal / lifecycle surface of <see cref="GizmoManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// The manager is hosted by the engine as its gizmo service (<c>Engine.Gizmos</c>). When the engine
/// is disposed it disposes the service, which stops tracking every gizmo the service owned by
/// removing each tracked gizmo's <see cref="GizmoDrawInfo"/> component from its carrier entity and
/// clearing all internal tracking and cache state (Requirement 4.2).
/// </para>
/// <para>
/// After disposal every service operation throws <see cref="ObjectDisposedException"/> so a stale
/// reference obtained before disposal cannot mutate a torn-down service (Requirement 4.3). Disposal
/// is idempotent.
/// </para>
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>Whether this manager has been disposed. Once set, service operations throw.</summary>
    private bool _disposed;

    /// <summary>Gets a value indicating whether this manager has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if this manager has already been disposed. Called
    /// at the entry of every mutating service operation so use-after-dispose is rejected
    /// (Requirement 4.3).
    /// </summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Stops tracking every gizmo the manager owns: removes each tracked gizmo's
    /// <see cref="GizmoDrawInfo"/> component from its carrier entity (both the factory instances and
    /// the legacy managed entity) and clears all instance, tracking, and handle-cache state
    /// (Requirement 4.2). Safe to call repeatedly.
    /// </summary>
    public void ClearAll()
    {
        // Remove the published component from every factory-created instance's carrier entity.
        foreach (var instance in _instances.Values.AsValueEnumerable().Where(x => x is not null && x.HasEntity))
        {
            instance.Entity.Remove<GizmoDrawInfo>();
        }
        _instances.Clear();

        // Remove the legacy managed-entity component if one is attached.
        if (_hasManagedEntity)
        {
            _managedEntity.Remove<GizmoDrawInfo>();
            _managedEntity = Entity.Null;
            _hasManagedEntity = false;
        }

        _tracked.Clear();
        _handleCache.Clear();
        _handles.Clear();
        _entityIdToHandle.Clear();
    }

    /// <summary>
    /// Disposes the manager: clears all tracked gizmos (Requirement 4.2) and marks the manager
    /// disposed so subsequent service operations throw <see cref="ObjectDisposedException"/>
    /// (Requirement 4.3). Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClearAll();
        _disposed = true;
    }
}
