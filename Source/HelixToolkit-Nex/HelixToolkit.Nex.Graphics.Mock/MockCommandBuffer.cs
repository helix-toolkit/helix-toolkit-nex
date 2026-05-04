namespace HelixToolkit.Nex.Graphics.Mock;

/// <summary>
/// Mock implementation of <see cref="ICommandBuffer"/> for unit testing.
/// </summary>
public class MockCommandBuffer : ICommandBuffer
{
    private readonly MockContext _context;
    private readonly FastList<string> _recordedCommands = new();
    private bool _isRendering = false;

    public MockCommandBuffer(MockContext context, bool isPrimary)
    {
        _context = context;
        IsSecondary = !isPrimary;
    }

    /// <inheritdoc/>
    public IContext Context => _context;

    /// <inheritdoc/>
    public bool IsSecondary { get; }

    /// <summary>
    /// Gets whether this command buffer has been submitted.
    /// </summary>
    public bool IsSubmitted { get; internal set; }

    /// <summary>
    /// Gets the list of recorded command names for validation.
    /// </summary>
    public IReadOnlyList<string> RecordedCommands => _recordedCommands;

    /// <summary>
    /// Gets whether a render pass is currently active.
    /// </summary>
    public bool IsRendering => _isRendering;

    /// <inheritdoc/>
    public void ExecuteCommands(params ICommandBuffer[] secondaryBuffers)
    {
        _recordedCommands.Add($"ExecuteCommands({secondaryBuffers.Length})");
    }

    /// <inheritdoc/>
    public void TransitionToShaderReadOnly(in TextureHandle handle)
    {
        _recordedCommands.Add($"TransitionToShaderReadOnly({handle.Index})");
    }

    /// <inheritdoc/>
    public void TransitionToRenderingLocalRead(in TextureHandle handle)
    {
        _recordedCommands.Add($"TransitionToRenderingLocalRead({handle.Index})");
    }

    /// <inheritdoc/>
    public void PushDebugGroupLabel(ReadOnlySpan<byte> label, Color4 color)
    {
        _recordedCommands.Add($"PushDebugGroupLabel({System.Text.Encoding.UTF8.GetString(label)})");
    }

    /// <inheritdoc/>
    public void InsertDebugEventLabel(ReadOnlySpan<byte> label, Color4 color)
    {
        _recordedCommands.Add($"InsertDebugEventLabel({System.Text.Encoding.UTF8.GetString(label)})");
    }

    /// <inheritdoc/>
    public void PopDebugGroupLabel()
    {
        _recordedCommands.Add("PopDebugGroupLabel");
    }

    /// <inheritdoc/>
    public void BindComputePipeline(in ComputePipelineHandle handle)
    {
        _recordedCommands.Add($"BindComputePipeline({handle.Index})");
    }

    /// <inheritdoc/>
    public void DispatchThreadGroups(Dimensions threadgroupCount, Dependencies deps)
    {
        _recordedCommands.Add(
            $"DispatchThreadGroups({threadgroupCount.Width}, {threadgroupCount.Height}, {threadgroupCount.Depth})"
        );
    }

    /// <inheritdoc/>
    public void BeginRendering(RenderPass renderPass, Framebuffer desc, Dependencies deps)
    {
        _isRendering = true;
        _recordedCommands.Add(
            $"BeginRendering(numColorAttachments={renderPass.GetNumColorAttachments()})"
        );
    }

    /// <inheritdoc/>
    public void EndRendering()
    {
        _isRendering = false;
        _recordedCommands.Add("EndRendering");
    }

    /// <inheritdoc/>
    public void NextSubpass()
    {
        _recordedCommands.Add("NextSubpass");
    }

    /// <inheritdoc/>
    public void BindViewport(ViewportF viewport)
    {
        _recordedCommands.Add($"BindViewport({viewport.Width}x{viewport.Height})");
    }

    /// <inheritdoc/>
    public void BindScissorRect(ScissorRect rect)
    {
        _recordedCommands.Add($"BindScissorRect({rect.Width}x{rect.Height})");
    }

    /// <inheritdoc/>
    public void BindRenderPipeline(in RenderPipelineHandle handle)
    {
        _recordedCommands.Add($"BindRenderPipeline({handle.Index})");
    }

    /// <inheritdoc/>
    public void BindDepthState(DepthState state)
    {
        _recordedCommands.Add($"BindDepthState(writeEnabled={state.IsDepthWriteEnabled})");
    }

    /// <inheritdoc/>
    public void BindVertexBuffer(size_t index, in BufferHandle buffer, size_t bufferOffset = 0)
    {
        _recordedCommands.Add($"BindVertexBuffer({index}, buffer={buffer.Index})");
    }

    /// <inheritdoc/>
    public void BindIndexBuffer(
        in BufferHandle indexBuffer,
        IndexFormat indexFormat,
        size_t indexBufferOffset = 0
    )
    {
        _recordedCommands.Add($"BindIndexBuffer(buffer={indexBuffer.Index}, format={indexFormat})");
    }

    /// <inheritdoc/>
    public void PushConstants(nint data, size_t size, size_t offset = 0)
    {
        _recordedCommands.Add($"PushConstants(size={size}, offset={offset})");
    }

