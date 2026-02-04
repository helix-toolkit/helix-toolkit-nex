namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class ShaderModuleState(VkDevice? device) : IDisposable
{
    public readonly VkDevice? Device = device;
    public VkShaderModule ShaderModule = VkShaderModule.Null;
    public uint32_t PushConstantsSize = 0;
    private bool _disposedValue;

    public bool Valid => ShaderModule != VkShaderModule.Null;

    public static readonly ShaderModuleState Null = new(null);

    public static implicit operator bool(ShaderModuleState? state) =>
        state is not null && state.Valid;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                unsafe
                {
                    VK.vkDestroyShaderModule(Device ?? VkDevice.Null, ShaderModule, null);
                }
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~ShaderModuleState()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class ComputePipelineState(VulkanContext? context) : IDisposable
{
    public readonly VulkanContext? Context = context;
    public ComputePipelineDesc Desc = new();

    // non-owning, the last seen VkDescriptorSetLayout from VulkanContext::vkDSL_ (invalidate all VkPipeline objects on new layout)
    public VkDescriptorSetLayout LastVkDescriptorSetLayout = VkDescriptorSetLayout.Null;

    public VkPipelineLayout PipelineLayout = VkPipelineLayout.Null;
    public VkPipeline Pipeline = VkPipeline.Null;

    public byte[] SpecConstantDataStorage = [];
    private bool _disposedValue;

    public bool Valid =>
        Pipeline != VkPipeline.Null
        && PipelineLayout != VkPipelineLayout.Null
        && Context is not null;

    public static readonly ComputePipelineState Null = new(null);

    public static implicit operator bool(ComputePipelineState? state) =>
        state is not null && state.Valid;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                SpecConstantDataStorage = [];
                Desc.SpecInfo.Data = [];

                Context?.DeferredTask(
                    () =>
                    {
                        unsafe
                        {
                            VK.vkDestroyPipeline(Context.VkDevice, Pipeline, null);
                            VK.vkDestroyPipelineLayout(Context.VkDevice, PipelineLayout, null);
                        }
                    },
                    SubmitHandle.Null
                );
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~ComputePipelineState()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class RenderPipelineState : IDisposable
{
    public readonly VulkanContext? Context;
    public RenderPipelineDesc Desc = new();

    public uint32_t NumBindings = 0;
    public uint32_t NumAttributes = 0;
    public readonly VkVertexInputBindingDescription[] VkBindings =
        new VkVertexInputBindingDescription[VertexInput.MAX_VERTEX_BINDINGS];
    public readonly VkVertexInputAttributeDescription[] VkAttributes =
        new VkVertexInputAttributeDescription[VertexInput.MAX_VERTEX_ATTRIBUTES];

    // non-owning, the last seen VkDescriptorSetLayout from VulkanContext::vkDSL_ (if the context has a new layout, invalidate all VkPipeline
    // objects)
    public VkDescriptorSetLayout LastVkDescriptorSetLayout = VkDescriptorSetLayout.Null;

    public VkShaderStageFlags ShaderStageFlags = 0;
    public VkPipelineLayout PipelineLayout = VkPipelineLayout.Null;
    public VkPipeline Pipeline = VkPipeline.Null;

    public byte[] SpecConstantDataStorage = [];

    public uint32_t VewMask = 0;
    private bool _disposedValue;

    public bool Valid =>
        Pipeline != VkPipeline.Null
        && PipelineLayout != VkPipelineLayout.Null
        && Context is not null;

    public static readonly RenderPipelineState Null = new(null);

    public static implicit operator bool(RenderPipelineState? state) =>
        state is not null && state.Valid;

    public RenderPipelineState(VulkanContext? context)
    {
        Context = context;
        for (int i = 0; i < VertexInput.MAX_VERTEX_BINDINGS; i++)
        {
            VkBindings[i] = new VkVertexInputBindingDescription();
        }
        for (int i = 0; i < VertexInput.MAX_VERTEX_ATTRIBUTES; i++)
        {
            VkAttributes[i] = new VkVertexInputAttributeDescription();
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                SpecConstantDataStorage = [];
                Desc.SpecInfo.Data = [];
                Context?.DeferredTask(
                    () =>
                    {
                        unsafe
                        {
                            VK.vkDestroyPipeline(Context.VkDevice, Pipeline, null);
                            VK.vkDestroyPipelineLayout(Context.VkDevice, PipelineLayout, null);
                        }
                    },
                    SubmitHandle.Null
                );
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~RenderPipelineState()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class SamplerState(VulkanContext? context, VkSampler Sampler) : IDisposable
{
    public readonly VulkanContext? Context = context;
    public VkSampler Sampler = Sampler;
    private bool _disposedValue;

    public bool Valid => Sampler != VkSampler.Null;
    public static readonly SamplerState Null = new(null, VkSampler.Null);

    public static implicit operator bool(SamplerState? state) => state is not null && state.Valid;

    public static implicit operator VkSampler(SamplerState? state) =>
        state?.Sampler ?? VkSampler.Null;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Context?.DeferredTask(
                    () =>
                    {
                        unsafe
                        {
                            VK.vkDestroySampler(Context.VkDevice, Sampler, null);
                        }
                    },
                    SubmitHandle.Null
                );
            }
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~SamplerState()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class QueryPoolState(VulkanContext? context, VkQueryPool QueryPool) : IDisposable
{
    public readonly VulkanContext? Context = context;
    public VkQueryPool QueryPool = QueryPool;
    private bool _disposedValue;

    public bool Valid => QueryPool != VkQueryPool.Null;
    public static readonly QueryPoolState Null = new(null, VkQueryPool.Null);

    public static implicit operator bool(QueryPoolState? state) => state is not null && state.Valid;

    public static implicit operator VkQueryPool(QueryPoolState? state) =>
        state?.QueryPool ?? VkQueryPool.Null;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Context?.DeferredTask(
                    () =>
                    {
                        unsafe
                        {
                            VK.vkDestroyQueryPool(Context.VkDevice, QueryPool, null);
                        }
                    },
                    SubmitHandle.Null
                );
            }
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~QueryPoolState()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
