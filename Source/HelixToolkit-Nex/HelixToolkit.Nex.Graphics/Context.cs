namespace HelixToolkit.Nex.Graphics;

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
    void Destroy(Framebuffer fb);


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