namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanPipelineBuilder
{
    public const uint32_t kMaxDynamicsStates = 128;
    uint32_t numDynamicStates = 0;
    readonly VkDynamicState[] dynamicStates = new VkDynamicState[kMaxDynamicsStates];

    uint32_t numShaderStages = 0;
    readonly VkPipelineShaderStageCreateInfo[] shaderStages = new VkPipelineShaderStageCreateInfo[(int)Graphics.ShaderStage.Fragment + 1];

    VkPipelineVertexInputStateCreateInfo vertexInputState = new();
    VkPipelineInputAssemblyStateCreateInfo inputAssembly = new() { topology = VkPrimitiveTopology.TriangleList };
    VkPipelineRasterizationStateCreateInfo rasterizationState = new() { polygonMode = VkPolygonMode.Fill, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1 };
    VkPipelineMultisampleStateCreateInfo multisampleState = new() { rasterizationSamples = VkSampleCountFlags.Count1 };
    VkPipelineDepthStencilStateCreateInfo depthStencilState = new()
    {
        depthCompareOp = VkCompareOp.Less,
        front = new VkStencilOpState() { failOp = VkStencilOp.Keep, passOp = VkStencilOp.Keep, depthFailOp = VkStencilOp.Keep, compareOp = VkCompareOp.Never },
        back = new VkStencilOpState() { failOp = VkStencilOp.Keep, passOp = VkStencilOp.Keep, depthFailOp = VkStencilOp.Keep, compareOp = VkCompareOp.Never },
        minDepthBounds = 0.0f,
        maxDepthBounds = 1.0f
    };

    VkPipelineTessellationStateCreateInfo tessellationState = new();

    uint32_t viewMask = 0;
    uint32_t numColorAttachments = 0;
    readonly VkPipelineColorBlendAttachmentState[] colorBlendAttachmentStates = new VkPipelineColorBlendAttachmentState[Constants.MAX_COLOR_ATTACHMENTS];
    readonly VkFormat[] colorAttachmentFormats = new VkFormat[Constants.MAX_COLOR_ATTACHMENTS];

    VkFormat depthAttachmentFormat = VK.VK_FORMAT_UNDEFINED;
    VkFormat stencilAttachmentFormat = VK.VK_FORMAT_UNDEFINED;

    static ulong numPipelinesCreated = 0;
    public static ulong NumPipelinesCreated => Interlocked.Read(ref numPipelinesCreated);

    public VulkanPipelineBuilder DynamicState(VkDynamicState state)
    {
        HxDebug.Assert(numDynamicStates < kMaxDynamicsStates);
        dynamicStates[numDynamicStates++] = state;
        return this;
    }

    public VulkanPipelineBuilder PrimitiveTopology(VkPrimitiveTopology topology)
    {
        inputAssembly.topology = topology;
        return this;
    }
    public VulkanPipelineBuilder RasterizationSamples(VkSampleCountFlags samples, float minSampleShading)
    {
        multisampleState.rasterizationSamples = samples;
        multisampleState.sampleShadingEnable = minSampleShading > 0 ? VK_BOOL.True : VK_BOOL.False;
        multisampleState.minSampleShading = minSampleShading;
        return this;
    }
    public VulkanPipelineBuilder ShaderStage(in VkPipelineShaderStageCreateInfo stage)
    {
        if (stage.module != VkShaderModule.Null)
        {
            HxDebug.Assert(numShaderStages < shaderStages.Length);
            shaderStages[numShaderStages++] = stage;
        }
        return this;
    }
    public VulkanPipelineBuilder StencilStateOps(VkStencilFaceFlags faceMask, VkStencilOp failOp, VkStencilOp passOp, VkStencilOp depthFailOp, VkCompareOp compareOp)
    {
        depthStencilState.stencilTestEnable = depthStencilState.stencilTestEnable == VK_BOOL.True || failOp != VK.VK_STENCIL_OP_KEEP ||
                                               passOp != VK.VK_STENCIL_OP_KEEP || depthFailOp != VK.VK_STENCIL_OP_KEEP ||
                                               compareOp != VK.VK_COMPARE_OP_ALWAYS
                                           ? VK_BOOL.True
                                           : VK_BOOL.False;

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_FRONT_BIT))
        {
            ref VkStencilOpState s = ref depthStencilState.front;
            s.failOp = failOp;
            s.passOp = passOp;
            s.depthFailOp = depthFailOp;
            s.compareOp = compareOp;
        }

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_BACK_BIT))
        {
            ref VkStencilOpState s = ref depthStencilState.back;
            s.failOp = failOp;
            s.passOp = passOp;
            s.depthFailOp = depthFailOp;
            s.compareOp = compareOp;
        }
        return this;
    }
    public VulkanPipelineBuilder StencilMasks(VkStencilFaceFlags faceMask, uint32_t compareMask, uint32_t writeMask, uint32_t reference)
    {
        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_FRONT_BIT))
        {
            ref VkStencilOpState s = ref depthStencilState.front;
            s.compareMask = compareMask;
            s.writeMask = writeMask;
            s.reference = reference;
        }

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_BACK_BIT))
        {
            ref VkStencilOpState s = ref depthStencilState.back;
            s.compareMask = compareMask;
            s.writeMask = writeMask;
            s.reference = reference;
        }
        return this;
    }
    public VulkanPipelineBuilder CullMode(VkCullModeFlags mode)
    {
        rasterizationState.cullMode = mode;
        return this;
    }
    public VulkanPipelineBuilder FrontFace(VkFrontFace mode)
    {
        rasterizationState.frontFace = mode;
        return this;
    }
    public VulkanPipelineBuilder PolygonMode(VkPolygonMode mode)
    {
        rasterizationState.polygonMode = mode;
        return this;
    }
    public VulkanPipelineBuilder VertexInputState(in VkPipelineVertexInputStateCreateInfo state)
    {
        vertexInputState = state;
        return this;
    }
    public VulkanPipelineBuilder ViewMask(uint32_t mask)
    {
        viewMask = mask;
        return this;
    }
    public VulkanPipelineBuilder ColorAttachments(IList<VkPipelineColorBlendAttachmentState> states,
                                              IList<VkFormat> formats,
                                          uint32_t numColorAttachments)
    {
        HxDebug.Assert(states.Count > 0);
        HxDebug.Assert(formats.Count > 0);
        HxDebug.Assert(numColorAttachments <= colorBlendAttachmentStates.Length);
        HxDebug.Assert(numColorAttachments <= colorAttachmentFormats.Length);
        for (int i = 0; i != numColorAttachments; i++)
        {
            colorBlendAttachmentStates[i] = states[i];
            colorAttachmentFormats[i] = formats[i];
        }
        this.numColorAttachments = numColorAttachments;
        return this;
    }
    public VulkanPipelineBuilder DepthAttachmentFormat(VkFormat format)
    {
        depthAttachmentFormat = format;
        return this;
    }
    public VulkanPipelineBuilder StencilAttachmentFormat(VkFormat format)
    {
        stencilAttachmentFormat = format;
        return this;
    }
    public VulkanPipelineBuilder PatchControlPoints(uint32_t numPoints)
    {
        tessellationState.patchControlPoints = numPoints;
        return this;
    }

    public VkResult Build(in VkDevice device, in VkPipelineCache pipelineCache, in VkPipelineLayout pipelineLayout, out VkPipeline outPipeline, string? debugName)
    {
        unsafe
        {
            using var pDynamicStates = MemoryMarshal.CreateFromPinnedArray(dynamicStates, 0, (int)numDynamicStates).Pin();
            using var pBlendAttachmentStates = MemoryMarshal.CreateFromPinnedArray(colorBlendAttachmentStates, 0, (int)numColorAttachments).Pin();
            using var pColorAttachmentFormats = MemoryMarshal.CreateFromPinnedArray(colorAttachmentFormats, 0, (int)numColorAttachments).Pin();
            using var pShaderStages = MemoryMarshal.CreateFromPinnedArray(shaderStages, 0, (int)numShaderStages).Pin();

            VkPipelineDynamicStateCreateInfo dynamicState = new()
            {
                dynamicStateCount = numDynamicStates,
                pDynamicStates = (VkDynamicState*)pDynamicStates.Pointer,
            };
            // viewport and scissor can be NULL if the viewport state is dynamic
            // https://www.khronos.org/registry/vulkan/specs/1.3-extensions/man/html/VkPipelineViewportStateCreateInfo.html
            VkPipelineViewportStateCreateInfo viewportState = new()
            {
                viewportCount = 1,
                pViewports = null,
                scissorCount = 1,
                pScissors = null,
            };
            VkPipelineColorBlendStateCreateInfo colorBlendState = new()
            {
                logicOpEnable = VK_BOOL.False,
                logicOp = VK.VK_LOGIC_OP_COPY,
                attachmentCount = numColorAttachments,
                pAttachments = (VkPipelineColorBlendAttachmentState*)pBlendAttachmentStates.Pointer,
            };
            VkPipelineRenderingCreateInfo renderingInfo = new()
            {
                pNext = null,
                viewMask = viewMask,
                colorAttachmentCount = numColorAttachments,
                pColorAttachmentFormats = (VkFormat*)pColorAttachmentFormats.Pointer,
                depthAttachmentFormat = depthAttachmentFormat,
                stencilAttachmentFormat = stencilAttachmentFormat,
            };

            var vertexInputState = this.vertexInputState;
            var inputAssembly = this.inputAssembly;
            var tessellationState = this.tessellationState;
            var rasterizationState = this.rasterizationState;
            var depthStencilState = this.depthStencilState;
            var multisampleState = this.multisampleState;

            VkGraphicsPipelineCreateInfo ci = new()
            {
                pNext = &renderingInfo,
                flags = 0,
                stageCount = numShaderStages,
                pStages = (VkPipelineShaderStageCreateInfo*)pShaderStages.Pointer,
                pVertexInputState = &vertexInputState,
                pInputAssemblyState = &inputAssembly,
                pTessellationState = &tessellationState,
                pViewportState = &viewportState,
                pRasterizationState = &rasterizationState,
                pMultisampleState = &multisampleState,
                pDepthStencilState = &depthStencilState,
                pColorBlendState = &colorBlendState,
                pDynamicState = &dynamicState,
                layout = pipelineLayout,
                renderPass = VkRenderPass.Null,
                subpass = 0,
                basePipelineHandle = VkPipeline.Null,
                basePipelineIndex = -1,
            };

            VkPipeline pipeline = VkPipeline.Null;
            VK.vkCreateGraphicsPipelines(device, pipelineCache, 1, &ci, null, &pipeline).CheckResult();
            outPipeline = pipeline;
            Interlocked.Increment(ref numPipelinesCreated);

            // set debug name
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                device.SetDebugObjectName(VK.VK_OBJECT_TYPE_PIPELINE, (nuint)outPipeline, $"[Vk.Pipeline]: {debugName}");
            }
            return VkResult.Success;
        }

    }

    public static VulkanPipelineBuilder Create()
    {
        return new VulkanPipelineBuilder();
    }
}
