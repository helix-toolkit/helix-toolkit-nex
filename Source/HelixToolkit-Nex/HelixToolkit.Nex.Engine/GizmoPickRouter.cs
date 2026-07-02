using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Engine-layer router that intercepts asynchronous picking results, decodes them with the unified
/// <see cref="Utils.UnpackEntityId(ulong)"/> decode, resolves gizmo picks against the engine's
/// <see cref="GizmoManager"/> service, and dispatches a begin/continue drag — all before the
/// application-supplied picking callback observes the result (Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// This type lives in <c>HelixToolkit.Nex.Engine</c> because it needs engine-layer types
/// (<see cref="PickingResponse"/>, <see cref="RenderContext"/>, <see cref="EntityIdDecodeResult"/>)
/// that the rendering layer must not reference. Placing the cross-layer routing here preserves the
/// one-way assembly reference (Requirements 5.1–5.3).
/// </para>
/// <para>
/// All routing work in <see cref="TryRoute(in PickingResponse)"/> is wrapped in a try/catch that logs
/// and swallows, so nothing — not a resolution miss, not a failed unprojection, not a dispatch error —
/// ever propagates to the application picking callback (Requirements 6.3, 6.4). Scene picks, no-hits,
/// unresolved gizmo picks, and disabled routing all pass the result through to the application
/// unchanged (Requirements 6.2, 6.4, 6.7).
/// </para>
/// </remarks>
public sealed class GizmoPickRouter
{
    private static readonly ILogger _logger = LogManager.Create<GizmoPickRouter>();

    private readonly GizmoManager _service;

    /// <summary>
    /// Creates a router over the given gizmo <paramref name="service"/>.
    /// </summary>
    /// <param name="service">The engine's gizmo service the router resolves and dispatches picks against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is <see langword="null"/>.</exception>
    public GizmoPickRouter(GizmoManager service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>
    /// Wraps an application-supplied picking callback so decoded gizmo picks are routed to the gizmo
    /// service before the application observes the result.
    /// </summary>
    /// <param name="appCallback">The application picking callback to deliver the result to.</param>
    /// <param name="routeGizmoPicks">
    /// When <see langword="false"/>, routing is disabled and <paramref name="appCallback"/> is returned
    /// unchanged so no decode or dispatch occurs (Requirement 6.7). When <see langword="true"/>, the
    /// returned wrapper runs <see cref="TryRoute(in PickingResponse)"/> first, then always invokes
    /// <paramref name="appCallback"/> with the unmodified result (Requirement 6.6).
    /// </param>
    /// <returns>
    /// The callback to register for the picking request: either <paramref name="appCallback"/>
    /// unchanged (routing disabled) or a routing wrapper (routing enabled).
    /// </returns>
    public Action<PickingResponse> Wrap(Action<PickingResponse> appCallback, bool routeGizmoPicks)
    {
        // Routing disabled: pass the application callback through untouched. No decode or dispatch
        // occurs and the result reaches the application unchanged (Requirement 6.7).
        if (!routeGizmoPicks)
        {
            return appCallback;
        }

        // Routing enabled: route gizmo picks first, then always deliver the unmodified result to the
        // application callback (Requirement 6.6). TryRoute never throws, so the application callback is
        // always invoked regardless of routing outcome (Requirements 6.3, 6.4).
        return response =>
        {
            TryRoute(in response);
            appCallback?.Invoke(response);
        };
    }

    /// <summary>
    /// Decodes, resolves, and dispatches a single picking result. Only gizmo picks that resolve to a
    /// gizmo the service tracks are dispatched to a begin/continue drag; every other case performs no
    /// dispatch. Never throws (Requirements 6.1–6.5).
    /// </summary>
    /// <param name="response">The delivered picking result to route.</param>
    /// <returns>
    /// <see langword="true"/> if and only if a gizmo pick was resolved and dispatched to the service;
    /// otherwise <see langword="false"/> (scene pick, no-hit, unresolved gizmo pick, or a failure that
    /// was logged and swallowed).
    /// </returns>
    public bool TryRoute(in PickingResponse response)
    {
        try
        {
            // Decode the shared entity-id pixel. Scene picks and no-hits are not gizmo picks and pass
            // through unchanged (Requirements 6.2, 6.4).
            EntityIdDecodeResult decoded = Utils.UnpackEntityId(response.Data);
            if (decoded.Kind != EntityIdPickKind.Gizmo)
            {
                return false;
            }

            // Resolve the gizmo pick against the tracked gizmos. An unresolved gizmo pick (untracked
            // owning entity or unexposed handle) does nothing (Requirement 6.3).
            if (!_service.TryResolvePick(in decoded, out GizmoPickResolution resolution))
            {
                return false;
            }

            // Derive the world ray from the pick's screen coordinate so the service can begin/continue
            // the drag (Requirement 6.5). A failed unprojection must not throw out — treat it as a
            // non-dispatch (Requirements 6.3, 6.4).
            if (!response.Context.TryUnProject(response.Coord.X, response.Coord.Y, out Ray ray))
            {
                return false;
            }

            // Begin or continue the drag on the resolved handle (Requirement 6.1). When a drag is
            // already active, continue it with the new pointer ray; otherwise begin a new drag bound to
            // the resolved gizmo + handle.
            if (_service.IsDragging)
            {
                _service.UpdateDrag(in ray, out _);
            }
            else
            {
                _service.BeginDrag(in resolution, in ray);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Swallow and log: nothing may propagate to the application picking callback
            // (Requirements 6.3, 6.4).
            _logger.LogError(
                ex,
                "Gizmo pick routing failed for picking request {RequestId}. The failure is swallowed so the application picking callback still receives the unmodified result.",
                response.RequestId
            );
            return false;
        }
    }
}
