namespace HelixToolkit.Nex.Graphics.Vulkan;


internal sealed class VulkanImage : IDisposable
{
    static readonly ILogger logger = LogManager.Create<VulkanImage>();
    readonly VulkanContext? ctx;
    VkImage vkImage = VkImage.Null;

    readonly VkDeviceMemory[] memory = [VkDeviceMemory.Null, VkDeviceMemory.Null, VkDeviceMemory.Null];
    VmaAllocation vmaAllocation = VmaAllocation.Null;
    VkFormatProperties formatProperties = new();

    nint mappedPtr = IntPtr.Zero;

    readonly string? debugName;
    // current image layout
    public VkImageLayout ImageLayout = VkImageLayout.Undefined;


    public bool IsSwapchainImage { get; } = false;
    public bool IsOwningVkImage { set; get; } = true;
    public bool IsResolveAttachment { set; get; } = false; // autoset by cmdBeginRendering() for extra synchronization

    public uint32_t NumLevels { private set; get; } = 1u;
    public uint32_t NumLayers { private set; get; } = 1u;
    public VkSampleCountFlags SampleCount { private set; get; } = VkSampleCountFlags.Count1;
    public bool IsDepthFormat { get; } = false;
    public bool IsStencilFormat { get; } = false;
    public readonly VkExtent3D Extent;
    public readonly VkImageType ImageType;
    public readonly VkFormat ImageFormat = VkFormat.Undefined;
    public readonly VkImageView[][] imageViewForFramebuffer_ = new VkImageView[Constants.LVK_MAX_MIP_LEVELS][]; // max 6 faces for cubemap rendering
    public readonly VkImageUsageFlags UsageFlags = 0;
    public VkImage Image => vkImage;
    // precached image views - owned by this VulkanImage
    public VkImageView ImageView = VkImageView.Null; // default view with all mip-levels
    public VkImageView ImageViewStorage = VkImageView.Null; // default view with identity swizzle (all mip-levels)
    public nint MappedPtr => mappedPtr;
    public bool Valid => ctx is not null && Image != VkImage.Null && UsageFlags != 0;

