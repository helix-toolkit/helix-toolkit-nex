namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a command buffer interface for recording GPU commands in a graphics rendering pipeline.
/// </summary>
/// <remarks>
/// The <see cref="ICommandBuffer"/> interface provides methods for recording various GPU commands including:
/// <list type="bullet">
/// <item><description>Resource state transitions</description></item>
/// <item><description>Debug markers and labels</description></item>
/// <item><description>Compute pipeline operations</description></item>
/// <item><description>Rendering operations (draw calls, pipeline binding, etc.)</description></item>
/// <item><description>Buffer and texture operations</description></item>
/// <item><description>Query and timestamp operations</description></item>
/// </list>
/// Commands recorded in the command buffer are not executed immediately but are submitted to the GPU for execution.
/// </remarks>
public interface ICommandBuffer
{
    /// <summary>
    /// Transitions a texture to a shader read-only state, making it accessible for sampling in shaders.
    /// </summary>
    /// <param name="surface">The texture handle to transition.</param>
    void TransitionToShaderReadOnly(TextureHandle surface);

    /// <summary>
    /// Begins a debug group with a label and color for debugging and profiling tools.
    /// </summary>
    /// <param name="label">The label text for the debug group.</param>
    /// <param name="color">The color associated with this debug group in debugging tools.</param>
    /// <remarks>
    /// Debug groups can be nested. Each call to <see cref="PushDebugGroupLabel"/> must be matched with 
    /// a corresponding <see cref="PopDebugGroupLabel"/> call.
    /// </remarks>
    void PushDebugGroupLabel(string label, in Color4 color);

    /// <summary>
    /// Inserts a single debug event marker at the current position in the command buffer.
    /// </summary>
    /// <param name="label">The label text for the debug event.</param>
    /// <param name="color">The color associated with this debug event in debugging tools.</param>
    void InsertDebugEventLabel(string label, in Color4 color);

    /// <summary>
    /// Ends the current debug group started by <see cref="PushDebugGroupLabel"/>.
    /// </summary>
    void PopDebugGroupLabel();

    /// <summary>
    /// Binds a compute pipeline for subsequent compute dispatch operations.
    /// </summary>
    /// <param name="handle">The handle to the compute pipeline to bind.</param>
    void BindComputePipeline(ComputePipelineHandle handle);

    /// <summary>
    /// Dispatches compute work in thread groups.
    /// </summary>
    /// <param name="threadgroupCount">The number of thread groups to dispatch in each dimension (X, Y, Z).</param>
    /// <param name="deps">Dependencies that must be satisfied before this dispatch executes.</param>
    void DispatchThreadGroups(in Dimensions threadgroupCount, in Dependencies deps);

    /// <summary>
    /// Begins a rendering pass with the specified render pass configuration and framebuffer.
    /// </summary>
    /// <param name="renderPass">The render pass configuration defining load/store operations and attachments.</param>
    /// <param name="desc">The framebuffer containing the render targets for this rendering pass.</param>
    /// <param name="deps">Dependencies that must be satisfied before rendering begins.</param>
    /// <remarks>
    /// Must be paired with a corresponding <see cref="EndRendering"/> call.
    /// </remarks>
    void BeginRendering(in RenderPass renderPass, in Framebuffer desc, in Dependencies deps);

    /// <summary>
    /// Ends the current rendering pass started by <see cref="BeginRendering"/>.
    /// </summary>
    void EndRendering();

    /// <summary>
    /// Binds a viewport for subsequent rendering operations.
    /// </summary>
    /// <param name="viewport">The viewport to bind, defining the visible rendering area.</param>
    void BindViewport(in ViewportF viewport);

    /// <summary>
    /// Binds a scissor rectangle to clip rendering output.
    /// </summary>
    /// <param name="rect">The scissor rectangle to bind. Pixels outside this rectangle are discarded.</param>
    void BindScissorRect(in ScissorRect rect);

    /// <summary>
    /// Binds a render pipeline for subsequent draw operations.
    /// </summary>
    /// <param name="handle">The handle to the render pipeline to bind.</param>
    void BindRenderPipeline(in RenderPipelineHandle handle);

