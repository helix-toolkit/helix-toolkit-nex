namespace HelixToolkit.Nex.Rendering;

internal sealed class PickingContext : IDisposable
{
    private readonly struct PickingParams
    {
        public Vector2 Coords { get; init; }
        public bool IsValid => Coords.X >= 0 && Coords.Y >= 0;

        public static readonly PickingParams Empty = new() { Coords = new Vector2(-1, -1) };
    }

    /// <summary>
    /// Sentinel Request Id returned by <see cref="SetPendingSubmit"/> when the current frame is
    /// already full. Distinguishable from any accepted Request Id (the accepted id counter never
    /// reaches it in practice, and callers compare against this constant).
    /// </summary>
    public const uint InvalidRequestId = 0;

    /// <summary>
    /// Maps an accepted Request Id to the exact place its pixel was copied: which Frame Slot's
    /// staging buffer holds the result and the Byte Offset within that buffer.
    /// </summary>
    private readonly record struct ReadbackLocation(int FrameSlot, int ByteOffset);

    private readonly IContext _context;

    /// <summary>
    /// Ring of host-visible staging buffers, one per Frame Slot. Each buffer is laid out as
    /// <see cref="GraphicsSettings.MaxRequestsPerFrame"/> contiguous 8-byte cells (2 × float32 =
    /// RG_F32 per cell) that receive entity-id pixels copied from the entity-id texture via
    /// <c>CopyTextureToBuffer</c>. Kept alive for the lifetime of this object.
    /// </summary>
    private readonly BufferResource[] _stagingBuffer = new BufferResource[
        GraphicsSettings.MaxFrameInFlight
    ];

    /// <summary>
    /// Per-frame accepted requests, indexed by Request Slot. Reset each record cycle.
    /// </summary>
    private readonly PickingParams[] _pendingCoords = new PickingParams[
        GraphicsSettings.MaxRequestsPerFrame
    ];

    /// <summary>
    /// Tracks, per Request Slot, the request id that currently occupies it. This lets
    /// <see cref="SendCommand"/> report the exact request id whose copy it recorded so that
    /// the engine and <see cref="ReadResult"/> agree on the slot/request pairing, independent
    /// of any later advance of <see cref="_requestId"/>.
    /// </summary>
    private readonly uint[] _slotRequestId = new uint[GraphicsSettings.MaxRequestsPerFrame];

    /// <summary>
    /// Number of picking requests accepted in the current frame, in
    /// <c>[0, MaxRequestsPerFrame]</c>. Reset to zero each record cycle.
    /// </summary>
    private int _acceptedThisFrame;

    /// <summary>
    /// Readback metadata: maps an accepted Request Id to its
    /// <see cref="ReadbackLocation"/> (Frame Slot + Byte Offset).
    /// </summary>
    private readonly Dictionary<uint, ReadbackLocation> _readbackByRequestId = new();

    /// <summary>
    /// Monotonic request-id counter. Incremented only when a request is accepted.
    /// </summary>
    private uint _requestId = 0;

