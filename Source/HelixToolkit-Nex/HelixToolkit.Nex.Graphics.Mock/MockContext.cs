namespace HelixToolkit.Nex.Graphics.Mock;

/// <summary>
/// Mock implementation of <see cref="IContext"/> for unit testing.
/// </summary>
/// <remarks>
/// This class provides a testable implementation of the graphics context interface
/// that tracks method calls, validates parameters, and returns mock resources without
/// requiring actual GPU access.
/// </remarks>
public class MockContext : IContext
{
    private readonly ConcurrentDictionary<BufferHandle, MockBufferData> _buffers = new();
    private readonly ConcurrentDictionary<TextureHandle, MockTextureData> _textures = new();
    private readonly ConcurrentDictionary<SamplerHandle, MockSamplerData> _samplers = new();
    private readonly ConcurrentDictionary<ShaderModuleHandle, MockShaderData> _shaderModules =
        new();
    private readonly ConcurrentDictionary<
        ComputePipelineHandle,
        MockComputePipelineData
    > _computePipelines = new();
    private readonly ConcurrentDictionary<
        RenderPipelineHandle,
        MockRenderPipelineData
    > _renderPipelines = new();
    private readonly ConcurrentDictionary<QueryPoolHandle, MockQueryPoolData> _queryPools = new();

    private uint _handleCounter = 1;
    private readonly object _handleLock = new();
    private bool _isInitialized = false;

    /// <summary>
    /// Gets whether the context has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets whether the context has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; } = false;

    /// <summary>
    /// Gets the name of this mock context.
    /// </summary>
    public string Name => "MockContext";

    /// <summary>
    /// Gets or sets the swapchain format for testing.
    /// </summary>
    public Format SwapchainFormat { get; set; } = Format.BGRA_UN8;

    /// <summary>
    /// Gets or sets the swapchain color space for testing.
    /// </summary>
    public ColorSpace SwapchainColorSpace { get; set; } = ColorSpace.SRGB_NONLINEAR;

    /// <summary>
    /// Gets or sets the number of swapchain images.
    /// </summary>
    public uint NumSwapchainImages { get; set; } = 3;

    /// <summary>
    /// Gets or sets the current swapchain image index.
    /// </summary>
    public uint CurrentSwapchainImageIndex { get; set; } = 0;

    /// <summary>
    /// Gets the current swapchain texture handle.
    /// </summary>
    public TextureHandle CurrentSwapchainTexture { get; private set; }

    /// <summary>
    /// Gets or sets the maximum storage buffer range.
    /// </summary>
    public uint MaxStorageBufferRange { get; set; } = 128 * 1024 * 1024; // 128MB

    /// <summary>
    /// Gets or sets the framebuffer MSAA bit mask.
    /// </summary>
    public uint FramebufferMSAABitMask { get; set; } = 0x7F; // All sample counts

    /// <summary>
    /// Gets or sets the timestamp period in milliseconds.
    /// </summary>
    public double TimestampPeriodMs { get; set; } = 0.000001; // 1ns in ms

    /// <summary>
    /// Gets the list of acquired command buffers for validation.
    /// </summary>
    public FastList<ICommandBuffer> AcquiredCommandBuffers { get; } = new();

    /// <summary>
    /// Gets the list of submitted command buffers with their handles.
    /// </summary>
    public FastList<(ICommandBuffer, SubmitHandle, TextureHandle)> SubmittedCommands { get; } =
        new();

    /// <summary>
    /// Initializes the mock context.
    /// </summary>
    public ResultCode Initialize()
    {
        if (_isInitialized)
            return ResultCode.Ok;

        // Create a mock swapchain texture
        var swapchainDesc = new TextureDesc
        {
            Type = TextureType.Texture2D,
            Format = SwapchainFormat,
            Dimensions = new Dimensions(1920, 1080, 1),
            Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
            NumMipLevels = 1,
            NumLayers = 1,
        };

        CreateTexture(swapchainDesc, out var swapchainTexture, "SwapchainTexture");
        CurrentSwapchainTexture = swapchainTexture.Handle;

        _isInitialized = true;
        return ResultCode.Ok;
    }

