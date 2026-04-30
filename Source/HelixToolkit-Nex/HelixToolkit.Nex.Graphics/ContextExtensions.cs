using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Graphics;

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
        ComputePipelineDesc desc
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
        RenderPipelineDesc desc
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
    public static SamplerResource CreateSampler(this IContext context, SamplerStateDesc desc)
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
        BufferDesc desc,
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
        TextureDesc desc,
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
    public static SubmitHandle Submit(this IContext context, ICommandBuffer commandBuffer)
    {
        return context.Submit(commandBuffer, TextureHandle.Null);
    }
}
