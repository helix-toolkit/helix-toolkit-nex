namespace HelixToolkit.Nex.Graphics;

public interface ICommandBuffer
{
    void TransitionToShaderReadOnly(TextureHandle surface);

    void PushDebugGroupLabel(string label, in Color4 color);
    void InsertDebugEventLabel(string label, in Color4 color);
    void PopDebugGroupLabel();

    void BindComputePipeline(ComputePipelineHandle handle);
    void DispatchThreadGroups(in Dimensions threadgroupCount, in Dependencies deps);

    void BeginRendering(in RenderPass renderPass, in Framebuffer desc, in Dependencies deps);
    void EndRendering();

    void BindViewport(in ViewportF viewport);
    void BindScissorRect(in ScissorRect rect);

    void BindRenderPipeline(in RenderPipelineHandle handle);
    void BindDepthState(in DepthState state);

    void BindVertexBuffer(size_t index, in BufferHandle buffer, size_t bufferOffset = 0);
    void BindIndexBuffer(in BufferHandle indexBuffer, IndexFormat indexFormat, size_t indexBufferOffset = 0);
    void PushConstants(nint data, size_t size, size_t offset = 0);
    void PushConstants<T>(in T data, size_t offset = 0) where T : unmanaged
    {
        unsafe
        {
            fixed (T* ptr = &data)
            {
                PushConstants((nint)ptr, (size_t)sizeof(T), offset);
            }
        }
    }

    void FillBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, size_t data);
    void UpdateBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, nint data);

    void UpdateBuffer<T>(in BufferHandle buffer, in T data, size_t bufferOffset = 0) where T : unmanaged
    {
        unsafe
        {
            fixed (T* ptr = &data)
            {
                UpdateBuffer(buffer, bufferOffset, (size_t)sizeof(T), (nint)ptr);
            }
        }
    }

    void Draw(size_t vertexCount, size_t instanceCount = 1, size_t firstVertex = 0, size_t baseInstance = 0);
    void DrawIndexed(size_t indexCount,
                                size_t instanceCount = 1,
                                size_t firstIndex = 0,
                                int32_t vertexOffset = 0,
                                size_t baseInstance = 0);
    void DrawIndirect(in BufferHandle indirectBuffer, size_t indirectBufferOffset, size_t drawCount, size_t stride = 0);
    void DrawIndexedIndirect(in BufferHandle indirectBuffer,
                                        size_t indirectBufferOffset,
                                        size_t drawCount,
                                        size_t stride = 0);
    void DrawIndexedIndirectCount(BufferHandle indirectBuffer,
                                             size_t indirectBufferOffset,
                                             BufferHandle countBuffer,
                                             size_t countBufferOffset,
                                             size_t maxDrawCount,
                                             size_t stride = 0);
    void DrawMeshTasks(in Dimensions threadgroupCount);
    void DrawMeshTasksIndirect(in BufferHandle indirectBuffer,
                                          size_t indirectBufferOffset,
                                          size_t drawCount,
                                          size_t stride = 0);
    void DrawMeshTasksIndirectCount(in BufferHandle indirectBuffer,
                                               size_t indirectBufferOffset,
                                               in BufferHandle countBuffer,
                                               size_t countBufferOffset,
                                               size_t maxDrawCount,
                                               size_t stride = 0);

    void SetBlendColor(in Color4 color);
    void SetDepthBias(float constantFactor, float slopeFactor, float clamp = 0.0f);
    void SetDepthBiasEnable(bool enable);

    void ResetQueryPool(in QueryPoolHandle pool, size_t firstQuery, size_t queryCount);
    void WriteTimestamp(in QueryPoolHandle pool, size_t query);

    void ClearColorImage(in TextureHandle tex, in Color4 value, in TextureLayers layers);
    void CopyImage(in TextureHandle src,
                              in TextureHandle dst,
                              in Dimensions extent,
                              in Offset3D srcOffset,
                              in Offset3D dstOffset,
                              in TextureLayers srcLayers,
                              in TextureLayers dstLayers);
    void GenerateMipmap(in TextureHandle handle);
}