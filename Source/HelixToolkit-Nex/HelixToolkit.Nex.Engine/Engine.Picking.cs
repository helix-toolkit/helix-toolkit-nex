namespace HelixToolkit.Nex.Engine;

public partial class Engine
{
    public uint CreatePickingRequest(
        RenderContext context,
        Vector2 coord,
        Action<PickingResponse> responseCallback
    )
    {
        return CreatePickingRequestCore(context, coord, responseCallback, routeGizmoPicks: false);
    }

    /// <summary>
    /// Registers a pending picking readback keyed by request id. Shared by both public overloads.
    /// The gizmo-routing decision is carried on the pending request (<see cref="PendingPicking.RouteGizmoPicks"/>)
    /// rather than by wrapping <paramref name="responseCallback"/> in a closure, so no per-call
    /// delegate is allocated for routing — the delivery loop routes inline before invoking the callback.
    /// </summary>
    private uint CreatePickingRequestCore(
        RenderContext context,
        Vector2 coord,
        Action<PickingResponse> responseCallback,
        bool routeGizmoPicks
    )
    {
        // Reject out-of-bounds picks early; otherwise the request can be accepted but never copied,
        // leaving a non-deliverable pending request in _pendingPickings.
        if (
            coord.X < 0
            || coord.Y < 0
            || coord.X >= context.WindowSize.Width
            || coord.Y >= context.WindowSize.Height
        )
        {
            return PickingContext.InvalidRequestId;
        }

        var requestId = context.SendPicking(coord);
        // Overflow: the per-frame picking capacity is full. SendPicking returns the sentinel and no
        // request slot was assigned, so do not register a pending readback — just propagate the
        // sentinel to the caller (Requirement 5.4).
        if (requestId == PickingContext.InvalidRequestId)
        {
            return requestId;
        }
        // Key the pending picking by the request id (consistent with the staging buffer). The copy
        // submit handle is filled in later, when the copy is actually submitted (see Submit), which
        // is what BeginFrame waits on before readback.
        _pendingPickings[requestId] = new PendingPicking
        {
            Context = context,
            Coord = coord,
            Id = requestId,
            Callback = responseCallback,
            RouteGizmoPicks = routeGizmoPicks,
        };
        return requestId;
    }

    /// <summary>
    /// Creates an asynchronous picking request that optionally routes decoded gizmo picks to the
    /// engine's gizmo service (<see cref="Gizmos"/>) automatically before the application callback
    /// observes the result (Requirement 6).
    /// </summary>
    /// <param name="context">The render context the pick is issued against.</param>
    /// <param name="coord">The pick's screen coordinate (viewport-relative pixels).</param>
    /// <param name="responseCallback">The application callback invoked when the result is delivered.</param>
    /// <param name="routeGizmoPicks">
    /// When <see langword="true"/>, a <see cref="GizmoPickRouter"/> over <see cref="Gizmos"/> routes
    /// decoded gizmo picks to the gizmo service (begin/continue drag) before the application callback
    /// runs (Requirement 6.6). Scene picks, no-hits, and unresolved gizmo picks pass through unchanged.
    /// When <see langword="false"/>, this behaves exactly like
    /// <see cref="CreatePickingRequest(RenderContext, Vector2, Action{PickingResponse})"/> — no gizmo
    /// pick is dispatched and the result reaches the application unchanged (Requirement 6.7).
    /// </param>
    /// <returns>The request id, or the overflow sentinel when the per-frame picking capacity is full.</returns>
    public uint CreatePickingRequest(
        RenderContext context,
        Vector2 coord,
        Action<PickingResponse> responseCallback,
        bool routeGizmoPicks
    )
    {
        // Only route when routing is requested and a gizmo service with a live instance and a router
        // exist (Requirement 6.7). The decision is carried on the pending request; the delivery loop
        // routes inline via _gizmoRouter.TryRoute before invoking the callback, so no wrapper delegate
        // is allocated per call.
        var shouldRoute =
            routeGizmoPicks
            && _gizmoService is not null
            && _gizmoRouter is not null
            && _gizmoService.HasGizmoInstance;

        return CreatePickingRequestCore(context, coord, responseCallback, shouldRoute);
    }

