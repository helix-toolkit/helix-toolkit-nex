namespace HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex;

public interface IContext : IDisposable
{
    ResultCode Initialize();
    ICommandBuffer AcquireCommandBuffer();
    SubmitHandle Submit(ICommandBuffer commandBuffer, in TextureHandle present);
    void Wait(in SubmitHandle handle); // waiting on an empty handle results in vkDeviceWaitIdle()
    ResultCode CreateBuffer(in BufferDesc desc, out BufferHolder buffer, string? debugName = null);
    ResultCode CreateSampler(in SamplerStateDesc desc, out SamplerHolder sampler);
    ResultCode CreateTexture(in TextureDesc desc, out TextureHolder texture, string? debugName = null);
    ResultCode CreateTextureView(in TextureHandle texture, in TextureViewDesc desc, out TextureHolder textureView, string? debugName = null);
    ResultCode CreateComputePipeline(in ComputePipelineDesc desc, out ComputePipelineHolder computePipeline);
    ResultCode CreateRenderPipeline(in RenderPipelineDesc desc, out RenderPipelineHolder renderPipeline);
    ResultCode CreateShaderModule(in ShaderModuleDesc desc, out ShaderModuleHolder shaderModule);

    ResultCode CreateQueryPool(uint32_t numQueries, out QueryPoolHolder queryPool, string? debugName = null);


    void Destroy(ComputePipelineHandle handle);
    void Destroy(RenderPipelineHandle handle);
    void Destroy(ShaderModuleHandle handle);
    void Destroy(SamplerHandle handle);
    void Destroy(BufferHandle handle);
    void Destroy(TextureHandle handle);
    void Destroy(QueryPoolHandle handle);
    void Destroy(in Framebuffer fb);


    ResultCode Upload(in BufferHandle handle, size_t offset, nint data, size_t size);
    ResultCode Upload<T>(in BufferHandle handle, size_t offset, in T data) where T : unmanaged
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
    ResultCode Download(in BufferHandle handle, nint data, size_t size, size_t offset = 0);
    ResultCode Download<T>(in BufferHandle handle, out T data, size_t offset = 0) where T : unmanaged
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
    nint GetMappedPtr(in BufferHandle handle);
    uint64_t GpuAddress(in BufferHandle handle, size_t offset = 0);
    void FlushMappedMemory(in BufferHandle handle, size_t offset, size_t size);
    uint32_t GetMaxStorageBufferRange();


    // `data` contains mip-levels and layers as in https://registry.khronos.org/KTX/specs/1.0/ktxspec.v1.html
    ResultCode Upload(in TextureHandle handle, in TextureRangeDesc range, nint data, size_t dataSize);
    ResultCode Download(in TextureHandle handle, in TextureRangeDesc range, nint outData, size_t dataSize);
    Dimensions GetDimensions(in TextureHandle handle);
    float GetAspectRatio(in TextureHandle handle);
    Format GetFormat(in TextureHandle handle);


    TextureHandle GetCurrentSwapchainTexture();
    Format GetSwapchainFormat();
    ColorSpace GetSwapchainColorSpace();
    uint32_t GetSwapchainCurrentImageIndex();
    uint32_t GetNumSwapchainImages();
    void RecreateSwapchain(int newWidth, int newHeight);

    // MSAA level is supported if ((samples & bitmask) != 0), where samples must be power of two.
    uint32_t GetFramebufferMSAABitMask();

    double GetTimestampPeriodToMs();
    bool GetQueryPoolResults(in QueryPoolHandle pool, uint32_t firstQuery, uint32_t queryCount, size_t dataSize, nint outData, size_t stride);
}

public static class ContextExtensions
{
    public static ResultCode CreateShaderModuleGlsl(this IContext context, string glsl, ShaderStage stage,
        out ShaderModuleHolder shaderModule, string? debugName = null)
    {
        using var data = glsl.ToArray().Pin();
        unsafe
        {
            return context.CreateShaderModule(new ShaderModuleDesc
            {
                Data = (nint)data.Pointer,
                DataSize = (uint)glsl.Length,
                Stage = stage,
                DataType = ShaderDataType.Glsl,
                DebugName = debugName ?? string.Empty
            }, out shaderModule);
        }
    }

    public static ShaderModuleHolder CreateShaderModuleGlsl(this IContext context, string glsl, ShaderStage stage, string? debugName = null)
    {
        using var data = glsl.ToArray().Pin();
        unsafe
        {
            context.CreateShaderModule(new ShaderModuleDesc
            {
                Data = (nint)data.Pointer,
                DataSize = (uint)glsl.Length,
                Stage = stage,
                DataType = ShaderDataType.Glsl,
                DebugName = debugName ?? string.Empty
            }, out var shaderModule).CheckResult();
            return shaderModule;
        }
    }

    public static SamplerHolder CreateSampler(this IContext context, in SamplerStateDesc desc)
    {
        context.CreateSampler(desc, out var sampler).CheckResult();
        return sampler;
    }

    public static ResultCode CreateBuffer<T>(this IContext context, T[] data, BufferUsageBits usage,
        StorageType storage, out BufferHolder buffer, string? debugName = null) where T : unmanaged
    {
        unsafe
        {
            using var pinnedData = data.Pin();
            return context.CreateBuffer(new BufferDesc(usage, storage, (nint)pinnedData.Pointer, (uint)(data.Length * sizeof(T)), debugName), out buffer, debugName);
        }
    }

    public static BufferHolder CreateBuffer<T>(this IContext context, T[] data, BufferUsageBits usage,
        StorageType storage, string? debugName = null) where T : unmanaged
    {
        CreateBuffer(context, data, usage, storage, out var buffer, debugName).CheckResult();
        return buffer;
    }

    public static BufferHolder CreateBuffer(this IContext context, in BufferDesc desc, string? debugName = null)
    {
        context.CreateBuffer(desc, out var buffer, debugName).CheckResult();
        return buffer;
    }

    public static TextureHolder CreateTexture(this IContext context, in TextureDesc desc, string? debugName = null)
    {
        context.CreateTexture(desc, out var texture, debugName).CheckResult();
        return texture;
    }
}