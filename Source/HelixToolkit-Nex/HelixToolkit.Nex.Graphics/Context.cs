namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents the main graphics context interface for creating and managing GPU resources.
/// </summary>
/// <remarks>
/// The <see cref="IContext"/> interface provides methods for:
/// <list type="bullet">
/// <item><description>Initializing the graphics system</description></item>
/// <item><description>Creating GPU resources (buffers, textures, pipelines, etc.)</description></item>
/// <item><description>Command buffer management and submission</description></item>
/// <item><description>Data upload/download operations</description></item>
/// <item><description>Swapchain management for presentation</description></item>
/// <item><description>Query pool operations for performance measurements</description></item>
/// </list>
/// </remarks>
public interface IContext : IInitializable
{
    /// <summary>
    /// Acquires a command buffer for recording GPU commands.
    /// </summary>
    /// <returns>A command buffer ready for recording.</returns>
    ICommandBuffer AcquireCommandBuffer();

    /// <summary>
    /// Creates a secondary command buffer for parallel recording.
    /// Secondary command buffers can be recorded independently and executed by a primary command buffer.
    /// </summary>
    /// <param name="renderPassInfo">The render pass information this secondary buffer will be used with.</param>
    /// <returns>A secondary command buffer ready for recording.</returns>
    /// <remarks>
    /// Secondary command buffers are useful for:
    /// <list type="bullet">
    /// <item>Parallel command recording across multiple threads</item>
    /// <item>Reusing pre-recorded command sequences</item>
    /// <item>Organizing complex rendering workloads</item>
    /// </list>
    /// The secondary buffer must be compatible with the render pass it will be executed in.
    /// </remarks>
    ICommandBuffer CreateSecondaryCommandBuffer(RenderPass renderPassInfo);

    /// <summary>
    /// Submits a command buffer for execution on the GPU.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <param name="present">Optional texture to present to the swapchain. Use <see cref="TextureHandle.Null"/> for no presentation.</param>
    /// <returns>A <see cref="SubmitHandle"/> that can be used to wait for completion.</returns>
    SubmitHandle Submit(ICommandBuffer commandBuffer, in TextureHandle present)
    {
        return Submit(commandBuffer, present, default);
    }

    /// <summary>
    /// Submits a command buffer for execution on the GPU.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <param name="present">Optional texture to present to the swapchain. Use <see cref="TextureHandle.Null"/> for no presentation.</param>
    /// <param name="syncInfo">Keyed mutex synchronization information.</param>
    /// <returns></returns>
    SubmitHandle Submit(
        ICommandBuffer commandBuffer,
        in TextureHandle present,
        KeyedMutexSyncInfo syncInfo
    );

    /// <summary>
    /// Waits for a submitted command buffer to complete execution.
    /// </summary>
    /// <param name="handle">The submit handle to wait on. Passing an empty handle waits for all GPU operations to complete (device idle).</param>
    /// <param name="reset">Indicates whether to reset the submit handle after waiting. Defaults to true.</param>
    void Wait(in SubmitHandle handle, bool reset = true);

    /// <summary>
    /// Waits for all submitted command buffers to complete execution, effectively idling the GPU.
    /// </summary>
    /// <param name="reset">Indicates whether to reset the submit handles after waiting.</param>
    void WaitAll(bool reset = true);

    /// <summary>
    /// Determines whether the specified submit handle (usually from a previously submitted frame) has been processed.
    /// </summary>
    /// <param name="handle">The submit handle to check for readiness. Represents the operation or resource has been processed.</param>
    /// <returns>true if the handle has been processed; otherwise, false.</returns>
    bool IsReady(in SubmitHandle handle);

    /// <summary>
    /// Creates a GPU buffer resource.
    /// </summary>
    /// <param name="desc">The buffer description.</param>
    /// <param name="buffer">Receives the created buffer resource.</param>
    /// <param name="debugName">Optional debug name for the buffer.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateBuffer(BufferDesc desc, out BufferResource buffer, string? debugName = null);

