using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Owns the set of active gizmos, builds per-frame handle geometry, and translates picking results
/// and pointer motion into transform edits on the target.
/// </summary>
/// <remarks>
/// <para>
/// This is the interaction layer, deliberately decoupled from the renderer. In the component-driven
/// model the manager describes its gizmo as data: each <see cref="Update"/> sets/updates a
/// <see cref="GizmoDrawInfo"/> component on the managed entity (see <see cref="AttachEntity"/>) that
/// the <see cref="GizmoRenderNode"/> gathers and draws, instead of the manager feeding the node a
/// per-frame handle list out of band.
/// </para>
/// <para>
/// The type is declared <see langword="partial"/> so that the successive workflow tasks extend it
/// without churn: this file provides the core state, <see cref="Update"/> (rebuild handles and
/// publish the managed <see cref="GizmoDrawInfo"/>), and the owning-entity handle tracking used by
/// pick resolution; draw emission, picking resolution, and drag manipulation are added by later
/// tasks.
/// </para>
/// </remarks>
public sealed partial class GizmoManager
{
    /// <summary>Computes the constant on-screen sizing factor for handles.</summary>
    private readonly GizmoScreenScale _screenScale;

    /// <summary>The handles generated for the current <see cref="Mode"/> during the last <see cref="Update"/>.</summary>
    private readonly List<GizmoHandle> _handles = [];

    /// <summary>
    /// Reverse map from a decoded gizmo pick's entity id to a handle's stable per-frame identity.
    /// Repopulated by the component-driven pick-resolution migration; kept here so the picking
    /// lookups compile while that migration lands.
    /// </summary>
    private readonly Dictionary<uint, GizmoHandleId> _entityIdToHandle = [];

    /// <summary>
    /// Owning entity id -&gt; the set of <see cref="GizmoHandleId"/> values that gizmo currently
    /// exposes. Kept in sync as gizmos are created, updated, and removed so pick resolution can map a
    /// decoded <em>(owning entity id, handle id)</em> back to a tracked gizmo (Requirement 6.1).
    /// </summary>
    private readonly Dictionary<uint, HashSet<GizmoHandleId>> _tracked = [];

    /// <summary>The entity this manager sets/updates a <see cref="GizmoDrawInfo"/> component on.</summary>
    private Entity _managedEntity = Entity.Null;

    /// <summary>Whether a managed entity has been attached via <see cref="AttachEntity"/>.</summary>
    private bool _hasManagedEntity;

    /// <summary>The camera captured during the last <see cref="Update"/>, used by <see cref="ScreenScaleAt"/>.</summary>
    private CameraParams _camera = CameraParams.Identity;

    /// <summary>The viewport captured during the last <see cref="Update"/>.</summary>
    private Size _viewport = Size.Empty;

    /// <summary>The target transform captured during the last <see cref="Update"/>.</summary>
    private Matrix4x4 _targetTransform = Matrix4x4.Identity;

    /// <summary>The gizmo origin (target pivot) in world space, taken from the target transform translation.</summary>
    private Vector3 _gizmoOrigin = Vector3.Zero;

    /// <summary>Whether a target has been supplied via <see cref="Update"/> so a gizmo is active.</summary>
    private bool _hasTarget;

    // --- Drag state -------------------------------------------------------------------------
    // The fields backing the drag lifecycle are declared here so the shared state lives in one
    // place; the BeginDrag / UpdateDrag / EndDrag methods that operate on them are implemented by
    // the drag-manipulation task.

    /// <summary>Whether a drag is currently active.</summary>
    private bool _isDragging;

    /// <summary>The identity of the handle being dragged while <see cref="_isDragging"/> is set.</summary>
    private GizmoHandleId _dragHandle;

    /// <summary>
    /// The owning entity id of the gizmo the active drag began on. An active drag stays bound to this
    /// single gizmo + <see cref="_dragHandle"/> so it leaves every other tracked gizmo unaffected
    /// (Requirements 7.5, 10.1).
    /// </summary>
    private uint _dragOwningEntityId;