    /// <summary>
    /// Sets the depth state for subsequent rendering operations.
    /// </summary>
    /// <param name="state">The depth state configuration, including depth testing and writing settings.</param>
    void BindDepthState(in DepthState state);

    /// <summary>
    /// Binds a vertex buffer to a specific binding index.
    /// </summary>
    /// <param name="index">The binding index for the vertex buffer.</param>
    /// <param name="buffer">The handle to the vertex buffer to bind.</param>
    /// <param name="bufferOffset">Byte offset into the buffer where vertex data begins. Default is 0.</param>
    void BindVertexBuffer(size_t index, in BufferHandle buffer, size_t bufferOffset = 0);

    /// <summary>
    /// Binds an index buffer for indexed drawing operations.
    /// </summary>
    /// <param name="indexBuffer">The handle to the index buffer to bind.</param>
    /// <param name="indexFormat">The format of indices in the buffer (e.g., 16-bit or 32-bit unsigned integers).</param>
    /// <param name="indexBufferOffset">Byte offset into the buffer where index data begins. Default is 0.</param>
    void BindIndexBuffer(in BufferHandle indexBuffer, IndexFormat indexFormat, size_t indexBufferOffset = 0);

    /// <summary>
    /// Pushes constant data to the pipeline for use in shaders.
    /// </summary>
    /// <param name="data">Pointer to the constant data to push.</param>
    /// <param name="size">Size of the data in bytes.</param>
    /// <param name="offset">Offset within the push constant block where data should be written. Default is 0.</param>
    /// <remarks>
    /// The data pointer must remain valid until the command buffer is submitted.
    /// </remarks>
    void PushConstants(nint data, size_t size, size_t offset = 0);

    /// <summary>
    /// Pushes constant data of a specific unmanaged type to the pipeline for use in shaders.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the constant data.</typeparam>
    /// <param name="data">Reference to the constant data to push.</param>
    /// <param name="offset">Offset within the push constant block where data should be written. Default is 0.</param>
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

