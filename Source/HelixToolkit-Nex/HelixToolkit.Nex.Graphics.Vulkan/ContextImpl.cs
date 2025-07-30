namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed partial class VulkanContext : IContext
{
    #region IContext implementation
    public ResultCode Upload(in BufferHandle handle, size_t offset, nint data, size_t size)
    {
        if (data.IsNull())
        {
            return ResultCode.ArgumentNull;
        }

        HxDebug.Assert(size > 0, "Data size should be non-zero");

        var buf = BuffersPool.Get(handle);

        if (buf is null)
        {
            logger.LogError("Buffer handle is invalid for upload: {HANDLE}", handle.ToString());
            return ResultCode.RuntimeError;
        }

        if (!buf.Valid)
        {
            logger.LogError("Buffer handle is invalid for upload: {HANDLE}", handle.ToString());
            return ResultCode.RuntimeError;
        }

        if (offset + size > (uint)buf.BufferSize)
        {
            logger.LogError("Buffer upload out of range: offset {OFFSET}, size {SIZE}, buffer size {BUFFER_SIZE}", offset, size, buf.BufferSize);
            return ResultCode.ArgumentOutOfRange;
        }

        return StagingDevice!.BufferSubData(buf, offset, size, data);
    }

    public ResultCode Upload(in TextureHandle handle, in TextureRangeDesc range, nint data, size_t dataSize)
    {
        if (data.IsNull() || dataSize == 0)
        {
            logger.LogError("Data pointer is null for texture upload");
            return ResultCode.ArgumentNull;
        }

        var texture = TexturesPool.Get(handle);

        if (texture is null)
        {
            logger.LogError("Texture handle is invalid for upload: {HANDLE}", handle.ToString());
            return ResultCode.RuntimeError;
        }

        var result = HxVkUtils.ValidateRange(texture.Extent, texture.NumLevels, range);

        if (result.HasError())
        {
            return result;
        }

        uint32_t numLayers = Math.Max(range.numLayers, 1u);

        VkFormat vkFormat = texture.ImageFormat;

        if (texture.ImageType == VK.VK_IMAGE_TYPE_3D)
        {
            return StagingDevice!.ImageData3D(texture,
                new VkOffset3D(range.offset.x, range.offset.y, range.offset.z),
                new VkExtent3D(range.dimensions.Width, range.dimensions.Height, range.dimensions.Depth),
                vkFormat, data, dataSize);
        }
        else
        {
            VkRect2D imageRegion = new()
            {
                offset = { x = range.offset.x, y = range.offset.y },
                extent = { width = range.dimensions.Width, height = range.dimensions.Height },
            };
            return StagingDevice!.ImageData2D(texture, imageRegion, range.mipLevel, range.numMipLevels, range.layer, numLayers, vkFormat, data, dataSize);
        }
    }

    public ResultCode Download(in BufferHandle handle, nint data, uint size, uint offset)
    {
        if (data.IsNull())
        {
            logger.LogError("Data pointer is null for buffer download");
            return ResultCode.ArgumentNull;
        }

        HxDebug.Assert(size > 0, "Data size should be non-zero");

        var buf = BuffersPool.Get(handle);

        if (buf is null)
        {
            logger.LogError("Buffer handle is invalid for download: {HANDLE}", handle.ToString());
            return ResultCode.RuntimeError;
        }

        if (!buf.Valid)
        {
            return ResultCode.RuntimeError;
        }

        if (offset + size > buf.BufferSize)
        {
            logger.LogError("Buffer download out of range: offset {OFFSET}, size {SIZE}, buffer size {BUFFER_SIZE}", offset, size, buf.BufferSize);
            return ResultCode.ArgumentOutOfRange;
        }

        return StagingDevice!.GetBufferData(buf, offset, data, size);
    }

    public ResultCode Download(in TextureHandle handle, in TextureRangeDesc range, nint outData, size_t dataSize)
    {
        if (outData.IsNull() || dataSize == 0)
        {
            return ResultCode.ArgumentError;
        }

        var texture = TexturesPool.Get(handle);

        HxDebug.Assert(texture is not null);

        if (texture is null)
        {
            return ResultCode.RuntimeError;
        }

        var result = HxVkUtils.ValidateRange(texture.Extent, texture.NumLevels, range);

        return result.HasError()
            ? result
            : StagingDevice!.GetImageData(texture,
                                     new VkOffset3D(range.offset.x, range.offset.y, range.offset.z),
                               new VkExtent3D(range.dimensions.Width, range.dimensions.Height, range.dimensions.Depth),
                               new VkImageSubresourceRange
                               {
                                   aspectMask = texture.GetImageAspectFlags(),
                                   baseMipLevel = range.mipLevel,
                                   levelCount = range.numMipLevels,
                                   baseArrayLayer = range.layer,
                                   layerCount = range.numLayers,
                               },
                               texture.ImageFormat,
                               outData, dataSize);
    }

    public ICommandBuffer AcquireCommandBuffer()
    {
        HxDebug.Assert(currentCommandBuffer_ == null, "Cannot acquire more than 1 command buffer simultaneously");
        if (RuntimeInformation.OSArchitecture.Equals(Architecture.Arm64) &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // a temporary workaround for Windows on Snapdragon
            VK.vkDeviceWaitIdle(vkDevice).CheckResult();
        }
        currentCommandBuffer_ = new CommandBuffer(this);

        return currentCommandBuffer_;
    }

    public ResultCode CreateComputePipeline(in ComputePipelineDesc desc, out ComputePipelineResource computePipeline)
    {
        computePipeline = ComputePipelineResource.Null;
        if (!desc.smComp.Valid)
        {
            logger.LogError("Missing compute shader");
            return ResultCode.ArgumentError;
        }

        ComputePipelineState cps = new() { Desc = desc };

        if (desc.SpecInfo.Data.Length > 0)
        {
            // copy into a local storage
            cps.SpecConstantDataStorage = new byte[desc.SpecInfo.Data.Length];
            Array.Copy(desc.SpecInfo.Data, cps.SpecConstantDataStorage, desc.SpecInfo.Data.Length);
            cps.Desc.SpecInfo.Data = cps.SpecConstantDataStorage;
        }
        var handle = ComputePipelinesPool.Create(cps);
        if (handle == ComputePipelineHandle.Null)
        {
            logger.LogError("Failed to create compute pipeline state");
            return ResultCode.RuntimeError;
        }
        computePipeline = new ComputePipelineResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateQueryPool(uint numQueries, out QueryPoolResource queryPool, string? debugName)
    {
        unsafe
        {
            VkQueryPoolCreateInfo createInfo = new()
            {
                flags = 0,
                queryType = VK.VK_QUERY_TYPE_TIMESTAMP,
                queryCount = numQueries,
                pipelineStatistics = 0,
            };

            VkQueryPool pool = VkQueryPool.Null;
            VK.vkCreateQueryPool(vkDevice, &createInfo, null, &pool).CheckResult();

            if (!string.IsNullOrEmpty(debugName))
            {
                vkDevice.SetDebugObjectName(VK.VK_OBJECT_TYPE_QUERY_POOL, (nuint)pool, $"[Vk.QueryPool]: {debugName}");
            }

            var handle = this.QueriesPool.Create(pool);

            if (handle == QueryPoolHandle.Null)
            {
                logger.LogError("Failed to create query pool state");
                queryPool = QueryPoolResource.Null;
                return ResultCode.RuntimeError;
            }
            queryPool = new QueryPoolResource(this, handle);
            return ResultCode.Ok;
        }
    }

    public ResultCode CreateRenderPipeline(in RenderPipelineDesc desc, out RenderPipelineResource renderPipeline)
    {
        bool hasColorAttachments = desc.GetNumColorAttachments() > 0;
        bool hasDepthAttachment = desc.DepthFormat != Format.Invalid;
        bool hasAnyAttachments = hasColorAttachments || hasDepthAttachment;
        renderPipeline = RenderPipelineResource.Null;
        if (!hasAnyAttachments)
        {
            logger.LogError("Need at least one attachment");
            return ResultCode.ArgumentError;
        }
        VkShaderStageFlags stageFlags = VkShaderStageFlags.None;
        if (desc.SmMesh.Valid)
        {
            if (desc.VertexInput.AttributeCount() > 0 || desc.VertexInput.BindingCount() > 0)
            {
                logger.LogError("CreateRenderPipeline failed. Cannot have vertexInput with mesh shaders");
                return ResultCode.ArgumentError;
            }
            if (desc.SmVert.Valid)
            {
                logger.LogError("CreateRenderPipeline failed. Cannot have both vertex and mesh shaders");
                return ResultCode.ArgumentError;
            }
            if (desc.SmTesc.Valid || desc.SmTese.Valid)
            {
                logger.LogError("CreateRenderPipeline failed. Cannot have both tessellation and mesh shaders");
                return ResultCode.ArgumentError;
            }
            if (desc.SmGeom.Valid)
            {
                logger.LogError("CreateRenderPipeline failed. Cannot have both geometry and mesh shaders");
                return ResultCode.ArgumentError;
            }
        }
        else
        {
            if (!desc.SmVert.Valid)
            {
                logger.LogError("Missing vertex shader");
                return ResultCode.ArgumentError;
            }
        }

        if (!desc.SmFrag.Valid)
        {
            logger.LogError("Missing fragment shader");
            return ResultCode.ArgumentError;
        }

        if (desc.SmVert.Valid)
        {
            stageFlags |= VkShaderStageFlags.Vertex;
        }

        if (desc.SmTesc.Valid)
        {
            stageFlags |= VkShaderStageFlags.TessellationControl;
        }

        if (desc.SmTese.Valid)
        {
            stageFlags |= VkShaderStageFlags.TessellationEvaluation;
        }

        if (desc.SmGeom.Valid)
        {
            stageFlags |= VkShaderStageFlags.Geometry;
        }

        if (desc.SmFrag.Valid)
        {
            stageFlags |= VkShaderStageFlags.Fragment;
        }

        RenderPipelineState rps = new()
        {
            Desc = desc,
            ShaderStageFlags = stageFlags
        };

        // Iterate and cache vertex input bindings and attributes
        ref var vstate = ref rps.Desc.VertexInput;
        unsafe
        {
            var bufferAlreadyBound = stackalloc bool[(int)VertexInput.MAX_VERTEX_BINDINGS];

            rps.NumAttributes = vstate.AttributeCount();

            for (uint32_t i = 0; i != rps.NumAttributes; i++)
            {
                ref var attr = ref vstate.Attributes[i];

                rps.VkAttributes[i] = new()
                {
                    location = attr.Location,
                    binding = attr.Binding,
                    format = attr.Format.ToVk(),
                    offset = (uint32_t)attr.Offset
                };

                if (!bufferAlreadyBound[attr.Binding])
                {
                    bufferAlreadyBound[attr.Binding] = true;
                    rps.VkBindings[rps.NumBindings++] = new()
                    {
                        binding = attr.Binding,
                        stride = vstate.Bindings[attr.Binding].Stride,
                        inputRate = VK.VK_VERTEX_INPUT_RATE_VERTEX
                    };
                }
            }

            if (desc.SpecInfo.Data.Length > 0)
            {
                // copy into a local storage
                rps.SpecConstantDataStorage = new byte[desc.SpecInfo.Data.Length];
                Array.Copy(desc.SpecInfo.Data, rps.SpecConstantDataStorage, desc.SpecInfo.Data.Length);
                rps.Desc.SpecInfo.Data = rps.SpecConstantDataStorage;
            }

            var handle = RenderPipelinesPool.Create(rps);
            if (handle == RenderPipelineHandle.Null)
            {
                logger.LogError("Failed to create render pipeline state");
                return ResultCode.RuntimeError;
            }
            renderPipeline = new RenderPipelineResource(this, handle);
        }
        return ResultCode.Ok;
    }

    public ResultCode CreateSampler(in SamplerStateDesc desc, out SamplerResource sampler)
    {
        sampler = SamplerResource.Null;

        VkSamplerCreateInfo info = desc.ToVkSamplerCreateInfo(GetVkPhysicalDeviceProperties().limits);

        var ret = CreateSampler(info, Format.Invalid, out var handle, desc.DebugName);

        if (ret != ResultCode.Ok)
        {
            logger.LogError("Cannot create Sampler");
            return ret;
        }

        sampler = new SamplerResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateShaderModule(in ShaderModuleDesc desc, out ShaderModuleResource shaderModule)
    {
        shaderModule = ShaderModuleResource.Null;
        if (desc.Data.IsNull() || desc.DataSize == 0)
        {
            logger.LogError("Shader module data is null or size is zero");
            return ResultCode.ArgumentNull;
        }
        ResultCode result;
        ShaderModuleState sm;
        switch (desc.DataType)
        {
            case ShaderDataType.Spirv:
                result = vkDevice.CreateShaderModuleFromSPIRV(desc.Data, desc.DataSize, out sm, desc.DebugName);
                break;
            case ShaderDataType.Glsl:
                result = vkDevice.CreateShaderModuleFromGLSL(desc.Stage, desc.Data, desc.Defines, GetVkPhysicalDeviceProperties().limits, out sm, desc.DebugName);
                break;
            default:
                HxDebug.Assert(false, $"Unsupported shader data type: {desc.DataType}");
                logger.LogError("Unsupported shader data type: {TYPE}", desc.DataType);
                return ResultCode.NotSupported;
        }

        if (result.HasError())
        {
            return ResultCode.CompileError;
        }
        shaderModule = new(this, ShaderModulesPool.Create(sm));
        return ResultCode.Ok;
    }

    public ResultCode CreateTexture(in TextureDesc requestedDesc, out TextureResource texture, string? debugName)
    {
        texture = TextureResource.Null;
        TextureDesc desc = requestedDesc;
        if (debugName is not null)
        {
            desc.DebugName = debugName;
        }

        var getClosestDepthStencilFormat = new Func<Format, VkFormat>((desiredFormat) =>
        {
            // Get a list of compatible depth formats for a given desired format.
            // The list will contain depth format that are ordered from most to least closest
            var compatibleDepthStencilFormatList = desiredFormat.GetCompatibleDepthStencilFormats();

            // check if any of the format in compatible list is supported
            foreach (var format in compatibleDepthStencilFormatList)
            {
                if (deviceDepthFormats.Contains(format))
                {
                    return format;
                }
            }

            // no matching found, choose the first supported format
            return !deviceDepthFormats.Empty ? deviceDepthFormats[0] : VK.VK_FORMAT_D24_UNORM_S8_UINT;
        });
        VkFormat vkFormat = desc.Format.IsDepthOrStencilFormat() ? getClosestDepthStencilFormat(desc.Format)
                                                                     : desc.Format.ToVk();

        HxDebug.Assert(vkFormat != VK.VK_FORMAT_UNDEFINED, "Invalid VkFormat value");

        TextureType type = desc.Type;
        if (!(type == TextureType.Texture2D || type == TextureType.TextureCube || type == TextureType.Texture3D))
        {
            HxDebug.Assert(false, "Only 2D, 3D and Cube textures are supported");
            logger.LogError("Only 2D, 3D and Cube textures are supported. Current format: {FORMAT}", type);
            return ResultCode.NotSupported;
        }

        if (desc.NumMipLevels == 0)
        {
            HxDebug.Assert(false, "The number of mip levels specified must be greater than 0");
            logger.LogWarning("The number of mip levels specified must be greater than 0 but is {LEVELS}", desc.NumMipLevels);
            desc.NumMipLevels = 1;
        }

        if (desc.NumSamples > 1 && desc.NumMipLevels != 1)
        {
            HxDebug.Assert(false, "The number of mip levels for multisampled images should be 1");
            logger.LogError("The number of mip levels for multisampled images should be 1 but is {LEVELS}", desc.NumMipLevels);
            return ResultCode.ArgumentError;
        }

        if (desc.NumSamples > 1 && type == TextureType.Texture3D)
        {
            HxDebug.Assert(false, "Multisampled 3D images are not supported");
            logger.LogError("Multisampled 3D images are not supported. Current format: {FORMAT}", type);
            return ResultCode.NotSupported;
        }

        if (!(desc.NumMipLevels <= HxVkUtils.CalcNumMipLevels(desc.Dimensions.Width, desc.Dimensions.Height)))
        {
            HxDebug.Assert(false, $"The number of specified mip-levels is greater than the maximum possible number of mip-levels.");
            logger.LogError("The number of specified mip-levels is greater than the maximum possible number of mip-levels. Current: {LEVELS} Max: {MAX_LEVELS}",
                              desc.NumMipLevels, HxVkUtils.CalcNumMipLevels(desc.Dimensions.Width, desc.Dimensions.Height));
            return ResultCode.ArgumentError;
        }

        if (desc.Usage == 0)
        {
            HxDebug.Assert(false, "Texture usage flags are not set");
            desc.Usage = TextureUsageBits.Sampled;
        }

        /* Use staging device to transfer data into the image when the storage is private to the device */
        VkImageUsageFlags usageFlags = (desc.Storage == StorageType.Device) ? VK.VK_IMAGE_USAGE_TRANSFER_DST_BIT : 0;

        if (desc.Usage.HasFlag(TextureUsageBits.Sampled))
        {
            usageFlags |= VK.VK_IMAGE_USAGE_SAMPLED_BIT;
        }
        if (desc.Usage.HasFlag(TextureUsageBits.Storage))
        {
            if (desc.Format.IsDepthOrStencilFormat())
            {
                HxDebug.Assert(false, "Depth stencil buffer cannot have TextureUsageBits.Storage as usage.");
                logger.LogError("Depth stencil buffer cannot have TextureUsageBits.Storage as usage.");
                return ResultCode.ArgumentError;
            }
            HxDebug.Assert(desc.NumSamples <= 1, "Storage images cannot be multisampled");
            usageFlags |= VK.VK_IMAGE_USAGE_STORAGE_BIT;
        }
        if (desc.Usage.HasFlag(TextureUsageBits.Attachment))
        {
            usageFlags |= desc.Format.IsDepthOrStencilFormat() ? VK.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT
                                                                   : VK.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
            if (desc.Storage == StorageType.Memoryless)
            {
                usageFlags |= VK.VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT;
            }
        }

        if (desc.Storage != StorageType.Memoryless)
        {
            // For now, always set this flag so we can read it back
            usageFlags |= VK.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        }

        HxDebug.Assert(usageFlags != 0, "Invalid usage flags");

        VkMemoryPropertyFlags memFlags = desc.Storage.ToVkMemoryPropertyFlags();

        VkImageCreateFlags vkCreateFlags = 0;
        VkImageViewType vkImageViewType;
        VkImageType vkImageType;
        VkSampleCountFlags vkSamples = VkSampleCountFlags.Count1;
        uint32_t numLayers = desc.NumLayers;
        switch (desc.Type)
        {
            case TextureType.Texture2D:
                vkImageViewType = numLayers > 1 ? VK.VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK.VK_IMAGE_VIEW_TYPE_2D;
                vkImageType = VK.VK_IMAGE_TYPE_2D;
                vkSamples = HxVkUtils.GetVulkanSampleCountFlags(desc.NumSamples, GetFramebufferMSAABitMaskVK());
                break;
            case TextureType.Texture3D:
                vkImageViewType = VK.VK_IMAGE_VIEW_TYPE_3D;
                vkImageType = VK.VK_IMAGE_TYPE_3D;
                break;
            case TextureType.TextureCube:
                vkImageViewType = numLayers > 1 ? VK.VK_IMAGE_VIEW_TYPE_CUBE_ARRAY : VK.VK_IMAGE_VIEW_TYPE_CUBE;
                vkImageType = VK.VK_IMAGE_TYPE_2D;
                vkCreateFlags = VK.VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT;
                numLayers *= 6;
                break;
            default:
                HxDebug.Assert(false, "Code should NOT be reached");
                logger.LogError("Unsupported texture type: {TYPE}", desc.Type);
                return ResultCode.NotSupported;
        }

        VkExtent3D vkExtent = new(desc.Dimensions.Width, desc.Dimensions.Height, desc.Dimensions.Depth);

        uint32_t numLevels = desc.NumMipLevels;

        if (!(HxVkUtils.ValidateImageLimits(vkImageType, vkSamples, vkExtent, GetVkPhysicalDeviceProperties().limits, out var result)))
        {
            return result;
        }

        HxDebug.Assert(numLevels > 0, "The image must contain at least one mip-level");
        HxDebug.Assert(numLayers > 0, "The image must contain at least one layer");
        HxDebug.Assert(vkSamples > 0, "The image must contain at least one sample");
        HxDebug.Assert(vkExtent.width > 0);
        HxDebug.Assert(vkExtent.height > 0);
        HxDebug.Assert(vkExtent.depth > 0);

        VulkanImage image = new(this, usageFlags, vkExtent, vkImageType, vkFormat, vkSamples, numLevels, numLayers, vkFormat.IsDepthFormat(), vkFormat.IsStencilFormat(), debugName);
        VkComponentMapping mapping = new()
        {
            r = desc.Swizzle.R.ToVk(),
            g = desc.Swizzle.G.ToVk(),
            b = desc.Swizzle.B.ToVk(),
            a = desc.Swizzle.A.ToVk(),
        };

        uint32_t numPlanes = desc.Format.GetNumImagePlanes();
        bool isDisjoint = numPlanes > 1;

        if (isDisjoint)
        {
            // some constraints for multiplanar image formats
            HxDebug.Assert(vkImageType == VK.VK_IMAGE_TYPE_2D);
            HxDebug.Assert(vkSamples == VK.VK_SAMPLE_COUNT_1_BIT);
            HxDebug.Assert(numLayers == 1);
            HxDebug.Assert(numLevels == 1);
            vkCreateFlags |= VK.VK_IMAGE_CREATE_DISJOINT_BIT | VK.VK_IMAGE_CREATE_ALIAS_BIT | VK.VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;
            AwaitingNewImmutableSamplers = true;
        }

        VkSamplerYcbcrConversionInfo? ycbcrInfo = isDisjoint ? GetOrCreateYcbcrConversionInfo(desc.Format) : null;
        var ret = image.Create(vkCreateFlags, memFlags, mapping, vkImageViewType, ycbcrInfo);

        if (ret.HasError())
        {
            HxDebug.Assert(false, "Failed to create image: {ERROR}", ret.ToString());
            logger.LogError("Failed to create image: {ERROR}", ret.ToString());
            image.Dispose();
            return ret;
        }

        var handle = TexturesPool.Create(image);

        AwaitingCreation = true;

        if (!desc.Data.IsNull())
        {
            HxDebug.Assert(desc.Type == TextureType.Texture2D || desc.Type == TextureType.TextureCube);
            HxDebug.Assert(desc.DataNumMipLevels <= desc.NumMipLevels);
            desc.NumLayers = desc.Type == TextureType.TextureCube ? 6u : 1u;
            ResultCode res = Upload(handle, new TextureRangeDesc() { dimensions = desc.Dimensions, numLayers = numLayers, numMipLevels = desc.DataNumMipLevels }, desc.Data, desc.DataSize);
            if (res != ResultCode.Ok)
            {
                return res;
            }
            if (desc.GenerateMipmaps)
            {
                GenerateMipmap(handle);
            }
        }

        texture = new TextureResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateTextureView(in TextureHandle texture, in TextureViewDesc desc, out TextureResource textureView, string? debugName)
    {
        textureView = TextureResource.Null;
        if (!texture)
        {
            HxDebug.Assert(texture.Valid);
            return ResultCode.ArgumentError;
        }

        // make a copy and make it non-owning
        var image = TexturesPool.Get(texture)?.Clone();
        if (image is null || image == VulkanImage.Null)
        {
            logger.LogError("Invalid texture handle: {HANDLE}", texture.ToString());
            return ResultCode.ArgumentError;
        }

        image.IsOwningVkImage = false;

        // drop all existing image views - they belong to the base image
        image.ImageViewStorage = new VkImageView();
        foreach (var buf in image.imageViewForFramebuffer_)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = new VkImageView();
            }
        }

        VkImageAspectFlags aspect = 0;
        if (image.IsDepthFormat || image.IsStencilFormat)
        {
            if (image.IsDepthFormat)
            {
                aspect |= VK.VK_IMAGE_ASPECT_DEPTH_BIT;
            }
            else if (image.IsStencilFormat)
            {
                aspect |= VK.VK_IMAGE_ASPECT_STENCIL_BIT;
            }
        }
        else
        {
            aspect = VK.VK_IMAGE_ASPECT_COLOR_BIT;
        }

        var vkImageViewType = VkImageViewType.Image1D;
        switch (desc.Type)
        {
            case TextureType.Texture2D:
                vkImageViewType = desc.NumLayers > 1 ? VK.VK_IMAGE_VIEW_TYPE_2D_ARRAY : VK.VK_IMAGE_VIEW_TYPE_2D;
                break;
            case TextureType.Texture3D:
                vkImageViewType = VkImageViewType.Image3D;
                break;
            case TextureType.TextureCube:
                vkImageViewType = desc.NumLayers > 1 ? VK.VK_IMAGE_VIEW_TYPE_CUBE_ARRAY : VK.VK_IMAGE_VIEW_TYPE_CUBE;
                break;
            default:
                HxDebug.Assert(false, "Code should NOT be reached");
                logger.LogError("Unsupported texture view type {TYPE}", desc.Type);
                return ResultCode.NotSupported;
        }

        VkComponentMapping mapping = new()
        {
            r = desc.Swizzle.R.ToVk(),
            g = desc.Swizzle.G.ToVk(),
            b = desc.Swizzle.B.ToVk(),
            a = desc.Swizzle.A.ToVk(),
        };

        HxDebug.Assert(image.ImageFormat.GetNumImagePlanes() == 1, "Unsupported multiplanar image");

        image.ImageView = image.CreateImageView(vkDevice,
                                                 vkImageViewType,
                                                 image.ImageFormat,
                                                 aspect,
                                                 desc.MipLevel,
                                                 desc.NumMipLevels,
                                                 desc.Layer,
                                                 desc.NumLayers,
                                                 mapping,
                                                 null,
                                                 debugName);
        if (image.ImageView == VkImage.Null)
        {
            HxDebug.Assert(false, "Cannot create VkImageView");
            logger.LogError("Cannot create VkImageView");
            return ResultCode.RuntimeError;
        }

        if (image.UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_STORAGE_BIT))
        {
            if (!desc.Swizzle.Identity())
            {
                // use identity swizzle for storage images
                image.ImageViewStorage = image.CreateImageView(vkDevice,
                                                                vkImageViewType,
                                                                image.ImageFormat,
                                                                aspect,
                                                                desc.MipLevel,
                                                                desc.NumMipLevels,
                                                                desc.Layer,
                                                                desc.NumLayers,
                                                              new VkComponentMapping(),
                                                              null,
                                                              debugName);
                HxDebug.Assert(image.ImageViewStorage != VkImageView.Null);
            }
        }

        TextureHandle handle = TexturesPool.Create(image);

        AwaitingCreation = true;

        textureView = new TextureResource(this, handle);
        return ResultCode.Ok;
    }

    public ResultCode CreateBuffer(in BufferDesc requestedDesc, out BufferResource buffer, string? debugName)
    {
        buffer = new BufferResource();
        BufferDesc desc = requestedDesc;

        if (debugName != null)
            desc.DebugName = debugName;

        if (!UseStaging && (desc.Storage == StorageType.Device))
        {
            desc.Storage = StorageType.HostVisible;
        }

        // Use staging device to transfer data into the buffer when the storage is private to the device
        VkBufferUsageFlags usageFlags = (desc.Storage == StorageType.Device) ? VK.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK.VK_BUFFER_USAGE_TRANSFER_SRC_BIT : 0;

        if (desc.Usage == BufferUsageBits.None)
        {
            logger.LogError("Invalid buffer usage");
            return ResultCode.ArgumentError;
        }

        if (desc.Usage.HasFlag(BufferUsageBits.Index))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
        }
        if (desc.Usage.HasFlag(BufferUsageBits.Vertex))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
        }
        if (desc.Usage.HasFlag(BufferUsageBits.Uniform))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT | VK.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT;
        }

        if (desc.Usage.HasFlag(BufferUsageBits.Storage))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK.VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT;
        }

        if (desc.Usage.HasFlag(BufferUsageBits.Indirect))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT;
        }

        if (desc.Usage.HasFlag(BufferUsageBits.ShaderBindingTable))
        {
            usageFlags |= VK.VK_BUFFER_USAGE_SHADER_BINDING_TABLE_BIT_KHR | VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT;
        }

        HxDebug.Assert(usageFlags != VkBufferUsageFlags.None, "Invalid buffer usage");

        VkMemoryPropertyFlags memFlags = desc.Storage.ToVkMemoryPropertyFlags();

        var result = CreateBuffer(desc.DataSize, usageFlags, memFlags, out var handle, desc.DebugName);

        if (result != ResultCode.Ok)
        {
            return result;
        }

        if (desc.Data != IntPtr.Zero)
        {
            Upload(handle, 0, desc.Data, desc.DataSize);
        }

        buffer = new BufferResource(this, handle);
        return ResultCode.Ok;
    }

    public void FlushMappedMemory(in BufferHandle handle, uint offset, uint size)
    {
        var buf = BuffersPool.Get(handle);
        HxDebug.Assert(buf is not null);

        if (buf == null)
        {
            logger.LogError("Buffer handle is invalid for flush: {HANDLE}", handle.ToString());
            return;
        }

        HxDebug.Assert(buf.Valid);

        buf.FlushMappedMemory(offset, size);
    }

    public float GetAspectRatio(in TextureHandle handle)
    {
        if (!handle.Valid)
        {
            return 1.0f;
        }

        var tex = TexturesPool.Get(handle);

        HxDebug.Assert(tex is not null);

        return tex is null ? 1.0f : tex.Extent.width / (float)tex.Extent.height;
    }

    public TextureHandle GetCurrentSwapchainTexture()
    {
        if (!HasSwapchain)
        {
            return TextureHandle.Null;
        }

        TextureHandle tex = Swapchain!.GetCurrentTexture();


        if (tex.Empty)
        {
            HxDebug.Assert(false, "Swapchain has no valid texture");
            return TextureHandle.Null;
        }

        HxDebug.Assert(TexturesPool.Get(tex)?.ImageFormat != VK.VK_FORMAT_UNDEFINED, "Invalid image format");

        return tex;
    }

    public Dimensions GetDimensions(in TextureHandle handle)
    {
        if (!handle)
        {
            return new();
        }

        var tex = TexturesPool.Get(handle);

        HxDebug.Assert(tex is not null);

        return tex is null
            ? new()
            : new()
            {
                Width = tex.Extent.width,
                Height = tex.Extent.height,
                Depth = tex.Extent.depth,
            };
    }

    public Format GetFormat(in TextureHandle handle)
    {
        if (handle.Empty)
        {
            return Format.Invalid;
        }
        var image = TexturesPool.Get(handle);
        if (image is null)
        {
            logger.LogError("Texture handle is invalid: {HANDLE}", handle.ToString());
            return Format.Invalid;
        }

        return image.ImageFormat.ToFormat();
    }

    public uint32_t GetFramebufferMSAABitMask()
    {
        return (uint32_t)GetFramebufferMSAABitMaskVK();
    }

    public nint GetMappedPtr(in BufferHandle handle)
    {
        var buf = BuffersPool.Get(handle);

        HxDebug.Assert(buf is not null);

        return buf!.IsMapped ? buf.MappedPtr : IntPtr.Zero;
    }

    public uint GetMaxStorageBufferRange()
    {
        return vkPhysicalDeviceProperties2.properties.limits.maxStorageBufferRange;
    }

    public uint GetNumSwapchainImages()
    {
        return HasSwapchain ? Swapchain!.NumSwapchainImages : 0;
    }

    public bool GetQueryPoolResults(in QueryPoolHandle pool, uint firstQuery, uint queryCount, uint dataSize, nint outData, uint stride)
    {
        var vkPool = QueriesPool.Get(pool);
        unsafe
        {
            VK.vkGetQueryPoolResults(
                vkDevice, vkPool, firstQuery, queryCount, dataSize, (void*)outData, stride, VK.VK_QUERY_RESULT_WAIT_BIT | VK.VK_QUERY_RESULT_64_BIT).CheckResult();
        }
        return true;
    }

    public ColorSpace GetSwapchainColorSpace()
    {
        if (!HasSwapchain)
        {
            return ColorSpace.SRGB_NONLINEAR;
        }

        return Swapchain!.SurfaceFormat.colorSpace.ToColorSpace();
    }

    public uint GetSwapchainCurrentImageIndex()
    {
        if (HasSwapchain)
        {
            // make sure we do not use a stale image
            Swapchain!.GetCurrentTexture();
        }

        return HasSwapchain ? Swapchain!.CurrentImageIndex : 0;
    }

    public Format GetSwapchainFormat()
    {
        return !HasSwapchain ? Format.Invalid : Swapchain!.SurfaceFormat.format.ToFormat();
    }

    public double GetTimestampPeriodToMs()
    {
        return GetVkPhysicalDeviceProperties().limits.timestampPeriod * 1e-6;
    }

    public ulong GpuAddress(in BufferHandle handle, uint offset)
    {
        HxDebug.Assert((offset & 7) == 0, "Buffer offset must be 8 bytes aligned as per GLSL_EXT_buffer_reference spec.");

        var buf = BuffersPool.Get(handle);

        HxDebug.Assert(buf && buf!.VkDeviceAddress != 0);

        return buf ? (uint64_t)buf!.VkDeviceAddress + offset : 0u;
    }

    public void RecreateSwapchain(int newWidth, int newHeight)
    {
        HxDebug.Assert(newWidth > 0 && newHeight > 0);
        InitSwapchain((uint)newWidth, (uint)newHeight);
    }

    public SubmitHandle Submit(ICommandBuffer commandBuffer, in TextureHandle present)
    {
        HxDebug.Assert(Immediate != null);
        if (commandBuffer is not CommandBuffer vkCmdBuffer)
        {
            throw new ArgumentException("CommandBuffer must be of type Vulkan CommandBuffer", nameof(commandBuffer));
        }

        if (present)
        {
            var tex = TexturesPool.Get(present);

            HxDebug.Assert(tex is not null && tex.IsSwapchainImage);
            if (tex is null || !tex.IsSwapchainImage)
            {
                logger.LogError("Cannot present texture: {HANDLE}", present.ToString());
                return SubmitHandle.Null;
            }

            tex.TransitionLayout(vkCmdBuffer.CmdBuffer,
                                 VK.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
                                 new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
        }

        var shouldPresent = HasSwapchain && present;

        if (shouldPresent)
        {
            // if we a presenting a swapchain image, signal our timeline semaphore
            uint64_t signalValue = Swapchain!.CurrentFrameIndex + Swapchain.NumSwapchainImages;
            // we wait for this value next time we want to acquire this swapchain image
            Swapchain.TimelineWaitValues[Swapchain.CurrentImageIndex] = signalValue;
            Immediate!.SignalSemaphore(TimelineSemaphore, signalValue);
        }

        vkCmdBuffer.LastSubmitHandle = Immediate!.Submit(vkCmdBuffer.Wrapper);

        if (shouldPresent)
        {
            Swapchain!.Present(Immediate.AcquireLastSubmitSemaphore());
        }

        ProcessDeferredTasks();

        SubmitHandle handle = vkCmdBuffer.LastSubmitHandle;

        // reset
        currentCommandBuffer_ = null;

        return handle;
    }

    public void Wait(in SubmitHandle handle)
    {
        Immediate!.Wait(handle);
    }

    public void Destroy(ComputePipelineHandle handle)
    {
        var cps = ComputePipelinesPool.Get(handle);

        if (cps is null || !cps.Valid)
        {
            return;
        }

        cps.SpecConstantDataStorage = [];
        cps.Desc.SpecInfo.Data = [];

        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroyPipeline(vkDevice, cps.Pipeline, null);
            }
        }, SubmitHandle.Null);
        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroyPipelineLayout(vkDevice, cps.PipelineLayout, null);
            }
        }, SubmitHandle.Null);

        ComputePipelinesPool.Destroy(handle);
    }

    public void Destroy(RenderPipelineHandle handle)
    {
        var rps = RenderPipelinesPool.Get(handle);

        if (rps is null || !rps.Valid)
        {
            return;
        }

        rps.SpecConstantDataStorage = [];
        rps.Desc.SpecInfo.Data = [];

        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroyPipeline(vkDevice, rps.Pipeline, null);
            }
        }, SubmitHandle.Null);
        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroyPipelineLayout(vkDevice, rps.PipelineLayout, null);
            }
        }, SubmitHandle.Null);

        RenderPipelinesPool.Destroy(handle);
    }

    public void Destroy(ShaderModuleHandle handle)
    {
        var state = ShaderModulesPool.Get(handle);

        if (state is null || !state.Valid)
        {
            logger.LogError("Shader module handle is invalid: {HANDLE}", handle.ToString());
            return;
        }

        unsafe
        {
            // a shader module can be destroyed while pipelines created using its shaders are still in use
            // https://registry.khronos.org/vulkan/specs/1.3/html/chap9.html#vkDestroyShaderModule
            VK.vkDestroyShaderModule(vkDevice, state.ShaderModule, null);
        }

        ShaderModulesPool.Destroy(handle);
    }

    public void Destroy(SamplerHandle handle)
    {
        var sampler = SamplersPool.Get(handle);

        SamplersPool.Destroy(handle);

        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroySampler(vkDevice, sampler, null);
            }
        }, SubmitHandle.Null);
    }

    public void Destroy(BufferHandle handle)
    {
        using var scope = new Scope(() =>
        {
            BuffersPool.Destroy(handle);
        });

        var buf = BuffersPool.Get(handle);
        buf?.Dispose();
    }

    public void Destroy(TextureHandle handle)
    {
        using var scope = new Scope(() =>
        {
            TexturesPool.Destroy(handle);
            AwaitingCreation = true; // make the validation layers happy
        });

        var tex = TexturesPool.Get(handle);
        if (tex is null || !tex.IsOwningVkImage)
        {
            return;
        }
        tex?.Dispose();
    }

    public void Destroy(QueryPoolHandle handle)
    {
        VkQueryPool pool = QueriesPool.Get(handle);
        using var scope = new Scope(() =>
        {
            QueriesPool.Destroy(handle);
        });

        DeferredTask(() =>
        {
            unsafe
            {
                VK.vkDestroyQueryPool(vkDevice, pool, null);
            }
        }, SubmitHandle.Null);
    }
    #endregion
}