    private void ProcessPickingResults()
    {
        // (b) Picking readback: for any pending picking whose copy has actually been submitted,
        // deliver its result without stalling the CPU. Rather than blocking on the copy's submit
        // handle (which is only one frame old and likely still executing on the GPU), poll the
        // handle with a non-blocking IsReady check. If the copy has not completed yet, leave the
        // pending picking in place and retry on a later BeginFrame; this defers delivery by a
        // frame or two on slow GPUs instead of stalling. Keyed by request id, consistent with the
        // staging buffer, so the polled handle, the buffer slot, and the request all refer to the
        // same picking request. The handle's fence is still reset through normal frame-pacing
        // reuse, so polling without an eventual blocking Wait is safe.
        foreach (var (requestId, pending) in _pendingPickings.AsValueEnumerable())
        {
            if (!pending.IsReady)
            {
                continue;
            }
            if (!Context.IsReady(pending.CopySubmitHandle))
            {
                // Copy not finished on the GPU yet — keep the request pending and try next frame.
                continue;
            }
            // Deliver this request's result. Isolate each delivery so a callback (or readback)
            // that throws for one request is caught and logged, and the remaining pending
            // requests in this frame are still delivered. The entry is marked delivered either
            // way so it is not retried indefinitely.
            try
            {
                var pickingResult = pending.Context!.PickingContext.ReadResult(pending.Id);
                var response = new PickingResponse
                {
                    Context = pending.Context!,
                    Coord = pending.Coord,
                    Data = pickingResult,
                    RequestId = pending.Id,
                };
                // Route decoded gizmo picks to the gizmo service first when requested, then always
                // deliver the unmodified result to the application callback (Requirement 6.6).
                // TryRoute never throws, so the callback still runs regardless of routing outcome
                // (Requirements 6.3, 6.4).
                if (pending.RouteGizmoPicks)
                {
                    _gizmoRouter?.TryRoute(in response);
                }
                pending.Callback?.Invoke(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Picking request {RequestId} failed during readback delivery. The request is discarded and remaining pending requests continue to be delivered.",
                    pending.Id
                );
            }
            _deliveredThisFrame.Add(requestId);
        }
        foreach (var requestId in _deliveredThisFrame.AsValueEnumerable())
        {
            _pendingPickings.Remove(requestId);
        }
        _deliveredThisFrame.Clear();
    }

    private readonly struct PendingPicking
    {
        public RenderContext? Context { get; init; }
        public Vector2 Coord { get; init; }
        public uint Id { get; init; }
        public Action<PickingResponse>? Callback { get; init; }

        /// <summary>
        /// When <c>true</c>, the delivery loop routes the decoded result through the engine's
        /// <see cref="GizmoPickRouter"/> (begin/continue gizmo drag) before invoking
        /// <see cref="Callback"/>. Carried on the request instead of wrapping the callback in a
        /// closure, so <c>CreatePickingRequest</c> allocates no per-call delegate for routing.
        /// </summary>
        public bool RouteGizmoPicks { get; init; }

        /// <summary>
        /// The submit handle under which this request's <c>CopyTextureToBuffer</c> was submitted.
        /// Only meaningful once <see cref="HasSubmitHandle"/> is <c>true</c>.
        /// </summary>
        public SubmitHandle CopySubmitHandle { get; init; }

        /// <summary>
        /// <c>true</c> once the copy has been submitted and <see cref="CopySubmitHandle"/> has been
        /// assigned. Until then the readback must not be performed.
        /// </summary>
        public bool HasSubmitHandle { get; init; }

        public bool IsValid => Context is not null && Callback is not null;

        /// <summary>
        /// A pending picking is ready to deliver only once its copy has been submitted, so the
        /// engine can wait on the copy's own submit handle before reading the result.
        /// </summary>
        public bool IsReady => IsValid && HasSubmitHandle;

        /// <summary>
        /// Returns a copy of this pending picking with the copy's submit handle recorded, marking
        /// it ready for readback.
        /// </summary>
        public PendingPicking WithSubmitHandle(in SubmitHandle handle) =>
            new()
            {
                Context = Context,
                Coord = Coord,
                Id = Id,
                Callback = Callback,
                RouteGizmoPicks = RouteGizmoPicks,
                CopySubmitHandle = handle,
                HasSubmitHandle = true,
            };

        public static readonly PendingPicking Empty = new()
        {
            Id = uint.MaxValue,
            Coord = default,
            Context = null,
            Callback = null,
            RouteGizmoPicks = false,
            CopySubmitHandle = default,
            HasSubmitHandle = false,
        };
    }
}
