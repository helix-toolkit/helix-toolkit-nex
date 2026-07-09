using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Engine hosting of the gizmo service (<see cref="GizmoManager"/>).
/// </summary>
/// <remarks>
/// <para>
/// This partial adds the engine-layer hosting for the gizmo manager that otherwise lives entirely in
/// <c>HelixToolkit.Nex.Rendering</c>, preserving the one-way assembly reference (the Engine layer
/// references Rendering, never the reverse — Requirement 5). The <see cref="Gizmos"/> accessor
/// lazily creates a single <see cref="GizmoManager"/> and returns that same instance for the engine's
/// lifetime (Requirement 4.1). On first creation it registers a <see cref="GizmoRenderNode"/> so
/// gizmos created through the service render without the application wiring the node manually
/// (Requirement 4.6).
/// </para>
/// <para>
/// When the engine is disposed (or torn down) the service is disposed, which stops tracking every
/// gizmo it owned (Requirement 4.2). After disposal the accessor and any service operation throw
/// <see cref="ObjectDisposedException"/> (Requirement 4.3).
/// </para>
/// </remarks>
public partial class Engine
{
    /// <summary>
    /// The single lazily-created gizmo service instance, cached for the engine's lifetime. Remains
    /// <see langword="null"/> until <see cref="Gizmos"/> is first accessed.
    /// </summary>
    private GizmoManager? _gizmoService;

    private GizmoPickRouter? _gizmoRouter;
    /// <summary>
    /// Whether a <see cref="GizmoRenderNode"/> has been registered for the gizmo service, so the
    /// lazy registration runs at most once (idempotent, Requirement 4.6).
    /// </summary>
    private bool _gizmoNodeRegistered;

    public event EventHandler<PickingResponse>? OnViewportHovering;

    /// <summary>
    /// Gets the engine's gizmo service. The first access lazily creates a single
    /// <see cref="GizmoManager"/> and registers a <see cref="GizmoRenderNode"/> to render gizmos
    /// created through it; every subsequent access returns that same instance for the engine's
    /// lifetime (Requirements 4.1, 4.5, 4.6).
    /// </summary>
    /// <exception cref="ObjectDisposedException">The engine has been disposed (Requirement 4.3).</exception>
    public GizmoManager Gizmos
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            if (_gizmoService is null)
            {
                _gizmoService = new GizmoManager();
                _gizmoRouter = new GizmoPickRouter(_gizmoService);
                // On the first gizmo service creation, auto-register the render node so gizmos
                // created through the service are rendered without manual wiring (Requirement 4.6).
                EnsureGizmoNodeRegistered();
            }

            return _gizmoService;
        }
    }

    /// <summary>
    /// Registers a <see cref="GizmoRenderNode"/> for the gizmo service if one is not already present.
    /// Idempotent: guarded by <see cref="_gizmoNodeRegistered"/> and a lookup so a node added manually
    /// (for example via the engine builder's <c>WithGizmos</c>) is never duplicated (Requirement 4.6).
    /// </summary>
    private void EnsureGizmoNodeRegistered()
    {
        if (_gizmoNodeRegistered)
        {
            return;
        }

        if (GetRenderNode<GizmoRenderNode>() is null)
        {
            AddNode(new GizmoRenderNode());
        }

        _gizmoNodeRegistered = true;
    }

    /// <summary>
    /// Disposes the gizmo service during engine teardown, stopping tracking of every gizmo it owned
    /// (Requirement 4.2). Safe when the service was never created. After this runs the service is
    /// disposed, so the <see cref="Gizmos"/> accessor and any retained service reference reject
    /// further use (Requirement 4.3).
    /// </summary>
    private void TeardownGizmoService()
    {
        Disposer.DisposeAndRemove(ref _gizmoService);
    }

    private bool CreateHoverHighlightRequest(RenderContext context)
    {
        if (
            _gizmoService is null
            || !_gizmoService.HasGizmoInstance
            || !context.PointerValid
            || _gizmoService.IsDragging
        )
        {
            return false;
        }
        CreatePickingRequest(
            context,
            context.Pointer,
            HandleHoverResponse
        );
        return true;
    }

    private void HandleHoverResponse(PickingResponse response)
    {
        if (_gizmoService is null || !_gizmoService.HasGizmoInstance)
        {
            return;
        }
        var decoded = Utils.UnpackEntityId(response.Data);
        Gizmos.ResolveHighlight(
            decoded.Kind == EntityIdPickKind.Gizmo,
            decoded.OwningEntityId,
            decoded.Handle
        );
        OnViewportHovering?.Invoke(this, response);
    }
}
