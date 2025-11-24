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
    /// Submits a command buffer for execution on the GPU.
    /// </summary>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <param name="present">Optional texture to present to the swapchain. Use <see cref="TextureHandle.Null"/> for no presentation.</param>
    /// <returns>A <see cref="SubmitHandle"/> that can be used to wait for completion.</returns>
    SubmitHandle Submit(ICommandBuffer commandBuffer, in TextureHandle present);

    /// <summary>
    /// Waits for a submitted command buffer to complete execution.
    /// </summary>
    /// <param name="handle">The submit handle to wait on. Passing an empty handle waits for all GPU operations to complete (device idle).</param>
    void Wait(in SubmitHandle handle); // waiting on an empty handle results in vkDeviceWaitIdle()

    /// <summary>
    /// Creates a GPU buffer resource.
    /// </summary>
    /// <param name="desc">The buffer description.</param>
    /// <param name="buffer">Receives the created buffer resource.</param>
    /// <param name="debugName">Optional debug name for the buffer.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateBuffer(
        in BufferDesc desc,
        out BufferResource buffer,
        string? debugName = null
    );

    /// <summary>
    /// Creates a sampler state object.
    /// </summary>
    /// <param name="desc">The sampler state description.</param>
    /// <param name="sampler">Receives the created sampler resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateSampler(in SamplerStateDesc desc, out SamplerResource sampler);

    /// <summary>
    /// Creates a texture resource.
    /// </summary>
    /// <param name="desc">The texture description.</param>
    /// <param name="texture">Receives the created texture resource.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateTexture(
        in TextureDesc desc,
        out TextureResource texture,
        string? debugName = null
    );

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
        in TextureViewDesc desc,
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
        in ComputePipelineDesc desc,
        out ComputePipelineResource computePipeline
    );

    /// <summary>
    /// Creates a render pipeline.
    /// </summary>
    /// <param name="desc">The render pipeline description.</param>
    /// <param name="renderPipeline">Receives the created render pipeline resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateRenderPipeline(
        in RenderPipelineDesc desc,
        out RenderPipelineResource renderPipeline
    );

    /// <summary>
    /// Creates a shader module from shader code.
    /// </summary>
    /// <param name="desc">The shader module description.</param>
    /// <param name="shaderModule">Receives the created shader module resource.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    ResultCode CreateShaderModule(in ShaderModuleDesc desc, out ShaderModuleResource shaderModule);

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
    void Destroy(ComputePipelineHandle handle);

    /// <summary>
    /// Destroys a render pipeline handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(RenderPipelineHandle handle);

    /// <summary>
    /// Destroys a shader module handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(ShaderModuleHandle handle);

    /// <summary>
    /// Destroys a sampler handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(SamplerHandle handle);

    /// <summary>
    /// Destroys a buffer handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(BufferHandle handle);

    /// <summary>
    /// Destroys a texture handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(TextureHandle handle);

    /// <summary>
    /// Destroys a query pool handle.
    /// </summary>
    /// <param name="handle">The handle to destroy.</param>
    void Destroy(QueryPoolHandle handle);

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
    ResultCode Upload<T>(in BufferHandle handle, size_t offset, in T data)
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
    ResultCode Upload(
        in TextureHandle handle,
        in TextureRangeDesc range,
        nint data,
        size_t dataSize
    );

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
        in TextureRangeDesc range,
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
}

