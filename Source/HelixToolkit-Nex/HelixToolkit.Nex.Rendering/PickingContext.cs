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
    private readonly BufferResource[] _stagingBuffer = new BufferResource[GraphicsSettings.MaxFrameInFlight];

    private readonly PickingParams[] _pendingCoords = new PickingParams[GraphicsSettings.MaxFrameInFlight];

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
        _pendingCoords[_requestId % _pendingCoords.Length] = new PickingParams { Coords = screenPos };
        return _requestId;
    }

    public void SendCommand(ICommandBuffer commandBuffer, RenderContext renderContext)
    {
        var submitIndex = _requestId % _pendingCoords.Length;
        var pending = _pendingCoords[submitIndex];
        if (!pending.IsValid)
        {
            return;
        }
        if (!renderContext.ResourceSet.Textures.TryGetValue(
                SystemBufferNames.TextureEntityId,
                out var srcTexture
            ) || srcTexture.Empty)
        {
            return;
        }
        var dims = _context.GetDimensions(srcTexture);
        if (pending.Coords.X < 0 || pending.Coords.Y < 0 || pending.Coords.X >= (int)dims.Width || pending.Coords.Y >= (int)dims.Height)
        {
            return;
        }
        commandBuffer.CopyTextureToBuffer(
            srcTexture,
            _stagingBuffer[submitIndex],
            bufferOffset: 0,
                srcOffset: new Offset3D((int)pending.Coords.X, (int)pending.Coords.Y, 0),
                extent: new Dimensions(1, 1, 1),
                layers: new TextureLayers()
            );
        _pendingCoords[submitIndex] = PickingParams.Empty;
    }

    public ulong ReadResult(uint requestId)
    {
        var submitIndex = requestId % _pendingCoords.Length;
        var buf = _stagingBuffer[submitIndex];
        unsafe
        {
            var mapped = _context.GetMappedPtr(buf);
            ulong result = *(ulong*)mapped;
            return result;
        }
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