    /// <summary>
    /// Tears down the mock context.
    /// </summary>
    public ResultCode Teardown()
    {
        if (!_isInitialized)
            return ResultCode.Ok;

        _buffers.Clear();
        _textures.Clear();
        _samplers.Clear();
        _shaderModules.Clear();
        _computePipelines.Clear();
        _renderPipelines.Clear();
        _queryPools.Clear();
        AcquiredCommandBuffers.Clear();
        SubmittedCommands.Clear();

        _isInitialized = false;
        return ResultCode.Ok;
    }

    private Handle<T> AllocateHandle<T>()
    {
        lock (_handleLock)
        {
            return new Handle<T>(_handleCounter++, 1);
        }
    }

    public ICommandBuffer AcquireCommandBuffer()
    {
        var cmdBuffer = new MockCommandBuffer(this, isPrimary: true);
        AcquiredCommandBuffers.Add(cmdBuffer);
        return cmdBuffer;
    }

    public ICommandBuffer CreateSecondaryCommandBuffer(RenderPass renderPassInfo)
    {
        var cmdBuffer = new MockCommandBuffer(this, isPrimary: false);
        AcquiredCommandBuffers.Add(cmdBuffer);
        return cmdBuffer;
    }

    public SubmitHandle Submit(
        ICommandBuffer commandBuffer,
        in TextureHandle present,
        KeyedMutexSyncInfo syncInfo
    )
    {
        if (commandBuffer is not MockCommandBuffer mockCmdBuffer)
            throw new ArgumentException(
                "Command buffer must be a MockCommandBuffer",
                nameof(commandBuffer)
            );

        var submitHandle = new SubmitHandle { BufferIndex = _handleCounter++, SubmitId = 1 };
        SubmittedCommands.Add((commandBuffer, submitHandle, present));
        mockCmdBuffer.IsSubmitted = true;

        return submitHandle;
    }

    public void Wait(in SubmitHandle handle)
    {
        // Mock: immediately complete
    }

