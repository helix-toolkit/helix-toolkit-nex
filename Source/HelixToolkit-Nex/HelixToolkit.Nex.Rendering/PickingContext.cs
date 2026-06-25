namespace HelixToolkit.Nex.Rendering;

internal sealed class PickingContext : IDisposable
{
    private readonly struct PickingParams
    {
        public Vector2 Coords { get; init; }
        public bool IsValid => Coords.X >= 0 && Coords.Y >= 0;

        public static readonly PickingParams Empty = new() { Coords = new Vector2(-1, -1) };
    }

    private readonly IContext _context;

    /// <summary>
    /// 8-byte host-visible buffer that receives one mesh-id pixel (2 × float32 = RG_F32)
    /// copied from the entity-id texture each frame via <c>CopyTextureToBuffer</c>.
    /// Kept alive for the lifetime of this object.
    /// </summary>
    private readonly BufferResource[] _stagingBuffer = new BufferResource[
        GraphicsSettings.MaxFrameInFlight
    ];

    private readonly PickingParams[] _pendingCoords = new PickingParams[
        GraphicsSettings.MaxFrameInFlight
    ];

    /// <summary>
    /// Tracks, per ring slot, the request id that currently occupies it. This lets
    /// <see cref="SendCommand"/> report the exact request id whose copy it recorded so that
    /// the engine and <see cref="ReadResult"/> agree on the slot/request pairing, independent
    /// of any later advance of <see cref="_requestId"/>.
    /// </summary>
    private readonly uint[] _slotRequestId = new uint[GraphicsSettings.MaxFrameInFlight];

    private uint _requestId = 0;

    /// <param name="context">The graphics context that owns the staging buffer.</param>
    public PickingContext(IContext context)
    {
        _context = context;
        for (var i = 0; i < _stagingBuffer.Length; i++)
        {
            // 8 bytes — two float32 channels of one RG_F32 pixel
            context
                .CreateBuffer(
                    new BufferDesc(
                        BufferUsageBits.Storage,
                        StorageType.HostVisible,
                        nint.Zero,
                        sizeof(ulong),
                        $"PickingReadback_{i}"
                    ),
                    out var buf
                )
                .CheckResult();
            _stagingBuffer[i] = buf;
            _pendingCoords[i] = PickingParams.Empty;
        }
    }

    public uint SetPendingSubmit(Vector2 screenPos)
    {
        ++_requestId;
        var slot = _requestId % _pendingCoords.Length;
        _pendingCoords[slot] = new PickingParams { Coords = screenPos };
        _slotRequestId[slot] = _requestId;
        return _requestId;
    }

    /// <summary>
    /// Records a <c>CopyTextureToBuffer</c> for the pending picking request into
    /// <paramref name="commandBuffer"/>, copying the clicked entity-id pixel into the staging
    /// buffer slot owned by that request.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to record the copy into.</param>
    /// <param name="renderContext">The render context that owns the entity-id texture.</param>
    /// <returns>
    /// The request id whose copy was recorded, so the caller can associate the resulting submit
    /// handle with that exact request. Returns <c>null</c> when no copy was recorded (no valid
    /// pending coordinate, missing/empty entity-id texture, or out-of-bounds coordinates) so the
    /// caller does not register a pending readback for it.
    /// </returns>
    public uint? SendCommand(ICommandBuffer commandBuffer, RenderContext renderContext)
    {
        // Resolve the slot from the request that owns the pending coordinate about to be copied,
        // then report that owning request id (not the live counter, which may have advanced).
        var submitIndex = _requestId % _pendingCoords.Length;
        var pending = _pendingCoords[submitIndex];
        if (!pending.IsValid)
        {
            return null;
        }
        if (
            !renderContext.ResourceSet.Textures.TryGetValue(
                SystemBufferNames.TextureEntityId,
                out var srcTexture
            ) || srcTexture.Empty
        )
        {
            return null;
        }
        var dims = _context.GetDimensions(srcTexture);
        if (
            pending.Coords.X < 0
            || pending.Coords.Y < 0
            || pending.Coords.X >= (int)dims.Width
            || pending.Coords.Y >= (int)dims.Height
        )
        {
            return null;
        }
        var requestId = _slotRequestId[submitIndex];
        commandBuffer.CopyTextureToBuffer(
            srcTexture,
            _stagingBuffer[submitIndex],
            bufferOffset: 0,
            srcOffset: new Offset3D((int)pending.Coords.X, (int)pending.Coords.Y, 0),
            extent: new Dimensions(1, 1, 1),
            layers: new TextureLayers()
        );
        _pendingCoords[submitIndex] = PickingParams.Empty;
        return requestId;
    }

    public ulong ReadResult(uint requestId)
    {
        var submitIndex = requestId % _pendingCoords.Length;
        var buf = _stagingBuffer[submitIndex];
        ulong result = 0;
        _context.GetBufferData(buf, ref result).CheckResult();
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
