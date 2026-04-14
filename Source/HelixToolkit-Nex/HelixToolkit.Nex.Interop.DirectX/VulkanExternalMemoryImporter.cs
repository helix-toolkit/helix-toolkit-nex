using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using Vortice.Vulkan;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;
using VK = Vortice.Vulkan.Vulkan;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Imports a shared DirectX texture handle into Vulkan as a VkImage
/// using VK_KHR_external_memory_win32.
/// </summary>
public static class VulkanExternalMemoryImporter
{
    /// <summary>
    /// Imports a shared handle into Vulkan using VK_KHR_external_memory_win32.
    /// Creates a VkImage with ExternalMemoryImageCreateInfo, allocates memory with
    /// ImportMemoryWin32HandleInfoKHR, and wraps it as a TextureHandle.
    /// </summary>
    /// <param name="context">The headless VulkanContext with external memory enabled.</param>
    /// <param name="sharedHandle">The KMT or NT handle from D3D11.</param>
    /// <param name="handleType">D3D11TextureKmtBit (WPF) or D3D11TextureBit (WinUI).</param>
    /// <param name="format">B8G8R8A8Unorm (WPF) or R8G8B8A8Unorm (WinUI).</param>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <returns>The imported texture wrapped as an ImportedVulkanTexture.</returns>
    public static unsafe ImportedVulkanTexture Import(
        IContext context,
        nint sharedHandle,
        VkExternalMemoryHandleTypeFlags handleType,
        VkFormat format,
        uint width,
        uint height
    )
    {
        if (context is not VulkanContext ctx)
            throw new InvalidOperationException("Import requires a VulkanContext instance.");

        var device = ctx.VkDevice;
        var physicalDevice = ctx.VkPhysicalDevice;

        // 1. Query external memory properties for the format/handle type
        var externalMemoryFeatures = QueryExternalMemoryFeatures(
            physicalDevice,
            format,
            handleType
        );

        // 2. Create VkImage with ExternalMemoryImageCreateInfo in pNext
        VkExternalMemoryImageCreateInfo externalMemoryImageInfo = new()
        {
            handleTypes = handleType,
        };

        VkImageCreateInfo imageCreateInfo = new()
        {
            pNext = &externalMemoryImageInfo,
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(width, height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage =
                VkImageUsageFlags.ColorAttachment
                | VkImageUsageFlags.Sampled
                | VkImageUsageFlags.TransferSrc
                | VkImageUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        VkImage image;
        VK.vkCreateImage(device, &imageCreateInfo, null, &image)
            .CheckResult("Failed to create external memory image");

        // 3. Get memory requirements
        VkMemoryRequirements memRequirements;
        VK.vkGetImageMemoryRequirements(device, image, &memRequirements);

        // 4. Allocate memory with ImportMemoryWin32HandleInfoKHR
        VkImportMemoryWin32HandleInfoKHR importMemoryInfo = new()
        {
            handleType = handleType,
            handle = sharedHandle,
        };

        // Check if dedicated allocation is required
        VkMemoryDedicatedAllocateInfo dedicatedAllocateInfo = new() { image = image };

        if (ShouldUseDedicatedAllocation(externalMemoryFeatures))
        {
            // Chain: importMemoryInfo -> dedicatedAllocateInfo
            importMemoryInfo.pNext = &dedicatedAllocateInfo;
        }

        VkMemoryAllocateInfo memoryAllocateInfo = new()
        {
            pNext = &importMemoryInfo,
            allocationSize = memRequirements.size,
            memoryTypeIndex = HxVkUtils.FindMemoryType(
                physicalDevice,
                memRequirements.memoryTypeBits,
                VkMemoryPropertyFlags.DeviceLocal
            ),
        };

        VkDeviceMemory memory;
        VK.vkAllocateMemory(device, &memoryAllocateInfo, null, &memory)
            .CheckResult("Failed to allocate imported external memory");

        // 5. Bind memory to image
        VK.vkBindImageMemory(device, image, memory, 0)
            .CheckResult("Failed to bind imported memory to image");

        // 6. Create image view
        VkImageViewCreateInfo imageViewCreateInfo = new()
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            components = new VkComponentMapping(
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity
            ),
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1,
            },
        };

        VkImageView imageView;
        VK.vkCreateImageView(device, &imageViewCreateInfo, null, &imageView)
            .CheckResult("Failed to create image view for imported texture");

        // 7. Wrap in VulkanImage (isOwningVkImage = false — we manage the VkImage ourselves)
        var vulkanImage = new VulkanImage(
            ctx,
            image,
            usage: imageCreateInfo.usage,
            extent: imageCreateInfo.extent,
            type: VkImageType.Image2D,
            format: format,
            isDepthFormat: false,
            isStencilFormat: false,
            isSwapchainImage: false,
            isOwningVkImage: false,
            debugName: "Imported DirectX Texture"
        );

        // Set the image view on the wrapper so the engine can use it
        vulkanImage.ImageView = imageView;

        // 8. Register in TexturesPool to obtain a TextureHandle
        TextureHandle textureHandle = ctx.TexturesPool.Create(vulkanImage);
        ctx.AwaitingCreation = true;

        return new ImportedVulkanTexture(ctx, image, memory, imageView, textureHandle);
    }

    /// <summary>
    /// Determines whether dedicated allocation is required based on external memory feature flags.
    /// Returns true when <see cref="VkExternalMemoryFeatureFlags.DedicatedOnly"/> is set.
    /// </summary>
    internal static bool ShouldUseDedicatedAllocation(VkExternalMemoryFeatureFlags featureFlags) =>
        featureFlags.HasFlag(VkExternalMemoryFeatureFlags.DedicatedOnly);

    /// <summary>
    /// Queries external memory feature flags for the given format and handle type.
    /// </summary>
    internal static unsafe VkExternalMemoryFeatureFlags QueryExternalMemoryFeatures(
        VkPhysicalDevice physicalDevice,
        VkFormat format,
        VkExternalMemoryHandleTypeFlags handleType
    )
    {
        VkPhysicalDeviceExternalImageFormatInfo externalFormatInfo = new()
        {
            handleType = handleType,
        };

        VkPhysicalDeviceImageFormatInfo2 formatInfo = new()
        {
            pNext = &externalFormatInfo,
            format = format,
            type = VkImageType.Image2D,
            tiling = VkImageTiling.Optimal,
            usage =
                VkImageUsageFlags.ColorAttachment
                | VkImageUsageFlags.Sampled
                | VkImageUsageFlags.TransferSrc
                | VkImageUsageFlags.TransferDst,
        };

        VkExternalImageFormatProperties externalFormatProperties = new();
        VkImageFormatProperties2 formatProperties = new() { pNext = &externalFormatProperties };

        VkResult result = VK.vkGetPhysicalDeviceImageFormatProperties2(
            physicalDevice,
            &formatInfo,
            &formatProperties
        );

        if (result == VkResult.ErrorFormatNotSupported)
        {
            throw new NotSupportedException(
                $"External memory handle type '{handleType}' is not supported for format '{format}'."
            );
        }

        result.CheckResult("Failed to query external image format properties");

        return externalFormatProperties.externalMemoryProperties.externalMemoryFeatures;
    }
}