    public bool IsSampledImage => UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_SAMPLED_BIT);
    public bool IsStorageImage => UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_STORAGE_BIT);
    public bool IsColorAttachment => UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT);
    public bool IsDepthAttachment => UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT);
    public bool IsAttachment => UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) | UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT);

    public VulkanImage() { }

    public VulkanImage(VulkanContext ctx, VkImage image, VkImageUsageFlags usage, VkExtent3D extent, VkImageType type,
           VkFormat format, bool isDepthFormat, bool isStencilFormat,
           bool isSwapchainImage, bool isOwningVkImage, string? debugName = null) : this(
               ctx, usage, extent, type, format, VkSampleCountFlags.None, 0, 0, isDepthFormat, isStencilFormat, debugName)
    {
        this.ctx = ctx;
        vkImage = image;
        IsSwapchainImage = isSwapchainImage;
        IsOwningVkImage = isOwningVkImage; // this image is not owned by this class, so don't destroy it
    }

    public VulkanImage(VulkanContext ctx, VkImageUsageFlags usage, VkExtent3D extent, VkImageType type,
           VkFormat format, VkSampleCountFlags samples, uint32_t numLevels, uint32_t numLayers,
           bool isDepthFormat, bool isStencilFormat, string? debugName)
    {
        this.ctx = ctx;
        UsageFlags = usage;
        Extent = extent;
        ImageType = type;
        ImageFormat = format;
        SampleCount = samples;
        NumLevels = numLevels > 0 ? numLevels : 1u; // at least one level
        NumLayers = numLayers > 0 ? numLayers : 1u; // at least one layer
        IsDepthFormat = isDepthFormat || format.IsDepthFormat();
        IsStencilFormat = isStencilFormat || format.IsStencilFormat();
        this.debugName = debugName;

        for (int i = 0; i < Constants.LVK_MAX_MIP_LEVELS; i++)
        {
            imageViewForFramebuffer_[i] = new VkImageView[6];
            for (int j = 0; j < 6; j++)
            {
                imageViewForFramebuffer_[i][j] = VkImageView.Null;
            }
        }
    }

    public ResultCode Create(VkImageCreateFlags vkCreateFlags, VkMemoryPropertyFlags memFlags, in VkComponentMapping mapping,
        VkImageViewType vkImageViewType, VkSamplerYcbcrConversionInfo? samplerYcbcrInfo)
    {
        uint32_t numPlanes = ImageFormat.GetNumImagePlanes();
        bool isDisjoint = numPlanes > 1;
        string debugNameImage = $"Image: {debugName ?? string.Empty}";
        string debugNameImageView = $"ImageView: {debugName ?? string.Empty}";

        if (isDisjoint)
        {
            // some constraints for multiplanar image formats
            HxDebug.Assert(ImageType == VK.VK_IMAGE_TYPE_2D);
            HxDebug.Assert(SampleCount == VK.VK_SAMPLE_COUNT_1_BIT);
            HxDebug.Assert(NumLayers == 1);
            HxDebug.Assert(NumLevels == 1);
            vkCreateFlags |= VK.VK_IMAGE_CREATE_DISJOINT_BIT | VK.VK_IMAGE_CREATE_ALIAS_BIT | VK.VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;
            ctx!.AwaitingNewImmutableSamplers = true;
        }

        VkImageCreateInfo ci = new()
        {
            pNext = null,
            flags = vkCreateFlags,
            imageType = ImageType,
            format = ImageFormat,
            extent = Extent,
            mipLevels = NumLevels,
            arrayLayers = NumLayers,
            samples = SampleCount,
            tiling = VK.VK_IMAGE_TILING_OPTIMAL,
            usage = UsageFlags,
            sharingMode = VK.VK_SHARING_MODE_EXCLUSIVE,
            queueFamilyIndexCount = 0,
            pQueueFamilyIndices = null,
            initialLayout = VK.VK_IMAGE_LAYOUT_UNDEFINED,
        };

        if (ctx!.UseVmaAllocator && numPlanes == 1)
        {
            unsafe
            {
                VmaAllocationCreateInfo vmaAllocInfo = new()
                {
                    usage = memFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) ? VmaMemoryUsage.CpuToGpu : VmaMemoryUsage.Auto,
                };

                var ret = Vma.vmaCreateImage(ctx!.VmaAllocator, &ci, &vmaAllocInfo, out vkImage, out vmaAllocation, out _);
                if (ret != VK.VK_SUCCESS)
                {
                    logger.LogError("Failed: error result: {RESULT}, memflags: {FLAG},  imageformat: {FORMAT}", ret, memFlags, ImageFormat);
                    logger.LogError("VmaCreateImage() failed");
                    return ResultCode.RuntimeError;
                }
                // handle memory-mapped buffers
                if (memFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
                {
                    void* mappedPtr;
                    Vma.vmaMapMemory(ctx!.VmaAllocator, vmaAllocation, &mappedPtr).CheckResult();
                    this.mappedPtr = (nint)mappedPtr;
                }
            }
        }
        else
        {
            unsafe
            {
                fixed (VkImage* pImage = &vkImage)
                {
                    VK.vkCreateImage(ctx!.VkDevice, &ci, null, pImage).CheckResult();
                }

                // back the image with some memory
                int kNumMaxImagePlanes = memory.Length;

                VkMemoryRequirements2[] memRequirements = [
                    new VkMemoryRequirements2(),
                    new VkMemoryRequirements2(),
                    new VkMemoryRequirements2()
                    ];
                VkImagePlaneMemoryRequirementsInfo[] planes = [
                    new (){planeAspect = VkImageAspectFlags.Plane0 },
                    new (){planeAspect = VkImageAspectFlags.Plane1 },
                    new (){planeAspect = VkImageAspectFlags.Plane2 },
                    ];
                var imgRequirements = new VkImageMemoryRequirementsInfo2[kNumMaxImagePlanes];
                var bindImagePlaneMemoryInfo = new VkBindImagePlaneMemoryInfo[kNumMaxImagePlanes];
                var bindInfo = new VkBindImageMemoryInfo[kNumMaxImagePlanes];

                using var pMemInfo = bindImagePlaneMemoryInfo.Pin();
                using var pPlanes = planes.Pin();
                using var pImageReq = imgRequirements.Pin();
                using var pMemReq = memRequirements.Pin();

                for (int i = 0; i < kNumMaxImagePlanes; i++)
                {
                    imgRequirements[i] = new VkImageMemoryRequirementsInfo2
                    {
                        pNext = i < numPlanes ? &((VkImagePlaneMemoryRequirementsInfo*)pPlanes.Pointer)[i] : null,
                        image = vkImage,
                    };
                }
                for (uint32_t p = 0; p != numPlanes; p++)
                {
                    VK.vkGetImageMemoryRequirements2(ctx!.VkDevice, &((VkImageMemoryRequirementsInfo2*)pImageReq.Pointer)[p], &((VkMemoryRequirements2*)pMemReq.Pointer)[p]);
                    HxVkUtils.AllocateMemory2(ctx!.VkPhysicalDevice, ctx!.VkDevice, memRequirements[p], memFlags, out memory[p]);
                }
                for (int i = 0; i < kNumMaxImagePlanes; i++)
                {

                    bindImagePlaneMemoryInfo[i] = new VkBindImagePlaneMemoryInfo
                    {
                        planeAspect = (VkImageAspectFlags)(1 << i), // VK_IMAGE_ASPECT_PLANE_0_BIT, VK_IMAGE_ASPECT_PLANE_1_BIT, etc.
                    };
                    bindInfo[i] = new VkBindImageMemoryInfo
                    {
                        pNext = isDisjoint ? &((VkBindImagePlaneMemoryInfo*)pMemInfo.Pointer)[i] : null,
                        image = vkImage,
                        memory = memory[i],
                        memoryOffset = 0,
                    };
                }
                using var pBindInfo = bindInfo.Pin();

                VK.vkBindImageMemory2(ctx!.VkDevice, numPlanes, (VkBindImageMemoryInfo*)pBindInfo.Pointer).CheckResult();

                // handle memory-mapped images
                if (memFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) && numPlanes == 1)
                {
                    void* mappedPtr;
                    VK.vkMapMemory(ctx!.VkDevice, memory[0], 0, VK.VK_WHOLE_SIZE, 0, &mappedPtr).CheckResult();
                    this.mappedPtr = (nint)mappedPtr;
                }
            }
        }
        if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugNameImage))
        {
            // set debug name for the image
            ctx!.VkDevice.SetDebugObjectName(VK.VK_OBJECT_TYPE_IMAGE, (nuint)vkImage, debugNameImage);
        }

        unsafe
        {
            fixed (VkFormatProperties* props = &formatProperties)
            {
                // Get physical device's properties for the image's format
                VK.vkGetPhysicalDeviceFormatProperties(ctx!.VkPhysicalDevice, ImageFormat, props);
            }
        }

        VkImageAspectFlags aspect = 0;
        if (IsDepthFormat || IsStencilFormat)
        {
            if (IsDepthFormat)
            {
                aspect |= VK.VK_IMAGE_ASPECT_DEPTH_BIT;
            }
            else if (IsStencilFormat)
            {
                aspect |= VK.VK_IMAGE_ASPECT_STENCIL_BIT;
            }
        }
        else
        {
            aspect = VK.VK_IMAGE_ASPECT_COLOR_BIT;
        }

        ImageView = CreateImageView(
            ctx!.VkDevice, vkImageViewType, ImageFormat, aspect, 0, VK.VK_REMAINING_MIP_LEVELS, 0, NumLayers, mapping, samplerYcbcrInfo, debugNameImageView);

        if (UsageFlags.HasFlag(VK.VK_IMAGE_USAGE_STORAGE_BIT))
        {
            if (!mapping.Identity())
            {
                // use identity swizzle for storage images
                ImageViewStorage = CreateImageView(
                    ctx!.VkDevice, vkImageViewType, ImageFormat, aspect, 0, VK.VK_REMAINING_MIP_LEVELS, 0, NumLayers, new VkComponentMapping(), samplerYcbcrInfo, debugNameImageView);
                HxDebug.Assert(ImageViewStorage != VkImageView.Null);
            }
        }

        if (ImageView == VkImageView.Null)
        {
            logger.LogError("Cannot create VkImageView");
            return ResultCode.RuntimeError;
        }
        return ResultCode.Ok;
    }


    public VkImageView CreateImageView(in VkDevice device,
                                        VkImageViewType type,
                                        VkFormat format,
                                        VkImageAspectFlags aspectMask,
                                        uint32_t baseLevel,
                                        uint32_t numLevels = VK.VK_REMAINING_MIP_LEVELS,
                                        uint32_t baseLayer = 0,
                                        uint32_t numLayers = 1,
                                        string? debugName = null)
    {
        return CreateImageView(device, type, format, aspectMask, baseLevel, numLevels, baseLayer, numLayers, new VkComponentMapping(), null, debugName);
    }
    /*
     * Setting `numLevels` to a non-zero value will override `mipLevels_` value from the original Vulkan image, and can be used to create
     * image views with different number of levels.
     */
    public VkImageView CreateImageView(in VkDevice device,
                                            VkImageViewType type,
                                            VkFormat format,
                                            VkImageAspectFlags aspectMask,
                                            uint32_t baseLevel,
                                            uint32_t numLevels,
                                            uint32_t baseLayer,
                                            uint32_t numLayers,
                                            in VkComponentMapping mapping,
                                            in VkSamplerYcbcrConversionInfo? ycbcr = null,
                                            string? debugName = null)
    {
        unsafe
        {
            VkSamplerYcbcrConversionInfo cYcbcr = ycbcr is null ? new VkSamplerYcbcrConversionInfo() : ycbcr.Value;
            VkImageViewCreateInfo ci = new()
            {
                pNext = ycbcr is null ? null : &cYcbcr,
                image = vkImage,
                viewType = type,
                format = format,
                components = mapping,
                subresourceRange = new() { aspectMask = aspectMask, baseMipLevel = baseLevel, levelCount = numLevels > 0 ? numLevels : NumLevels, baseArrayLayer = baseLayer, layerCount = numLayers },
            };
            VkImageView vkView = VkImageView.Null;
            VK.vkCreateImageView(device, &ci, null, &vkView).CheckResult();
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                device.SetDebugObjectName(VK.VK_OBJECT_TYPE_IMAGE_VIEW, (nuint)vkView.Handle, debugName);
            }
            return vkView;
        }

    }

    public void GenerateMipmap(in VkCommandBuffer commandBuffer)
    {
        const VkFormatFeatureFlags formatFeatureMask = (VK.VK_FORMAT_FEATURE_BLIT_SRC_BIT | VK.VK_FORMAT_FEATURE_BLIT_DST_BIT);

        bool hardwareDownscalingSupported = (formatProperties.optimalTilingFeatures & formatFeatureMask) == formatFeatureMask;

        if (!hardwareDownscalingSupported)
        {
            logger.LogWarning("Doesn't support hardware downscaling of this image format: {FORMAT}", ImageFormat);
            return;
        }
        // Choose linear filter for color formats if supported by the device, else use nearest filter
        // Choose nearest filter by default for depth/stencil formats
        var isDepthOrStencilFormat = IsDepthFormat || IsStencilFormat;
        var imageFilterLinear = formatProperties.optimalTilingFeatures.HasFlag(VK.VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT);
        var blitFilter = VK.VK_FILTER_NEAREST;
        if (isDepthOrStencilFormat)
        {
            blitFilter = VK.VK_FILTER_NEAREST;
        }
        if (imageFilterLinear)
        {
            blitFilter = VK.VK_FILTER_LINEAR;
        }
        VkImageAspectFlags imageAspectFlags = GetImageAspectFlags();

        unsafe
        {

            if (GraphicsSettings.EnableDebug)
            {
                VkUtf8ReadOnlyString label = "GenerateMipMaps"u8;
                VkDebugUtilsLabelEXT utilsLabel = new()
                {
                    pLabelName = label
                };
                utilsLabel.color[0] = 1.0f;
                utilsLabel.color[1] = 0.75f;
                utilsLabel.color[2] = 1.0f;
                utilsLabel.color[3] = 1.0f;
                VK.vkCmdBeginDebugUtilsLabelEXT(commandBuffer, &utilsLabel);
            }

            VkImageLayout originalImageLayout = ImageLayout;

            HxDebug.Assert(originalImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);

            // 0: Transition the first level and all layers into VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
            TransitionLayout(commandBuffer, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, new VkImageSubresourceRange
            {
                aspectMask = imageAspectFlags,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = NumLayers
            });

            for (uint32_t layer = 0; layer < NumLayers; ++layer)
            {
                int32_t mipWidth = (int32_t)Extent.width;
                int32_t mipHeight = (int32_t)Extent.height;

                for (uint32_t i = 1; i < NumLevels; ++i)
                {
                    // 1: Transition the i-th level to VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL; it will be copied into from the (i-1)-th layer
                    commandBuffer.ImageMemoryBarrier2(vkImage, new StageAccess2
                    { stage = VkPipelineStageFlags2.TopOfPipe, access = VkAccessFlags2.None },
                    new StageAccess2
                    { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferWrite },
                    VK.VK_IMAGE_LAYOUT_UNDEFINED, // oldImageLayout
                    VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, // newImageLayout
                    new VkImageSubresourceRange(imageAspectFlags, i, 1, layer, 1));

                    int32_t nextLevelWidth = mipWidth > 1 ? mipWidth / 2 : 1;
                    int32_t nextLevelHeight = mipHeight > 1 ? mipHeight / 2 : 1;

                    // 2: Blit the image from the prev mip-level (i-1) (VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL) to the current mip-level (i)
                    // (VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL)

                    VkImageBlit blit = new()
                    {
                        srcSubresource = new VkImageSubresourceLayers(imageAspectFlags, i - 1, layer, 1),
                        dstSubresource = new VkImageSubresourceLayers(imageAspectFlags, i, layer, 1),
                    };
                    blit.srcOffsets[0] = new VkOffset3D(0, 0, 0);
                    blit.srcOffsets[1] = new VkOffset3D(mipWidth, mipHeight, 1);
                    blit.dstOffsets[0] = new VkOffset3D(0, 0, 0);
                    blit.dstOffsets[1] = new VkOffset3D(nextLevelWidth, nextLevelHeight, 1);
                    VK.vkCmdBlitImage(commandBuffer,
                                   vkImage,
                                   VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                                   vkImage,
                                   VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                   1,
                                   &blit,
                                   blitFilter);
                    // 3: Transition i-th level to VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL as it will be read from in the next iteration
                    commandBuffer.ImageMemoryBarrier2(vkImage, new StageAccess2
                    { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferWrite },
                    new StageAccess2 { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferRead },
                    VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, /* oldImageLayout */
                    VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, /* newImageLayout */
                    new VkImageSubresourceRange(imageAspectFlags, i, 1, layer, 1));

                    // Compute the size of the next mip-level
                    mipWidth = nextLevelWidth;
                    mipHeight = nextLevelHeight;
                }
            }

            // 4: Transition all levels and layers (faces) to their final layout
            commandBuffer.ImageMemoryBarrier2(vkImage,
                new StageAccess2 { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferRead },
                new StageAccess2 { stage = VkPipelineStageFlags2.AllCommands, access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite },
                VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, // oldImageLayout
                originalImageLayout, // newImageLayout
                new VkImageSubresourceRange(imageAspectFlags, 0, NumLevels, 0, NumLayers));

            if (GraphicsSettings.EnableDebug)
            {
                VK.vkCmdEndDebugUtilsLabelEXT(commandBuffer);
            }

            ImageLayout = originalImageLayout;
        }
    }

    public void TransitionLayout(in VkCommandBuffer commandBuffer, VkImageLayout newImageLayout, in VkImageSubresourceRange subresourceRange)
    {
        VkImageLayout oldImageLayout = ImageLayout == VK.VK_IMAGE_LAYOUT_ATTACHMENT_OPTIMAL
            ? (IsDepthAttachment ? VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL : VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL)
            : ImageLayout;

        if (newImageLayout == VK.VK_IMAGE_LAYOUT_ATTACHMENT_OPTIMAL)
        {
            newImageLayout = IsDepthAttachment ? VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL : VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        }

        var src = oldImageLayout.GetPipelineStageAccess();
        var dst = newImageLayout.GetPipelineStageAccess();

        if (IsDepthAttachment && IsResolveAttachment)
        {
            // https://registry.khronos.org/vulkan/specs/latest/html/vkspec.html#renderpass-resolve-operations
            src.stage |= VkPipelineStageFlags2.ColorAttachmentOutput;
            dst.stage |= VkPipelineStageFlags2.ColorAttachmentOutput;
            src.access |= VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ColorAttachmentWrite;
            dst.access |= VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ColorAttachmentWrite;
        }
        commandBuffer.ImageMemoryBarrier2(vkImage, src, dst, oldImageLayout, newImageLayout, subresourceRange);
        ImageLayout = newImageLayout;
    }

    public VkImageAspectFlags GetImageAspectFlags()
    {
        VkImageAspectFlags flags = 0;

        flags |= IsDepthFormat ? VkImageAspectFlags.Depth : 0;
        flags |= IsStencilFormat ? VkImageAspectFlags.Stencil : 0;
        flags |= !(IsDepthFormat || IsStencilFormat) ? VkImageAspectFlags.Color : 0;

        return flags;
    }

    // framebuffers can render only into one level/layer
    public VkImageView GetOrCreateVkImageViewForFramebuffer(VulkanContext ctx, uint8_t level, uint16_t layer)
    {
        HxDebug.Assert(level < Constants.LVK_MAX_MIP_LEVELS);
        HxDebug.Assert(layer < imageViewForFramebuffer_[0].Length);

        if (level >= Constants.LVK_MAX_MIP_LEVELS || layer >= imageViewForFramebuffer_[0].Length)
        {
            return VkImageView.Null;
        }

        if (imageViewForFramebuffer_[level][layer] != VkImageView.Null)
        {
            return imageViewForFramebuffer_[level][layer];
        }

        var debugNameImageView = $"Image View: '{debugName}' imageViewForFramebuffer_[{level}][{layer}]";

        imageViewForFramebuffer_[level][layer] = CreateImageView(ctx.GetVkDevice(),
                                                                 VK.VK_IMAGE_VIEW_TYPE_2D,
                                                                 ImageFormat,
                                                                 GetImageAspectFlags(),
                                                                 level,
                                                                 1u,
                                                                 layer,
                                                                 1u,
                                                                   debugNameImageView);

        return imageViewForFramebuffer_[level][layer];
    }

    public VulkanImage Clone()
    {
        if (!Valid)
        {
            logger.LogError("Cannot clone an invalid VulkanImage.");
            return Null;
        }
        return new VulkanImage(ctx!, vkImage, UsageFlags, Extent, ImageType, ImageFormat, IsDepthFormat, IsStencilFormat,
            IsSwapchainImage, IsOwningVkImage, debugName)
        {
            ImageLayout = ImageLayout,
            NumLevels = NumLevels,
            NumLayers = NumLayers,
            SampleCount = SampleCount,
            ImageView = ImageView,
            ImageViewStorage = ImageViewStorage,
            mappedPtr = mappedPtr
        };
    }

    #region IDisposable Support
    private bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                if (!Valid)
                {
                    return;
                }
                var vkDevice = ctx!.VkDevice;
                if (ImageView.IsNotNull)
                {
                    ctx!.DeferredTask(() =>
                    {
                        unsafe
                        {
                            VK.vkDestroyImageView(vkDevice, ImageView, null);
                        }
                    }, SubmitHandle.Null);
                }

                if (ImageViewStorage.IsNotNull)
                {
                    ctx!.DeferredTask(() =>
                    {
                        unsafe
                        {
                            VK.vkDestroyImageView(vkDevice, ImageViewStorage, null);
                        }
                    }, SubmitHandle.Null);
                }

                for (size_t i = 0; i < Constants.LVK_MAX_MIP_LEVELS; i++)
                {
                    for (size_t j = 0; j < imageViewForFramebuffer_[0].Length; j++)
                    {
                        VkImageView v = imageViewForFramebuffer_[i][j];
                        if (v.IsNotNull)
                        {
                            ctx!.DeferredTask(() =>
                            {
                                unsafe
                                {
                                    VK.vkDestroyImageView(vkDevice, v, null);
                                }
                            }, SubmitHandle.Null);
                        }
                    }
                }

                if (!IsOwningVkImage)
                {
                    return;
                }

                if (ctx.UseVmaAllocator && memory[1].IsNull)
                {
                    if (mappedPtr.Valid())
                    {
                        Vma.vmaUnmapMemory(ctx!.VmaAllocator, vmaAllocation);
                    }
                    ctx!.DeferredTask(() =>
                    {
                        Vma.vmaDestroyImage(ctx!.VmaAllocator, vkImage, vmaAllocation);
                    }, SubmitHandle.Null);
                }
                else
                {
                    if (mappedPtr.Valid())
                    {
                        VK.vkUnmapMemory(vkDevice, memory[0]);
                    }
                    var image = vkImage;
                    ctx!.DeferredTask(() =>
                    {
                        unsafe
                        {
                            VK.vkDestroyImage(vkDevice, vkImage, null);
                            if (memory[0].IsNotNull)
                            {
                                VK.vkFreeMemory(vkDevice, memory[0], null);
                            }
                            if (memory[1].IsNotNull)
                            {
                                VK.vkFreeMemory(vkDevice, memory[1], null);
                            }
                            if (memory[2].IsNotNull)
                            {
                                VK.vkFreeMemory(vkDevice, memory[2], null);
                            }
                        }
                    }, SubmitHandle.Null);
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~VulkanImage()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
    #endregion

    public static readonly VulkanImage Null = new();
};