    /// <summary>The world-space axis the active drag is constrained to.</summary>
    private Vector3 _dragAxisWorld = Vector3.UnitX;

    /// <summary>The world-space point captured as the drag start / most recent drag position.</summary>
    private Vector3 _dragStartPoint = Vector3.Zero;

    /// <summary>
    /// The gizmo origin captured when the active drag began. Captured per-drag so the drag stays
    /// anchored to the originating gizmo's frame even if <see cref="Update"/> subsequently changes the
    /// manager's current state (Requirement 6.6).
    /// </summary>
    private Vector3 _dragOrigin = Vector3.Zero;

    /// <summary>The reference frame captured when the active drag began.</summary>
    private GizmoSpace _dragSpace = GizmoSpace.World;

    /// <summary>The target transform captured when the active drag began (orients local-space axes).</summary>
    private Matrix4x4 _dragTargetTransform = Matrix4x4.Identity;

    /// <summary>
    /// Initializes a new instance using the default desired handle pixel size
    /// (<see cref="GizmoScreenScale.DefaultDesiredPixelSize"/>, 100 pixels).
    /// </summary>
    public GizmoManager()
    {
        _screenScale = new GizmoScreenScale();
    }

    /// <summary>
    /// Initializes a new instance with an explicit desired handle pixel size.
    /// </summary>
    /// <param name="desiredPixelSize">
    /// The desired on-screen handle size in pixels; clamped to
    /// <c>[<see cref="GizmoScreenScale.MinDesiredPixelSize"/>, <see cref="GizmoScreenScale.MaxDesiredPixelSize"/>]</c>.
    /// </param>
    public GizmoManager(float desiredPixelSize)
    {
        _screenScale = new GizmoScreenScale(desiredPixelSize);
    }

    /// <summary>Gets or sets the transform-manipulation mode the gizmo represents.</summary>
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    /// <summary>Gets or sets the reference frame the gizmo operates in.</summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>
    /// Gets or sets whether the managed gizmo's handles draw always-on-top (X-ray) or respect scene
    /// depth. Published on the <see cref="GizmoDrawInfo"/> component so the render node draws each
    /// gizmo with its own occlusion mode. Defaults to <see cref="GizmoOcclusionMode.AlwaysOnTop"/> so
    /// manipulator handles stay grabbable even when behind geometry.
    /// </summary>
    public GizmoOcclusionMode OcclusionMode { get; set; } = GizmoOcclusionMode.AlwaysOnTop;

    /// <summary>
    /// Gets or sets the desired on-screen handle size in pixels. Assigned values are clamped to
    /// <c>[1, 1000]</c>; a default of 100 pixels applies when none is configured (Requirement 2.2).
    /// </summary>
    public float DesiredPixelSize
    {
        get => _screenScale.DesiredPixelSize;
        set => _screenScale.DesiredPixelSize = value;
    }

    /// <summary>Gets a value indicating whether a drag is currently active.</summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Gets the owning entity id of the gizmo the active drag is bound to, or <c>0</c> when no drag is
    /// active. While a drag is active this identifies the single originating gizmo the drag manipulates
    /// (Requirements 7.5, 10.1).
    /// </summary>
    public uint DragOwningEntityId => _isDragging ? _dragOwningEntityId : 0u;

    /// <summary>Gets the gizmo origin (target pivot) in world space from the last <see cref="Update"/>.</summary>
    public Vector3 GizmoOrigin => _gizmoOrigin;

    /// <summary>Gets the target transform captured during the last <see cref="Update"/>.</summary>
    public Matrix4x4 TargetTransform => _targetTransform;

    /// <summary>Gets a value indicating whether at least one handle is active for the current frame.</summary>
    public bool HasActiveHandles => _hasTarget && _handles.Count > 0;