    public ResultCode CreateBuffer(
        BufferDesc desc,
        out BufferResource buffer,
        string? debugName = null
    )
    {
        var handle = AllocateHandle<Buffer>();
        var mockData = new MockBufferData
        {
            Desc = desc,
            DebugName = debugName ?? string.Empty,
            Data = new byte[desc.DataSize],
        };

        if (desc.Data != IntPtr.Zero && desc.DataSize > 0)
        {
            unsafe
            {
                fixed (byte* dst = mockData.Data)
                {
                    System.Buffer.MemoryCopy((void*)desc.Data, dst, desc.DataSize, desc.DataSize);
                }
            }
        }

        _buffers[handle] = mockData;
        buffer = new BufferResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateSampler(SamplerStateDesc desc, out SamplerResource sampler)
    {
        var handle = AllocateHandle<Sampler>();
        var mockData = new MockSamplerData { Desc = desc };
        _samplers[handle] = mockData;
        sampler = new SamplerResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateTexture(
        TextureDesc desc,
        out TextureResource texture,
        string? debugName = null
    )
    {
        var handle = AllocateHandle<Texture>();
        var mockData = new MockTextureData { Desc = desc, DebugName = debugName ?? string.Empty };
        _textures[handle] = mockData;
        texture = new TextureResource(this, handle);
        return ResultCode.Ok;
    }

    /// <summary>
    /// Gets the <see cref="TextureDesc"/> that was used to create the texture with the given handle.
    /// Returns null if the handle is not found.
    /// </summary>
    public TextureDesc? GetTextureDesc(in TextureHandle handle)
    {
        if (_textures.TryGetValue(handle, out var data))
            return data.Desc;
        return null;
    }

    public ResultCode CreateTextureView(
        in TextureHandle texture,
        TextureViewDesc desc,
        out TextureResource textureView,
        string? debugName = null
    )
    {
        if (!_textures.ContainsKey(texture))
        {
            textureView = TextureResource.Null;
            return ResultCode.ArgumentError;
        }

        var handle = AllocateHandle<Texture>();
        var mockData = new MockTextureData
        {
            Desc = _textures[texture].Desc,
            DebugName = debugName ?? string.Empty,
            IsView = true,
            BaseTexture = texture,
        };
        _textures[handle] = mockData;
        textureView = new TextureResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateComputePipeline(
        ComputePipelineDesc desc,
        out ComputePipelineResource computePipeline
    )
    {
        var handle = AllocateHandle<ComputePipeline>();
        var mockData = new MockComputePipelineData { Desc = desc };
        _computePipelines[handle] = mockData;
        computePipeline = new ComputePipelineResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateRenderPipeline(
        RenderPipelineDesc desc,
        out RenderPipelineResource renderPipeline
    )
    {
        var handle = AllocateHandle<RenderPipeline>();
        var mockData = new MockRenderPipelineData { Desc = desc };
        _renderPipelines[handle] = mockData;
        renderPipeline = new RenderPipelineResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateShaderModule(
        ShaderModuleDesc desc,
        out ShaderModuleResource shaderModule
    )
    {
        var handle = AllocateHandle<ShaderModule>();
        var mockData = new MockShaderData { Desc = desc };
        _shaderModules[handle] = mockData;
        shaderModule = new ShaderModuleResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateQueryPool(
        uint numQueries,
        out QueryPoolResource queryPool,
        string? debugName = null
    )
    {
        var handle = AllocateHandle<QueryPool>();
        var mockData = new MockQueryPoolData
        {
            NumQueries = numQueries,
            DebugName = debugName ?? string.Empty,
            Results = new ulong[numQueries],
        };
        _queryPools[handle] = mockData;
        queryPool = new QueryPoolResource(this, handle);
        return ResultCode.Ok;
    }

    public void Destroy(in ComputePipelineHandle handle)
    {
        _computePipelines.TryRemove(handle, out _);
    }

    public void Destroy(in RenderPipelineHandle handle)
    {
        _renderPipelines.TryRemove(handle, out _);
    }

    public void Destroy(in ShaderModuleHandle handle)
    {
        _shaderModules.TryRemove(handle, out _);
    }

    public void Destroy(in SamplerHandle handle)
    {
        _samplers.TryRemove(handle, out _);
    }

    public void Destroy(in BufferHandle handle)
    {
        _buffers.TryRemove(handle, out _);
    }

    public void Destroy(in TextureHandle handle)
    {
        _textures.TryRemove(handle, out _);
    }

    public void Destroy(in QueryPoolHandle handle)
    {
        _queryPools.TryRemove(handle, out _);
    }

    public ResultCode Upload(in BufferHandle handle, size_t offset, nint data, size_t size)
    {
        if (!_buffers.TryGetValue(handle, out var bufferData))
            return ResultCode.ArgumentError;

        if (offset + size > bufferData.Data.Length)
            return ResultCode.ArgumentOutOfRange;

        unsafe
        {
            fixed (byte* dst = &bufferData.Data[offset])
            {
                System.Buffer.MemoryCopy((void*)data, dst, size, size);
            }
        }

        return ResultCode.Ok;
    }

    public ResultCode Download(in BufferHandle handle, nint data, size_t size, size_t offset = 0)
    {
        if (!_buffers.TryGetValue(handle, out var bufferData))
            return ResultCode.ArgumentError;

        if (offset + size > bufferData.Data.Length)
            return ResultCode.ArgumentOutOfRange;

        unsafe
        {
            fixed (byte* src = &bufferData.Data[offset])
            {
                System.Buffer.MemoryCopy(src, (void*)data, size, size);
            }
        }

        return ResultCode.Ok;
    }

    public nint GetMappedPtr(in BufferHandle handle)
    {
        if (!_buffers.TryGetValue(handle, out var bufferData))
            return IntPtr.Zero;

        unsafe
        {
            fixed (byte* ptr = bufferData.Data)
            {
                return (nint)ptr;
            }
        }
    }

    public uint64_t GpuAddress(in BufferHandle handle, size_t offset = 0)
    {
        // Return a mock GPU address
        return ((ulong)handle.Index << 32) | offset;
    }

    public void FlushMappedMemory(in BufferHandle handle, size_t offset, size_t size)
    {
        // Mock: no-op
    }

    public uint GetMaxStorageBufferRange()
    {
        return MaxStorageBufferRange;
    }

    public ResultCode Upload(
        in TextureHandle handle,
        TextureRangeDesc range,
        nint data,
        size_t dataSize
    )
    {
        if (!_textures.TryGetValue(handle, out var textureData))
            return ResultCode.ArgumentError;

        // Mock: store upload info for validation
        textureData.UploadCount++;
        return ResultCode.Ok;
    }

    public ResultCode Download(
        in TextureHandle handle,
        TextureRangeDesc range,
        nint outData,
        size_t dataSize
    )
    {
        if (!_textures.TryGetValue(handle, out _))
            return ResultCode.ArgumentError;

        // Mock: fill with dummy data
        unsafe
        {
            var ptr = (byte*)outData;
            for (uint i = 0; i < dataSize; i++)
                ptr[i] = (byte)(i % 256);
        }

        return ResultCode.Ok;
    }

    public Dimensions GetDimensions(in TextureHandle handle)
    {
        if (!_textures.TryGetValue(handle, out var textureData))
            return new Dimensions(0, 0, 0);

        return textureData.Desc.Dimensions;
    }

    public float GetAspectRatio(in TextureHandle handle)
    {
        var dim = GetDimensions(handle);
        return dim.Height > 0 ? (float)dim.Width / dim.Height : 0f;
    }

    public Format GetFormat(in TextureHandle handle)
    {
        if (!_textures.TryGetValue(handle, out var textureData))
            return Format.Invalid;

        return textureData.Desc.Format;
    }

    public TextureHandle GetCurrentSwapchainTexture()
    {
        return CurrentSwapchainTexture;
    }

    public Format GetSwapchainFormat()
    {
        return SwapchainFormat;
    }

    public ColorSpace GetSwapchainColorSpace()
    {
        return SwapchainColorSpace;
    }

    public uint GetSwapchainCurrentImageIndex()
    {
        return CurrentSwapchainImageIndex;
    }

    public uint GetNumSwapchainImages()
    {
        return NumSwapchainImages;
    }

    public void RecreateSwapchain(int newWidth, int newHeight)
    {
        // Mock: just update dimensions
        if (_textures.TryGetValue(CurrentSwapchainTexture, out var textureData))
        {
            var desc = textureData.Desc;
            desc.Dimensions = new Dimensions((uint)newWidth, (uint)newHeight, 1);
            textureData.Desc = desc;
        }
    }

    public uint GetFramebufferMSAABitMask()
    {
        return FramebufferMSAABitMask;
    }

    public double GetTimestampPeriodToMs()
    {
        return TimestampPeriodMs;
    }

    public bool GetQueryPoolResults(
        in QueryPoolHandle pool,
        uint firstQuery,
        uint queryCount,
        size_t dataSize,
        nint outData,
        size_t stride
    )
    {
        if (!_queryPools.TryGetValue(pool, out var poolData))
            return false;

        if (firstQuery + queryCount > poolData.NumQueries)
            return false;

        unsafe
        {
            var ptr = (ulong*)outData;
            for (uint i = 0; i < queryCount; i++)
            {
                *ptr = poolData.Results[firstQuery + i];
                ptr = (ulong*)((byte*)ptr + stride);
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        Teardown();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    public void GenerateMipmap(in TextureHandle texture, out uint levels)
    {
        levels = 0;
    }

    public void WaitAll(bool reset)
    {
    }

    public bool IsReady(in SubmitHandle handle)
    {
        return true;
    }

    public void Wait(in SubmitHandle handle, bool reset = true)
    {
    }

    // Mock data structures
    private class MockBufferData
    {
        public BufferDesc Desc { get; set; }
        public string DebugName { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    private class MockTextureData
    {
        public TextureDesc Desc { get; set; }
        public string DebugName { get; set; } = string.Empty;
        public bool IsView { get; set; }
        public TextureHandle BaseTexture { get; set; }
        public int UploadCount { get; set; }
    }

    private class MockSamplerData
    {
        public SamplerStateDesc Desc { get; set; } = new();
    }

    private class MockShaderData
    {
        public ShaderModuleDesc Desc { get; set; } = new();
    }

    private class MockComputePipelineData
    {
        public ComputePipelineDesc Desc { get; set; } = new();
    }

    private class MockRenderPipelineData
    {
        public RenderPipelineDesc Desc { get; set; } = new();
    }

    private class MockQueryPoolData
    {
        public uint NumQueries { get; set; }
        public string DebugName { get; set; } = string.Empty;
        public ulong[] Results { get; set; } = Array.Empty<ulong>();
    }
}
