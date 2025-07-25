namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class ShaderModuleState()
{
    public VkShaderModule ShaderModule = VkShaderModule.Null;
    public uint32_t PushConstantsSize = 0;

    public bool Valid => ShaderModule != VkShaderModule.Null;

    public static readonly ShaderModuleState Null = new();

    public static implicit operator bool(ShaderModuleState? state) => state is not null && state.Valid;
}

internal sealed class ComputePipelineState()
{
    public ComputePipelineDesc Desc = new();

    // non-owning, the last seen VkDescriptorSetLayout from VulkanContext::vkDSL_ (invalidate all VkPipeline objects on new layout)
    public VkDescriptorSetLayout LastVkDescriptorSetLayout = VkDescriptorSetLayout.Null;

    public VkPipelineLayout PipelineLayout = VkPipelineLayout.Null;
    public VkPipeline Pipeline = VkPipeline.Null;

    public nint SpecConstantDataStorage = nint.Zero;

    public bool Valid => Pipeline != VkPipeline.Null && PipelineLayout != VkPipelineLayout.Null;

    public static readonly ComputePipelineState Null = new();


    public static implicit operator bool(ComputePipelineState? state) => state is not null && state.Valid;
}

internal sealed class RenderPipelineState()
{
    public RenderPipelineDesc Desc = new();

    public uint32_t NumBindings = 0;
    public uint32_t NumAttributes = 0;
    public readonly VkVertexInputBindingDescription[] VkBindings = new VkVertexInputBindingDescription[VertexInput.MAX_VERTEX_BINDINGS];
    public readonly VkVertexInputAttributeDescription[] VkAttributes = new VkVertexInputAttributeDescription[VertexInput.MAX_VERTEX_ATTRIBUTES];

    // non-owning, the last seen VkDescriptorSetLayout from VulkanContext::vkDSL_ (if the context has a new layout, invalidate all VkPipeline
    // objects)
    public VkDescriptorSetLayout LastVkDescriptorSetLayout = VkDescriptorSetLayout.Null;

    public VkShaderStageFlags ShaderStageFlags = 0;
    public VkPipelineLayout PipelineLayout = VkPipelineLayout.Null;
    public VkPipeline Pipeline = VkPipeline.Null;

    public nint SpecConstantDataStorage = nint.Zero;

    public uint32_t VewMask = 0;

    public bool Valid => Pipeline != VkPipeline.Null && PipelineLayout != VkPipelineLayout.Null;

    public static readonly RenderPipelineState Null = new();

    public static implicit operator bool(RenderPipelineState? state) => state is not null && state.Valid;
}