    /// <param name="context">The graphics context that owns the staging buffer.</param>
    public PickingContext(IContext context)
    {
        _context = context;
        // Each staging buffer holds MaxRequestsPerFrame contiguous 8-byte cells
        // (8 bytes = two float32 channels of one RG_F32 pixel per cell).
        var bufferSizeBytes = (uint)sizeof(ulong) * GraphicsSettings.MaxRequestsPerFrame;
        for (var i = 0; i < _stagingBuffer.Length; i++)
        {
            context
                .CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Storage,
                        StorageType.HostVisible,
                        nint.Zero,
                        bufferSizeBytes,
                        $"PickingReadback_{i}"
                    ),
                    out var buf
                )
                .CheckResult();
            _stagingBuffer[i] = buf;
        }
        for (var i = 0; i < _pendingCoords.Length; i++)
        {
            _pendingCoords[i] = PickingParams.Empty;
        }
    }

    /// <summary>
    /// Accepts a picking request for <paramref name="screenPos"/> in the current frame, assigning
    /// it the next free Request Slot and a unique Request Id. Returns <see cref="InvalidRequestId"/>
    /// without mutating any state when the current frame has already accepted
    /// <see cref="GraphicsSettings.MaxRequestsPerFrame"/> requests.
    /// </summary>
    /// <param name="screenPos">The screen-space coordinate to pick.</param>
    /// <returns>
    /// The accepted request's Request Id, or <see cref="InvalidRequestId"/> if the current frame is
    /// already full.
    /// </returns>
    public uint SetPendingSubmit(Vector2 screenPos)
    {
        if (_acceptedThisFrame >= (int)GraphicsSettings.MaxRequestsPerFrame)
        {
            return InvalidRequestId;
        }
        var slot = _acceptedThisFrame++;
        ++_requestId;
        if (_requestId == InvalidRequestId)
        {
            // Skip the sentinel value.
            ++_requestId;
        }

        _pendingCoords[slot] = new PickingParams { Coords = screenPos };
        _slotRequestId[slot] = _requestId;
        return _requestId;
    }

    /// <summary>
    /// Test seam: returns the Request Slot currently assigned to <paramref name="requestId"/> within
    /// the current frame, or <c>-1</c> if no request accepted in the current frame owns that id. The
    /// request's Byte Offset within its staging buffer is <c>slot * sizeof(ulong)</c>. Exposed for
    /// property tests that verify distinct, non-overlapping per-request offsets without a live GPU.
    /// </summary>
    internal int GetRequestSlot(uint requestId)
    {
        for (var slot = 0; slot < _acceptedThisFrame; slot++)
        {
            if (_slotRequestId[slot] == requestId)
            {
                return slot;
            }
        }
        return -1;
    }

    /// <summary>
    /// Test seam (test-only): registers a <see cref="ReadbackLocation"/> for
    /// <paramref name="requestId"/> directly, bypassing <see cref="SendCommand"/>. This lets tests
    /// inject an out-of-bounds Byte Offset so the fail-fast bounds check in <see cref="ReadResult"/>
    /// can be exercised without a live GPU (normal code paths only ever produce in-bounds offsets).
    /// Not used by production code.
    /// </summary>
    /// <param name="requestId">The Request Id to associate with the injected readback location.</param>
    /// <param name="frameSlot">The Frame Slot (staging buffer index) to record.</param>
    /// <param name="byteOffset">The Byte Offset to record (may be intentionally out of bounds).</param>
    internal void SetReadbackLocationForTest(uint requestId, int frameSlot, int byteOffset)
    {
        _readbackByRequestId[requestId] = new ReadbackLocation(frameSlot, byteOffset);
    }

    /// <summary>
    /// Records a batch of <c>CopyTextureToBuffer</c> commands into <paramref name="commandBuffer"/>,
    /// copying the clicked entity-id pixel of every accepted, in-bounds picking request in the
    /// current frame into the staging buffer for <paramref name="frameSlot"/> at that request's
    /// Byte Offset. Remembers each copied request's <see cref="ReadbackLocation"/> so a later
    /// <see cref="ReadResult"/> can find its pixel, then clears the pending set and resets the
    /// per-frame capacity for the next record cycle.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to record the copies into.</param>
    /// <param name="renderContext">The render context that owns the entity-id texture.</param>
    /// <param name="frameSlot">
    /// The current Frame Slot in <c>[0, MaxFrameInFlight)</c>. Selects the staging buffer copies
    /// are recorded into and is stored in each recorded <see cref="ReadbackLocation"/> so readback
    /// resolves to the same buffer.
    /// </param>
    /// <param name="copiedRequestIds">
    /// The list of Request Ids whose copies were recorded, so the caller can associate the
    /// resulting submit handle with those exact requests. Empty when no copies were recorded
    /// (missing/empty entity-id texture, or no valid/in-bounds pending coordinates).
    /// </param>
    public void SendCommand(
        ICommandBuffer commandBuffer,
        RenderContext renderContext,
        int frameSlot,
        FastList<uint> copiedRequestIds
    )
    {
        if (
            !renderContext.ResourceSet.Textures.TryGetValue(
                SystemBufferNames.TextureEntityId,
                out var srcTexture
            ) || srcTexture.Empty
        )
        {
            // Missing/empty entity-id texture: record no copies and drop this frame's pending
            // coordinates so stale coordinates are not retried against a later texture.
            ClearPending();
            _acceptedThisFrame = 0;
        }

        var dims = _context.GetDimensions(srcTexture);

        for (var s = 0; s < _acceptedThisFrame; s++)
        {
            var pending = _pendingCoords[s];
            if (!pending.IsValid)
            {
                continue;
            }
            // Skip coordinates outside the texture: no copy, no readback registered.
            if (
                pending.Coords.X < 0
                || pending.Coords.Y < 0
                || pending.Coords.X >= (int)dims.Width
                || pending.Coords.Y >= (int)dims.Height
            )
            {
                continue;
            }
            var byteOffset = s * sizeof(ulong);
            commandBuffer.CopyTextureToBuffer(
                srcTexture,
                _stagingBuffer[frameSlot],
                bufferOffset: (uint)byteOffset,
                srcOffset: new Offset3D((int)pending.Coords.X, (int)pending.Coords.Y, 0),
                extent: new Dimensions(1, 1, 1),
                layers: new TextureLayers()
            );
            var requestId = _slotRequestId[s];
            _readbackByRequestId[requestId] = new ReadbackLocation(frameSlot, byteOffset);
            copiedRequestIds.Add(requestId);
        }

        ClearPending();
        _acceptedThisFrame = 0;
    }

    /// <summary>
    /// Resets every Request Slot's pending coordinate to <see cref="PickingParams.Empty"/> so the
    /// coordinates recorded (or skipped) this frame are not recorded again in a later frame.
    /// </summary>
    private void ClearPending()
    {
        for (var s = 0; s < _pendingCoords.Length; s++)
        {
            _pendingCoords[s] = PickingParams.Empty;
        }
    }

    /// <summary>
    /// Reads back the entity-id pixel for <paramref name="requestId"/> by resolving its recorded
    /// <see cref="ReadbackLocation"/> (Frame Slot + Byte Offset) and downloading exactly
    /// <c>sizeof(ulong)</c> bytes from that request's own cell in its staging buffer. The entry is
    /// removed after a successful read so the result is delivered only once.
    /// </summary>
    /// <param name="requestId">The Request Id whose result should be read.</param>
    /// <returns>The 8-byte (RG_F32) entity-id value copied for the request.</returns>
    /// <exception cref="InvalidOperationException">
    /// No pending readback is registered for <paramref name="requestId"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The resolved Byte Offset falls outside the staging buffer bounds (fail-fast; Requirement 3.4).
    /// </exception>
    public ulong ReadResult(uint requestId)
    {
        if (!_readbackByRequestId.TryGetValue(requestId, out var location))
        {
            throw new InvalidOperationException(
                $"No pending picking readback is registered for request id {requestId}."
            );
        }

        const int elementSize = sizeof(ulong);
        var bufferSizeBytes = elementSize * (int)GraphicsSettings.MaxRequestsPerFrame;

        // Fail-fast bounds check: never read from a location outside the request's staging buffer.
        if (location.ByteOffset < 0 || location.ByteOffset + elementSize > bufferSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestId),
                $"Readback byte offset {location.ByteOffset} for request id {requestId} is outside "
                    + $"the staging buffer bounds [0, {bufferSizeBytes})."
            );
        }

        var result = 0UL;
        unsafe
        {
            BufferHandle handle = _stagingBuffer[location.FrameSlot];
            _context
                .GetBufferSubData(
                    handle,
                    offset: (uint)location.ByteOffset,
                    size: (uint)elementSize,
                    data: (nint)(&result)
                )
                .CheckResult();
        }

        // Deliver the result only once.
        _readbackByRequestId.Remove(requestId);
        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var buf in _stagingBuffer)
        {
            buf.Dispose();
        }
    }
}
