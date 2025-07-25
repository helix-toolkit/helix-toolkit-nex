

namespace HelixToolkit.Nex.Graphics.Vulkan;

internal class HxVkUtils
{
    private static readonly ILogger logger = LogManager.Create<HxVkUtils>();



    public unsafe static uint32_t FindQueueFamilyIndex(in VkPhysicalDevice physicalDevice, VkQueueFlags flags)
    {
        uint32_t queueFamilyCount = 0;
        // Use a temporary variable to avoid taking the address of a local variable directly
        uint32_t tempQueueFamilyCount = 0;
        VK.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &tempQueueFamilyCount, null);
        queueFamilyCount = tempQueueFamilyCount;

        var queueFamilies = new VkQueueFamilyProperties[queueFamilyCount];
        fixed (VkQueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            VK.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &tempQueueFamilyCount, queueFamiliesPtr);
        }

        uint32_t findDedicatedQueueFamilyIndex(VkQueueFlags require, VkQueueFlags avoid)
        {
            for (uint32_t i = 0; i < queueFamilyCount; i++)
            {
                if ((queueFamilies[i].queueFlags & require) == require && (queueFamilies[i].queueFlags & avoid) == 0
                && queueFamilies[i].queueCount > 0)
                {
                    return i;
                }
            }
            return DeviceQueues.INVALID;
        }

        // dedicated queue for compute
        if (flags.HasFlag(VK.VK_QUEUE_COMPUTE_BIT))
        {
            var q = findDedicatedQueueFamilyIndex(flags, VK.VK_QUEUE_GRAPHICS_BIT);
            if (q != DeviceQueues.INVALID)
                return q;
        }

        // dedicated queue for transfer
        if (flags.HasFlag(VK.VK_QUEUE_TRANSFER_BIT))
        {
            var q = findDedicatedQueueFamilyIndex(flags, VK.VK_QUEUE_GRAPHICS_BIT);
            if (q != DeviceQueues.INVALID)
                return q;
        }

        // any suitable
        return findDedicatedQueueFamilyIndex(flags, 0);
    }

    public static VmaAllocator CreateVmaAllocator(in VkPhysicalDevice physDev, in VkDevice device, in VkInstance instance, in VkVersion apiVersion)
    {
        unsafe
        {
            VmaAllocatorCreateInfo ci = new()
            {
                flags = VmaAllocatorCreateFlags.BufferDeviceAddress,
                physicalDevice = physDev,
                device = device,
                instance = instance,
                vulkanApiVersion = apiVersion,
            };

            Vma.vmaCreateAllocator(ci, out var vmaAllocator).CheckResult();
            return vmaAllocator;
        }
    }

    public unsafe static VkSpecializationInfo GetPipelineShaderStageSpecializationInfo(in SpecializationConstantDesc desc,
                                                                   VkSpecializationMapEntry* outEntries, void* dataStorage)
    {
        uint32_t numEntries = desc.NumSpecializationConstants();
        if (outEntries != null)
        {
            for (uint32_t i = 0; i < numEntries; i++)
            {
                outEntries[i] = new()
                {
                    constantID = desc.Entries[i].ConstantId,
                    offset = desc.Entries[i].Offset,
                    size = desc.Entries[i].Size,
                };
            }
        }

        return new VkSpecializationInfo
        {
            mapEntryCount = numEntries,
            pMapEntries = outEntries,
            dataSize = (nuint)desc.Data.Length,
            pData = dataStorage,
        };
    }

    public unsafe static VkBindImageMemoryInfo GetBindImageMemoryInfo(VkBindImagePlaneMemoryInfo* next, in VkImage image, in VkDeviceMemory memory)
    {
        return new VkBindImageMemoryInfo
        {
            pNext = next,
            image = image,
            memory = memory,
            memoryOffset = 0,
        };
    }

    public unsafe static VkPipelineShaderStageCreateInfo GetPipelineShaderStageCreateInfo(VkShaderStageFlags stage,
                                                                      in VkShaderModule shaderModule,
                                                                      in VkUtf8ReadOnlyString entryPoint,
                                                                      VkSpecializationInfo* specializationInfo)
    {
        return new VkPipelineShaderStageCreateInfo
        {
            stage = stage,
            module = shaderModule,
            pName = entryPoint,
            pSpecializationInfo = specializationInfo,
        };
    }

    public static uint32_t FindMemoryType(in VkPhysicalDevice physDev, uint32_t memoryTypeBits, VkMemoryPropertyFlags flags)
    {
        VkPhysicalDeviceMemoryProperties memProperties;
        unsafe
        {
            VK.vkGetPhysicalDeviceMemoryProperties(physDev, &memProperties);
        }

        for (uint32_t i = 0; i < memProperties.memoryTypeCount; i++)
        {
            bool hasProperties = (memProperties.memoryTypes[(int)i].propertyFlags & flags) == flags;
            if (((memoryTypeBits & (1u << (int)i)) != 0) && hasProperties) // Cast 'i' to int for the shift operation
            {
                return i;
            }
        }

        HxDebug.Assert(false, "Failed to find suitable memory type for the requested properties.");

        return 0;
    }

    public static VkResult AllocateMemory2(in VkPhysicalDevice physDev, in VkDevice device, in VkMemoryRequirements2 memRequirements,
        VkMemoryPropertyFlags props, out VkDeviceMemory outMemory)
    {
        outMemory = new VkDeviceMemory();
        unsafe
        {
            VkMemoryAllocateFlagsInfo memoryAllocateFlagsInfo = new()
            {
                flags = VkMemoryAllocateFlags.DeviceAddress,
            };
            VkMemoryAllocateInfo ai = new()
            {
                pNext = &memoryAllocateFlagsInfo,
                allocationSize = memRequirements.memoryRequirements.size,
                memoryTypeIndex = FindMemoryType(physDev, memRequirements.memoryRequirements.memoryTypeBits, props),
            };
            fixed (VkDeviceMemory* pOutMemory = &outMemory)
            {
                return VK.vkAllocateMemory(device, &ai, null, pOutMemory);
            }
        }
    }

    public static unsafe VkDescriptorSetLayoutBinding GetDSLBinding(uint32_t binding,
                                                VkDescriptorType descriptorType,
                                                uint32_t descriptorCount,
                                                VkShaderStageFlags stageFlags,
                                                VkSampler* immutableSamplers = null)
    {
        return new VkDescriptorSetLayoutBinding
        {
            binding = binding,
            descriptorType = descriptorType,
            descriptorCount = descriptorCount,
            stageFlags = stageFlags,
            pImmutableSamplers = immutableSamplers,
        };
    }

    public static VkSampleCountFlags GetVulkanSampleCountFlags(uint32_t numSamples, VkSampleCountFlags maxSamplesMask)
    {
        if (numSamples <= 1 || VK.VK_SAMPLE_COUNT_2_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_1_BIT;
        }
        if (numSamples <= 2 || VK.VK_SAMPLE_COUNT_4_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_2_BIT;
        }
        if (numSamples <= 4 || VK.VK_SAMPLE_COUNT_8_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_4_BIT;
        }
        if (numSamples <= 8 || VK.VK_SAMPLE_COUNT_16_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_8_BIT;
        }
        if (numSamples <= 16 || VK.VK_SAMPLE_COUNT_32_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_16_BIT;
        }
        if (numSamples <= 32 || VK.VK_SAMPLE_COUNT_64_BIT > maxSamplesMask)
        {
            return VK.VK_SAMPLE_COUNT_32_BIT;
        }
        return VK.VK_SAMPLE_COUNT_64_BIT;
    }

    public static VkExtent2D GetImagePlaneExtent(in VkExtent2D plane0, Format format, uint32_t plane)
    {
        switch (format)
        {
            case Format.YUV_NV12:
                return new VkExtent2D()
                {
                    width = plane0.width >> (int)plane,
                    height = plane0.height >> (int)plane,
                };
            case Format.YUV_420p:
                return new VkExtent2D()
                {
                    width = plane0.width >> (plane != 0 ? 1 : 0),
                    height = plane0.height >> (plane != 0 ? 1 : 0),
                };
        }
        return plane0;
    }

    public static bool HasExtension(in ReadOnlySpan<byte> ext, IList<VkExtensionProperties> props)
    {
        unsafe
        {
            foreach (var p in props)
            {
                int len = 0;
                while (p.extensionName[len] != 0)
                {
                    len++;
                }
                var span = new ReadOnlySpan<byte>(p.extensionName, len);
                if (ext.SequenceCompareTo(span) == 0)
                    return true;
            }
            return false;
        }
    }

    public static void GetOptimalValidationLayers(HashSet<VkUtf8String> availableLayers, List<VkUtf8String> instanceLayers)
    {
        // The preferred validation layer is "VK_LAYER_KHRONOS_validation"
        List<VkUtf8String> validationLayers =
        [
            "VK_LAYER_KHRONOS_validation"u8
        ];

        if (ValidateLayers(availableLayers, validationLayers))
        {
            instanceLayers.AddRange(validationLayers);
            return;
        }

        // Otherwise we fallback to using the LunarG meta layer
        validationLayers =
        [
            "VK_LAYER_LUNARG_standard_validation"u8
        ];

        if (ValidateLayers(availableLayers, validationLayers))
        {
            instanceLayers.AddRange(validationLayers);
            return;
        }

        // Otherwise we attempt to enable the individual layers that compose the LunarG meta layer since it doesn't exist
        validationLayers =
        [
            "VK_LAYER_GOOGLE_threading"u8,
            "VK_LAYER_LUNARG_parameter_validation"u8,
            "VK_LAYER_LUNARG_object_tracker"u8,
            "VK_LAYER_LUNARG_core_validation"u8,
            "VK_LAYER_GOOGLE_unique_objects"u8,
        ];

        if (ValidateLayers(availableLayers, validationLayers))
        {
            instanceLayers.AddRange(validationLayers);
            return;
        }

        // Otherwise as a last resort we fallback to attempting to enable the LunarG core layer
        validationLayers =
        [
            "VK_LAYER_LUNARG_core_validation"u8
        ];

        if (ValidateLayers(availableLayers, validationLayers))
        {
            instanceLayers.AddRange(validationLayers);
            return;
        }
    }

    private static bool ValidateLayers(HashSet<VkUtf8String> availableLayers, List<VkUtf8String> required)
    {
        foreach (VkUtf8String layer in required)
        {
            bool found = false;
            foreach (VkUtf8String availableLayer in availableLayers)
            {
                if (availableLayer == layer)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                //Log.Warn("Validation Layer '{}' not found", layer);
                return false;
            }
        }

        return true;
    }
    public ref struct SwapChainSupportDetails
    {
        public VkSurfaceCapabilitiesKHR Capabilities;
        public ReadOnlySpan<VkSurfaceFormatKHR> Formats;
        public ReadOnlySpan<VkPresentModeKHR> PresentModes;
    }

    public static SwapChainSupportDetails QuerySwapChainSupport(in VkPhysicalDevice physicalDevice, in VkSurfaceKHR surface)
    {
        SwapChainSupportDetails details = new();
        VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, out details.Capabilities).CheckResult();

        details.Formats = VK.vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface);
        details.PresentModes = VK.vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface);
        return details;
    }

    public static bool CheckDeviceExtensionSupport(in VkUtf8ReadOnlyString extensionName, ReadOnlySpan<VkExtensionProperties> availableDeviceExtensions)
    {
        unsafe
        {
            foreach (var property in availableDeviceExtensions)
            {
                if (extensionName == property.extensionName)
                    return true;
            }

            return false;
        }
    }

    public static uint32_t CalcNumMipLevels(uint32_t width, uint32_t height)
    {
        int32_t levels = 1;

        while ((width | height) >> levels != 0)
            levels++;

        return (uint32_t)levels;
    }

    public static bool ValidateImageLimits(VkImageType imageType, VkSampleCountFlags samples, in VkExtent3D extent, in VkPhysicalDeviceLimits limits, out ResultCode result)
    {

        if (samples != VK.VK_SAMPLE_COUNT_1_BIT && !(imageType == VK.VK_IMAGE_TYPE_2D))
        {
            logger.LogError("Multisampling is supported only for 2D images");
            result = ResultCode.ArgumentOutOfRange;
            return false;
        }
        if (imageType == VK.VK_IMAGE_TYPE_2D &&
            !(extent.width <= limits.maxImageDimension2D && extent.height <= limits.maxImageDimension2D))
        {
            logger.LogError("2D texture size exceeded");
            result = ResultCode.ArgumentOutOfRange;
            return false;
        }
        if (imageType == VK.VK_IMAGE_TYPE_3D &&
            !(extent.width <= limits.maxImageDimension3D && extent.height <= limits.maxImageDimension3D &&
                        extent.depth <= limits.maxImageDimension3D))
        {
            logger.LogError("3D texture size exceeded");
            result = ResultCode.ArgumentOutOfRange;
            return false;
        }
        result = ResultCode.Ok;
        return true;
    }

    public static ResultCode ValidateRange(in VkExtent3D ext, uint32_t numLevels, in TextureRangeDesc range)
    {
        if (!(range.dimensions.Width > 0 && range.dimensions.Height > 0 || range.dimensions.Depth > 0 || range.numLayers > 0 ||
                        range.numMipLevels > 0))
        {
            logger.LogError("width, height, depth numLayers, and numMipLevels must be > 0");
            return ResultCode.ArgumentOutOfRange;
        }
        if (range.mipLevel > numLevels)
        {
            logger.LogError("range.mipLevel exceeds texture mip-levels: {MipLevel} > {NumLevels}", range.mipLevel, numLevels);
            return ResultCode.ArgumentOutOfRange;
        }

        uint32_t texWidth = Math.Max(ext.width >> (int)range.mipLevel, 1u);
        uint32_t texHeight = Math.Max(ext.height >> (int)range.mipLevel, 1u);
        uint32_t texDepth = Math.Max(ext.depth >> (int)range.mipLevel, 1u);

        if (range.dimensions.Width > texWidth || range.dimensions.Height > texHeight || range.dimensions.Depth > texDepth)
        {
            logger.LogError("range dimensions exceed texture dimensions: {RangeWidth}x{RangeHeight}x{RangeDepth} > {TexWidth}x{TexHeight}x{TexDepth}",
                        range.dimensions.Width, range.dimensions.Height, range.dimensions.Depth, texWidth, texHeight, texDepth);
            return ResultCode.ArgumentOutOfRange;
        }
        if (range.offset.x > texWidth - range.dimensions.Width || range.offset.y > texHeight - range.dimensions.Height ||
            range.offset.z > texDepth - range.dimensions.Depth)
        {
            logger.LogError("range offset exceeds texture dimensions: {OffsetX}x{OffsetY}x{OffsetZ} > {TexWidth}x{TexHeight}x{TexDepth}",
                        range.offset.x, range.offset.y, range.offset.z, texWidth, texHeight, texDepth);
            return ResultCode.ArgumentOutOfRange;
        }

        return ResultCode.Ok;
    }

    public static uint32_t GetTextureBytesPerLayer(uint32_t width, uint32_t height, Format format, uint32_t level)
    {
        uint32_t levelWidth = Math.Max(width >> (int)level, 1u);
        uint32_t levelHeight = Math.Max(height >> (int)level, 1u);

        ref TextureFormatProperties props = ref TextureFormatProperties.GetProperty(format);

        if (!props.compressed)
        {
            return props.bytesPerBlock * levelWidth * levelHeight;
        }

        uint32_t blockWidth = Math.Max(props.blockWidth, 1u);
        uint32_t blockHeight = Math.Max(props.blockHeight, 1u);
        uint32_t widthInBlocks = (levelWidth + blockWidth - 1) / blockWidth;
        uint32_t heightInBlocks = (levelHeight + blockHeight - 1) / blockHeight;
        return widthInBlocks * heightInBlocks * props.bytesPerBlock;
    }

    public static uint32_t GetTextureBytesPerPlane(uint32_t width, uint32_t height, Format format, uint32_t plane)
    {
        ref var props = ref TextureFormatProperties.GetProperty(format);

        HxDebug.Assert(plane < props.numPlanes);

        return format switch
        {
            Format.YUV_NV12 => width * height / (plane + 1),
            Format.YUV_420p => width * height / (plane != 0 ? 4u : 1u),
            _ => GetTextureBytesPerLayer(width, height, format, 0),
        };
    }
}