    /// <summary>
    /// Fills a region of a buffer with a repeated 4-byte value.
    /// </summary>
    /// <param name="buffer">The handle to the buffer to fill.</param>
    /// <param name="bufferOffset">Byte offset into the buffer where filling begins.</param>
    /// <param name="size">Number of bytes to fill. Must be a multiple of 4.</param>
    /// <param name="data">The 4-byte value to repeat throughout the filled region.</param>
    void FillBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, size_t data);

    /// <summary>
    /// Updates a region of a buffer with new data.
    /// </summary>
    /// <param name="buffer">The handle to the buffer to update.</param>
    /// <param name="bufferOffset">Byte offset into the buffer where the update begins.</param>
    /// <param name="size">Number of bytes to update.</param>
    /// <param name="data">Pointer to the source data to copy into the buffer.</param>
    /// <remarks>
    /// The data pointer must remain valid until the command buffer is submitted.
    /// </remarks>
    void UpdateBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, nint data);

    /// <summary>
    /// Updates a buffer with data of a specific unmanaged type.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the data to update.</typeparam>
    /// <param name="buffer">The handle to the buffer to update.</param>
    /// <param name="data">Reference to the data to copy into the buffer.</param>
    /// <param name="bufferOffset">Byte offset into the buffer where the update begins. Default is 0.</param>
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

    /// <summary>
    /// Draws non-indexed primitives.
    /// </summary>
    /// <param name="vertexCount">Number of vertices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw. Default is 1.</param>
    /// <param name="firstVertex">Index of the first vertex to draw. Default is 0.</param>
    /// <param name="baseInstance">Instance ID offset for the first instance. Default is 0.</param>
    void Draw(size_t vertexCount, size_t instanceCount = 1, size_t firstVertex = 0, size_t baseInstance = 0);

    /// <summary>
    /// Draws indexed primitives using the bound index buffer.
    /// </summary>
    /// <param name="indexCount">Number of indices to draw.</param>
    /// <param name="instanceCount">Number of instances to draw. Default is 1.</param>
    /// <param name="firstIndex">Index of the first index in the index buffer. Default is 0.</param>
    /// <param name="vertexOffset">Value added to each index before indexing into the vertex buffer. Default is 0.</param>
    /// <param name="baseInstance">Instance ID offset for the first instance. Default is 0.</param>
    void DrawIndexed(size_t indexCount,
                                size_t instanceCount = 1,
                                size_t firstIndex = 0,
                                int32_t vertexOffset = 0,
                                size_t baseInstance = 0);

    /// <summary>
    /// Draws non-indexed primitives with parameters sourced from a buffer (indirect drawing).
    /// </summary>
    /// <param name="indirectBuffer">The buffer containing draw parameters.</param>
    /// <param name="indirectBufferOffset">Byte offset into the indirect buffer where parameters begin.</param>
    /// <param name="drawCount">Number of draw calls to execute.</param>
    /// <param name="stride">Byte stride between consecutive draw parameter structures. If 0, assumes tightly packed. Default is 0.</param>
    void DrawIndirect(in BufferHandle indirectBuffer, size_t indirectBufferOffset, size_t drawCount, size_t stride = 0);

    /// <summary>
    /// Draws indexed primitives with parameters sourced from a buffer (indirect drawing).
    /// </summary>
    /// <param name="indirectBuffer">The buffer containing draw parameters.</param>
    /// <param name="indirectBufferOffset">Byte offset into the indirect buffer where parameters begin.</param>
    /// <param name="drawCount">Number of draw calls to execute.</param>
    /// <param name="stride">Byte stride between consecutive draw parameter structures. If 0, assumes tightly packed. Default is 0.</param>
    void DrawIndexedIndirect(in BufferHandle indirectBuffer,
                                        size_t indirectBufferOffset,
                                        size_t drawCount,
                                        size_t stride = 0);

    /// <summary>
    /// Draws indexed primitives with parameters and draw count sourced from buffers (indirect drawing with count).
    /// </summary>
    /// <param name="indirectBuffer">The buffer containing draw parameters.</param>
    /// <param name="indirectBufferOffset">Byte offset into the indirect buffer where parameters begin.</param>
    /// <param name="countBuffer">The buffer containing the actual draw count.</param>
    /// <param name="countBufferOffset">Byte offset into the count buffer where the draw count is stored.</param>
    /// <param name="maxDrawCount">Maximum number of draw calls that can be executed.</param>
    /// <param name="stride">Byte stride between consecutive draw parameter structures. If 0, assumes tightly packed. Default is 0.</param>
    void DrawIndexedIndirectCount(BufferHandle indirectBuffer,
                                             size_t indirectBufferOffset,
                                             BufferHandle countBuffer,
                                             size_t countBufferOffset,
                                             size_t maxDrawCount,
                                             size_t stride = 0);

    /// <summary>
    /// Dispatches mesh shader tasks for mesh shading pipeline.
    /// </summary>
    /// <param name="threadgroupCount">The number of task shader thread groups to dispatch in each dimension (X, Y, Z).</param>
    void DrawMeshTasks(in Dimensions threadgroupCount);

    /// <summary>
    /// Dispatches mesh shader tasks with parameters sourced from a buffer (indirect mesh drawing).
    /// </summary>
    /// <param name="indirectBuffer">The buffer containing dispatch parameters.</param>
    /// <param name="indirectBufferOffset">Byte offset into the indirect buffer where parameters begin.</param>
    /// <param name="drawCount">Number of mesh task dispatches to execute.</param>
    /// <param name="stride">Byte stride between consecutive dispatch parameter structures. If 0, assumes tightly packed. Default is 0.</param>
    void DrawMeshTasksIndirect(in BufferHandle indirectBuffer,
                                          size_t indirectBufferOffset,
                                          size_t drawCount,
                                          size_t stride = 0);

    /// <summary>
    /// Dispatches mesh shader tasks with parameters and count sourced from buffers (indirect mesh drawing with count).
    /// </summary>
    /// <param name="indirectBuffer">The buffer containing dispatch parameters.</param>
    /// <param name="indirectBufferOffset">Byte offset into the indirect buffer where parameters begin.</param>
    /// <param name="countBuffer">The buffer containing the actual dispatch count.</param>
    /// <param name="countBufferOffset">Byte offset into the count buffer where the dispatch count is stored.</param>
    /// <param name="maxDrawCount">Maximum number of mesh task dispatches that can be executed.</param>
    /// <param name="stride">Byte stride between consecutive dispatch parameter structures. If 0, assumes tightly packed. Default is 0.</param>
    void DrawMeshTasksIndirectCount(in BufferHandle indirectBuffer,
                                               size_t indirectBufferOffset,
                                               in BufferHandle countBuffer,
                                               size_t countBufferOffset,
                                               size_t maxDrawCount,
                                               size_t stride = 0);

    /// <summary>
    /// Sets the blend constant color used in blending operations.
    /// </summary>
    /// <param name="color">The blend constant color to set.</param>
    void SetBlendColor(in Color4 color);

    /// <summary>
    /// Sets the depth bias parameters for depth value adjustment during rasterization.
    /// </summary>
    /// <param name="constantFactor">Constant depth value added to each fragment.</param>
    /// <param name="slopeFactor">Scalar factor applied to the fragment's slope in depth bias calculation.</param>
    /// <param name="clamp">Maximum (or minimum) depth bias of a fragment. Default is 0.0f.</param>
    void SetDepthBias(float constantFactor, float slopeFactor, float clamp = 0.0f);

    /// <summary>
    /// Enables or disables depth bias for subsequent rendering operations.
    /// </summary>
    /// <param name="enable">True to enable depth bias; false to disable.</param>
    void SetDepthBiasEnable(bool enable);

    /// <summary>
    /// Resets a range of queries in a query pool to an initial state.
    /// </summary>
    /// <param name="pool">The handle to the query pool containing the queries to reset.</param>
    /// <param name="firstQuery">The index of the first query to reset.</param>
    /// <param name="queryCount">The number of consecutive queries to reset.</param>
    void ResetQueryPool(in QueryPoolHandle pool, size_t firstQuery, size_t queryCount);

    /// <summary>
    /// Writes a GPU timestamp to a specific query in a query pool.
    /// </summary>
    /// <param name="pool">The handle to the query pool where the timestamp will be written.</param>
    /// <param name="query">The index of the query within the pool to write the timestamp to.</param>
    void WriteTimestamp(in QueryPoolHandle pool, size_t query);

    /// <summary>
    /// Clears a color image (texture) to a specified color value.
    /// </summary>
    /// <param name="tex">The handle to the texture to clear.</param>
    /// <param name="value">The color value to clear the texture to.</param>
    /// <param name="layers">The texture layers (mip levels, array layers) to clear.</param>
    void ClearColorImage(in TextureHandle tex, in Color4 value, in TextureLayers layers);

    /// <summary>
    /// Copies a region from one texture to another.
    /// </summary>
    /// <param name="src">The handle to the source texture.</param>
    /// <param name="dst">The handle to the destination texture.</param>
    /// <param name="extent">The dimensions of the region to copy (width, height, depth).</param>
    /// <param name="srcOffset">The offset within the source texture where copying begins.</param>
    /// <param name="dstOffset">The offset within the destination texture where copying begins.</param>
    /// <param name="srcLayers">The layers of the source texture to copy from.</param>
    /// <param name="dstLayers">The layers of the destination texture to copy to.</param>
    void CopyImage(in TextureHandle src,
                              in TextureHandle dst,
                              in Dimensions extent,
                              in Offset3D srcOffset,
                              in Offset3D dstOffset,
                              in TextureLayers srcLayers,
                              in TextureLayers dstLayers);

    /// <summary>
    /// Generates mipmaps for a texture automatically.
    /// </summary>
    /// <param name="handle">The handle to the texture for which to generate mipmaps.</param>
    /// <remarks>
    /// The texture must have been created with mipmap generation support. This operation
    /// generates all mipmap levels from the base level (level 0) by downsampling.
    /// </remarks>
    void GenerateMipmap(in TextureHandle handle);
}