    /// <summary>Gets the handles generated for the current frame (empty when no gizmo is active).</summary>
    public IReadOnlyList<GizmoHandle> Handles => _handles;

    /// <summary>
    /// Gets the entity this manager sets/updates a <see cref="GizmoDrawInfo"/> component on, or
    /// <see cref="Entity.Null"/> when no entity is attached.
    /// </summary>
    public Entity ManagedEntity => _managedEntity;

    /// <summary>Gets a value indicating whether an entity is currently managed by this manager.</summary>
    public bool HasManagedEntity => _hasManagedEntity;

    /// <summary>
    /// Gets the owning entity id -&gt; handle-id set tracking used by pick resolution. Exposed for
    /// consumers (and tests) that need to observe which gizmos and handles are currently tracked.
    /// </summary>
    public IReadOnlyDictionary<uint, HashSet<GizmoHandleId>> Tracked => _tracked;

    /// <summary>
    /// Attaches the entity this manager describes its gizmo on. Subsequent <see cref="Update"/> calls
    /// set/update a <see cref="GizmoDrawInfo"/> component on <paramref name="entity"/> and keep the
    /// owning-entity handle tracking in sync (Requirements 6.1, 10.2).
    /// </summary>
    /// <param name="entity">The entity to describe the managed gizmo on.</param>
    /// <remarks>
    /// Re-targeting to a different entity first releases the previously managed entity (removing its
    /// component and tracking entry) so tracking never retains a stale gizmo. If a handle set has
    /// already been built, the component is published immediately so an active gizmo is represented
    /// without waiting for the next <see cref="Update"/>.
    /// </remarks>
    public void AttachEntity(Entity entity)
    {
        if (_hasManagedEntity && !_managedEntity.Equals(entity))
        {
            DetachEntity();
        }

        _managedEntity = entity;
        _hasManagedEntity = true;

        // Publish the current handle set immediately (creation path) so an already-active gizmo is
        // represented without waiting for the next Update.
        SyncManagedComponent();
    }

    /// <summary>
    /// Releases the managed entity: removes its <see cref="GizmoDrawInfo"/> component and stops
    /// tracking its handles (removal path). Safe to call when no entity is attached.
    /// </summary>
    public void DetachEntity()
    {
        if (!_hasManagedEntity)
        {
            return;
        }

        uint owningId = (uint)_managedEntity.Id;
        _managedEntity.Remove<GizmoDrawInfo>();
        _tracked.Remove(owningId);

        _managedEntity = Entity.Null;
        _hasManagedEntity = false;
    }

    /// <summary>
    /// Rebuilds the current frame's handle geometry for the active <see cref="Mode"/> and refreshes
    /// the reverse <c>entityId -&gt; GizmoHandleId</c> picking map.
    /// </summary>
    /// <param name="camera">The current camera parameters (supplies the view-projection and projection Y scale).</param>
    /// <param name="viewport">The viewport size in pixels.</param>
    /// <param name="targetTransform">The target's world transform; its translation defines the gizmo origin.</param>
    /// <remarks>
    /// Handle geometry is expressed in gizmo-local space; constant screen-size scaling is applied later
    /// via <see cref="ScreenScaleAt"/> in the vertex shader and is not baked into the emitted geometry.
    /// </remarks>
    public void Update(in CameraParams camera, Size viewport, Matrix4x4 targetTransform)
    {
        _camera = camera;
        _viewport = viewport;
        _targetTransform = targetTransform;
        _gizmoOrigin = targetTransform.Translation;
        _hasTarget = true;

        RebuildHandles();
    }

