using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Factory surface of the <see cref="GizmoManager"/>: turns a <see cref="GizmoDefinition"/> into a
/// reusable <see cref="GizmoInstanceHandle"/> backed by a handle set that is built exactly once per
/// distinct <see cref="GizmoShapeKey"/> and cached for reuse.
/// </summary>
/// <remarks>
/// <para>
/// Handle <em>geometry</em> depends only on the geometry-affecting subset of a definition captured by
/// <see cref="GizmoDefinition.ShapeKey"/>. Equal definitions therefore share one cached
/// <see cref="IReadOnlyList{GizmoHandle}"/> instance and never trigger a second build (Requirements
/// 1.1, 1.2, 2.6).
/// </para>
/// <para>
/// This file adds only the cache, the build-once helper, <see cref="TryCreateGizmo"/>, and the
/// <see cref="HandleSetBuildCount"/> telemetry seam. Cache-backed component publishing
/// (<c>TrySetGizmo</c>) and the per-instance update path are added by later tasks.
/// </para>
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>
    /// Cache of built handle sets keyed by <see cref="GizmoShapeKey"/>. Each entry is an immutable,
    /// shared handle set built exactly once and reused by every instance with the same shape.
    /// </summary>
    private readonly Dictionary<GizmoShapeKey, IReadOnlyList<GizmoHandle>> _handleCache = [];

    /// <summary>
    /// Per-instance state keyed by the opaque <see cref="GizmoInstanceHandle"/> the factory returns.
    /// Populated on a successful <see cref="TryCreateGizmo"/> so the handle references a real gizmo.
    /// </summary>
    private readonly Dictionary<GizmoInstanceHandle, GizmoInstance> _instances = [];

    /// <summary>The next monotonic instance id to allocate; <c>0</c> is reserved for <see cref="GizmoInstanceHandle.None"/>.</summary>
    private int _nextInstanceId = 1;

    /// <summary>The number of times an underlying handle-set build has actually run.</summary>
    private int _handleSetBuildCount;

    /// <summary>
    /// Gets the number of times the underlying handle-set build has run. A cache hit does not
    /// increment this count, so it stays flat while definitions are unchanged (a test/telemetry seam
    /// for Requirements 1.1, 1.2, 2.6).
    /// </summary>
    public int HandleSetBuildCount => _handleSetBuildCount;

    /// <summary>
    /// Creates a gizmo for <paramref name="definition"/>, building and caching its handle set on the
    /// first request for that shape and returning a reusable <see cref="GizmoInstanceHandle"/> that
    /// references the cached set (Requirements 1.1, 1.2).
    /// </summary>
    /// <param name="definition">The gizmo definition to create.</param>
    /// <param name="handle">
    /// On success, a valid handle referencing the created gizmo; on failure,
    /// <see cref="GizmoInstanceHandle.None"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the gizmo was created; <see langword="false"/> when
    /// <paramref name="definition"/> is invalid.
    /// </returns>
    /// <remarks>
    /// An invalid definition is rejected without building anything and leaves the cache unchanged
    /// (Requirement 1.5). A request whose shape matches a previously created gizmo reuses the cached
    /// handle set without a rebuild (Requirement 1.2).
    /// </remarks>
    public bool TryCreateGizmo(in GizmoDefinition definition, out GizmoInstanceHandle handle)
    {
        ThrowIfDisposed();

        // Reject invalid definitions up front: no build, cache untouched (Requirement 1.5).
        if (!definition.IsValid)
        {
            handle = GizmoInstanceHandle.None;
            return false;
        }

        IReadOnlyList<GizmoHandle> handleSet = GetOrBuildHandleSet(definition.ShapeKey);

        handle = new GizmoInstanceHandle(_nextInstanceId++);
        _instances[handle] = new GizmoInstance
        {
            Definition = definition,
            Handles = handleSet,
        };

        return true;
    }

    /// <summary>
    /// Returns the cached handle set for <paramref name="shapeKey"/>, building it exactly once via
    /// <see cref="GizmoModelBuilder"/> and incrementing <see cref="_handleSetBuildCount"/> only on an
    /// actual build (Requirements 1.1, 1.2, 2.6).
    /// </summary>
    /// <param name="shapeKey">The shape key whose handle set to fetch or build.</param>
    /// <returns>The immutable, shared handle set for the shape key.</returns>
    private IReadOnlyList<GizmoHandle> GetOrBuildHandleSet(in GizmoShapeKey shapeKey)
    {
        if (_handleCache.TryGetValue(shapeKey, out IReadOnlyList<GizmoHandle>? cached))
        {
            return cached;
        }

        List<GizmoHandle> handles = [];
        switch (shapeKey.Mode)
        {
            case GizmoMode.Translate:
                GizmoModelBuilder.BuildTranslate(handles);
                break;
            case GizmoMode.Rotate:
                GizmoModelBuilder.BuildRotate(handles);
                break;
            case GizmoMode.Scale:
                GizmoModelBuilder.BuildScale(handles);
                break;
        }

        _handleCache[shapeKey] = handles;
        _handleSetBuildCount++;
        return handles;
    }

    /// <summary>
    /// Associates the cached handle set for <paramref name="handle"/> with <paramref name="entity"/>
    /// and, within the same call, publishes a <see cref="GizmoDrawInfo"/> component on that entity
    /// whose <see cref="GizmoDrawInfo.Handles"/> is reference-equal to the instance's cached handle
    /// set (Requirements 1.3, 2.1).
    /// </summary>
    /// <param name="entity">The entity to associate the cached gizmo with and publish the component on.</param>
    /// <param name="handle">The instance handle returned by <see cref="TryCreateGizmo"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the gizmo was associated and its component published;
    /// <see langword="false"/> when <paramref name="handle"/> is unresolvable.
    /// </returns>
    /// <remarks>
    /// An unresolvable handle — <see cref="GizmoInstanceHandle.None"/>, one that was never created, or
    /// one already removed — is rejected: no component is created and tracking state is left unchanged
    /// (Requirement 2.7). The published component references the shared cached handle set directly (no
    /// per-frame copy), plus the live <see cref="GizmoInstance.Origin"/>, the highlight overlay, and
    /// the mode/space/sizing/occlusion taken from the instance's definition.
    /// </remarks>
    public bool TrySetGizmo(Entity entity, in GizmoInstanceHandle handle)
    {
        ThrowIfDisposed();

        // Reject unresolvable handles (None, unknown, or already removed): no component, tracking
        // left unchanged (Requirement 2.7).
        if (!handle.IsValid || !_instances.TryGetValue(handle, out GizmoInstance? instance))
        {
            return false;
        }

        // Record the entity association on the instance.
        instance.Entity = entity;
        instance.HasEntity = true;

        // Publish GizmoDrawInfo referencing the cached handle set by reference (Requirements 1.3, 2.1).
        PublishDrawInfo(instance);
        return true;
    }

    /// <summary>
    /// Refreshes a tracked gizmo's live state for the current frame: updates only its origin (from
    /// <paramref name="targetTransform"/>'s translation) and the camera-derived screen sizing, then
    /// republishes its <see cref="GizmoDrawInfo"/> component. While the instance's definition is
    /// unchanged this performs zero handle-set rebuilds and keeps the published
    /// <see cref="GizmoDrawInfo.Handles"/> reference-equal to the cached set (Requirements 2.2, 2.4,
    /// 3.1, 3.2, 3.4).
    /// </summary>
    /// <param name="handle">The instance handle returned by <see cref="TryCreateGizmo"/>.</param>
    /// <param name="camera">The current camera parameters (view-projection and projection Y scale).</param>
    /// <param name="viewport">The viewport size in pixels.</param>
    /// <param name="targetTransform">The target's world transform; its translation defines the gizmo origin.</param>
    /// <remarks>
    /// A <see cref="GizmoInstanceHandle.None"/>, unknown, or already-removed handle is a safe no-op.
    /// The cached handle set is never touched here, so no handle-set collection or per-frame handle
    /// array is allocated; constant screen-size scaling is applied later by the render node using the
    /// captured camera and each gizmo's origin, matching the legacy per-frame sizing math.
    /// </remarks>
    public void UpdateInstance(in GizmoInstanceHandle handle, in CameraParams camera, Size viewport, Matrix4x4 targetTransform)
    {
        ThrowIfDisposed();

        // Safe no-op for None / unknown / already-removed handles.
        if (!handle.IsValid || !_instances.TryGetValue(handle, out GizmoInstance? instance))
        {
            return;
        }

        // Capture the live camera/viewport/target frame so screen-derived sizing (applied at draw
        // time by the render node) and drag-ray derivation use the current view. Reuses the legacy
        // origin + screen-scale inputs; no handle-set rebuild occurs here.
        _camera = camera;
        _viewport = viewport;
        _targetTransform = targetTransform;

        // Update only the live origin from the target transform. The cached handle set is left
        // untouched, so the republished Handles reference stays reference-equal to the cached set
        // (zero rebuild / zero allocation while the definition is unchanged).
        instance.Origin = targetTransform.Translation;

        // Bind the live target frame to the instance so a drag begun on it orients local-space axes
        // by this gizmo's own transform (Requirement 7.7).
        instance.TargetTransform = targetTransform;

        PublishDrawInfo(instance);
    }

    /// <summary>
    /// Changes the definition of an existing tracked gizmo. When the new definition's
    /// <see cref="GizmoShapeKey"/> differs from the current one, the manager obtains (building or
    /// reusing) the new shape's cached handle set and updates the instance's handle reference; it then
    /// republishes the <see cref="GizmoDrawInfo"/> component within the same call so the new mode,
    /// space, sizing, occlusion, and (on a shape change) handle set take effect immediately
    /// (Requirement 2.3).
    /// </summary>
    /// <param name="handle">The instance handle returned by <see cref="TryCreateGizmo"/>.</param>
    /// <param name="definition">The new definition to apply to the instance.</param>
    /// <returns>
    /// <see langword="true"/> when the instance's definition was updated and its component
    /// republished; <see langword="false"/> when <paramref name="handle"/> is unresolvable
    /// (<see cref="GizmoInstanceHandle.None"/>, unknown, or already removed) or
    /// <paramref name="definition"/> is invalid.
    /// </returns>
    public bool TryUpdateDefinition(in GizmoInstanceHandle handle, in GizmoDefinition definition)
    {
        ThrowIfDisposed();

        // Reject unresolvable handles and invalid definitions; leave tracking unchanged.
        if (!handle.IsValid || !definition.IsValid || !_instances.TryGetValue(handle, out GizmoInstance? instance))
        {
            return false;
        }

        GizmoShapeKey previousShape = instance.Definition.ShapeKey;
        instance.Definition = definition;

        // Rebuild-or-reuse the handle set only when the geometry-affecting shape key actually changes
        // (Requirement 2.3). Definitions differing only in space/target/sizing keep the cached set.
        if (!definition.ShapeKey.Equals(previousShape))
        {
            instance.Handles = GetOrBuildHandleSet(definition.ShapeKey);
        }

        // Republish within the same call so the component reflects the changed definition.
        PublishDrawInfo(instance);
        return true;
    }

    /// <summary>
    /// Removes a tracked gizmo: removes its <see cref="GizmoDrawInfo"/> component from its entity,
    /// drops its per-instance tracking entry, and stops tracking its handles for pick resolution
    /// (Requirement 2.5).
    /// </summary>
    /// <param name="handle">The instance handle returned by <see cref="TryCreateGizmo"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a tracked gizmo was removed; <see langword="false"/> when
    /// <paramref name="handle"/> is <see cref="GizmoInstanceHandle.None"/> or is not tracked (never
    /// created or already removed), in which case the call is a no-op that leaves tracking state
    /// unchanged (Requirement 2.8).
    /// </returns>
    public bool RemoveGizmo(in GizmoInstanceHandle handle)
    {
        ThrowIfDisposed();

        // No-op for None / untracked handles (Requirement 2.8).
        if (!handle.IsValid || !_instances.TryGetValue(handle, out GizmoInstance? instance))
        {
            return false;
        }

        // Remove the published component and drop the owning-entity pick-resolution tracking entry.
        if (instance.HasEntity)
        {
            instance.Entity.Remove<GizmoDrawInfo>();
            _tracked.Remove((uint)instance.Entity.Id);
        }

        _instances.Remove(handle);
        return true;
    }

    /// <summary>
    /// Sets or clears the highlighted handle for a specific tracked gizmo and refreshes its published
    /// component. Only the per-instance <see cref="GizmoInstance.Highlighted"/> overlay changes; the
    /// shared cached handle set is referenced unchanged, so no new handle-set collections or per-frame
    /// handle snapshots are allocated (Requirements 3.3, 7.3, 7.4).
    /// </summary>
    /// <param name="handle">The instance handle returned by <see cref="TryCreateGizmo"/>.</param>
    /// <param name="highlighted">The handle to highlight, or <see langword="null"/> to clear the highlight.</param>
    /// <remarks>
    /// A <see cref="GizmoInstanceHandle.None"/>, unknown, or already-removed handle is a safe no-op.
    /// </remarks>
    public void SetHighlight(in GizmoInstanceHandle handle, GizmoHandleId? highlighted)
    {
        ThrowIfDisposed();

        // Safe no-op for None / unknown / already-removed handles.
        if (!handle.IsValid || !_instances.TryGetValue(handle, out GizmoInstance? instance))
        {
            return;
        }

        // Mutate only the per-instance highlight overlay, then refresh the component. The published
        // Handles reference stays the cached set, so this allocates no new collections or snapshots.
        instance.Highlighted = highlighted;
        PublishDrawInfo(instance);
    }

    /// <summary>
    /// Sets/updates the <see cref="GizmoDrawInfo"/> component on <paramref name="instance"/>'s entity
    /// from its cached handle set, live origin, highlight overlay, and its definition's mode, space,
    /// sizing, and occlusion, then keeps the owning-entity handle tracking in sync. The component's
    /// <see cref="GizmoDrawInfo.Handles"/> references the cached set directly, so no per-frame handle
    /// array is allocated.
    /// </summary>
    /// <param name="instance">The instance whose component to publish.</param>
    /// <remarks>
    /// Populating the shared <c>owningEntityId -&gt; handle-id set</c> tracking here — from this
    /// instance's live cached handle set — is what lets <see cref="TryResolvePick"/> /
    /// <see cref="TryResolveHandle"/> resolve a decoded pick to exactly one tracked gizmo among many,
    /// so a pick/drag routed through the resolution is isolated to that single gizmo (Requirement 7.2).
    /// </remarks>
    private void PublishDrawInfo(GizmoInstance instance)
    {
        if (!instance.HasEntity)
        {
            return;
        }

        GizmoDefinition definition = instance.Definition;
        var info = new GizmoDrawInfo
        {
            Mode = definition.Mode,
            Space = definition.Space,
            Origin = instance.Origin,
            OcclusionMode = definition.Handles.OcclusionMode,
            DesiredPixelSize = definition.Handles.DesiredPixelSize,
            Handles = instance.Handles,
            HighlightedHandle = instance.Highlighted,
        };
        instance.Entity.Set(ref info);

        // Keep the owning-entity -> handle-id set tracking in sync from this instance's cached handle
        // set so multi-gizmo pick resolution maps a decoded (owning entity, handle) back to exactly
        // this gizmo (Requirement 7.2).
        TrackInstance(instance);
    }

    /// <summary>
    /// Refreshes the shared <c>owningEntityId -&gt; handle-id set</c> tracking entry for
    /// <paramref name="instance"/> from its live cached handle set, replacing any prior handle ids for
    /// that owning entity. This mirrors the live instances into the <c>_tracked</c> map that
    /// <see cref="TryResolvePick"/> / <see cref="TryResolveHandle"/> consult so pick resolution
    /// isolates to a single gizmo (Requirement 7.2).
    /// </summary>
    /// <param name="instance">The instance whose handle set to mirror into tracking.</param>
    private void TrackInstance(GizmoInstance instance)
    {
        if (!instance.HasEntity)
        {
            return;
        }

        uint owningId = (uint)instance.Entity.Id;
        if (!_tracked.TryGetValue(owningId, out HashSet<GizmoHandleId>? handleIds))
        {
            handleIds = [];
            _tracked[owningId] = handleIds;
        }

        handleIds.Clear();
        foreach (GizmoHandle handle in instance.Handles)
        {
            handleIds.Add(handle.Id);
        }
    }

    /// <summary>
    /// Finds the tracked instance whose carrier entity id equals <paramref name="owningEntityId"/>, so
    /// a decoded pick resolved to an owning entity can be bound to that gizmo's per-instance frame
    /// (origin, space, target transform) for drag manipulation.
    /// </summary>
    /// <param name="owningEntityId">The owning entity id carried in a decoded gizmo pick.</param>
    /// <param name="instance">On success, the instance carried by that entity; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a matching tracked instance was found; otherwise <see langword="false"/>.</returns>
    private bool TryGetInstanceByOwningEntity(uint owningEntityId, out GizmoInstance? instance)
    {
        foreach (GizmoInstance candidate in _instances.Values)
        {
            if (candidate.HasEntity && (uint)candidate.Entity.Id == owningEntityId)
            {
                instance = candidate;
                return true;
            }
        }

        instance = null;
        return false;
    }

    /// <summary>
    /// Per-instance state the factory tracks for a created gizmo: the originating definition, the
    /// cached handle set the instance references, the carrier entity it was set on, the live gizmo
    /// origin, and the per-instance highlight overlay.
    /// </summary>
    private sealed class GizmoInstance
    {
        /// <summary>The definition this instance was created from.</summary>
        public GizmoDefinition Definition;

        /// <summary>The cached handle set (a reference into <see cref="_handleCache"/>) this instance uses.</summary>
        public IReadOnlyList<GizmoHandle> Handles = [];

        /// <summary>The entity this instance's <see cref="GizmoDrawInfo"/> component is published on.</summary>
        public Entity Entity = Entity.Null;

        /// <summary>Whether this instance has been associated with an entity via <see cref="TrySetGizmo"/>.</summary>
        public bool HasEntity;

        /// <summary>The gizmo origin (target pivot) in world space, published on the component.</summary>
        public Vector3 Origin;

        /// <summary>
        /// The target's world transform captured on the most recent set/update. Bound to an active
        /// drag begun on this instance so local-space axes are oriented by this gizmo's own frame,
        /// keeping the drag isolated to the originating gizmo (Requirement 7.7).
        /// </summary>
        public Matrix4x4 TargetTransform = Matrix4x4.Identity;

        /// <summary>The handle to render highlighted for this instance, or <see langword="null"/> when none.</summary>
        public GizmoHandleId? Highlighted;
    }
}
