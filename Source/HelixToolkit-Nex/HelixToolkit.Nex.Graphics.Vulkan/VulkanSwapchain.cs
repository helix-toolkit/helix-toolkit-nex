using Vortice.Vulkan;

namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanSwapchain : IDisposable
{
    public const uint32_t MAX_SWAPCHAIN_IMAGES = 16;
    private static readonly ILogger logger = LogManager.Create<VulkanSwapchain>();

    readonly VulkanContext vkContext;
    readonly VkDevice device;
    readonly VkQueue graphicsQueue;
    readonly VkSurfaceFormatKHR surfaceFormat;

    uint32_t width;
    uint32_t height;
    uint32_t numSwapchainImages = 0;
    uint32_t currentImageIndex = 0; // [0...numSwapchainImages_)
    uint64_t currentFrameIndex = 0; // [0...+inf)
    bool getNextImage_ = true;
    VkSwapchainKHR swapchain = VkSwapchainKHR.Null;


    public uint32_t Width => width;
    public uint32_t Height => height;

    public uint32_t NumSwapchainImages => numSwapchainImages;
    public uint32_t CurrentImageIndex => currentImageIndex;
    public uint64_t CurrentFrameIndex => currentFrameIndex;

    public VkSurfaceFormatKHR SurfaceFormat => surfaceFormat;

    public readonly TextureHandle[] SwapchainTextures = new TextureHandle[MAX_SWAPCHAIN_IMAGES];
    public readonly VkSemaphore[] AcquireSemaphore = new VkSemaphore[MAX_SWAPCHAIN_IMAGES];
    public readonly VkFence[] PresentFence = new VkFence[MAX_SWAPCHAIN_IMAGES];
    public readonly uint64_t[] TimelineWaitValues = new uint64_t[MAX_SWAPCHAIN_IMAGES];

    public bool Valid => swapchain != VkSwapchainKHR.Null && surfaceFormat.format != VK.VK_FORMAT_UNDEFINED;

    public VulkanSwapchain(VulkanContext ctx, uint32_t width, uint32_t height)
    {
        this.vkContext = ctx;
        this.width = width;
        this.height = height;
        this.device = ctx.GetVkDevice();
        surfaceFormat = ChooseSwapSurfaceFormat(ctx.DeviceSurfaceFormats, ctx.Config.SwapchainRequestedColorSpace, ctx.HasExtSwapchainColorspace);
        graphicsQueue = ctx.GraphicsQueue.graphicsQueue;

        // Initialize swapchain textures and semaphores
        for (int i = 0; i < MAX_SWAPCHAIN_IMAGES; i++)
        {
            SwapchainTextures[i] = TextureHandle.Null;
            AcquireSemaphore[i] = VkSemaphore.Null;
            PresentFence[i] = VkFence.Null;
            TimelineWaitValues[i] = 0;
        }

        CreateSwapchain().CheckResult();
    }

    private VkResult CreateSwapchain()
    {
        HxDebug.Assert(surfaceFormat.format != VK.VK_FORMAT_UNDEFINED, "Invalid swapchain surface format. Ensure that the surface is created and the format is supported by the device.");
        HxDebug.Assert(width > 0 && height > 0, "Swapchain dimensions must be greater than zero.");
        HxDebug.Assert(vkContext.VkSurface != VkSurfaceKHR.Null, "Vulkan surface must be created before creating the swapchain.");
        var queueFamilySupportsPresentation = VK_BOOL.False;
        unsafe
        {
            VK.vkGetPhysicalDeviceSurfaceSupportKHR(
                vkContext.GetVkPhysicalDevice(), vkContext.DeviceQueues.graphicsQueueFamilyIndex, vkContext.VkSurface, &queueFamilySupportsPresentation).CheckResult();
            HxDebug.Assert(queueFamilySupportsPresentation == VK_BOOL.True, "The queue family used with the swapchain does not support presentation");
            var chooseSwapImageCount = new Func<VkSurfaceCapabilitiesKHR, uint32_t>((caps) =>
            {
                uint32_t desired = caps.minImageCount + 1;
                bool exceeded = caps.maxImageCount > 0 && desired > caps.maxImageCount;
                return exceeded ? caps.maxImageCount : desired;
            });

            var chooseSwapPresentMode = new Func<IReadOnlyList<VkPresentModeKHR>, VkPresentModeKHR>(modes =>
            {
                if (SystemInfo.IsLinuxPlatform() || SystemInfo.IsArmArchitecture())
                {
                    if (modes.Contains(VkPresentModeKHR.Immediate))
                    {
                        return VkPresentModeKHR.Immediate;
                    }
                }
                return modes.Contains(VkPresentModeKHR.Mailbox) ? VkPresentModeKHR.Mailbox : VK.VK_PRESENT_MODE_FIFO_KHR;
            });

            var chooseUsageFlags = new Func<VkPhysicalDevice, VkSurfaceKHR, VkFormat, VkImageUsageFlags>((pd, surface, format) =>
            {
                VkImageUsageFlags usageFlags = VK.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK.VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;

                VkSurfaceCapabilitiesKHR caps = new();
                VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(pd, surface, &caps).CheckResult();

                VkFormatProperties props = new();
                VK.vkGetPhysicalDeviceFormatProperties(pd, format, &props);

                var isStorageSupported = caps.supportedUsageFlags.HasFlag(VK.VK_IMAGE_USAGE_STORAGE_BIT);
                var isTilingOptimalSupported = props.optimalTilingFeatures.HasFlag(VK.VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT);

                if (isStorageSupported && isTilingOptimalSupported)
                {
                    usageFlags |= VK.VK_IMAGE_USAGE_STORAGE_BIT;
                }

                return usageFlags;
            });
            var usageFlags = chooseUsageFlags(vkContext.GetVkPhysicalDevice(), vkContext.VkSurface, surfaceFormat.format);
            var isCompositeAlphaOpaqueSupported = vkContext.DeviceSurfaceCapabilities.supportedCompositeAlpha.HasFlag(VK.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR);
            var graphicsQueueFamilyIndex = vkContext.DeviceQueues.graphicsQueueFamilyIndex;
            VkSurfaceCapabilitiesKHR capabilities = new();
            VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(vkContext.VkPhysicalDevice, vkContext.VkSurface, &capabilities).CheckResult();
            VkSwapchainCreateInfoKHR ci = new()
            {
                surface = vkContext.VkSurface,
                minImageCount = chooseSwapImageCount(vkContext.DeviceSurfaceCapabilities),
                imageFormat = surfaceFormat.format,
                imageColorSpace = surfaceFormat.colorSpace,
                imageExtent = new VkExtent2D(width, height),
                imageArrayLayers = 1,
                imageUsage = usageFlags,
                imageSharingMode = VK.VK_SHARING_MODE_EXCLUSIVE,
                queueFamilyIndexCount = 1,
                pQueueFamilyIndices = &graphicsQueueFamilyIndex,
                compositeAlpha = isCompositeAlphaOpaqueSupported ? VK.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR : VK.VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR,
                presentMode = chooseSwapPresentMode(vkContext.DevicePresentModes),
                clipped = VK_BOOL.True,
                oldSwapchain = VkSwapchainKHR.Null,
                preTransform = vkContext.DeviceSurfaceCapabilities.currentTransform,
            };
            VkSwapchainKHR sc = VkSwapchainKHR.Null;
            VK.vkCreateSwapchainKHR(device, &ci, null, &sc).CheckResult();
            HxDebug.Assert(sc.IsNotNull, "Failed to create swapchain. Ensure that the surface is created and the format is supported by the device.");
            if (sc.IsNull)
            {
                logger.LogError("Failed to create swapchain. Ensure that the surface is created and the format is supported by the device.");
                return VkResult.ErrorInitializationFailed;
            }
            swapchain = sc;

            if (vkContext.HasExtHdrMetadata)
            {
                VkHdrMetadataEXT metadata = new()
                {
                    displayPrimaryRed = new VkXYColorEXT() { x = 0.680f, y = 0.320f },
                    displayPrimaryGreen = new VkXYColorEXT() { x = 0.265f, y = 0.690f },
                    displayPrimaryBlue = new VkXYColorEXT() { x = 0.150f, y = 0.060f },
                    whitePoint = new VkXYColorEXT() { x = 0.3127f, y = 0.3290f },
                    maxLuminance = 80.0f,
                    minLuminance = 0.001f,
                    maxContentLightLevel = 2000.0f,
                    maxFrameAverageLightLevel = 500.0f,
                };
                VK.vkSetHdrMetadataEXT(device, 1, &sc, &metadata);
            }

            var swapchainImages = stackalloc VkImage[(int)MAX_SWAPCHAIN_IMAGES];
            uint numScImages = 0;
            VK.vkGetSwapchainImagesKHR(device, swapchain, &numScImages, null).CheckResult();
            if (numScImages > MAX_SWAPCHAIN_IMAGES)
            {
                HxDebug.Assert(numScImages <= MAX_SWAPCHAIN_IMAGES);
                numScImages = MAX_SWAPCHAIN_IMAGES;
            }
            VK.vkGetSwapchainImagesKHR(device, swapchain, &numScImages, swapchainImages).CheckResult();

            HxDebug.Assert(numScImages > 0);

            numSwapchainImages = numScImages;

            // create images, image views and framebuffers
            for (uint32_t i = 0; i < numSwapchainImages; i++)
            {
                AcquireSemaphore[i] = device.CreateSemaphore("Semaphore: swapchain-acquire");

                VulkanImage image = new(vkContext, swapchainImages[i], usageFlags, new VkExtent3D { width = width, height = height, depth = 1 },
                    VK.VK_IMAGE_TYPE_2D,
                    surfaceFormat.format,
                    isDepthFormat: surfaceFormat.format.IsDepthFormat(),
                    isStencilFormat: surfaceFormat.format.IsStencilFormat(), true, false);
                device.SetDebugObjectName(VK.VK_OBJECT_TYPE_IMAGE, (nuint)image.Image, $"Image: Swapchain {i}");

                image.ImageView = image.CreateImageView(device, VK.VK_IMAGE_VIEW_TYPE_2D, surfaceFormat.format,
                                                         VK.VK_IMAGE_ASPECT_COLOR_BIT, 0,
                                                         VK.VK_REMAINING_MIP_LEVELS,
                                                         0, 1, $"Image View: Swapchain {i}");
                SwapchainTextures[i] = vkContext.TexturesPool.Create(image);
            }
        }
        return VkResult.Success;
    }

    public ResultCode Resize(uint32_t newWidth, uint32_t newHeight)
    {
        if (newWidth == width && newHeight == height)
        {
            return ResultCode.Ok; // No resize needed
        }
        width = newWidth;
        height = newHeight;
        foreach (var handle in SwapchainTextures)
        {
            vkContext.Destroy(handle);
        }
        unsafe
        {
            VK.vkDestroySwapchainKHR(device, swapchain, null);
        }
        swapchain = VkSwapchainKHR.Null;
        // Recreate the swapchain with the new dimensions
        return CreateSwapchain() == VkResult.Success ? ResultCode.Ok : ResultCode.RuntimeError;
    }

    public ResultCode Present(VkSemaphore waitSemaphore)
    {
        unsafe
        {
            VkFence presentFence = PresentFence[currentImageIndex];
            VkSwapchainKHR swapchain_1 = swapchain;
            var idx = currentImageIndex;

            VkSwapchainPresentFenceInfoEXT fenceInfo = new()
            {
                swapchainCount = 1,
                pFences = &presentFence,
            };
            VkPresentInfoKHR pi = new()
            {
                pNext = vkContext.HasExtSwapchainMaintenance1 ? &fenceInfo : null,
                waitSemaphoreCount = 1,
                pWaitSemaphores = &waitSemaphore,
                swapchainCount = 1u,
                pSwapchains = &swapchain_1,
                pImageIndices = &idx,
            };

            if (vkContext.HasExtSwapchainMaintenance1)
            {
                if (PresentFence[currentImageIndex].IsNull)
                {
                    PresentFence[currentImageIndex] = device.CreateFence($"Fence: present-fence [{currentImageIndex}]");
                }
            }
            VkResult r = VK.vkQueuePresentKHR(graphicsQueue, &pi);
            HxDebug.Assert(r == VK.VK_SUCCESS || r == VK.VK_SUBOPTIMAL_KHR || r == VK.VK_ERROR_OUT_OF_DATE_KHR);

            // Ready to call acquireNextImage() on the next getCurrentVulkanTexture();
            getNextImage_ = true;
            currentFrameIndex++;

            return ResultCode.Ok;
        }
    }
    public VkImage GetCurrentVkImage()
    {

        if (currentImageIndex < numSwapchainImages)
        {
            var tex = vkContext.TexturesPool.Get(SwapchainTextures[currentImageIndex]);
            HxDebug.Assert(tex is not null && tex.Valid, "Current swapchain texture is not valid.");
            return tex!.Image;
        }
        return VkImage.Null;
    }

    public VkImageView GetCurrentVkImageView()
    {
        if (currentImageIndex < numSwapchainImages)
        {
            var tex = vkContext.TexturesPool.Get(SwapchainTextures[currentImageIndex]);
            HxDebug.Assert(tex is not null && tex.Valid, "Current swapchain texture is not valid.");
            return tex!.ImageView;
        }
        return VkImageView.Null;
    }


    public static VkSurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<VkSurfaceFormatKHR> formats,
                                           in ColorSpace requestedColorSpace,
                                           bool hasSwapchainColorspaceExt)
    {
        HxDebug.Assert(formats.Count > 0);

        var isNativeSwapChainBGR = new Func<IReadOnlyList<VkSurfaceFormatKHR>, bool>((formats) =>
        {
            foreach (var fmt in formats)
            {
                // The preferred format should be the one which is closer to the beginning of the formats
                // container. If BGR is encountered earlier, it should be picked as the format of choice. If RGB
                // happens to be earlier, take it.
                if (fmt.format == VK.VK_FORMAT_R8G8B8A8_UNORM || fmt.format == VK.VK_FORMAT_R8G8B8A8_SRGB ||
                    fmt.format == VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32)
                {
                    return false;
                }
                if (fmt.format == VK.VK_FORMAT_B8G8R8A8_UNORM || fmt.format == VK.VK_FORMAT_B8G8R8A8_SRGB ||
                    fmt.format == VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32)
                {
                    return true;
                }
            }
            return false;
        });

        var colorSpaceToVkSurfaceFormat = new Func<ColorSpace, bool, bool, VkSurfaceFormatKHR>((colorSpace, isBGR, hasSwapChainColorspaceExt) =>
        {
            switch (colorSpace)
            {
                case ColorSpace.SRGB_LINEAR:
                    // the closest thing to sRGB linear
                    return new VkSurfaceFormatKHR() { format = isBGR ? VK.VK_FORMAT_B8G8R8A8_UNORM : VK.VK_FORMAT_R8G8B8A8_UNORM, colorSpace = VK.VK_COLOR_SPACE_BT709_LINEAR_EXT };
                case ColorSpace.SRGB_EXTENDED_LINEAR:
                    {
                        if (hasSwapchainColorspaceExt)
                            return new VkSurfaceFormatKHR() { format = VK.VK_FORMAT_R16G16B16A16_SFLOAT, colorSpace = VK.VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT };
                        goto case ColorSpace.HDR10; // fall through to HDR10 case
                    }
                case ColorSpace.HDR10:
                    if (hasSwapchainColorspaceExt)
                        return new VkSurfaceFormatKHR()
                        {
                            format = isBGR ? VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32 : VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32,
                            colorSpace = VK.VK_COLOR_SPACE_HDR10_ST2084_EXT
                        };
                    goto case ColorSpace.SRGB_NONLINEAR; // fall through to default case
                case ColorSpace.SRGB_NONLINEAR:
                default:
                    // default to normal sRGB non linear.
                    return new VkSurfaceFormatKHR() { format = isBGR ? VK.VK_FORMAT_B8G8R8A8_SRGB : VK.VK_FORMAT_R8G8B8A8_SRGB, colorSpace = VK.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR };
            }
        });

        VkSurfaceFormatKHR preferred = colorSpaceToVkSurfaceFormat(requestedColorSpace, isNativeSwapChainBGR(formats), hasSwapchainColorspaceExt);

        foreach (var fmt in formats)
        {
            if (fmt.format == preferred.format && fmt.colorSpace == preferred.colorSpace)
            {
                return fmt;
            }
        }

        // if we can't find a matching format and color space, fallback on matching only format
        foreach (var fmt in formats)
        {
            if (fmt.format == preferred.format)
            {
                return fmt;
            }
        }

        logger.LogWarning("Could not find a native swap chain format that matched our designed swapchain format. Defaulting to first supported format.");

        return formats[0];
    }
    public TextureHandle GetCurrentTexture()
    {
        unsafe
        {
            if (getNextImage_)
            {
                if (PresentFence[currentImageIndex].IsNotNull)
                {
                    var fence = PresentFence[currentImageIndex];
                    // Wait for the previous present operation to finish before acquiring the next image
                    // This is necessary to ensure that the image is ready for use
                    // VK_EXT_swapchain_maintenance1: wait for the fence associated with the current image index
                    VK.vkWaitForFences(device, 1, &fence, VkBool32.True, uint64_t.MaxValue).CheckResult();
                    VK.vkResetFences(device, 1, &fence).CheckResult();
                    PresentFence[currentImageIndex] = fence;
                }
                var semaphore = vkContext.TimelineSemaphore;
                {
                    var waitValue = TimelineWaitValues[currentImageIndex];

                    VkSemaphoreWaitInfo waitInfo = new()
                    {
                        semaphoreCount = 1,
                        pSemaphores = &semaphore,
                        pValues = &waitValue,
                    };
                    // Wait for the timeline semaphore to be sign

                    VK.vkWaitSemaphores(device, &waitInfo, uint64_t.MaxValue).CheckResult();
                    // when timeout is set to UINT64_MAX, we wait until the next image has been acquired
                    ref VkSemaphore acquireSemaphore = ref AcquireSemaphore[currentImageIndex];
                    VkResult r = VK.vkAcquireNextImageKHR(device, swapchain, uint64_t.MaxValue, acquireSemaphore, VkFence.Null, out currentImageIndex);
                    if (r != VkResult.Success && r != VkResult.SuboptimalKHR && r != VkResult.ErrorOutOfDateKHR)
                    {
                        HxDebug.Assert(false);
                    }
                    getNextImage_ = false;
                    vkContext.Immediate!.WaitSemaphore(acquireSemaphore);
                }
            }
        }
        return currentImageIndex < numSwapchainImages ? SwapchainTextures[currentImageIndex] : TextureHandle.Null;
    }
    #region IDisposable Support
    private bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                foreach (var handle in SwapchainTextures)
                {
                    vkContext.Destroy(handle);
                }
                unsafe
                {
                    VK.vkDestroySwapchainKHR(device, swapchain, null);

                    foreach (var sem in AcquireSemaphore)
                    {
                        VK.vkDestroySemaphore(device, sem, null);
                    }
                    foreach (var fence in PresentFence)
                    {
                        if (fence.IsNotNull)
                            VK.vkDestroyFence(device, fence, null);
                    }
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~VulkanSwapChain()
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
}