    /// <inheritdoc/>
    public void FillBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, size_t data)
    {
        _recordedCommands.Add($"FillBuffer(buffer={buffer.Index}, size={size})");
    }

    /// <inheritdoc/>
    public ResultCode UpdateBuffer(
        in BufferHandle buffer,
        size_t bufferOffset,
        size_t size,
        nint data
    )
    {
        _recordedCommands.Add($"UpdateBuffer(buffer={buffer.Index}, size={size})");
        return ResultCode.Ok;
    }

    /// <inheritdoc/>
    public void CopyBuffer(
        in BufferHandle src,
        size_t srcOffset,
        in BufferHandle dst,
        size_t dstOffset,
        size_t size
    )
    {
        _recordedCommands.Add($"CopyBuffer(src={src.Index}, dst={dst.Index}, size={size})");
    }

    /// <inheritdoc/>
    public void Draw(
        size_t vertexCount,
        size_t instanceCount = 1,
        size_t firstVertex = 0,
        size_t baseInstance = 0
    )
    {
        _recordedCommands.Add($"Draw(vertices={vertexCount}, instances={instanceCount})");
    }

    /// <inheritdoc/>
    public void DrawIndexed(
        size_t indexCount,
        size_t instanceCount = 1,
        size_t firstIndex = 0,
        int32_t vertexOffset = 0,
        size_t baseInstance = 0
    )
    {
        _recordedCommands.Add($"DrawIndexed(indices={indexCount}, instances={instanceCount})");
    }

    /// <inheritdoc/>
    public void DrawIndirect(
        in BufferHandle indirectBuffer,
        size_t indirectBufferOffset,
        size_t drawCount,
        size_t stride = 0
    )
    {
        _recordedCommands.Add($"DrawIndirect(buffer={indirectBuffer.Index}, count={drawCount})");
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(
        in BufferHandle indirectBuffer,
        size_t indirectBufferOffset,
        size_t drawCount,
        size_t stride = 0
    )
    {
        _recordedCommands.Add(
            $"DrawIndexedIndirect(buffer={indirectBuffer.Index}, count={drawCount})"
        );
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirectCount(
        in BufferHandle indirectBuffer,
        size_t indirectBufferOffset,
        in BufferHandle countBuffer,
        size_t countBufferOffset,
        size_t maxDrawCount,
        size_t stride = 0
    )
    {
        _recordedCommands.Add($"DrawIndexedIndirectCount(maxCount={maxDrawCount})");
    }

    /// <inheritdoc/>
    public void DrawMeshTasks(Dimensions threadgroupCount)
    {
        _recordedCommands.Add(
            $"DrawMeshTasks({threadgroupCount.Width}, {threadgroupCount.Height}, {threadgroupCount.Depth})"
        );
    }

    /// <inheritdoc/>
    public void DrawMeshTasksIndirect(
        in BufferHandle indirectBuffer,
        size_t indirectBufferOffset,
        size_t drawCount,
        size_t stride = 0
    )
    {
        _recordedCommands.Add(
            $"DrawMeshTasksIndirect(buffer={indirectBuffer.Index}, count={drawCount})"
        );
    }

    /// <inheritdoc/>
    public void DrawMeshTasksIndirectCount(
        in BufferHandle indirectBuffer,
        size_t indirectBufferOffset,
        in BufferHandle countBuffer,
        size_t countBufferOffset,
        size_t maxDrawCount,
        size_t stride = 0
    )
    {
        _recordedCommands.Add($"DrawMeshTasksIndirectCount(maxCount={maxDrawCount})");
    }

    /// <inheritdoc/>
    public void SetBlendColor(Color4 color)
    {
        _recordedCommands.Add(
            $"SetBlendColor({color.Red}, {color.Green}, {color.Blue}, {color.Alpha})"
        );
    }

    /// <inheritdoc/>
    public void SetDepthBias(float constantFactor, float slopeFactor, float clamp = 0.0f)
    {
        _recordedCommands.Add($"SetDepthBias(constant={constantFactor}, slope={slopeFactor})");
    }

    /// <inheritdoc/>
    public void SetDepthBiasEnable(bool enable)
    {
        _recordedCommands.Add($"SetDepthBiasEnable({enable})");
    }

    /// <inheritdoc/>
    public void ResetQueryPool(in QueryPoolHandle pool, size_t firstQuery, size_t queryCount)
    {
        _recordedCommands.Add(
            $"ResetQueryPool(pool={pool.Index}, first={firstQuery}, count={queryCount})"
        );
    }

    /// <inheritdoc/>
    public void WriteTimestamp(in QueryPoolHandle pool, size_t query)
    {
        _recordedCommands.Add($"WriteTimestamp(pool={pool.Index}, query={query})");
    }

    /// <inheritdoc/>
    public void ClearColorImage(in TextureHandle tex, Color4 value, TextureLayers layers)
    {
        _recordedCommands.Add($"ClearColorImage(texture={tex.Index})");
    }

    /// <inheritdoc/>
    public void CopyImage(
        in TextureHandle src,
        in TextureHandle dst,
        Dimensions extent,
        Offset3D srcOffset,
        Offset3D dstOffset,
        TextureLayers srcLayers,
        TextureLayers dstLayers
    )
    {
        _recordedCommands.Add($"CopyImage(src={src.Index}, dst={dst.Index})");
    }

    /// <inheritdoc/>
    public void GenerateMipmap(in TextureHandle handle)
    {
        _recordedCommands.Add($"GenerateMipmap(texture={handle.Index})");
    }

    public bool Barrier(in BufferHandle buffer)
    {
        _recordedCommands.Add($"Barrier(buffer={buffer.Index})");
        return true;
    }

    public void SetCheckpointMarker(ReadOnlySpan<byte> label)
    {
        _recordedCommands.Add($"SetCheckpointMarker({System.Text.Encoding.UTF8.GetString(label)})");
    }
}
