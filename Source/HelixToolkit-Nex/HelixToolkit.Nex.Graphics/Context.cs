using System.Runtime.CompilerServices;

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
    ICommandBuffer CreateSecondaryCommandBuffer(in RenderPass renderPassInfo);

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
    /// <returns>An <see cref="AsyncUploadHandle"/> that tracks the upload's completion.</returns>
    AsyncUploadHandle UploadAsync<T>(in BufferHandle handle, size_t offset, T[] data, size_t count)
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
            var h = new AsyncUploadHandle();
            h.Complete(result);
            return h;
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
    /// <returns>An <see cref="AsyncUploadHandle"/> that tracks the upload's completion.</returns>
    AsyncUploadHandle UploadAsync<T>(in BufferHandle handle, size_t offset, FastList<T> data)
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
    /// <returns>An <see cref="AsyncUploadHandle"/> that tracks the upload's completion.</returns>
    AsyncUploadHandle UploadAsync<T>(
        in TextureHandle handle,
        in TextureRangeDesc range,
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
            var h = new AsyncUploadHandle();
            h.Complete(result);
            return h;
        }
    }
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
        unsafe
        {
            return context.CreateShaderModule(
                new ShaderModuleDesc
                {
                    GlslSource = glsl,
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
        unsafe
        {
            context
                .CreateShaderModule(
                    new ShaderModuleDesc
                    {
                        GlslSource = glsl,
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
    /// Creates a compute pipeline using the specified compute shader and an optional debug name.
    /// </summary>
    /// <remarks>This method wraps the creation of a compute pipeline, ensuring that the provided compute
    /// shader is used. The debug name, if provided, can be used for diagnostic purposes.</remarks>
    /// <param name="context">The context used to create the compute pipeline. Cannot be <see langword="null"/>.</param>
    /// <param name="computeShader">The compute shader module to be used in the pipeline. Cannot be <see langword="null"/>.</param>
    /// <param name="debugName">An optional debug name for the compute pipeline. If <see langword="null"/>, an empty string is used.</param>
    /// <returns>The created compute pipeline resource.</returns>
    public static ComputePipelineResource CreateComputePipeline(
        this IContext context,
        ShaderModuleResource computeShader,
        string? debugName = null
    )
    {
        context
            .CreateComputePipeline(
                new ComputePipelineDesc
                {
                    ComputeShader = computeShader,
                    DebugName = debugName ?? string.Empty,
                },
                out var computePipeline
            )
            .CheckResult();
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
    /// Creates a new buffer resource initialized with the specified data.
    /// </summary>
    /// <remarks>This method creates a buffer resource and initializes it with the contents of <paramref
    /// name="data"/>. The buffer's size is determined by the size of <typeparamref name="T"/>. The caller is
    /// responsible for releasing the buffer resource when it is no longer needed.</remarks>
    /// <typeparam name="T">The type of the data used to initialize the buffer. Must be an unmanaged type.</typeparam>
    /// <param name="context">The graphics context used to create the buffer.</param>
    /// <param name="data">The value to initialize the buffer with. The type must be unmanaged.</param>
    /// <param name="usage">A bitmask specifying the intended usage of the buffer.</param>
    /// <param name="storage">The storage type that determines how the buffer's memory is allocated and accessed.</param>
    /// <param name="buffer">When this method returns, contains the created <see cref="BufferResource"/> if the operation succeeds;
    /// otherwise, contains <see langword="null"/>.</param>
    /// <param name="debugName">An optional name for the buffer resource, used for debugging purposes. Can be <see langword="null"/>.</param>
    /// <returns>A <see cref="ResultCode"/> indicating the result of the buffer creation operation. Returns <see
    /// cref="ResultCode.Success"/> if the buffer was created successfully; otherwise, returns an error code.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ResultCode CreateBuffer<T>(
        this IContext context,
        T data,
        BufferUsageBits usage,
        StorageType storage,
        out BufferResource buffer,
        string? debugName = null
    )
        where T : unmanaged
    {
        unsafe
        {
            return context.CreateBuffer(
                new BufferDesc(usage, storage, (nint)(&data), (uint)sizeof(T), debugName),
                out buffer,
                debugName
            );
        }
    }

    /// <summary>
    /// Creates a new buffer resource initialized with the specified data.
    /// </summary>
    /// <typeparam name="T">The type of the data to initialize the buffer with. Must be an unmanaged type.</typeparam>
    /// <param name="context">The graphics context used to create the buffer resource.</param>
    /// <param name="data">The value to initialize the buffer with. Must be an unmanaged type.</param>
    /// <param name="usage">A bitmask specifying how the buffer will be used (e.g., for vertex data, index data, etc.).</param>
    /// <param name="storage">The type of storage to use for the buffer, such as device-local or host-visible memory.</param>
    /// <param name="debugName">An optional name for the buffer resource, used for debugging purposes. Can be <see langword="null"/>.</param>
    /// <returns>A <see cref="BufferResource"/> representing the newly created buffer initialized with <paramref name="data"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BufferResource CreateBuffer<T>(
        this IContext context,
        T data,
        BufferUsageBits usage,
        StorageType storage,
        string? debugName = null
    )
        where T : unmanaged
    {
        context.CreateBuffer(data, usage, storage, out var buffer, debugName).CheckResult();
        return buffer;
    }

    /// <summary>
    /// Creates a new buffer resource from the specified list of unmanaged data elements.
    /// </summary>
    /// <remarks>The buffer is initialized with the contents of <paramref name="data"/>. The usage and storage
    /// parameters control how the buffer can be accessed and where it is allocated.</remarks>
    /// <typeparam name="T">The type of elements in the buffer. Must be an unmanaged type.</typeparam>
    /// <param name="context">The context in which the buffer will be created. Must not be <c>null</c>.</param>
    /// <param name="data">The list of unmanaged elements to initialize the buffer with. The buffer will contain a copy of these elements.</param>
    /// <param name="usage">A set of flags specifying the intended usage of the buffer, such as read, write, or copy operations.</param>
    /// <param name="storage">The storage type that determines how and where the buffer's memory is allocated.</param>
    /// <param name="buffer">When this method returns, contains the created <see cref="BufferResource"/> if the operation succeeds;
    /// otherwise, <c>null</c>.</param>
    /// <param name="debugName">An optional name for debugging purposes. If <c>null</c>, no debug name is assigned.</param>
    /// <returns>A <see cref="ResultCode"/> value indicating the result of the buffer creation operation.</returns>
    public static ResultCode CreateBuffer<T>(
        this IContext context,
        FastList<T> data,
        BufferUsageBits usage,
        StorageType storage,
        out BufferResource buffer,
        string? debugName = null
    )
        where T : unmanaged
    {
        return CreateBuffer(
            context,
            data.GetInternalArray(),
            data.Count,
            usage,
            storage,
            out buffer,
            debugName
        );
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
        return CreateBuffer(context, data, data.Length, usage, storage, out buffer, debugName);
    }

    /// <summary>
    /// Creates a new buffer resource and initializes it with the specified data.
    /// </summary>
    /// <remarks>The buffer is created using the provided usage and storage options, and is initialized with
    /// the contents of <paramref name="data"/> up to <paramref name="count"/> elements. The buffer can be used for GPU
    /// operations as defined by <paramref name="usage"/>. The caller is responsible for ensuring that <paramref
    /// name="data"/> contains at least <paramref name="count"/> elements.</remarks>
    /// <typeparam name="T"></typeparam>
    /// <param name="context">The context used to create the buffer resource.</param>
    /// <param name="data">The array of unmanaged elements to initialize the buffer with. Must not be <see langword="null"/> and must
    /// contain at least <paramref name="count"/> elements.</param>
    /// <param name="count">The number of elements from <paramref name="data"/> to copy into the buffer. Must be non-negative and not exceed
    /// the length of <paramref name="data"/>.</param>
    /// <param name="usage">A set of flags specifying how the buffer will be used (e.g., for vertex data, index data, etc.).</param>
    /// <param name="storage">The storage type indicating where and how the buffer will be allocated (e.g., device-local, host-visible).</param>
    /// <param name="buffer">When this method returns, contains the created buffer resource initialized with the specified data.</param>
    /// <param name="debugName">An optional name for the buffer resource, used for debugging purposes. Can be <see langword="null"/>.</param>
    /// <returns>A <see cref="ResultCode"/> indicating the result of the buffer creation operation. Returns <see
    /// cref="ResultCode.Success"/> if the buffer was created successfully; otherwise, returns an error code.</returns>
    public static ResultCode CreateBuffer<T>(
        this IContext context,
        T[] data,
        int count,
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
                    (uint)(count * sizeof(T)),
                    debugName
                ),
                out buffer,
                debugName
            );
        }
    }

    /// <summary>
    /// Creates a new <see cref="BufferResource"/> containing the elements of the specified list, with the given usage
    /// and storage options.
    /// </summary>
    /// <remarks>The returned buffer will have a size equal to the number of elements in <paramref
    /// name="data"/>. The buffer's usage and storage are determined by the <paramref name="usage"/> and <paramref
    /// name="storage"/> parameters.</remarks>
    /// <typeparam name="T">The type of elements in the buffer. Must be an unmanaged type.</typeparam>
    /// <param name="context">The graphics context used to create the buffer. Must not be <c>null</c>.</param>
    /// <param name="data">The list of elements to populate the buffer. The buffer will contain exactly <paramref name="data"/>.Count
    /// elements.</param>
    /// <param name="usage">A set of flags specifying how the buffer will be used (e.g., read, write, copy).</param>
    /// <param name="storage">The type of memory storage to use for the buffer (e.g., device-local, host-visible).</param>
    /// <param name="debugName">An optional name for debugging purposes. If <c>null</c>, no debug name is assigned.</param>
    /// <returns>A <see cref="BufferResource"/> containing the data from <paramref name="data"/> and configured with the
    /// specified usage and storage.</returns>
    public static BufferResource CreateBuffer<T>(
        this IContext context,
        FastList<T> data,
        BufferUsageBits usage,
        StorageType storage,
        string? debugName = null
    )
        where T : unmanaged
    {
        CreateBuffer(
                context,
                data.GetInternalArray(),
                data.Count,
                usage,
                storage,
                out var buffer,
                debugName
            )
            .CheckResult();
        return buffer;
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
    /// Creates a new <see cref="BufferResource"/> containing the specified data and configuration.
    /// </summary>
    /// <remarks>The buffer is created with the specified usage and storage options, and is initialized with
    /// the first <paramref name="count"/> elements from <paramref name="data"/>. The caller is responsible for
    /// disposing the returned <see cref="BufferResource"/> when it is no longer needed.</remarks>
    /// <typeparam name="T">The unmanaged value type of the buffer elements.</typeparam>
    /// <param name="context">The graphics context used to create the buffer. Must not be <c>null</c>.</param>
    /// <param name="data">The array of elements to initialize the buffer with. The array length must be at least <paramref name="count"/>.</param>
    /// <param name="count">The number of elements from <paramref name="data"/> to include in the buffer. Must be non-negative and not
    /// greater than <paramref name="data"/>.Length.</param>
    /// <param name="usage">A set of flags specifying how the buffer will be used (e.g., for vertex data, index data, etc.).</param>
    /// <param name="storage">The storage type that determines how and where the buffer's memory is allocated.</param>
    /// <param name="debugName">An optional name for the buffer resource, used for debugging and profiling. Can be <c>null</c>.</param>
    /// <returns>A <see cref="BufferResource"/> initialized with the specified data and configuration.</returns>
    public static BufferResource CreateBuffer<T>(
        this IContext context,
        T[] data,
        int count,
        BufferUsageBits usage,
        StorageType storage,
        string? debugName = null
    )
        where T : unmanaged
    {
        CreateBuffer(context, data, count, usage, storage, out var buffer, debugName).CheckResult();
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