    /// <summary>
    /// Creates a sampler state object.
    /// </summary>
    /// <param name="desc">The sampler state description.</param>
    /// <param name="sampler">Receives the created sampler resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateSampler(SamplerStateDesc desc, out SamplerResource sampler);

    /// <summary>
    /// Creates a texture resource.
    /// </summary>
    /// <param name="desc">The texture description.</param>
    /// <param name="texture">Receives the created texture resource.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateTexture(
        TextureDesc desc,
        out TextureResource texture,
        string? debugName = null
    );

    /// <summary>
    /// Creates a two-dimensional texture resource with the specified format, dimensions, usage, and storage options.
    /// </summary>
    /// <param name="format">The pixel format to use for the texture.</param>
    /// <param name="width">The width of the texture, in pixels. Must be greater than 0.</param>
    /// <param name="height">The height of the texture, in pixels. Must be greater than 0.</param>
    /// <param name="usage">A bitmask specifying how the texture will be used (e.g., sampling, rendering, etc.).</param>
    /// <param name="storage">The storage type that determines how the texture data is allocated and managed.</param>
    /// <param name="numLayers">The number of array layers in the texture. Must be at least 1. Defaults to 1.</param>
    /// <param name="numSamples">The number of samples per pixel for multisampling. Must be at least 1. Defaults to 1 (no multisampling).</param>
    /// <param name="numMipLevels">The number of mipmap levels for the texture. Must be at least 1. Defaults to 1 (no mipmaps).</param>
    /// <param name="debugName">An optional name for debugging purposes. Can be <see langword="null"/>.</param>
    /// <returns>A <see cref="TextureResource"/> representing the created 2D texture.</returns>
    TextureResource CreateTexture2D(
        Format format,
        uint width,
        uint height,
        TextureUsageBits usage,
        StorageType storage,
        uint numLayers = 1,
        uint numSamples = 1,
        uint numMipLevels = 1,
        string? debugName = null
    )
    {
        CreateTexture(
                new TextureDesc
                {
                    Type = TextureType.Texture2D,
                    Format = format,
                    Dimensions = new Dimensions(width, height, 1),
                    NumLayers = numLayers,
                    NumSamples = numSamples,
                    NumMipLevels = numMipLevels,
                    Usage = usage,
                    Storage = storage,
                },
                out var texture,
                debugName
            )
            .CheckResult();
        return texture;
    }

    /// <summary>
    /// Create a 2D texture resource specifically configured for use as a render target, with the specified format, dimensions, and optional multisampling and mipmapping.
    /// </summary>
    /// <param name="format">The pixel format to use for the texture.</param>
    /// <param name="width">The width of the texture, in pixels. Must be greater than 0.</param>
    /// <param name="height">The height of the texture, in pixels. Must be greater than 0.</param>
    /// <param name="numLayers">The number of array layers in the texture. Must be at least 1. Defaults to 1.</param>
    /// <param name="numSamples">The number of samples per pixel for multisampling. Must be at least 1. Defaults to 1 (no multisampling).</param>
    /// <param name="numMipLevels">The number of mipmap levels for the texture. Must be at least 1. Defaults to 1 (no mipmaps).</param>
    /// <param name="debugName">An optional name for debugging purposes. Can be <see langword="null"/>.</param>
    /// <returns>A <see cref="TextureResource"/> representing the created render target.</returns>
    TextureResource CreateRenderTarget2D(
        Format format,
        uint width,
        uint height,
        uint numLayers = 1,
        uint numSamples = 1,
        uint numMipLevels = 1,
        string? debugName = null
    )
    {
        return CreateTexture2D(
            format,
            width,
            height,
            TextureUsageBits.Attachment | TextureUsageBits.Sampled,
            StorageType.Device,
            numLayers,
            numSamples,
            numMipLevels,
            debugName
        );
    }

    /// <summary>
    /// Creates a texture view from an existing texture.
    /// </summary>
    /// <param name="texture">The base texture handle.</param>
    /// <param name="desc">The texture view description.</param>
    /// <param name="textureView">Receives the created texture view resource.</param>
    /// <param name="debugName">Optional debug name for the texture view.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateTextureView(
        in TextureHandle texture,
        TextureViewDesc desc,
        out TextureResource textureView,
        string? debugName = null
    );

    /// <summary>
    /// Creates a compute pipeline.
    /// </summary>
    /// <param name="desc">The compute pipeline description.</param>
    /// <param name="computePipeline">Receives the created compute pipeline resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateComputePipeline(
        ComputePipelineDesc desc,
        out ComputePipelineResource computePipeline
    );

    /// <summary>
    /// Creates a render pipeline.
    /// </summary>
    /// <param name="desc">The render pipeline description.</param>
    /// <param name="renderPipeline">Receives the created render pipeline resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateRenderPipeline(
        RenderPipelineDesc desc,
        out RenderPipelineResource renderPipeline
    );

    /// <summary>
    /// Creates a shader module from shader code.
    /// </summary>
    /// <param name="desc">The shader module description.</param>
    /// <param name="shaderModule">Receives the created shader module resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateShaderModule(ShaderModuleDesc desc, out ShaderModuleResource shaderModule);

    /// <summary>
    /// Creates a query pool for GPU timestamp or performance queries.
    /// </summary>
    /// <param name="numQueries">The number of queries in the pool.</param>
    /// <param name="queryPool">Receives the created query pool resource.</param>
    /// <param name="debugName">Optional debug name for the query pool.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateQueryPool(
        uint32_t numQueries,
        out QueryPoolResource queryPool,
        string? debugName = null
    );

    /// <summary>
    /// Destroys a compute pipeline handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in ComputePipelineHandle handle);

    /// <summary>
    /// Destroys a render pipeline handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in RenderPipelineHandle handle);

    /// <summary>
    /// Destroys a shader module handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in ShaderModuleHandle handle);

    /// <summary>
    /// Destroys a sampler handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in SamplerHandle handle);

    /// <summary>
    /// Destroys a buffer handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in BufferHandle handle);

    /// <summary>
    /// Destroys a texture handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in TextureHandle handle);

    /// <summary>
    /// Destroys a query pool handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(in QueryPoolHandle handle);

    /// <summary>
    /// Uploads data to a buffer.
    /// </summary>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="offset">Byte offset within the buffer.</param>
    /// <param name="data">Pointer to the source data.</param>
    /// <param name="size">Size of the data in bytes.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Upload(in BufferHandle handle, size_t offset, nint data, size_t size);

    /// <summary>
    /// Uploads data of a specific unmanaged type to a buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the data.</typeparam>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="offset">Byte offset within the buffer.</param>
    /// <param name="data">Reference to the data to upload.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Upload<T>(in BufferHandle handle, size_t offset, T data)
        where T : unmanaged
    {
        return Upload(handle, offset, ref data);
    }

    /// <summary>
    /// Uploads data of a specific unmanaged type to a buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the data.</typeparam>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="offset">Byte offset within the buffer.</param>
    /// <param name="data">Reference to the data to upload.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Upload<T>(in BufferHandle handle, size_t offset, ref T data)
        where T : unmanaged
    {
        unsafe
        {
            var size = (size_t)sizeof(T);
            fixed (T* ptr = &data)
            {
                return Upload(handle, offset, (nint)ptr, size);
            }
        }
    }

    /// <summary>
    /// Downloads data from a buffer.
    /// </summary>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="data">Pointer to the destination buffer.</param>
    /// <param name="size">Size of the data to download in bytes.</param>
    /// <param name="offset">Byte offset within the buffer. Defaults to 0.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Download(in BufferHandle handle, nint data, size_t size, size_t offset = 0);

    /// <summary>
    /// Downloads data of a specific unmanaged type from a buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the data.</typeparam>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="data">Receives the downloaded data.</param>
    /// <param name="offset">Byte offset within the buffer. Defaults to 0.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Download<T>(in BufferHandle handle, out T data, size_t offset = 0)
        where T : unmanaged
    {
        unsafe
        {
            data = default; // Initialize data to default value to avoid CS0165
            var size = (size_t)sizeof(T);
            fixed (T* ptr = &data)
            {
                return Download(handle, (nint)ptr, size, offset);
            }
        }
    }

    /// <summary>
    /// Gets a mapped pointer to a host-visible buffer's memory.
    /// </summary>
    /// <param name="handle">The buffer handle.</param>
    /// <returns>A pointer to the mapped memory, or IntPtr.Zero if not mappable.</returns>
    nint GetMappedPtr(in BufferHandle handle);

    /// <summary>
    /// Gets the GPU device address of a buffer.
    /// </summary>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="offset">Byte offset within the buffer. Defaults to 0.</param>
    /// <returns>The 64-bit GPU address.</returns>
    uint64_t GpuAddress(in BufferHandle handle, size_t offset = 0);

    /// <summary>
    /// Flushes mapped memory to ensure writes are visible to the GPU.
    /// </summary>
    /// <param name="handle">The buffer handle.</param>
    /// <param name="offset">Byte offset of the region to flush.</param>
    /// <param name="size">Size of the region to flush in bytes.</param>
    void FlushMappedMemory(in BufferHandle handle, size_t offset, size_t size);

    /// <summary>
    /// Gets the maximum storage buffer range supported by the device.
    /// </summary>
    /// <returns>The maximum range in bytes.</returns>
    uint32_t GetMaxStorageBufferRange();

    // `data` contains mip-levels and layers as in https://registry.khronos.org/KTX/specs/1.0/ktxspec.v1.html
    /// <summary>
    /// Uploads data to a texture.
    /// </summary>
    /// <param name="handle">The texture handle.</param>
    /// <param name="range">The texture range to upload to.</param>
    /// <param name="data">Pointer to the source data. Data should be formatted as per KTX specification for mip-levels and layers.</param>
    /// <param name="dataSize">Size of the data in bytes.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    /// <remarks>
    /// Data layout follows the KTX specification: https://registry.khronos.org/KTX/specs/1.0/ktxspec.v1.html
    /// </remarks>
    ResultCode Upload(in TextureHandle handle, TextureRangeDesc range, nint data, size_t dataSize);

    /// <summary>
    /// Downloads data from a texture.
    /// </summary>
    /// <param name="handle">The texture handle.</param>
    /// <param name="range">The texture range to download from.</param>
    /// <param name="outData">Pointer to the destination buffer.</param>
    /// <param name="dataSize">Size of the destination buffer in bytes.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode Download(
        in TextureHandle handle,
        TextureRangeDesc range,
        nint outData,
        size_t dataSize
    );

    /// <summary>
    /// Gets the dimensions of a texture.
    /// </summary>
    /// <param name="handle">The texture handle.</param>
    /// <returns>The texture dimensions (width, height, depth).</returns>
    Dimensions GetDimensions(in TextureHandle handle);

    /// <summary>
    /// Gets the aspect ratio (width / height) of a texture.
    /// </summary>
    /// <param name="handle">The texture handle.</param>
    /// <returns>The aspect ratio as a float.</returns>
    float GetAspectRatio(in TextureHandle handle);

    /// <summary>
    /// Gets the pixel format of a texture.
    /// </summary>
    /// <param name="handle">The texture handle.</param>
    /// <returns>The texture format.</returns>
    Format GetFormat(in TextureHandle handle);

    /// <summary>
    /// Gets the current swapchain texture for presentation.
    /// </summary>
    /// <returns>A handle to the current swapchain texture.</returns>
    TextureHandle GetCurrentSwapchainTexture();

    /// <summary>
    /// Gets the pixel format of the swapchain.
    /// </summary>
    /// <returns>The swapchain format.</returns>
    Format GetSwapchainFormat();

    /// <summary>
    /// Gets the color space of the swapchain.
    /// </summary>
    /// <returns>The swapchain color space.</returns>
    ColorSpace GetSwapchainColorSpace();

    /// <summary>
    /// Gets the index of the current swapchain image.
    /// </summary>
    /// <returns>The current image index.</returns>
    uint32_t GetSwapchainCurrentImageIndex();

    /// <summary>
    /// Gets the total number of images in the swapchain.
    /// </summary>
    /// <returns>The number of swapchain images.</returns>
    uint32_t GetNumSwapchainImages();

    /// <summary>
    /// Recreates the swapchain with new dimensions.
    /// </summary>
    /// <param name="newWidth">The new width in pixels.</param>
    /// <param name="newHeight">The new height in pixels.</param>
    void RecreateSwapchain(int newWidth, int newHeight);

    // MSAA level is supported if ((samples & bitmask) != 0), where samples must be power of two.
    /// <summary>
    /// Gets a bitmask of supported MSAA sample counts for framebuffers.
    /// </summary>
    /// <returns>A bitmask where each bit represents a supported sample count. Check support using ((samples &amp; bitmask) != 0), where samples must be a power of two.</returns>
    uint32_t GetFramebufferMSAABitMask();

    /// <summary>
    /// Gets the GPU timestamp period converted to milliseconds.
    /// </summary>
    /// <returns>The period in milliseconds per timestamp unit.</returns>
    double GetTimestampPeriodToMs();

    /// <summary>
    /// Retrieves results from a query pool.
    /// </summary>
    /// <param name="pool">The query pool handle.</param>
    /// <param name="firstQuery">The index of the first query to retrieve.</param>
    /// <param name="queryCount">The number of queries to retrieve.</param>
    /// <param name="dataSize">The size of the output buffer in bytes.</param>
    /// <param name="outData">Pointer to the destination buffer for query results.</param>
    /// <param name="stride">The stride between consecutive query results in bytes.</param>
    /// <returns>True if results are available; false otherwise.</returns>
    bool GetQueryPoolResults(
        in QueryPoolHandle pool,
        uint32_t firstQuery,
        uint32_t queryCount,
        size_t dataSize,
        nint outData,
        size_t stride
    );

    /// <summary>
    /// Gets a value indicating whether a dedicated transfer queue is available for async uploads.
    /// </summary>
    /// <remarks>
    /// When a dedicated transfer queue is available, async upload operations can run concurrently
    /// with graphics/compute work. When not available, async uploads fall back to submitting
    /// transfer commands on the graphics queue from a background thread, which still provides
    /// asynchronous semantics but may contend with graphics work.
    /// </remarks>
    bool HasDedicatedTransferQueue => false;

    /// <summary>
    /// Asynchronously uploads data to a buffer on a background thread using the transfer queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method copies the data into an internal staging buffer and schedules a GPU transfer
    /// command on a dedicated transfer queue (if available) or the graphics queue. The caller's
    /// data can be freed immediately after this method returns.
    /// </para>
    /// <para>
    /// The returned <see cref="AsyncUploadHandle"/> can be polled via <see cref="AsyncUploadHandle.IsCompleted"/>
    /// or awaited via <see cref="AsyncUploadHandle.Task"/>.
    /// </para>
    /// <para>
    /// <b>Important:</b> The buffer must not be used for rendering until the upload is complete.
    /// </para>
    /// </remarks>
    /// <param name="handle">The buffer handle to upload data to.</param>
    /// <param name="offset">Byte offset within the buffer.</param>
    /// <param name="data">Data array.</param>
    /// <param name="count">Data count in data array.</param>
    /// <returns>An <see cref="AsyncUploadHandle{THandle}"/> that tracks the upload's completion,
    /// yielding the <see cref="BufferHandle"/> alongside the <see cref="ResultCode"/> when awaited.</returns>
    AsyncUploadHandle<BufferHandle> UploadAsync<T>(
        in BufferHandle handle,
        size_t offset,
        T[] data,
        size_t count
    )
        where T : unmanaged
    {
        // Default implementation: synchronous fallback
        unsafe
        {
            using var ptr = data.Pin();
            var result = Upload(
                handle,
                offset,
                (nint)ptr.Pointer,
                count * NativeHelper.SizeOf<T>()
            );
            return AsyncUploadHandle<BufferHandle>.CreateCompleted(result, handle);
        }
    }

    /// <summary>
    /// Asynchronously uploads data to a buffer on a background thread using the transfer queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method copies the data into an internal staging buffer and schedules a GPU transfer
    /// command on a dedicated transfer queue (if available) or the graphics queue. The caller's
    /// data can be freed immediately after this method returns.
    /// </para>
    /// <para>
    /// The returned <see cref="AsyncUploadHandle"/> can be polled via <see cref="AsyncUploadHandle.IsCompleted"/>
    /// or awaited via <see cref="AsyncUploadHandle.Task"/>.
    /// </para>
    /// <para>
    /// <b>Important:</b> The buffer must not be used for rendering until the upload is complete.
    /// </para>
    /// </remarks>
    /// <param name="handle">The buffer handle to upload data to.</param>
    /// <param name="offset">Byte offset within the buffer.</param>
    /// <param name="data">Data in <see cref="FastList{T}"/>.</param>
    /// <returns>An <see cref="AsyncUploadHandle{THandle}"/> that tracks the upload's completion,
    /// yielding the <see cref="BufferHandle"/> alongside the <see cref="ResultCode"/> when awaited.</returns>
    AsyncUploadHandle<BufferHandle> UploadAsync<T>(
        in BufferHandle handle,
        size_t offset,
        FastList<T> data
    )
        where T : unmanaged
    {
        return UploadAsync(handle, offset, data.GetInternalArray(), (size_t)data.Count);
    }

    /// <summary>
    /// Asynchronously uploads data to a texture on a background thread using the transfer queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method copies the data into an internal staging buffer and schedules a GPU transfer
    /// command on a dedicated transfer queue (if available) or the graphics queue. The caller's
    /// data can be freed immediately after this method returns.
    /// </para>
    /// <para>
    /// The returned <see cref="AsyncUploadHandle"/> can be polled via <see cref="AsyncUploadHandle.IsCompleted"/>
    /// or awaited via <see cref="AsyncUploadHandle.Task"/>.
    /// </para>
    /// <para>
    /// <b>Important:</b> The texture must not be used for rendering until the upload is complete.
    /// </para>
    /// </remarks>
    /// <param name="handle">The texture handle to upload data to.</param>
    /// <param name="range">The texture range to upload to.</param>
    /// <param name="data">Data array.</param>
    /// <param name="count">data count in data array</param>
    /// <returns>An <see cref="AsyncUploadHandle{THandle}"/> that tracks the upload's completion,
    /// yielding the <see cref="TextureHandle"/> alongside the <see cref="ResultCode"/> when awaited.</returns>
    AsyncUploadHandle<TextureHandle> UploadAsync<T>(
        in TextureHandle handle,
        TextureRangeDesc range,
        T[] data,
        size_t count
    )
        where T : unmanaged
    {
        // Default implementation: synchronous fallback
        unsafe
        {
            using var ptr = data.Pin();
            var result = Upload(handle, range, (nint)ptr.Pointer, count * NativeHelper.SizeOf<T>());
            return AsyncUploadHandle<TextureHandle>.CreateCompleted(result, handle);
        }
    }


    /// <summary>
    /// Generates mipmaps for a texture. The texture must have been created with the appropriate usage flags to allow for mipmap generation (e.g., TextureUsageBits.GenerateMips).
    /// </summary>
    /// <param name="texture">The texture handle for which to generate mipmaps.</param>
    /// <param name="levels">The number of mipmap levels generated.</param>
    void GenerateMipmap(in TextureHandle texture, out uint levels);
}