    /// <summary>
    /// Computes the world units per desired-pixel at a world-space origin using the camera and
    /// viewport captured during the last <see cref="Update"/>, so handles keep a constant pixel size.
    /// </summary>
    /// <param name="originWorld">The world-space position to size a handle at.</param>
    /// <returns>The world size that spans <see cref="DesiredPixelSize"/> pixels at <paramref name="originWorld"/>.</returns>
    public float ScreenScaleAt(Vector3 originWorld)
        => _screenScale.ScreenScaleAt(originWorld, _camera, _viewport.Height);

    /// <summary>
    /// Regenerates <see cref="_handles"/> for the active <see cref="Mode"/> using
    /// <see cref="GizmoModelBuilder"/>, then publishes the handle set as a <see cref="GizmoDrawInfo"/>
    /// component on the managed entity and refreshes the owning-entity handle tracking. Each handle's
    /// identity is its <see cref="GizmoHandleId"/> (mode + axis), distinct within the gizmo by
    /// construction; no reserved-band entity id is allocated.
    /// </summary>
    private void RebuildHandles()
    {
        _handles.Clear();
        _entityIdToHandle.Clear();

        switch (Mode)
        {
            case GizmoMode.Translate:
                GizmoModelBuilder.BuildTranslate(_handles);
                break;
            case GizmoMode.Rotate:
                GizmoModelBuilder.BuildRotate(_handles);
                break;
            case GizmoMode.Scale:
                GizmoModelBuilder.BuildScale(_handles);
                break;
        }

        // Publish the freshly built handle set as component data and keep tracking in sync so the
        // render node draws from the component and pick resolution can map back to this gizmo.
        SyncManagedComponent();
    }

    /// <summary>
    /// Sets/updates the <see cref="GizmoDrawInfo"/> component on the managed entity from the current
    /// mode, space, origin, occlusion, desired pixel size, and handle set, and keeps the
    /// <c>owningEntityId -&gt; handle-id set</c> tracking in sync (Requirements 6.1, 10.2). A no-op
    /// when no entity is attached.
    /// </summary>
    /// <remarks>
    /// A gizmo with no drawable handles is not represented: its component is removed and its tracking
    /// entry dropped (removal path), so gathering and pick resolution never observe an empty gizmo.
    /// Otherwise the handle set is snapshotted so the published component does not alias the manager's
    /// mutable per-frame <see cref="_handles"/> list.
    /// </remarks>
    private void SyncManagedComponent()
    {
        if (!_hasManagedEntity)
        {
            return;
        }

        uint owningId = (uint)_managedEntity.Id;

        // Removal path: a gizmo with no drawable handles is dropped entirely.
        if (_handles.Count == 0)
        {
            _managedEntity.Remove<GizmoDrawInfo>();
            _tracked.Remove(owningId);
            return;
        }

        // Snapshot so the component does not alias the mutable per-frame handle list. The hovered
        // handle and the handle being dragged are published with the highlight color baked in, so the
        // pure-consumer render node draws them highlighted straight from the component (it no longer
        // calls ResolveDrawColor). Presentation-only: handle identity, geometry, and picking are
        // unchanged.
        GizmoHandle[] handles = new GizmoHandle[_handles.Count];
        for (int i = 0; i < _handles.Count; i++)
        {
            GizmoHandle handle = _handles[i];
            handles[i] = handle with { Color = ResolveDrawColor(in handle) };
        }

        var info = new GizmoDrawInfo
        {
            Mode = Mode,
            Space = Space,
            Origin = _gizmoOrigin,
            OcclusionMode = OcclusionMode,
            DesiredPixelSize = DesiredPixelSize,
            Handles = handles,
        };
        _managedEntity.Set(ref info);

        // Create-or-update the tracking entry, replacing any prior handle ids for this gizmo.
        if (!_tracked.TryGetValue(owningId, out HashSet<GizmoHandleId>? handleIds))
        {
            handleIds = [];
            _tracked[owningId] = handleIds;
        }

        handleIds.Clear();
        foreach (GizmoHandle handle in handles)
        {
            handleIds.Add(handle.Id);
        }
    }
}
