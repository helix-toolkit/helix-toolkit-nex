namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanPipelineBuilder
{
    public const uint32_t KMaxDynamicsStates = 128;
    private uint32_t _numDynamicStates = 0;
    private readonly VkDynamicState[] _dynamicStates = new VkDynamicState[KMaxDynamicsStates];

    private uint32_t _numShaderStages = 0;
    private readonly VkPipelineShaderStageCreateInfo[] _shaderStages =
        new VkPipelineShaderStageCreateInfo[(int)Graphics.ShaderStage.Fragment + 1];

    private VkPipelineVertexInputStateCreateInfo _vertexInputState = new();
    private VkPipelineInputAssemblyStateCreateInfo _inputAssembly = new()
    {
        topology = VkPrimitiveTopology.TriangleList,
    };
    private VkPipelineRasterizationStateCreateInfo _rasterizationState = new()
    {
        polygonMode = VkPolygonMode.Fill,
        frontFace = VkFrontFace.CounterClockwise,
        lineWidth = 1,
    };
    private VkPipelineMultisampleStateCreateInfo _multisampleState = new()
    {
        rasterizationSamples = VkSampleCountFlags.Count1,
    };
    private VkPipelineDepthStencilStateCreateInfo _depthStencilState = new()
    {
        depthCompareOp = VkCompareOp.Less,
        front = new VkStencilOpState()
        {
            failOp = VkStencilOp.Keep,
            passOp = VkStencilOp.Keep,
            depthFailOp = VkStencilOp.Keep,
            compareOp = VkCompareOp.Never,
        },
        back = new VkStencilOpState()
        {
            failOp = VkStencilOp.Keep,
            passOp = VkStencilOp.Keep,
            depthFailOp = VkStencilOp.Keep,
            compareOp = VkCompareOp.Never,
        },
        minDepthBounds = 0.0f,
        maxDepthBounds = 1.0f,
    };

    private VkPipelineTessellationStateCreateInfo _tessellationState = new();

    private uint32_t _viewMask = 0;
    private uint32_t _numColorAttachments = 0;
    private readonly VkPipelineColorBlendAttachmentState[] _colorBlendAttachmentStates =
        new VkPipelineColorBlendAttachmentState[Constants.MAX_COLOR_ATTACHMENTS];
    private readonly VkFormat[] _colorAttachmentFormats = new VkFormat[
        Constants.MAX_COLOR_ATTACHMENTS
    ];

    private VkFormat _depthAttachmentFormat = VK.VK_FORMAT_UNDEFINED;
    private VkFormat _stencilAttachmentFormat = VK.VK_FORMAT_UNDEFINED;

    private static ulong _numPipelinesCreated = 0;
    public static ulong NumPipelinesCreated => Interlocked.Read(ref _numPipelinesCreated);

    public VulkanPipelineBuilder DynamicState(VkDynamicState state)
    {
        HxDebug.Assert(_numDynamicStates < KMaxDynamicsStates);
        _dynamicStates[_numDynamicStates++] = state;
        return this;
    }

    public VulkanPipelineBuilder PrimitiveTopology(VkPrimitiveTopology topology)
    {
        _inputAssembly.topology = topology;
        return this;
    }

    public VulkanPipelineBuilder RasterizationSamples(
        VkSampleCountFlags samples,
        float minSampleShading
    )
    {
        _multisampleState.rasterizationSamples = samples;
        _multisampleState.sampleShadingEnable = minSampleShading > 0 ? VK_BOOL.True : VK_BOOL.False;
        _multisampleState.minSampleShading = minSampleShading;
        return this;
    }

    public VulkanPipelineBuilder ShaderStage(in VkPipelineShaderStageCreateInfo stage)
    {
        if (stage.module != VkShaderModule.Null)
        {
            HxDebug.Assert(_numShaderStages < _shaderStages.Length);
            _shaderStages[_numShaderStages++] = stage;
        }
        return this;
    }

    public VulkanPipelineBuilder StencilStateOps(
        VkStencilFaceFlags faceMask,
        VkStencilOp failOp,
        VkStencilOp passOp,
        VkStencilOp depthFailOp,
        VkCompareOp compareOp
    )
    {
        _depthStencilState.stencilTestEnable =
            _depthStencilState.stencilTestEnable == VK_BOOL.True
            || failOp != VK.VK_STENCIL_OP_KEEP
            || passOp != VK.VK_STENCIL_OP_KEEP
            || depthFailOp != VK.VK_STENCIL_OP_KEEP
            || compareOp != VK.VK_COMPARE_OP_ALWAYS
                ? VK_BOOL.True
                : VK_BOOL.False;

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_FRONT_BIT))
        {
            ref VkStencilOpState s = ref _depthStencilState.front;
            s.failOp = failOp;
            s.passOp = passOp;
            s.depthFailOp = depthFailOp;
            s.compareOp = compareOp;
        }

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_BACK_BIT))
        {
            ref VkStencilOpState s = ref _depthStencilState.back;
            s.failOp = failOp;
            s.passOp = passOp;
            s.depthFailOp = depthFailOp;
            s.compareOp = compareOp;
        }
        return this;
    }

    public VulkanPipelineBuilder StencilMasks(
        VkStencilFaceFlags faceMask,
        uint32_t compareMask,
        uint32_t writeMask,
        uint32_t reference
    )
    {
        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_FRONT_BIT))
        {
            ref VkStencilOpState s = ref _depthStencilState.front;
            s.compareMask = compareMask;
            s.writeMask = writeMask;
            s.reference = reference;
        }

        if (faceMask.HasFlag(VK.VK_STENCIL_FACE_BACK_BIT))
        {
            ref VkStencilOpState s = ref _depthStencilState.back;
            s.compareMask = compareMask;
            s.writeMask = writeMask;
            s.reference = reference;
        }
        return this;
    }

    public VulkanPipelineBuilder CullMode(VkCullModeFlags mode)
    {
        _rasterizationState.cullMode = mode;
        return this;
    }

    public VulkanPipelineBuilder FrontFace(VkFrontFace mode)
    {
        _rasterizationState.frontFace = mode;
        return this;
    }

    public VulkanPipelineBuilder PolygonMode(VkPolygonMode mode)
    {
        _rasterizationState.polygonMode = mode;
        return this;
    }

    public VulkanPipelineBuilder VertexInputState(in VkPipelineVertexInputStateCreateInfo state)
    {
        _vertexInputState = state;
        return this;
    }

    public VulkanPipelineBuilder ViewMask(uint32_t mask)
    {
        _viewMask = mask;
        return this;
    }

    public VulkanPipelineBuilder ColorAttachments(
        IList<VkPipelineColorBlendAttachmentState> states,
        IList<VkFormat> formats,
        uint32_t numColorAttachments
    )
    {
        HxDebug.Assert(states.Count > 0);
        HxDebug.Assert(formats.Count > 0);
        HxDebug.Assert(numColorAttachments <= _colorBlendAttachmentStates.Length);
        HxDebug.Assert(numColorAttachments <= _colorAttachmentFormats.Length);
        for (int i = 0; i != numColorAttachments; i++)
        {
            _colorBlendAttachmentStates[i] = states[i];
            _colorAttachmentFormats[i] = formats[i];
        }
        this._numColorAttachments = numColorAttachments;
        return this;
    }

    public VulkanPipelineBuilder DepthAttachmentFormat(VkFormat format)
    {
        _depthAttachmentFormat = format;
        return this;
    }

    public VulkanPipelineBuilder StencilAttachmentFormat(VkFormat format)
    {
        _stencilAttachmentFormat = format;
        return this;
    }

    public VulkanPipelineBuilder PatchControlPoints(uint32_t numPoints)
    {
        _tessellationState.patchControlPoints = numPoints;
        return this;
    }

    public VkResult Build(
        in VkDevice device,
        in VkPipelineCache pipelineCache,
        in VkPipelineLayout pipelineLayout,
        out VkPipeline outPipeline,
        string? debugName
    )
    {
        unsafe
        {
            using var pDynamicStates = MemoryMarshal
                .CreateFromPinnedArray(_dynamicStates, 0, (int)_numDynamicStates)
                .Pin();
            using var pBlendAttachmentStates = MemoryMarshal
                .CreateFromPinnedArray(_colorBlendAttachmentStates, 0, (int)_numColorAttachments)
                .Pin();
            using var pColorAttachmentFormats = MemoryMarshal
                .CreateFromPinnedArray(_colorAttachmentFormats, 0, (int)_numColorAttachments)
                .Pin();
            using var pShaderStages = MemoryMarshal
                .CreateFromPinnedArray(_shaderStages, 0, (int)_numShaderStages)
                .Pin();

            VkPipelineDynamicStateCreateInfo dynamicState = new()
            {
                dynamicStateCount = _numDynamicStates,
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
                attachmentCount = _numColorAttachments,
                pAttachments = (VkPipelineColorBlendAttachmentState*)pBlendAttachmentStates.Pointer,
            };
            VkPipelineRenderingCreateInfo renderingInfo = new()
            {
                pNext = null,
                viewMask = _viewMask,
                colorAttachmentCount = _numColorAttachments,
                pColorAttachmentFormats = (VkFormat*)pColorAttachmentFormats.Pointer,
                depthAttachmentFormat = _depthAttachmentFormat,
                stencilAttachmentFormat = _stencilAttachmentFormat,
            };

            var vertexInputState = this._vertexInputState;
            var inputAssembly = this._inputAssembly;
            var tessellationState = this._tessellationState;
            var rasterizationState = this._rasterizationState;
            var depthStencilState = this._depthStencilState;
            var multisampleState = this._multisampleState;

            VkGraphicsPipelineCreateInfo ci = new()
            {
                pNext = &renderingInfo,
                flags = 0,
                stageCount = _numShaderStages,
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
            VK.vkCreateGraphicsPipelines(device, pipelineCache, 1, &ci, null, &pipeline)
                .CheckResult();
            outPipeline = pipeline;
            Interlocked.Increment(ref _numPipelinesCreated);

            // set debug name
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                device.SetDebugObjectName(
                    VK.VK_OBJECT_TYPE_PIPELINE,
                    (nuint)outPipeline,
                    $"[Vk.Pipeline]: {debugName}"
                );
            }
            return VkResult.Success;
        }
    }

    public static VulkanPipelineBuilder Create()
    {
        return new VulkanPipelineBuilder();
    }
}
