using Vortice.Vulkan;

namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanSwapchain : IDisposable
{
    public const uint32_t MAX_SWAPCHAIN_IMAGES = 16;
    private static readonly ILogger Logger = LogManager.Create<VulkanSwapchain>();

    private readonly VulkanContext _ctx;
    private readonly VkDevice _device;
    private readonly VkQueue _graphicsQueue;
    private uint32_t _currentImageIndex = 0; // [0...numSwapchainImages_)
    private bool _getNextImage = true;
    private VkSwapchainKHR _swapchain = VkSwapchainKHR.Null;

    public uint32_t Width { get; private set; }
    public uint32_t Height { get; private set; }

    public uint32_t NumSwapchainImages { get; private set; } = 0;
    public uint32_t CurrentImageIndex => _currentImageIndex;
    public uint64_t CurrentFrameIndex { get; private set; } = 0;

    public VkSurfaceFormatKHR SurfaceFormat { get; }

    public readonly TextureHandle[] SwapchainTextures = new TextureHandle[MAX_SWAPCHAIN_IMAGES];
    public readonly VkSemaphore[] AcquireSemaphore = new VkSemaphore[MAX_SWAPCHAIN_IMAGES];
    public readonly VkFence[] PresentFence = new VkFence[MAX_SWAPCHAIN_IMAGES];
    public readonly uint64_t[] TimelineWaitValues = new uint64_t[MAX_SWAPCHAIN_IMAGES];

    public bool Valid =>
        _swapchain != VkSwapchainKHR.Null && SurfaceFormat.format != VK.VK_FORMAT_UNDEFINED;

    public VulkanSwapchain(VulkanContext ctx, uint32_t width, uint32_t height)
    {
        this._ctx = ctx;
        Width = width;
        Height = height;
        this._device = ctx.GetVkDevice();
        SurfaceFormat = ChooseSwapSurfaceFormat(
            ctx.DeviceSurfaceFormats,
            ctx.Config.SwapchainRequestedColorSpace,
            ctx.HasExtSwapchainColorspace
        );
        _graphicsQueue = ctx.GraphicsQueue.GraphicsQueue;

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
        HxDebug.Assert(
            SurfaceFormat.format != VK.VK_FORMAT_UNDEFINED,
            "Invalid swapchain surface format. Ensure that the surface is created and the format is supported by the device."
        );
        HxDebug.Assert(Width > 0 && Height > 0, "Swapchain dimensions must be greater than zero.");
        HxDebug.Assert(
            _ctx.VkSurface != VkSurfaceKHR.Null,
            "Vulkan surface must be created before creating the swapchain."
        );
        var queueFamilySupportsPresentation = VK_BOOL.False;
        unsafe
        {
            VK.vkGetPhysicalDeviceSurfaceSupportKHR(
                    _ctx.GetVkPhysicalDevice(),
                    _ctx.DeviceQueues.GraphicsQueueFamilyIndex,
                    _ctx.VkSurface,
                    &queueFamilySupportsPresentation
                )
                .CheckResult();
            HxDebug.Assert(
                queueFamilySupportsPresentation == VK_BOOL.True,
                "The queue family used with the swapchain does not support presentation"
            );
            var chooseSwapImageCount = new Func<VkSurfaceCapabilitiesKHR, uint32_t>(
                (caps) =>
                {
                    uint32_t desired = caps.minImageCount + 1;
                    bool exceeded = caps.maxImageCount > 0 && desired > caps.maxImageCount;
                    return exceeded ? caps.maxImageCount : desired;
                }
            );

            var chooseSwapPresentMode = new Func<IReadOnlyList<VkPresentModeKHR>, VkPresentModeKHR>(
                modes =>
                {
                    if (SystemInfo.IsLinuxPlatform() || SystemInfo.IsArmArchitecture())
                    {
                        if (modes.Contains(VkPresentModeKHR.Immediate))
                        {
                            return VkPresentModeKHR.Immediate;
                        }
                    }
                    return modes.Contains(VkPresentModeKHR.Mailbox)
                        ? VkPresentModeKHR.Mailbox
                        : VK.VK_PRESENT_MODE_FIFO_KHR;
                }
            );

            var chooseUsageFlags = new Func<
                VkPhysicalDevice,
                VkSurfaceKHR,
                VkFormat,
                VkImageUsageFlags
            >(
                (pd, surface, format) =>
                {
                    VkImageUsageFlags usageFlags =
                        VK.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT
                        | VK.VK_IMAGE_USAGE_TRANSFER_DST_BIT
                        | VK.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;

                    VkSurfaceCapabilitiesKHR caps = new();
                    VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(pd, surface, &caps).CheckResult();

                    VkFormatProperties props = new();
                    VK.vkGetPhysicalDeviceFormatProperties(pd, format, &props);

                    var isStorageSupported = caps.supportedUsageFlags.HasFlag(
                        VK.VK_IMAGE_USAGE_STORAGE_BIT
                    );
                    var isTilingOptimalSupported = props.optimalTilingFeatures.HasFlag(
                        VK.VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT
                    );

                    if (isStorageSupported && isTilingOptimalSupported)
                    {
                        usageFlags |= VK.VK_IMAGE_USAGE_STORAGE_BIT;
                    }

                    return usageFlags;
                }
            );
            var usageFlags = chooseUsageFlags(
                _ctx.GetVkPhysicalDevice(),
                _ctx.VkSurface,
                SurfaceFormat.format
            );
            var isCompositeAlphaOpaqueSupported =
                _ctx.DeviceSurfaceCapabilities.supportedCompositeAlpha.HasFlag(
                    VK.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR
                );
            var graphicsQueueFamilyIndex = _ctx.DeviceQueues.GraphicsQueueFamilyIndex;
            VkSurfaceCapabilitiesKHR capabilities = new();
            VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(
                    _ctx.VkPhysicalDevice,
                    _ctx.VkSurface,
                    &capabilities
                )
                .CheckResult();
            VkSwapchainCreateInfoKHR ci = new()
            {
                surface = _ctx.VkSurface,
                minImageCount = chooseSwapImageCount(_ctx.DeviceSurfaceCapabilities),
                imageFormat = SurfaceFormat.format,
                imageColorSpace = SurfaceFormat.colorSpace,
                imageExtent = new VkExtent2D(Width, Height),
                imageArrayLayers = 1,
                imageUsage = usageFlags,
                imageSharingMode = VK.VK_SHARING_MODE_EXCLUSIVE,
                queueFamilyIndexCount = 1,
                pQueueFamilyIndices = &graphicsQueueFamilyIndex,
                compositeAlpha = isCompositeAlphaOpaqueSupported
                    ? VK.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR
                    : VK.VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR,
                presentMode = _ctx.Config.ForcePresentModeFIFO
                    ? VkPresentModeKHR.Fifo
                    : chooseSwapPresentMode(_ctx.DevicePresentModes),
                clipped = VK_BOOL.True,
                oldSwapchain = VkSwapchainKHR.Null,
                preTransform = _ctx.DeviceSurfaceCapabilities.currentTransform,
            };
            VkSwapchainKHR sc = VkSwapchainKHR.Null;
            VK.vkCreateSwapchainKHR(_device, &ci, null, &sc).CheckResult();
            HxDebug.Assert(
                sc.IsNotNull,
                "Failed to create swapchain. Ensure that the surface is created and the format is supported by the device."
            );
            if (sc.IsNull)
            {
                Logger.LogError(
                    "Failed to create swapchain. Ensure that the surface is created and the format is supported by the device."
                );
                return VkResult.ErrorInitializationFailed;
            }
            _swapchain = sc;

            if (_ctx.HasExtHdrMetadata)
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
                VK.vkSetHdrMetadataEXT(_device, 1, &sc, &metadata);
            }

            var swapchainImages = stackalloc VkImage[(int)MAX_SWAPCHAIN_IMAGES];
            uint numScImages = 0;
            VK.vkGetSwapchainImagesKHR(_device, _swapchain, &numScImages, null).CheckResult();
            if (numScImages > MAX_SWAPCHAIN_IMAGES)
            {
                HxDebug.Assert(numScImages <= MAX_SWAPCHAIN_IMAGES);
                numScImages = MAX_SWAPCHAIN_IMAGES;
            }
            VK.vkGetSwapchainImagesKHR(_device, _swapchain, &numScImages, swapchainImages)
                .CheckResult();

            HxDebug.Assert(numScImages > 0);

            NumSwapchainImages = numScImages;

            // create images, image views and framebuffers
            for (uint32_t i = 0; i < NumSwapchainImages; i++)
            {
                AcquireSemaphore[i] = _device.CreateSemaphore("Semaphore: swapchain-acquire");

                VulkanImage image = new(
                    _ctx,
                    swapchainImages[i],
                    usageFlags,
                    new VkExtent3D
                    {
                        width = Width,
                        height = Height,
                        depth = 1,
                    },
                    VK.VK_IMAGE_TYPE_2D,
                    SurfaceFormat.format,
                    isDepthFormat: SurfaceFormat.format.IsDepthFormat(),
                    isStencilFormat: SurfaceFormat.format.IsStencilFormat(),
                    true,
                    false
                );
                _device.SetDebugObjectName(
                    VK.VK_OBJECT_TYPE_IMAGE,
                    (nuint)image.Image,
                    $"[Vk.SwapChainImage]: Swapchain {i}"
                );

                image.ImageView = image.CreateImageView(
                    _device,
                    VK.VK_IMAGE_VIEW_TYPE_2D,
                    SurfaceFormat.format,
                    VK.VK_IMAGE_ASPECT_COLOR_BIT,
                    0,
                    VK.VK_REMAINING_MIP_LEVELS,
                    0,
                    1,
                    $"Image View: Swapchain {i}"
                );
                SwapchainTextures[i] = _ctx.TexturesPool.Create(image);
            }
        }
        return VkResult.Success;
    }

    public ResultCode Resize(uint32_t newWidth, uint32_t newHeight)
    {
        if (newWidth == Width && newHeight == Height)
        {
            return ResultCode.Ok; // No resize needed
        }
        Width = newWidth;
        Height = newHeight;
        foreach (var handle in SwapchainTextures)
        {
            _ctx.Destroy(handle);
        }
        unsafe
        {
            VK.vkDestroySwapchainKHR(_device, _swapchain, null);
        }
        _swapchain = VkSwapchainKHR.Null;
        // Recreate the swapchain with the new dimensions
        return CreateSwapchain() == VkResult.Success ? ResultCode.Ok : ResultCode.RuntimeError;
    }

    public ResultCode Present(VkSemaphore waitSemaphore)
    {
        unsafe
        {
            VkFence presentFence = PresentFence[_currentImageIndex];
            VkSwapchainKHR swapchain_1 = _swapchain;
            var idx = _currentImageIndex;

            VkSwapchainPresentFenceInfoEXT fenceInfo = new()
            {
                swapchainCount = 1,
                pFences = &presentFence,
            };
            VkPresentInfoKHR pi = new()
            {
                pNext = _ctx.HasExtSwapchainMaintenance1 ? &fenceInfo : null,
                waitSemaphoreCount = 1,
                pWaitSemaphores = &waitSemaphore,
                swapchainCount = 1u,
                pSwapchains = &swapchain_1,
                pImageIndices = &idx,
            };

            if (_ctx.HasExtSwapchainMaintenance1)
            {
                if (PresentFence[_currentImageIndex].IsNull)
                {
                    PresentFence[_currentImageIndex] = _device.CreateFence(
                        $"Fence: present-fence [{_currentImageIndex}]"
                    );
                }
            }
            VkResult r = VK.vkQueuePresentKHR(_graphicsQueue, &pi);
            HxDebug.Assert(
                r == VK.VK_SUCCESS || r == VK.VK_SUBOPTIMAL_KHR || r == VK.VK_ERROR_OUT_OF_DATE_KHR
            );

            // Ready to call acquireNextImage() on the next getCurrentVulkanTexture();
            _getNextImage = true;
            CurrentFrameIndex++;

            return ResultCode.Ok;
        }
    }

    public VkImage GetCurrentVkImage()
    {
        if (_currentImageIndex < NumSwapchainImages)
        {
            var tex = _ctx.TexturesPool.Get(SwapchainTextures[_currentImageIndex]);
            HxDebug.Assert(tex is not null && tex.Valid, "Current swapchain texture is not valid.");
            return tex!.Image;
        }
        return VkImage.Null;
    }

    public VkImageView GetCurrentVkImageView()
    {
        if (_currentImageIndex < NumSwapchainImages)
        {
            var tex = _ctx.TexturesPool.Get(SwapchainTextures[_currentImageIndex]);
            HxDebug.Assert(tex is not null && tex.Valid, "Current swapchain texture is not valid.");
            return tex!.ImageView;
        }
        return VkImageView.Null;
    }

    public static VkSurfaceFormatKHR ChooseSwapSurfaceFormat(
        IReadOnlyList<VkSurfaceFormatKHR> formats,
        in ColorSpace requestedColorSpace,
        bool hasSwapchainColorspaceExt
    )
    {
        HxDebug.Assert(formats.Count > 0);

        var isNativeSwapChainBGR = new Func<IReadOnlyList<VkSurfaceFormatKHR>, bool>(
            (formats) =>
            {
                foreach (var fmt in formats)
                {
                    // The preferred format should be the one which is closer to the beginning of the formats
                    // container. If BGR is encountered earlier, it should be picked as the format of choice. If RGB
                    // happens to be earlier, take it.
                    if (
                        fmt.format == VK.VK_FORMAT_R8G8B8A8_UNORM
                        || fmt.format == VK.VK_FORMAT_R8G8B8A8_SRGB
                        || fmt.format == VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32
                    )
                    {
                        return false;
                    }
                    if (
                        fmt.format == VK.VK_FORMAT_B8G8R8A8_UNORM
                        || fmt.format == VK.VK_FORMAT_B8G8R8A8_SRGB
                        || fmt.format == VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32
                    )
                    {
                        return true;
                    }
                }
                return false;
            }
        );

        var colorSpaceToVkSurfaceFormat = new Func<ColorSpace, bool, bool, VkSurfaceFormatKHR>(
            (colorSpace, isBGR, hasSwapChainColorspaceExt) =>
            {
                switch (colorSpace)
                {
                    case ColorSpace.SRGB_LINEAR:
                        // the closest thing to sRGB linear
                        return new VkSurfaceFormatKHR()
                        {
                            format = isBGR
                                ? VK.VK_FORMAT_B8G8R8A8_UNORM
                                : VK.VK_FORMAT_R8G8B8A8_UNORM,
                            colorSpace = VK.VK_COLOR_SPACE_BT709_LINEAR_EXT,
                        };
                    case ColorSpace.SRGB_EXTENDED_LINEAR:
                    {
                        if (hasSwapchainColorspaceExt)
                            return new VkSurfaceFormatKHR()
                            {
                                format = VK.VK_FORMAT_R16G16B16A16_SFLOAT,
                                colorSpace = VK.VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT,
                            };
                        goto case ColorSpace.HDR10; // fall through to HDR10 case
                    }
                    case ColorSpace.HDR10:
                        if (hasSwapchainColorspaceExt)
                            return new VkSurfaceFormatKHR()
                            {
                                format = isBGR
                                    ? VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32
                                    : VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32,
                                colorSpace = VK.VK_COLOR_SPACE_HDR10_ST2084_EXT,
                            };
                        goto case ColorSpace.SRGB_NONLINEAR; // fall through to default case
                    case ColorSpace.SRGB_NONLINEAR:
                    default:
                        // default to normal sRGB non linear.
                        return new VkSurfaceFormatKHR()
                        {
                            format = isBGR
                                ? VK.VK_FORMAT_B8G8R8A8_SRGB
                                : VK.VK_FORMAT_R8G8B8A8_SRGB,
                            colorSpace = VK.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR,
                        };
                }
            }
        );

        VkSurfaceFormatKHR preferred = colorSpaceToVkSurfaceFormat(
            requestedColorSpace,
            isNativeSwapChainBGR(formats),
            hasSwapchainColorspaceExt
        );

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

        Logger.LogWarning(
            "Could not find a native swap chain format that matched our designed swapchain format. Defaulting to first supported format."
        );

        return formats[0];
    }

    public TextureHandle GetCurrentTexture()
    {
        unsafe
        {
            if (_getNextImage)
            {
                if (PresentFence[_currentImageIndex].IsNotNull)
                {
                    var fence = PresentFence[_currentImageIndex];
                    // Wait for the previous present operation to finish before acquiring the next image
                    // This is necessary to ensure that the image is ready for use
                    // VK_EXT_swapchain_maintenance1: wait for the fence associated with the current image index
                    VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, uint64_t.MaxValue)
                        .CheckResult();
                    VK.vkResetFences(_device, 1, &fence).CheckResult();
                    PresentFence[_currentImageIndex] = fence;
                }
                var semaphore = _ctx.TimelineSemaphore;
                {
                    var waitValue = TimelineWaitValues[_currentImageIndex];

                    VkSemaphoreWaitInfo waitInfo = new()
                    {
                        semaphoreCount = 1,
                        pSemaphores = &semaphore,
                        pValues = &waitValue,
                    };
                    // Wait for the timeline semaphore to be sign

                    VK.vkWaitSemaphores(_device, &waitInfo, uint64_t.MaxValue).CheckResult();
                    // when timeout is set to UINT64_MAX, we wait until the next image has been acquired
                    ref VkSemaphore acquireSemaphore = ref AcquireSemaphore[_currentImageIndex];
                    VkResult r = VK.vkAcquireNextImageKHR(
                        _device,
                        _swapchain,
                        uint64_t.MaxValue,
                        acquireSemaphore,
                        VkFence.Null,
                        out _currentImageIndex
                    );
                    if (
                        r != VkResult.Success
                        && r != VkResult.SuboptimalKHR
                        && r != VkResult.ErrorOutOfDateKHR
                    )
                    {
                        HxDebug.Assert(false);
                    }
                    _getNextImage = false;
                    _ctx.Immediate!.WaitSemaphore(acquireSemaphore);
                }
            }
        }
        return _currentImageIndex < NumSwapchainImages
            ? SwapchainTextures[_currentImageIndex]
            : TextureHandle.Null;
    }

    #region IDisposable Support
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                foreach (var handle in SwapchainTextures)
                {
                    _ctx.Destroy(handle);
                }
                unsafe
                {
                    VK.vkDestroySwapchainKHR(_device, _swapchain, null);

                    foreach (var sem in AcquireSemaphore)
                    {
                        VK.vkDestroySemaphore(_device, sem, null);
                    }
                    foreach (var fence in PresentFence)
                    {
                        if (fence.IsNotNull)
                            VK.vkDestroyFence(_device, fence, null);
                    }
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
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