/// <summary>
/// Provides extension methods for the <see cref="IContext"/> interface to simplify common operations.
/// </summary>
public static class ContextExtensions
{
    /// <summary>
    /// Creates a shader module from GLSL source code.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="glsl">The GLSL source code as a string.</param>
    /// <param name="stage">The shader stage.</param>
    /// <param name="shaderModule">Receives the created shader module resource.</param>
    /// <param name="debugName">Optional debug name for the shader module.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    public static ResultCode CreateShaderModuleGlsl(
        this IContext context,
        string glsl,
        ShaderStage stage,
        out ShaderModuleResource shaderModule,
        string? debugName = null
    )
    {
        using var data = glsl.ToArray().Pin();
        unsafe
        {
            return context.CreateShaderModule(
                new ShaderModuleDesc
                {
                    Data = (nint)data.Pointer,
                    DataSize = (uint)glsl.Length,
                    Stage = stage,
                    DataType = ShaderDataType.Glsl,
                    DebugName = debugName ?? string.Empty,
                },
                out shaderModule
            );
        }
    }

    /// <summary>
    /// Creates a shader module from GLSL source code, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="glsl">The GLSL source code as a string.</param>
    /// <param name="stage">The shader stage.</param>
    /// <param name="debugName">Optional debug name for the shader module.</param>
    /// <returns>The created shader module resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if shader creation fails.</exception>
    public static ShaderModuleResource CreateShaderModuleGlsl(
        this IContext context,
        string glsl,
        ShaderStage stage,
        string? debugName = null
    )
    {
        using var data = glsl.ToArray().Pin();
        unsafe
        {
            context
                .CreateShaderModule(
                    new ShaderModuleDesc
                    {
                        Data = (nint)data.Pointer,
                        DataSize = (uint)glsl.Length,
                        Stage = stage,
                        DataType = ShaderDataType.Glsl,
                        DebugName = debugName ?? string.Empty,
                    },
                    out var shaderModule
                )
                .CheckResult();
            return shaderModule;
        }
    }

    /// <summary>
    /// Creates a compute pipeline, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="desc">The compute pipeline description.</param>
    /// <returns>The created compute pipeline resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if pipeline creation fails.</exception>
    public static ComputePipelineResource CreateComputePipeline(
        this IContext context,
        in ComputePipelineDesc desc
    )
    {
        context.CreateComputePipeline(desc, out var computePipeline).CheckResult();
        return computePipeline;
    }

    /// <summary>
    /// Creates a render pipeline, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="desc">The render pipeline description.</param>
    /// <returns>The created render pipeline resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if pipeline creation fails.</exception>
    public static RenderPipelineResource CreateRenderPipeline(
        this IContext context,
        in RenderPipelineDesc desc
    )
    {
        context.CreateRenderPipeline(desc, out var renderPipeline).CheckResult();
        return renderPipeline;
    }

    /// <summary>
    /// Creates a sampler, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="desc">The sampler state description.</param>
    /// <returns>The created sampler resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if sampler creation fails.</exception>
    public static SamplerResource CreateSampler(this IContext context, in SamplerStateDesc desc)
    {
        context.CreateSampler(desc, out var sampler).CheckResult();
        return sampler;
    }

    /// <summary>
    /// Creates a buffer from an array of unmanaged data.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the array elements.</typeparam>
    /// <param name="context">The graphics context.</param>
    /// <param name="data">The array of data to upload.</param>
    /// <param name="usage">Buffer usage flags.</param>
    /// <param name="storage">Storage type for the buffer.</param>
    /// <param name="buffer">Receives the created buffer resource.</param>
    /// <param name="debugName">Optional debug name for the buffer.</param>
    /// <returns>A <see cref="ResultCode"/> indicating success or failure.</returns>
    public static ResultCode CreateBuffer<T>(
        this IContext context,
        T[] data,
        BufferUsageBits usage,
        StorageType storage,
        out BufferResource buffer,
        string? debugName = null
    )
        where T : unmanaged
    {
        unsafe
        {
            using var pinnedData = data.Pin();
            return context.CreateBuffer(
                new BufferDesc(
                    usage,
                    storage,
                    (nint)pinnedData.Pointer,
                    (uint)(data.Length * sizeof(T)),
                    debugName
                ),
                out buffer,
                debugName
            );
        }
    }

    /// <summary>
    /// Creates a buffer from an array of unmanaged data, throwing an exception on failure.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the array elements.</typeparam>
    /// <param name="context">The graphics context.</param>
    /// <param name="data">The array of data to upload.</param>
    /// <param name="usage">Buffer usage flags.</param>
    /// <param name="storage">Storage type for the buffer.</param>
    /// <param name="debugName">Optional debug name for the buffer.</param>
    /// <returns>The created buffer resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if buffer creation fails.</exception>
    public static BufferResource CreateBuffer<T>(
        this IContext context,
        T[] data,
        BufferUsageBits usage,
        StorageType storage,
        string? debugName = null
    )
        where T : unmanaged
    {
        CreateBuffer(context, data, usage, storage, out var buffer, debugName).CheckResult();
        return buffer;
    }

    /// <summary>
    /// Creates a buffer, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="desc">The buffer description.</param>
    /// <param name="debugName">Optional debug name for the buffer.</param>
    /// <returns>The created buffer resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if buffer creation fails.</exception>
    public static BufferResource CreateBuffer(
        this IContext context,
        in BufferDesc desc,
        string? debugName = null
    )
    {
        context.CreateBuffer(desc, out var buffer, debugName).CheckResult();
        return buffer;
    }

    /// <summary>
    /// Creates a texture, throwing an exception on failure.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="desc">The texture description.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>The created texture resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if texture creation fails.</exception>
    public static TextureResource CreateTexture(
        this IContext context,
        in TextureDesc desc,
        string? debugName = null
    )
    {
        context.CreateTexture(desc, out var texture, debugName).CheckResult();
        return texture;
    }

    /// <summary>
    /// Submits a command buffer without presenting to the swapchain.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="commandBuffer">The command buffer to submit.</param>
    /// <returns>A <see cref="SubmitHandle"/> that can be used to wait for completion.</returns>
    public static SubmitHandle Submit(this IContext context, in ICommandBuffer commandBuffer)
    {
        return context.Submit(commandBuffer, TextureHandle.Null);
    }
}
