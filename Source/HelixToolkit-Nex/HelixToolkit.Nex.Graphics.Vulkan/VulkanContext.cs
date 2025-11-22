namespace HelixToolkit.Nex.Graphics.Vulkan;

public sealed class VulkanContextConfig()
{
    public delegate VkSurfaceKHR CreateSurface(VkInstance instance);
    public readonly VkVersion VulkanVersion = VkVersion.Version_1_3;
    public bool TerminateOnValidationError = false; // invoke std::terminate() on any validation error
    public bool EnableValidation = true;
    public ColorSpace SwapchainRequestedColorSpace = ColorSpace.SRGB_LINEAR;
    public bool EnableVma = true;

    // owned by the application - should be alive until createVulkanContextWithSwapchain() returns
    public nint PipelineCacheData = nint.Zero;
    public size_t PipelineCacheDataSize = 0;
    public readonly List<string> ExtensionsInstance = []; // add extra instance extensions on top of required ones
    public readonly List<string> ExtensionsDevice = []; // add extra device extensions on top of required ones
    public nint ExtensionsDeviceFeatures = nint.Zero; // inserted into VkPhysicalDeviceVulkan11Features::pNext
    public bool UseWayland = true; // use Wayland instead of X11 on Linux (requires VK_KHR_wayland_surface)

    // LVK knows about these extensions and can manage them automatically upon request
    public bool EnableHeadlessSurface = false; // VK_EXT_headless_surface

    public bool ForcePresentModeFIFO = false; // force VK_PRESENT_MODE_FIFO_KHR as the only present mode, even if other modes are available

    public delegate void ShaderModuleErrorCallback(
        in string errorMessage,
        in string sourceFile,
        int lineNumber,
        int columnNumber
    );

    public CreateSurface? OnCreateSurface = null; // custom surface creator, if not set, default surface creation will be used
};

internal sealed partial class VulkanContext
{
    public const uint32_t kMaxYcbcrConversionData = 256; // maximum number of Ycbcr conversions that can be created in the context
    private static readonly ILogger logger = LogManager.Create<VulkanContext>();

    [StructLayout(LayoutKind.Sequential)]
    private struct ValidationSettings
    {
        public bool TerminateOnValidationError;
    }

    private NativeObj<ValidationSettings>? _validationSettings;
    private readonly nint _window = nint.Zero;
    private readonly nint _display = nint.Zero;
    private readonly FastList<VkFormat> _deviceDepthFormats = [];
    private readonly FastList<VkSurfaceFormatKHR> _deviceSurfaceFormats = [];
    private VkSurfaceCapabilitiesKHR _deviceSurfaceCaps = new();
    private readonly FastList<VkPresentModeKHR> _devicePresentModes = [];
    private readonly FastList<DeferredTask> _deferredTasks = [];

    private struct YcbcrConversionData
    {
        public VkSamplerYcbcrConversionInfo Info;
        public SamplerResource Sampler;
    };

    private readonly YcbcrConversionData[] _ycbcrConversionData = new YcbcrConversionData[
        kMaxYcbcrConversionData
    ]; // indexed by lvk::Format

    private VkInstance _vkInstance = VkInstance.Null;
    private readonly VkDebugUtilsMessengerEXT _vkDebugUtilsMessenger =
        VkDebugUtilsMessengerEXT.Null;
    private VkSurfaceKHR _vkSurface = VkSurfaceKHR.Null;
    private VkPhysicalDevice _vkPhysicalDevice = VkPhysicalDevice.Null;
    private VkDevice _vkDevice = VkDevice.Null;

    private uint32_t _khronosValidationVersion = 0;
    private bool _hasExtHeadlessSurface = false; // VK_EXT_headless_surface

    private VkPhysicalDeviceVulkan13Features _vkFeatures13 = new();
    private VkPhysicalDeviceVulkan12Features _vkFeatures12 = new();
    private VkPhysicalDeviceVulkan11Features _vkFeatures11 = new();
    private VkPhysicalDeviceMeshShaderFeaturesEXT _vkFeatureMeshShader = new();
    private VkPhysicalDeviceFeatures2 _vkFeatures10 = new();

    private VkPhysicalDeviceVulkan13Properties _vkPhysicalDeviceVulkan13Properties = new();
    private VkPhysicalDeviceVulkan12Properties _vkPhysicalDeviceVulkan12Properties = new();
    private VkPhysicalDeviceVulkan11Properties _vkPhysicalDeviceVulkan11Properties = new();
    private VkPhysicalDeviceProperties2 _vkPhysicalDeviceProperties2 = new();
    private VulkanSwapchain? _swapchain = null;
    private VulkanStagingDevice? _stagingDevice = null;
    private VulkanImmediateCommands? _immediate = null;
    private VkDescriptorSetLayout _vkDesSetLayout = VkDescriptorSetLayout.Null;
    private VkDescriptorPool _vkDesPool = VkDescriptorPool.Null;
    private VkDescriptorSet _vkDesSet = VkDescriptorSet.Null;
    private VkPipelineCache _pipelineCache = VkPipelineCache.Null;
    private VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
    private VmaAllocator _vma = VmaAllocator.Null;
    private CommandBuffer? _currentCommandBuffer;
    private uint32_t _numYcbcrSamplers = 0;
    private TextureHandle _dummyTexture = TextureHandle.Null;
    private SamplerHandle _defaultSampler = SamplerHandle.Null;

    public ref readonly VkPhysicalDeviceProperties2 VkPhysicalDeviceProperties2 =>
        ref _vkPhysicalDeviceProperties2;
    public ref readonly VkPhysicalDeviceVulkan11Properties VkPhysicalDeviceVulkan11Properties =>
        ref _vkPhysicalDeviceVulkan11Properties;
    public ref readonly VkPhysicalDeviceVulkan12Properties VkPhysicalDeviceVulkan12Properties =>
        ref _vkPhysicalDeviceVulkan12Properties;

    public ref readonly VkPhysicalDeviceVulkan13Properties VkPhysicalDeviceVulkan13Properties =>
        ref _vkPhysicalDeviceVulkan13Properties;

    public bool SupportMeshShader => _vkFeatureMeshShader.meshShader;

    public IReadOnlyList<VkFormat> DeviceDepthFormats => _deviceDepthFormats.AsReadOnly();
    public IReadOnlyList<VkSurfaceFormatKHR> DeviceSurfaceFormats =>
        _deviceSurfaceFormats.AsReadOnly();
    public IReadOnlyList<VkPresentModeKHR> DevicePresentModes => _devicePresentModes.AsReadOnly();
    public VkSurfaceCapabilitiesKHR DeviceSurfaceCapabilities => _deviceSurfaceCaps;

    public DeviceQueues DeviceQueues { get; } = new();
    public VulkanSwapchain? Swapchain => _swapchain;
    public VkSemaphore TimelineSemaphore { private set; get; } = VkSemaphore.Null;
    public VulkanImmediateCommands? Immediate => _immediate;
    public VulkanStagingDevice? StagingDevice => _stagingDevice;

    public DeviceQueues GraphicsQueue { get; } = new();

    public uint32_t CurrentMaxTextures { private set; get; } = 16;
    public uint32_t CurrentMaxSamplers { private set; get; } = 16;

    public VkDescriptorSetLayout VkDesSetLayout => _vkDesSetLayout;
    public VkDescriptorPool VkDesPool => _vkDesPool;
    public VkDescriptorSet VkDesSet => _vkDesSet;

    // don't use staging on devices with shared host-visible memory
    public bool UseStaging { set; get; } = true;

    public VkPipelineCache PipelineCache => _pipelineCache;

    // a texture/sampler was created since the last descriptor set update
    public bool AwaitingCreation { set; get; } = false;
    public bool AwaitingNewImmutableSamplers { set; get; } = false;

    public VulkanContextConfig Config { get; }
    public bool UseVmaAllocator => Config.EnableVma && _vma.IsNotNull;
    public bool Has8BitIndices { private set; get; } = false; // VK_KHR_index_type_uint8 or VK_EXT_index_type_uint8
    public bool HasExtCalibratedTimestamps { private set; get; } = false;
    public bool HasExtSwapchainColorspace { private set; get; } = false;
    public bool HasExtSwapchainMaintenance1 { private set; get; } = false;
    public bool HasExtHdrMetadata { private set; get; } = false;
    public bool HasExtDeviceFault { private set; get; } = false;
    public bool HasExtDebugUtils { private set; get; } = false;

    public Pool<ShaderModule, ShaderModuleState> ShaderModulesPool { get; } = new();
    public Pool<RenderPipeline, RenderPipelineState> RenderPipelinesPool { get; } = new();
    public Pool<ComputePipeline, ComputePipelineState> ComputePipelinesPool { get; } = new();
    public Pool<Sampler, VkSampler> SamplersPool { get; } = new();
    public Pool<Buffer, VulkanBuffer> BuffersPool { get; } = new();
    public Pool<Texture, VulkanImage> TexturesPool { get; } = new();
    public Pool<QueryPool, VkQueryPool> QueriesPool { get; } = new();

    public VkDevice VkDevice => _vkDevice;

    public VkPhysicalDevice VkPhysicalDevice => _vkPhysicalDevice;

    public VmaAllocator VmaAllocator => _vma;

    public VkSurfaceKHR VkSurface => _vkSurface;

    public VulkanContext(VulkanContextConfig config)
    {
        Config = config;
        try
        {
            CreateInstance();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create Vulkan instance.");
            throw new Exception("Failed to create Vulkan instance.", ex);
        }
    }

    public VulkanContext(VulkanContextConfig config, IntPtr window, IntPtr display)
        : this(config)
    {
        this._window = window;
        this._display = display;
    }

    public ResultCode Initialize()
    {
        _vkSurface =
            Config.OnCreateSurface != null
                ? Config.OnCreateSurface(_vkInstance)
                : VkSurfaceKHR.Null;
        try
        {
            if (_vkSurface == VkSurfaceKHR.Null)
            {
                if (Config.EnableHeadlessSurface)
                {
                    CreateHeadlessSurface();
                }
                else if (_window != IntPtr.Zero || _display != IntPtr.Zero)
                {
                    CreateSurface(_window, _display);
                }
            }
            return InitContext();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize vulkan context.");
            return ResultCode.RuntimeError;
        }
    }

    public bool HasSwapchain => Swapchain != null && Swapchain.Valid;
    private static readonly Lazy<bool> s_isSupported = new(CheckIsSupported);

    public static bool IsSupported() => s_isSupported.Value;

    private static bool CheckIsSupported()
    {
        try
        {
            unsafe
            {
                VkResult result = VK.vkInitialize();
                if (result != VkResult.Success)
                    return false;

                uint propCount;
                result = VK.vkEnumerateInstanceExtensionProperties(&propCount, null);
                if (result != VkResult.Success)
                {
                    return false;
                }

                // We require Vulkan 1.1 or higher
                VkVersion version = VK.vkEnumerateInstanceVersion();
                if (version < VkVersion.Version_1_1)
                    return false;

                // TODO: Enumerate physical devices and try to create instance.

                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public ref readonly VkPhysicalDeviceProperties GetVkPhysicalDeviceProperties()
    {
        return ref _vkPhysicalDeviceProperties2.properties;
    }

    private unsafe void CreateInstance()
    {
        _vkInstance = VkInstance.Null;
        HashSet<VkUtf8String> availableInstanceLayers = [.. EnumerateInstanceLayers()];
        HashSet<VkUtf8String> availableInstanceExtensions = [.. GetInstanceExtensions()];

        List<VkUtf8String> instanceExtensions = [];
        using VkStringArray extensionsInstance = new(Config.ExtensionsInstance);
        byte** pExtensionsInstance = extensionsInstance;

        for (int i = 0; i < extensionsInstance.Length; ++i)
        {
            instanceExtensions.Add(new VkUtf8String(pExtensionsInstance[i]));
        }
        if (!availableInstanceExtensions.Contains(VK.VK_KHR_SURFACE_EXTENSION_NAME))
        {
            throw new Exception(
                "Vulkan: Required instance extension 'VK_KHR_surface' is not supported by the Vulkan implementation."
            );
        }
        instanceExtensions.Add(VK.VK_KHR_SURFACE_EXTENSION_NAME); // always required

        List<VkUtf8String> instanceLayers = [];

        if (Config.EnableValidation)
        {
            // Determine the optimal validation layers to enable that are necessary for useful debugging
            HxVkUtils.GetOptimalValidationLayers(availableInstanceLayers, instanceLayers);
        }
        foreach (VkUtf8String availableExtension in availableInstanceExtensions)
        {
            if (
                Config.EnableHeadlessSurface
                && availableExtension == VK.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME
            )
            {
                _hasExtHeadlessSurface = true;
                instanceExtensions.Add(VK.VK_EXT_HEADLESS_SURFACE_EXTENSION_NAME);
            }
            else if (availableExtension == VK.VK_EXT_DEBUG_UTILS_EXTENSION_NAME)
            {
                HasExtDebugUtils = true;
                instanceExtensions.Add(VK.VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
            }
            else if (availableExtension == VK.VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME)
            {
                instanceExtensions.Add(VK.VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME);
            }
        }

        if (SystemInfo.IsWindowsPlatform())
        {
            if (!availableInstanceExtensions.Contains(VK.VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
            {
                throw new Exception(
                    "Vulkan: Required instance extension 'VK_KHR_win32_surface' is not supported by the Vulkan implementation."
                );
            }
            instanceExtensions.Add(VK.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
        }
        else if (SystemInfo.IsLinuxPlatform())
        {
            if (Config.UseWayland)
            {
                if (!availableInstanceExtensions.Contains(VK.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                {
                    throw new Exception(
                        "Vulkan: Required instance extension 'VK_KHR_wayland_surface' is not supported by the Vulkan implementation."
                    );
                }
                instanceExtensions.Add(VK.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
            }
            else
            {
                if (!availableInstanceExtensions.Contains(VK.VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                {
                    throw new Exception(
                        "Vulkan: Required instance extension 'VK_KHR_xlib_surface' is not supported by the Vulkan implementation."
                    );
                }
                instanceExtensions.Add(VK.VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
            }
        }

        VkUtf8ReadOnlyString pApplicationName = "HelixToolkit-Nex/Vulkan"u8;
        VkUtf8ReadOnlyString pEngineName = "HelixToolkit-Nex/Vulkan"u8;
        VkApplicationInfo appInfo = new()
        {
            pNext = null,
            pApplicationName = pApplicationName,
            applicationVersion = new VkVersion(1, 0, 0),
            pEngineName = pEngineName,
            engineVersion = new VkVersion(1, 0, 0),
            apiVersion = Config.VulkanVersion,
        };
        using VkStringArray vkLayerNames = new(instanceLayers);
        using VkStringArray vkInstanceExtensions = new(instanceExtensions);

        VkInstanceCreateInfo instanceCreateInfo = new()
        {
            pApplicationInfo = &appInfo,
            enabledLayerCount = vkLayerNames.Length,
            ppEnabledLayerNames = vkLayerNames,
            enabledExtensionCount = vkInstanceExtensions.Length,
            ppEnabledExtensionNames = vkInstanceExtensions,
        };
        VkDebugUtilsMessengerCreateInfoEXT debugUtilsCreateInfo = new();

        if (instanceLayers.Count > 0)
        {
            _validationSettings = NativeObj<ValidationSettings>.Create(
                new ValidationSettings
                {
                    TerminateOnValidationError = Config.TerminateOnValidationError,
                }
            );
            debugUtilsCreateInfo.messageSeverity =
                VkDebugUtilsMessageSeverityFlagsEXT.Error
                | VkDebugUtilsMessageSeverityFlagsEXT.Warning;
            debugUtilsCreateInfo.messageType =
                VkDebugUtilsMessageTypeFlagsEXT.Validation
                | VkDebugUtilsMessageTypeFlagsEXT.Performance;
            debugUtilsCreateInfo.pfnUserCallback = &DebugMessengerCallback;
            debugUtilsCreateInfo.pUserData = _validationSettings; // Pass validation settings to the callback
            instanceCreateInfo.pNext = &debugUtilsCreateInfo;
        }
        VK.vkCreateInstance(instanceCreateInfo, null, out _vkInstance).CheckResult();
        VK.vkLoadInstanceOnly(_vkInstance);

        if (instanceLayers.Count > 0)
        {
            VK.vkCreateDebugUtilsMessengerEXT(
                    _vkInstance,
                    &debugUtilsCreateInfo,
                    null,
                    out _debugMessenger
                )
                .CheckResult();
        }

        logger.LogInformation(
            $"Created VkInstance with version: {appInfo.apiVersion.Major}.{appInfo.apiVersion.Minor}.{appInfo.apiVersion.Patch}"
        );
        if (instanceLayers.Count > 0)
        {
            foreach (var layer in instanceLayers)
            {
                logger.LogInformation("Instance layer '{LAYER}'", layer);
            }
        }

        foreach (VkUtf8String extension in instanceExtensions)
        {
            logger.LogInformation("Instance extension '{EXT}'", extension);
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe uint DebugMessengerCallback(
        VkDebugUtilsMessageSeverityFlagsEXT messageSeverity,
        VkDebugUtilsMessageTypeFlagsEXT messageTypes,
        VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* userData
    )
    {
        VkUtf8String message = new VkUtf8String(pCallbackData->pMessage)!;
        LogLevel level = LogLevel.Debug;

        level =
            messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Error ? LogLevel.Error
            : messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Warning ? LogLevel.Warning
            : LogLevel.Information;

        if (messageTypes == VkDebugUtilsMessageTypeFlagsEXT.Validation)
        {
            logger.Log(level, "[Vulkan Validation]: {MESSAGE}", message);
        }
        else
        {
            logger.Log(level, "[Vulkan]: {MESSAGE}", message);
        }
        if (
            userData != null
            && ((ValidationSettings*)userData)->TerminateOnValidationError == true
            && messageSeverity == VkDebugUtilsMessageSeverityFlagsEXT.Error
        )
        {
            logger.LogCritical("Vulkan validation error occurred, terminating application.");
            throw new Exception("Vulkan validation error occurred, terminating application.");
        }

        return VK.VK_FALSE;
    }

    private static VkUtf8String[] GetInstanceExtensions()
    {
        unsafe
        {
            uint count = 0;
            VkResult result = VK.vkEnumerateInstanceExtensionProperties(&count, null);
            if (result != VkResult.Success)
            {
                return [];
            }

            if (count == 0)
            {
                return [];
            }

            var props = new VkExtensionProperties[(int)count];
            VK.vkEnumerateInstanceExtensionProperties(props);

            var extensions = new VkUtf8String[count];
            using var pProps = props.Pin();
            for (int i = 0; i < count; i++)
            {
                extensions[i] = new VkUtf8String(
                    ((VkExtensionProperties*)pProps.Pointer)[i].extensionName
                );
            }

            return extensions;
        }
    }

    private unsafe VkUtf8String[] EnumerateInstanceLayers()
    {
        if (!IsSupported())
        {
            return [];
        }

        uint count = 0;
        VkResult result = VK.vkEnumerateInstanceLayerProperties(&count, null);
        if (result != VkResult.Success || count == 0)
        {
            return [];
        }

        var props = new VkLayerProperties[(int)count];
        VK.vkEnumerateInstanceLayerProperties(props).CheckResult();

        var resultExt = new VkUtf8String[count];
        Config.EnableValidation = false;
        using var pProps = props.Pin();
        for (int i = 0; i < count; i++)
        {
            resultExt[i] = new VkUtf8String(((VkLayerProperties*)pProps.Pointer)[i].layerName);
            if (resultExt[i] == VK.VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME)
            {
                // We found the KHRONOS validation layer, get its version
                _khronosValidationVersion = ((VkLayerProperties*)pProps.Pointer)[i].specVersion;
                Config.EnableValidation = true; // Enable validation by default if the KHRONOS validation layer is available
            }
        }

        return resultExt;
    }

    private void CreateSurface(IntPtr window, IntPtr display)
    {
        unsafe
        {
            // Implementation for creating a Vulkan surface using the provided window and display handles.
            if (OperatingSystem.IsWindows())
            {
                VkWin32SurfaceCreateInfoKHR ci = new()
                {
                    hinstance = Native.GetModuleHandle(null),
                    hwnd = (nint)window,
                };
                VkSurfaceKHR surface;
                VK.vkCreateWin32SurfaceKHR(_vkInstance, &ci, null, &surface).CheckResult();
                _vkSurface = surface;
            }
            else if (OperatingSystem.IsLinux())
            {
                VkSurfaceKHR surface;
                if (Config.UseWayland)
                {
                    VkWaylandSurfaceCreateInfoKHR ci = new()
                    {
                        flags = 0,
                        display = display,
                        surface = window,
                    };
                    VK.vkCreateWaylandSurfaceKHR(_vkInstance, &ci, null, &surface);
                }
                else
                {
                    VkXlibSurfaceCreateInfoKHR ci = new() { dpy = display, window = (ulong)window };

                    VK.vkCreateXlibSurfaceKHR(_vkInstance, &ci, null, &surface).CheckResult();
                }
                _vkSurface = surface;
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "Unsupported platform for Vulkan surface creation."
                );
            }
        }
    }

    private void CreateHeadlessSurface()
    {
        if (!_hasExtHeadlessSurface)
        {
            return;
        }

        VkHeadlessSurfaceCreateInfoEXT ci = new() { pNext = null, flags = 0 };
        unsafe
        {
            VkSurfaceKHR surface;
            VK.vkCreateHeadlessSurfaceEXT(_vkInstance, &ci, null, &surface).CheckResult();
            _vkSurface = surface;
        }
    }

    private static bool IsDeviceSuitable(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface)
    {
        var (graphicsFamily, presentFamily, computeFamily) = FindQueueFamilies(
            physicalDevice,
            surface
        );
        if (
            graphicsFamily == VK.VK_QUEUE_FAMILY_IGNORED
            || computeFamily == VK.VK_QUEUE_FAMILY_IGNORED
        )
            return false;

        if (surface == VkSurfaceKHR.Null)
        {
            return true;
        }
        if (presentFamily == VK.VK_QUEUE_FAMILY_IGNORED)
            return false;
        try
        {
            var swapChainSupport = HxVkUtils.QuerySwapChainSupport(physicalDevice, surface);
            return !swapChainSupport.Formats.IsEmpty && !swapChainSupport.PresentModes.IsEmpty;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (uint graphicsFamily, uint presentFamily, uint computeFamily) FindQueueFamilies(
        in VkPhysicalDevice device,
        in VkSurfaceKHR surface
    )
    {
        ReadOnlySpan<VkQueueFamilyProperties> queueFamilies =
            VK.vkGetPhysicalDeviceQueueFamilyProperties(device);

        uint graphicsFamily = VK.VK_QUEUE_FAMILY_IGNORED;
        uint presentFamily = VK.VK_QUEUE_FAMILY_IGNORED;
        uint computeFamily = VK.VK_QUEUE_FAMILY_IGNORED; // Optional, if compute queue is needed
        uint i = 0;
        foreach (VkQueueFamilyProperties queueFamily in queueFamilies)
        {
            if (queueFamily.queueFlags.HasFlag(VkQueueFlags.Graphics))
            {
                graphicsFamily = i;
            }
            if (queueFamily.queueFlags.HasFlag(VkQueueFlags.Compute))
            {
                computeFamily = i;
            }
            if (surface != VkSurfaceKHR.Null)
            {
                // Check if this queue family supports presentation to the surface
                VK.vkGetPhysicalDeviceSurfaceSupportKHR(
                        device,
                        i,
                        surface,
                        out VkBool32 presentSupport
                    )
                    .CheckResult();
                if (presentSupport)
                {
                    presentFamily = i;
                }
            }
            if (
                graphicsFamily != VK.VK_QUEUE_FAMILY_IGNORED
                && (surface == VkSurfaceKHR.Null || presentFamily != VK.VK_QUEUE_FAMILY_IGNORED)
                && computeFamily != VK.VK_QUEUE_FAMILY_IGNORED
            )
            {
                break;
            }

            i++;
        }

        return (graphicsFamily, presentFamily, computeFamily);
    }

    private unsafe ResultCode InitContext()
    {
        // Find physical device, setup queue's and create device.
        uint physicalDevicesCount = 0;
        VK.vkEnumeratePhysicalDevices(_vkInstance, &physicalDevicesCount, null).CheckResult();

        if (physicalDevicesCount == 0)
        {
            throw new Exception("Vulkan: Failed to find GPUs with Vulkan support");
        }

        VkPhysicalDevice* physicalDevices = stackalloc VkPhysicalDevice[(int)physicalDevicesCount];
        VK.vkEnumeratePhysicalDevices(_vkInstance, &physicalDevicesCount, physicalDevices)
            .CheckResult();

        for (int i = 0; i < physicalDevicesCount; i++)
        {
            VkPhysicalDevice physicalDevice = physicalDevices[i];

            if (!IsDeviceSuitable(physicalDevice, _vkSurface))
                continue;

            VK.vkGetPhysicalDeviceProperties(
                physicalDevice,
                out VkPhysicalDeviceProperties checkProperties
            );
            bool discrete = checkProperties.deviceType == VkPhysicalDeviceType.DiscreteGpu;
            _vkPhysicalDevice = physicalDevice;
            if (discrete)
            {
                // If this is discrete GPU, look no further (prioritize discrete GPU)
                break;
            }
        }
        if (_vkPhysicalDevice.IsNull)
        {
            logger.LogError("Vulkan: No suitable physical device found");
            return ResultCode.RuntimeError;
        }
        VK.vkGetPhysicalDeviceProperties(
            _vkPhysicalDevice,
            out VkPhysicalDeviceProperties properties
        );

        if (properties.apiVersion < Config.VulkanVersion)
        {
            logger.LogError("Vulkan: The physical device does not support Vulkan 1.3 or higher.");
            return ResultCode.RuntimeError;
        }

        DeviceQueues.GraphicsQueueFamilyIndex = HxVkUtils.FindQueueFamilyIndex(
            _vkPhysicalDevice,
            VkQueueFlags.Graphics
        );
        DeviceQueues.ComputeQueueFamilyIndex = HxVkUtils.FindQueueFamilyIndex(
            _vkPhysicalDevice,
            VkQueueFlags.Compute
        );
        if (DeviceQueues.GraphicsQueueFamilyIndex == DeviceQueues.INVALID)
        {
            logger.LogError("VK_QUEUE_GRAPHICS_BIT is not supported");
            return ResultCode.RuntimeError;
        }
        if (DeviceQueues.ComputeQueueFamilyIndex == DeviceQueues.INVALID)
        {
            logger.LogError("VK_QUEUE_COMPUTE_BIT is not supported");
            return ResultCode.RuntimeError;
        }
        var availableDeviceExtensions = VK.vkEnumerateDeviceExtensionProperties(_vkPhysicalDevice);

        //var supportPresent = vkGetPhysicalDeviceWin32PresentationSupportKHR(PhysicalDevice, queueFamilies.graphicsFamily);
        VkDeviceQueueCreateInfo* queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[2];
        float queuePriority = 1f;
        queueCreateInfos[0] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = DeviceQueues.GraphicsQueueFamilyIndex,
            queueCount = 1,
            pQueuePriorities = &queuePriority, // Priority for the graphics queue
        };
        queueCreateInfos[1] = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = DeviceQueues.ComputeQueueFamilyIndex,
            queueCount = 1,
            pQueuePriorities = &queuePriority, // Priority for the graphics queue
        };
        uint32_t numQueues =
            queueCreateInfos[0].queueFamilyIndex == queueCreateInfos[1].queueFamilyIndex ? 1u : 2u;

        {
            // Get features and properties of the physical device and create the logical device
            // VkPhysicalDeviceMeshShaderFeaturesEXT meshShaderFeatures = new();
            VkPhysicalDeviceVulkan13Features features1_3 = new() { pNext = null };
            VkPhysicalDeviceVulkan12Features features1_2 = new() { pNext = &features1_3 };
            VkPhysicalDeviceVulkan11Features features1_1 = new() { pNext = &features1_2 };

            VkPhysicalDeviceFeatures2 deviceFeatures2 = new() { pNext = &features1_1 };

            void** features_chain = &features1_2.pNext;

            VkPhysicalDevice8BitStorageFeatures storage_8bit_features = default;
            List<VkUtf8String> enabledExtensions = [VK.VK_KHR_SWAPCHAIN_EXTENSION_NAME];
            if (properties.apiVersion <= VkVersion.Version_1_3)
            {
                if (
                    HxVkUtils.CheckDeviceExtensionSupport(
                        VK.VK_KHR_8BIT_STORAGE_EXTENSION_NAME,
                        availableDeviceExtensions
                    )
                )
                {
                    enabledExtensions.Add(VK.VK_KHR_8BIT_STORAGE_EXTENSION_NAME);
                    //storage_8bit_features.sType = VkStructureType.PhysicalDevice8bitStorageFeatures;
                    *features_chain = &storage_8bit_features;
                    features_chain = &storage_8bit_features.pNext;
                }
            }

            VK.vkGetPhysicalDeviceFeatures2(_vkPhysicalDevice, &deviceFeatures2);
            _vkFeatures10 = deviceFeatures2;
            _vkFeatures11 = features1_1;
            _vkFeatures12 = features1_2;
            _vkFeatures13 = features1_3;
            //vkFeatureMeshShader = meshShaderFeatures;

            GraphicsSettings.SupportMeshShader = _vkFeatureMeshShader.meshShader;
            // VkPhysicalDeviceMeshShaderPropertiesEXT meshShaderProps = new();
            VkPhysicalDeviceVulkan13Properties deviceProps1_3 = new();
            VkPhysicalDeviceVulkan12Properties deviceProps1_2 = new() { pNext = &deviceProps1_3 };
            VkPhysicalDeviceVulkan11Properties deviceProps1_1 = new() { pNext = &deviceProps1_2 };
            VkPhysicalDeviceProperties2 deviceProps2 = new() { pNext = &deviceProps1_1 };
            VK.vkGetPhysicalDeviceProperties2(_vkPhysicalDevice, &deviceProps2);
            _vkPhysicalDeviceProperties2 = deviceProps2;
            _vkPhysicalDeviceVulkan11Properties = deviceProps1_1;
            _vkPhysicalDeviceVulkan12Properties = deviceProps1_2;
            _vkPhysicalDeviceVulkan13Properties = deviceProps1_3;

            using var deviceExtensionNames = new VkStringArray(enabledExtensions);

            VkDeviceCreateInfo deviceCreateInfo = new()
            {
                pNext = &deviceFeatures2,
                queueCreateInfoCount = numQueues,
                pQueueCreateInfos = queueCreateInfos,
                enabledExtensionCount = deviceExtensionNames.Length,
                ppEnabledExtensionNames = deviceExtensionNames,
                pEnabledFeatures = null,
            };

            VK.vkCreateDevice(_vkPhysicalDevice, &deviceCreateInfo, null, out _vkDevice)
                .CheckResult("Failed to create Vulkan Logical Device");
        }

        VK.vkLoadDevice(_vkDevice);
        VK.vkGetDeviceQueue(
            _vkDevice,
            DeviceQueues.GraphicsQueueFamilyIndex,
            0,
            out GraphicsQueue.GraphicsQueue
        );
        VK.vkGetDeviceQueue(
            _vkDevice,
            DeviceQueues.ComputeQueueFamilyIndex,
            0,
            out GraphicsQueue.ComputeQueue
        );

        if (GraphicsSettings.EnableDebug)
        {
            _vkDevice.SetDebugObjectName(
                VkObjectType.Device,
                (nuint)_vkDevice.Handle,
                "[Vk.Device]: vkDevice"
            );
        }

        _immediate = new VulkanImmediateCommands(
            _vkDevice,
            DeviceQueues.GraphicsQueueFamilyIndex,
            HasExtDeviceFault,
            "VkContext::immediate"
        );

        // create Vulkan pipeline cache
        {
            VkPipelineCacheCreateInfo ci = new()
            {
                flags = VkPipelineCacheCreateFlags.None,
                initialDataSize = (uint)Config.PipelineCacheDataSize,
                pInitialData = (void*)Config.PipelineCacheData,
            };
            VK.vkCreatePipelineCache(_vkDevice, &ci, null, out _pipelineCache);
        }

        if (Config.EnableVma)
        {
            _vma = HxVkUtils.CreateVmaAllocator(
                _vkPhysicalDevice,
                _vkDevice,
                _vkInstance,
                Config.VulkanVersion
            );
            HxDebug.Assert(_vma.IsNotNull);
        }

        _stagingDevice = new VulkanStagingDevice(this);

        // default texture
        {
            uint32_t pixel = 0xFF000000;
            var result = CreateTexture(
                new TextureDesc
                {
                    Format = Format.RGBA_UN8,
                    Dimensions = new(1, 1, 1),
                    Usage = TextureUsageBits.Sampled | TextureUsageBits.Storage,
                    Data = (nint)(void*)&pixel,
                    DataSize = sizeof(uint32_t), // 1x1 pixel RGBA
                },
                out var texture,
                "Dummy 1x1 (black)"
            );
            if (result != ResultCode.Ok)
            {
                return result;
            }
            _dummyTexture = texture;
            HxDebug.Assert(TexturesPool.Count == 1, "Dummy texture should be created successfully");
        }

        {
            HxDebug.Assert(SamplersPool.Count == 0);
            var result = CreateSampler(
                new VkSamplerCreateInfo
                {
                    magFilter = VK.VK_FILTER_LINEAR,
                    minFilter = VK.VK_FILTER_LINEAR,
                    mipmapMode = VK.VK_SAMPLER_MIPMAP_MODE_LINEAR,
                    addressModeU = VK.VK_SAMPLER_ADDRESS_MODE_REPEAT,
                    addressModeV = VK.VK_SAMPLER_ADDRESS_MODE_REPEAT,
                    addressModeW = VK.VK_SAMPLER_ADDRESS_MODE_REPEAT,
                    anisotropyEnable = VK_BOOL.False,
                    compareEnable = VK_BOOL.False,
                    compareOp = VK.VK_COMPARE_OP_ALWAYS,
                    minLod = 0.0f,
                    maxLod = (Constants.MAX_MIP_LEVELS - 1),
                    borderColor = VK.VK_BORDER_COLOR_INT_OPAQUE_BLACK,
                    unnormalizedCoordinates = VK_BOOL.False,
                },
                Format.Invalid,
                out var sampler,
                "Sampler: default"
            );
            if (result != ResultCode.Ok)
            {
                return result;
            }
            _defaultSampler = sampler;
            HxDebug.Assert(
                SamplersPool.Count == 1,
                "Default sampler should be created successfully"
            );
        }
        GrowDescriptorPool(CurrentMaxTextures, CurrentMaxSamplers);
        QuerySurfaceCapabilities();
        return ResultCode.Ok;
    }

    public void WaitDeferredTasks()
    {
        foreach (var task in _deferredTasks)
        {
            Immediate!.Wait(task.Handle);
            task.Action();
        }
        _deferredTasks.Clear();
    }

    public void ProcessDeferredTasks()
    {
        if (_deferredTasks.Count == 0)
        {
            return;
        }
        var count = _deferredTasks.Count;
        for (int i = 0; i < count; ++i)
        {
            ref readonly var task = ref _deferredTasks.GetInternalArray()[i];
            if (Immediate!.IsReady(task.Handle, true))
            {
                task.Action();
            }
        }
        _deferredTasks.RemoveRange(0, count);
    }

    public void GenerateMipmap(in TextureHandle handle)
    {
        if (handle.Empty)
        {
            return;
        }

        var tex = TexturesPool.Get(handle);

        if (tex is null)
        {
            logger.LogWarning("Texture {HANDLE} not found in pool", handle);
            return;
        }

        if (tex.NumLevels <= 1)
        {
            return;
        }

        HxDebug.Assert(tex.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);
        var wrapper = Immediate!.Acquire();
        tex.GenerateMipmap(wrapper.Instance);
        Immediate!.Submit(wrapper);
    }

    private VkSampleCountFlags GetFramebufferMSAABitMaskVK()
    {
        ref readonly VkPhysicalDeviceLimits limits = ref GetVkPhysicalDeviceProperties().limits;
        return limits.framebufferColorSampleCounts & limits.framebufferDepthSampleCounts;
    }

    private ResultCode CreateSampler(
        in VkSamplerCreateInfo info,
        Format yuvFormat,
        out SamplerHandle sampler,
        string? debugName
    )
    {
        VkSamplerCreateInfo cinfo = info;
        unsafe
        {
            VkSamplerYcbcrConversionInfo? ycbrInfo;
            if (yuvFormat != Format.Invalid)
            {
                ycbrInfo = GetOrCreateYcbcrConversionInfo(yuvFormat);
                cinfo.pNext = &ycbrInfo;
                // must be CLAMP_TO_EDGE
                // https://vulkan.lunarg.com/doc/view/1.3.268.0/windows/1.3-extensions/vkspec.html#VUID-VkSamplerCreateInfo-addressModeU-01646
                cinfo.addressModeU = VK.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
                cinfo.addressModeV = VK.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
                cinfo.addressModeW = VK.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
                cinfo.anisotropyEnable = VK_BOOL.False;
                cinfo.unnormalizedCoordinates = VK_BOOL.False;
            }

            VK.vkCreateSampler(_vkDevice, &cinfo, null, out var vkSampler).CheckResult();
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                _vkDevice.SetDebugObjectName(
                    VK.VK_OBJECT_TYPE_SAMPLER,
                    (nuint)vkSampler.Handle,
                    $"[Vk.Sampler]: {debugName}"
                );
            }

            sampler = SamplersPool.Create(vkSampler);

            AwaitingCreation = true;
        }
        return ResultCode.Ok;
    }

    private ResultCode GrowDescriptorPool(uint32_t maxTextures, uint32_t maxSamplers)
    {
        CurrentMaxTextures = maxTextures;
        CurrentMaxSamplers = maxSamplers;
        if (
            maxTextures
            > _vkPhysicalDeviceVulkan12Properties.maxDescriptorSetUpdateAfterBindSampledImages
        )
        {
            HxDebug.Assert(false);
            logger.LogWarning(
                "Max Textures exceeded: {CURRENT} (max {MAX})",
                maxTextures,
                _vkPhysicalDeviceVulkan12Properties.maxDescriptorSetUpdateAfterBindSampledImages
            );
        }

        if (
            maxSamplers
            > _vkPhysicalDeviceVulkan12Properties.maxDescriptorSetUpdateAfterBindSamplers
        )
        {
            HxDebug.Assert(false);
            logger.LogWarning(
                "Max Samplers exceeded: {CURRENT} (max {MAX})",
                maxSamplers,
                _vkPhysicalDeviceVulkan12Properties.maxDescriptorSetUpdateAfterBindSamplers
            );
        }

        if (VkDesSetLayout.IsNotNull)
        {
            DeferredTask(() =>
            {
                unsafe
                {
                    VK.vkDestroyDescriptorSetLayout(_vkDevice, VkDesSetLayout, null);
                }
            });
        }
        if (VkDesPool.IsNotNull)
        {
            DeferredTask(() =>
            {
                unsafe
                {
                    VK.vkDestroyDescriptorPool(_vkDevice, VkDesPool, null);
                }
            });
        }

        bool hasYUVImages = false;
        unsafe
        {
            // check if we have any YUV images
            foreach (var texture in TexturesPool.Objects)
            {
                var img = texture.Obj;
                if (img is null)
                {
                    continue;
                }
                // multisampled images cannot be directly accessed from shaders
                bool isTextureAvailable =
                    (img.SampleCount & VK.VK_SAMPLE_COUNT_1_BIT) == VK.VK_SAMPLE_COUNT_1_BIT;
                hasYUVImages =
                    isTextureAvailable
                    && img.IsSampledImage
                    && img.ImageFormat.GetNumImagePlanes() > 1;
                if (hasYUVImages)
                {
                    break;
                }
            }

            {
                FastList<VkSampler> immutableSamplers = [];

                if (hasYUVImages)
                {
                    VkSampler dummySampler = SamplersPool.Objects[0].Obj;
                    immutableSamplers.EnsureCapacity(TexturesPool.Objects.Count);
                    foreach (var obj in TexturesPool.Objects)
                    {
                        if (obj.Obj is null)
                        {
                            continue;
                        }
                        var img = obj.Obj;
                        // multisampled images cannot be directly accessed from shaders
                        bool isTextureAvailable =
                            (img.SampleCount & VK.VK_SAMPLE_COUNT_1_BIT)
                            == VK.VK_SAMPLE_COUNT_1_BIT;
                        bool isYUVImage =
                            isTextureAvailable
                            && img.IsSampledImage
                            && img.ImageFormat.GetNumImagePlanes() > 1;
                        immutableSamplers.Add(
                            isYUVImage
                                ? GetOrCreateYcbcrSampler(img.ImageFormat.ToFormat())
                                : dummySampler
                        );
                    }
                }

                VkShaderStageFlags stageFlags =
                    VK.VK_SHADER_STAGE_VERTEX_BIT
                    | VK.VK_SHADER_STAGE_TESSELLATION_CONTROL_BIT
                    | VK.VK_SHADER_STAGE_TESSELLATION_EVALUATION_BIT
                    | VK.VK_SHADER_STAGE_FRAGMENT_BIT
                    | VK.VK_SHADER_STAGE_COMPUTE_BIT;
                using var pImmutableSamplers = immutableSamplers
                    .GetInternalArray()
                    .Pin(0, immutableSamplers.Count);

                var bindings =
                    stackalloc VkDescriptorSetLayoutBinding[(int)Bindings.NumBindings] {
                        HxVkUtils.GetDSLBinding(
                            (uint)Bindings.Textures,
                            VK.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,
                            maxTextures,
                            stageFlags
                        ),
                        HxVkUtils.GetDSLBinding(
                            (uint)Bindings.Samplers,
                            VK.VK_DESCRIPTOR_TYPE_SAMPLER,
                            maxSamplers,
                            stageFlags
                        ),
                        HxVkUtils.GetDSLBinding(
                            (uint)Bindings.StorageImages,
                            VK.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
                            maxTextures,
                            stageFlags
                        ),
                        HxVkUtils.GetDSLBinding(
                            (uint)Bindings.YUVImages,
                            VK.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                            (uint32_t)immutableSamplers.Count,
                            stageFlags,
                            (VkSampler*)pImmutableSamplers.Pointer
                        ),
                    };

                VkDescriptorBindingFlags flags =
                    VK.VK_DESCRIPTOR_BINDING_UPDATE_AFTER_BIND_BIT
                    | VK.VK_DESCRIPTOR_BINDING_UPDATE_UNUSED_WHILE_PENDING_BIT
                    | VK.VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT;
                var bindingFlags = new VkDescriptorBindingFlags[(int)Bindings.NumBindings];

                for (int i = 0; i < (int)Bindings.NumBindings; ++i)
                {
                    bindingFlags[i] = flags;
                }
                using var pBindingFlags = bindingFlags.Pin();
                VkDescriptorSetLayoutBindingFlagsCreateInfo setLayoutBindingFlagsCI = new()
                {
                    bindingCount = (uint32_t)Bindings.NumBindings,
                    pBindingFlags = (VkDescriptorBindingFlags*)pBindingFlags.Pointer,
                };

                VkDescriptorSetLayoutCreateInfo dslci = new()
                {
                    pNext = &setLayoutBindingFlagsCI,
                    flags = VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool,
                    bindingCount = (uint32_t)Bindings.NumBindings,
                    pBindings = bindings,
                };

                VK.vkCreateDescriptorSetLayout(_vkDevice, &dslci, null, out _vkDesSetLayout)
                    .CheckResult();

                if (GraphicsSettings.EnableDebug)
                {
                    // Set debug name for the descriptor set layout
                    _vkDevice.SetDebugObjectName(
                        VkObjectType.DescriptorSetLayout,
                        (nuint)VkDesSetLayout,
                        "[Vk.DescSetLayout]: VulkanContext::vkDSL_"
                    );
                }
            }

            {
                // create default descriptor pool and allocate 1 descriptor set
                var poolSizes =
                    stackalloc VkDescriptorPoolSize[(int)Bindings.NumBindings] {
                        new VkDescriptorPoolSize
                        {
                            type = VK.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,
                            descriptorCount = maxTextures,
                        },
                        new VkDescriptorPoolSize
                        {
                            type = VK.VK_DESCRIPTOR_TYPE_SAMPLER,
                            descriptorCount = maxSamplers,
                        },
                        new VkDescriptorPoolSize
                        {
                            type = VK.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
                            descriptorCount = maxTextures,
                        },
                        new VkDescriptorPoolSize
                        {
                            type = VK.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                            descriptorCount = maxTextures,
                        },
                    };

                VkDescriptorPoolCreateInfo ci = new()
                {
                    flags = VkDescriptorPoolCreateFlags.UpdateAfterBind,
                    maxSets = 1,
                    poolSizeCount = (uint)Bindings.NumBindings,
                    pPoolSizes = poolSizes,
                };
                VK.vkCreateDescriptorPool(_vkDevice, &ci, null, out _vkDesPool).CheckResult();

                var vkDSL = VkDesSetLayout;
                VkDescriptorSetAllocateInfo ai = new()
                {
                    descriptorPool = VkDesPool,
                    descriptorSetCount = 1,
                    pSetLayouts = &vkDSL,
                };
                var vkDSet = VkDescriptorSet.Null;
                VK.vkAllocateDescriptorSets(_vkDevice, &ai, &vkDSet).CheckResult();
                _vkDesSet = vkDSet;
            }

            AwaitingNewImmutableSamplers = false;
        }
        return ResultCode.Ok;
    }

    private VkSampler GetOrCreateYcbcrSampler(Format format)
    {
        var info = GetOrCreateYcbcrConversionInfo(format);

        return info is null
            ? VkSampler.Null
            : SamplersPool.Get(_ycbcrConversionData[(int)format].Sampler);
    }

    private VkSamplerYcbcrConversionInfo? GetOrCreateYcbcrConversionInfo(Format format)
    {
        if (_ycbcrConversionData[(int)format].Info.sType != 0)
        {
            return _ycbcrConversionData[(int)format].Info;
        }
        if (!_vkFeatures11.samplerYcbcrConversion)
        {
            HxDebug.Assert(false, "Ycbcr samplers are not supported.");
            return null;
        }
        var vkFormat = format.ToVk();
        VkFormatProperties props;
        unsafe
        {
            VK.vkGetPhysicalDeviceFormatProperties(_vkPhysicalDevice, vkFormat, &props);
        }
        bool cosited =
            (props.optimalTilingFeatures & VK.VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT) != 0;
        bool midpoint =
            (props.optimalTilingFeatures & VK.VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT) != 0;
        if (!cosited && !midpoint)
        {
            HxDebug.Assert(false, "Ycbcr samplers are not supported for this format.");
            return null;
        }
        VkSamplerYcbcrConversionCreateInfo ci = new()
        {
            format = vkFormat,
            ycbcrModel = VK.VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709,
            ycbcrRange = VK.VK_SAMPLER_YCBCR_RANGE_ITU_FULL,
            components = new VkComponentMapping(
                VK.VK_COMPONENT_SWIZZLE_IDENTITY,
                VK.VK_COMPONENT_SWIZZLE_IDENTITY,
                VK.VK_COMPONENT_SWIZZLE_IDENTITY,
                VK.VK_COMPONENT_SWIZZLE_IDENTITY
            ),
            xChromaOffset = midpoint
                ? VK.VK_CHROMA_LOCATION_MIDPOINT
                : VK.VK_CHROMA_LOCATION_COSITED_EVEN,
            yChromaOffset = midpoint
                ? VK.VK_CHROMA_LOCATION_MIDPOINT
                : VK.VK_CHROMA_LOCATION_COSITED_EVEN,
            chromaFilter = VK.VK_FILTER_LINEAR,
            forceExplicitReconstruction = VK_BOOL.False,
        };
        VkSamplerYcbcrConversionInfo info = new();
        unsafe
        {
            VK.vkCreateSamplerYcbcrConversion(_vkDevice, &ci, null, &info.conversion)
                .CheckResult("Failed on vkCreateSamplerYcbcrConversion.");
            VkSamplerYcbcrConversionImageFormatProperties samplerYcbcrConversionImageFormatProps =
                new();
            VkImageFormatProperties2 imageFormatProps = new()
            {
                pNext = &samplerYcbcrConversionImageFormatProps,
            };
            VkPhysicalDeviceImageFormatInfo2 imageFormatInfo = new()
            {
                format = vkFormat,
                type = VK.VK_IMAGE_TYPE_2D,
                tiling = VK.VK_IMAGE_TILING_OPTIMAL,
                usage = VK.VK_IMAGE_USAGE_SAMPLED_BIT,
                flags = VK.VK_IMAGE_CREATE_DISJOINT_BIT,
            };
            VK.vkGetPhysicalDeviceImageFormatProperties2(
                    VkPhysicalDevice,
                    &imageFormatInfo,
                    &imageFormatProps
                )
                .CheckResult("Failed on vkGetPhysicalDeviceImageFormatProperties2");
            HxDebug.Assert(
                samplerYcbcrConversionImageFormatProps.combinedImageSamplerDescriptorCount <= 3
            );
        }

        var cinfo = HxVkExtensions.ToVkSamplerCreateInfo(
            new SamplerStateDesc(),
            GetVkPhysicalDeviceProperties().limits
        );

        _ycbcrConversionData[(int)format].Info = info;
        var ret = CreateSampler(cinfo, format, out var sampler, "YUV sampler");
        if (ret != ResultCode.Ok)
        {
            HxDebug.Assert(false, "Failed to create YUV sampler.");
            return null;
        }
        _ycbcrConversionData[(int)format].Sampler = new SamplerResource(this, sampler);
        _numYcbcrSamplers++;
        AwaitingNewImmutableSamplers = true;

        return _ycbcrConversionData[(int)format].Info;
    }

    public void DeferredTask(Action action)
    {
        DeferredTask(action, SubmitHandle.Null);
    }

    public void DeferredTask(Action action, SubmitHandle handle)
    {
        HxDebug.Assert(Immediate is not null);
        if (handle.Empty)
        {
            handle = Immediate!.GetNextSubmitHandle();
        }
        lock (_deferredTasks)
        {
            _deferredTasks.Add(new DeferredTask(action, handle));
        }
    }

    public ResultCode InitSwapchain(uint32_t width, uint32_t height)
    {
        if (_vkDevice.IsNull || Immediate is null)
        {
            logger.LogWarning("Call initContext() first");
            return ResultCode.RuntimeError;
        }
        unsafe
        {
            if (Swapchain is not null && Swapchain.Valid)
            {
                // destroy the old swapchain first
                VK.vkDeviceWaitIdle(_vkDevice).CheckResult();
                Disposer.DisposeAndRemove(ref _swapchain);
                VK.vkDestroySemaphore(_vkDevice, TimelineSemaphore, null);
            }

            if (width == 0 || height == 0)
            {
                return ResultCode.Ok;
            }

            _swapchain = new(this, width, height);

            TimelineSemaphore = _vkDevice.CreateSemaphoreTimeline(
                _swapchain.NumSwapchainImages - 1,
                "Semaphore: timelineSemaphore"
            );

            return Swapchain is not null ? ResultCode.Ok : ResultCode.RuntimeError;
        }
    }

    private unsafe void QuerySurfaceCapabilities()
    {
        // enumerate only the formats we are using
        VkFormat[] depthFormats =
        [
            VkFormat.D32SfloatS8Uint,
            VkFormat.D24UnormS8Uint,
            VkFormat.D16UnormS8Uint,
            VkFormat.D32Sfloat,
            VkFormat.D16Unorm,
        ];
        foreach (var depthFormat in depthFormats)
        {
            VkFormatProperties formatProps;
            VK.vkGetPhysicalDeviceFormatProperties(_vkPhysicalDevice, depthFormat, &formatProps);

            if (formatProps.optimalTilingFeatures != VkFormatFeatureFlags.None)
            {
                _deviceDepthFormats.Add(depthFormat);
            }
        }

        if (_vkSurface.IsNull)
        {
            return;
        }

        {
            VkSurfaceCapabilitiesKHR capabilities = new();
            VK.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(
                    _vkPhysicalDevice,
                    _vkSurface,
                    &capabilities
                )
                .CheckResult();
            _deviceSurfaceCaps = capabilities;
        }

        uint32_t formatCount;
        VK.vkGetPhysicalDeviceSurfaceFormatsKHR(_vkPhysicalDevice, _vkSurface, &formatCount, null);

        if (formatCount > 0)
        {
            _deviceSurfaceFormats.Resize((int)formatCount);
            using var pSurfaceFormats = _deviceSurfaceFormats.GetInternalArray().Pin();

            // Get the surface formats supported by the physical device
            VK.vkGetPhysicalDeviceSurfaceFormatsKHR(
                    _vkPhysicalDevice,
                    _vkSurface,
                    &formatCount,
                    (VkSurfaceFormatKHR*)pSurfaceFormats.Pointer
                )
                .CheckResult();
        }

        uint32_t presentModeCount;
        VK.vkGetPhysicalDeviceSurfacePresentModesKHR(
            _vkPhysicalDevice,
            _vkSurface,
            &presentModeCount,
            null
        );

        if (presentModeCount > 0)
        {
            _devicePresentModes.Resize((int)presentModeCount);
            using var pPresentModes = _devicePresentModes.GetInternalArray().Pin();
            // Get the present modes supported by the physical device
            VK.vkGetPhysicalDeviceSurfacePresentModesKHR(
                    _vkPhysicalDevice,
                    _vkSurface,
                    &presentModeCount,
                    (VkPresentModeKHR*)pPresentModes.Pointer
                )
                .CheckResult();
        }
    }

    public ResultCode CreateBuffer(
        in VkDeviceSize bufferSize,
        VkBufferUsageFlags usageFlags,
        VkMemoryPropertyFlags memFlags,
        out BufferHandle buffer,
        string? debugName
    )
    {
        buffer = BufferHandle.Null;
        HxDebug.Assert(bufferSize > 0);
        ref readonly var limits = ref GetVkPhysicalDeviceProperties().limits;
        if (usageFlags.HasFlag(VK.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT))
        {
            if (bufferSize > limits.maxUniformBufferRange)
            {
                logger.LogError(
                    "Buffer size {SIZE} exceeds max uniform buffer range {MAX}",
                    bufferSize,
                    limits.maxUniformBufferRange
                );
                buffer = BufferHandle.Null;
                return ResultCode.ArgumentOutOfRange;
            }
        }

        if (bufferSize > limits.maxStorageBufferRange)
        {
            logger.LogError(
                "Buffer size {SIZE} exceeds max storage buffer range {MAX}",
                bufferSize,
                limits.maxStorageBufferRange
            );
            buffer = BufferHandle.Null;
            return ResultCode.ArgumentOutOfRange;
        }

        VulkanBuffer buf = new(this, bufferSize, usageFlags, memFlags);

        var ret = buf.Create(debugName);
        if (ret.HasError())
        {
            HxDebug.Assert(false);
            logger.LogError("Failed to create buffer: {ERROR}", ret.ToString());
            buf.Dispose();
            return ret;
        }

        buffer = BuffersPool.Create(buf);
        return ResultCode.Ok;
    }

    public void BindDefaultDescriptorSets(
        in VkCommandBuffer cmdBuf,
        VkPipelineBindPoint bindPoint,
        in VkPipelineLayout layout
    )
    {
        unsafe
        {
            const int length = 4;
            VkDescriptorSet* dsets = stackalloc VkDescriptorSet[length];
            for (int i = 0; i < length; ++i)
            {
                dsets[i] = VkDesSet;
            }
            VK.vkCmdBindDescriptorSets(cmdBuf, bindPoint, layout, 0, length, &dsets[0], 0, null);
        }
    }

    public void CheckAndUpdateDescriptorSets()
    {
        if (!AwaitingCreation)
        {
            // nothing to update here
            return;
        }

        HxDebug.Assert(TexturesPool.Count >= 1);
        HxDebug.Assert(SamplersPool.Count >= 1);

        uint32_t newMaxTextures = CurrentMaxTextures;
        uint32_t newMaxSamplers = CurrentMaxSamplers;

        while (TexturesPool.Objects.Count > newMaxTextures)
        {
            newMaxTextures *= 2;
        }
        while (SamplersPool.Objects.Count > newMaxSamplers)
        {
            newMaxSamplers *= 2;
        }
        if (
            newMaxTextures != CurrentMaxTextures
            || newMaxSamplers != CurrentMaxSamplers
            || AwaitingNewImmutableSamplers
        )
        {
            GrowDescriptorPool(newMaxTextures, newMaxSamplers);
        }

        // 1. Sampled and storage images
        FastList<VkDescriptorImageInfo> infoSampledImages = [];
        FastList<VkDescriptorImageInfo> infoStorageImages = [];
        FastList<VkDescriptorImageInfo> infoYUVImages = [];

        infoSampledImages.Capacity = TexturesPool.Count;
        infoStorageImages.Capacity = TexturesPool.Count;

        bool hasYcbcrSamplers = _numYcbcrSamplers > 0;

        if (hasYcbcrSamplers)
        {
            infoYUVImages.Capacity = TexturesPool.Count;
        }

        // use the dummy texture to avoid sparse array
        if (TexturesPool.Objects[0].Obj is null)
        {
            logger.LogError("Dummy texture image view is null, cannot create descriptor sets");
            return;
        }
        var dummyImageView = TexturesPool.Objects[0].Obj!.ImageView;

        foreach (var obj in TexturesPool.Objects)
        {
            var img = obj.Obj;
            if (img is null)
            {
                continue;
            }
            VkImageView view = img.ImageView;
            VkImageView storageView = img.ImageViewStorage.IsNotNull ? img.ImageViewStorage : view;
            // multisampled images cannot be directly accessed from shaders
            bool isTextureAvailable = img.SampleCount.HasFlag(VK.VK_SAMPLE_COUNT_1_BIT);
            bool isYUVImage =
                isTextureAvailable && img.IsSampledImage && img.ImageFormat.GetNumImagePlanes() > 1;
            bool isSampledImage = isTextureAvailable && img.IsSampledImage && !isYUVImage;
            bool isStorageImage = isTextureAvailable && img.IsStorageImage;
            infoSampledImages.Add(
                new VkDescriptorImageInfo
                {
                    sampler = VkSampler.Null,
                    imageView = isSampledImage ? view : dummyImageView,
                    imageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                }
            );
            HxDebug.Assert(infoSampledImages.Last().imageView != VkImage.Null);
            infoStorageImages.Add(
                new VkDescriptorImageInfo
                {
                    sampler = VkSampler.Null,
                    imageView = isStorageImage ? storageView : dummyImageView,
                    imageLayout = VK.VK_IMAGE_LAYOUT_GENERAL,
                }
            );
            if (hasYcbcrSamplers)
            {
                // we don't need to update this if there're no YUV samplers
                infoYUVImages.Add(
                    new VkDescriptorImageInfo
                    {
                        imageView = isYUVImage ? view : dummyImageView,
                        imageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    }
                );
            }
        }

        // 2. Samplers
        FastList<VkDescriptorImageInfo> infoSamplers = [];
        infoSamplers.Capacity = SamplersPool.Objects.Count;

        foreach (var sampler in SamplersPool.Objects)
        {
            infoSamplers.Add(
                new VkDescriptorImageInfo
                {
                    sampler = sampler.Obj.IsNotNull ? sampler.Obj : SamplersPool.Objects[0].Obj,
                    imageView = VkImageView.Null,
                    imageLayout = VK.VK_IMAGE_LAYOUT_UNDEFINED,
                }
            );
        }

        unsafe
        {
            using var pSampledImages = infoSampledImages.GetInternalArray().Pin();

            using var pSamplers = infoSamplers.GetInternalArray().Pin();

            using var pStorageImages = infoStorageImages.GetInternalArray().Pin();

            using var pYUVImages = infoYUVImages.GetInternalArray().Pin();

            var write = stackalloc VkWriteDescriptorSet[(int)Bindings.NumBindings];
            uint32_t numWrites = 0;
            if (!infoSampledImages.Empty)
            {
                write[numWrites++] = new VkWriteDescriptorSet
                {
                    dstSet = VkDesSet,
                    dstBinding = (uint)Bindings.Textures,
                    dstArrayElement = 0,
                    descriptorCount = (uint32_t)infoSampledImages.Count,
                    descriptorType = VK.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,
                    pImageInfo = (VkDescriptorImageInfo*)pSampledImages.Pointer,
                };
            }

            if (!infoSamplers.Empty)
            {
                write[numWrites++] = new VkWriteDescriptorSet
                {
                    dstSet = VkDesSet,
                    dstBinding = (uint)Bindings.Samplers,
                    dstArrayElement = 0,
                    descriptorCount = (uint32_t)infoSamplers.Count,
                    descriptorType = VK.VK_DESCRIPTOR_TYPE_SAMPLER,
                    pImageInfo = (VkDescriptorImageInfo*)pSamplers.Pointer,
                };
            }

            if (!infoStorageImages.Empty)
            {
                write[numWrites++] = new VkWriteDescriptorSet
                {
                    dstSet = VkDesSet,
                    dstBinding = (uint)Bindings.StorageImages,
                    dstArrayElement = 0,
                    descriptorCount = (uint32_t)infoStorageImages.Count,
                    descriptorType = VK.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,
                    pImageInfo = (VkDescriptorImageInfo*)pStorageImages.Pointer,
                };
            }

            if (!infoYUVImages.Empty)
            {
                write[numWrites++] = new VkWriteDescriptorSet
                {
                    dstSet = VkDesSet,
                    dstBinding = (uint)Bindings.YUVImages,
                    dstArrayElement = 0,
                    descriptorCount = (uint32_t)infoYUVImages.Count,
                    descriptorType = VK.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
                    pImageInfo = (VkDescriptorImageInfo*)pYUVImages.Pointer,
                };
            }

            // do not switch to the next descriptor set if there is nothing to update
            if (numWrites > 0)
            {
                Immediate!.Wait(Immediate.GetLastSubmitHandle());
                VK.vkUpdateDescriptorSets(_vkDevice, numWrites, write, 0, null);
            }
        }
        AwaitingCreation = false;
    }

    public VkPipeline GetVkPipeline(RenderPipelineHandle handle, uint32_t viewMask)
    {
        var rps = RenderPipelinesPool.Get(handle);

        if (rps == null)
        {
            return VkPipeline.Null;
        }

        if (rps!.LastVkDescriptorSetLayout != VkDesSetLayout || rps!.VewMask != viewMask)
        {
            DeferredTask(() =>
            {
                unsafe
                {
                    VK.vkDestroyPipeline(VkDevice, rps.Pipeline, null);
                }
            });
            DeferredTask(() =>
            {
                unsafe
                {
                    VK.vkDestroyPipelineLayout(VkDevice, rps.PipelineLayout, null);
                }
            });
            rps.Pipeline = VkPipeline.Null;
            rps.LastVkDescriptorSetLayout = VkDesSetLayout;
            rps.VewMask = viewMask;
        }

        if (rps.Pipeline != VkPipeline.Null)
        {
            return rps.Pipeline;
        }

        // build a new Vulkan pipeline

        VkPipelineLayout layout = VkPipelineLayout.Null;
        VkPipeline pipeline = VkPipeline.Null;

        ref var desc = ref rps.Desc;

        uint32_t numColorAttachments = rps.Desc.GetNumColorAttachments();
        unsafe
        {
            // Not all attachments are valid. We need to create color blend attachments only for active attachments
            var colorBlendAttachmentStates = new VkPipelineColorBlendAttachmentState[
                Constants.MAX_COLOR_ATTACHMENTS
            ];
            var colorAttachmentFormats = new VkFormat[Constants.MAX_COLOR_ATTACHMENTS];

            for (uint32_t i = 0; i != numColorAttachments; i++)
            {
                ref ColorAttachment attachment = ref desc.Colors[i];
                HxDebug.Assert(attachment.Format != Format.Invalid);
                colorAttachmentFormats[i] = attachment.Format.ToVk();
                if (!attachment.BlendEnabled)
                {
                    colorBlendAttachmentStates[i] = new VkPipelineColorBlendAttachmentState
                    {
                        blendEnable = VK_BOOL.False,
                        srcColorBlendFactor = VK.VK_BLEND_FACTOR_ONE,
                        dstColorBlendFactor = VK.VK_BLEND_FACTOR_ZERO,
                        colorBlendOp = VK.VK_BLEND_OP_ADD,
                        srcAlphaBlendFactor = VK.VK_BLEND_FACTOR_ONE,
                        dstAlphaBlendFactor = VK.VK_BLEND_FACTOR_ZERO,
                        alphaBlendOp = VK.VK_BLEND_OP_ADD,
                        colorWriteMask =
                            VK.VK_COLOR_COMPONENT_R_BIT
                            | VK.VK_COLOR_COMPONENT_G_BIT
                            | VK.VK_COLOR_COMPONENT_B_BIT
                            | VK.VK_COLOR_COMPONENT_A_BIT,
                    };
                }
                else
                {
                    colorBlendAttachmentStates[i] = new VkPipelineColorBlendAttachmentState
                    {
                        blendEnable = VK_BOOL.True,
                        srcColorBlendFactor = attachment.SrcRGBBlendFactor.ToVk(),
                        dstColorBlendFactor = attachment.DstRGBBlendFactor.ToVk(),
                        colorBlendOp = attachment.RgbBlendOp.ToVk(),
                        srcAlphaBlendFactor = attachment.SrcAlphaBlendFactor.ToVk(),
                        dstAlphaBlendFactor = attachment.DstAlphaBlendFactor.ToVk(),
                        alphaBlendOp = attachment.AlphaBlendOp.ToVk(),
                        colorWriteMask =
                            VK.VK_COLOR_COMPONENT_R_BIT
                            | VK.VK_COLOR_COMPONENT_G_BIT
                            | VK.VK_COLOR_COMPONENT_B_BIT
                            | VK.VK_COLOR_COMPONENT_A_BIT,
                    };
                }
            }

            var vertModule = desc.VertexShader
                ? ShaderModulesPool.Get(desc.VertexShader)
                : ShaderModuleState.Null;
            var tescModule = desc.TessControlShader
                ? ShaderModulesPool.Get(desc.TessControlShader)
                : ShaderModuleState.Null;
            var teseModule = desc.TessEvalShader
                ? ShaderModulesPool.Get(desc.TessEvalShader)
                : ShaderModuleState.Null;
            var geomModule = desc.GeometryShader
                ? ShaderModulesPool.Get(desc.GeometryShader)
                : ShaderModuleState.Null;
            var fragModule = desc.FragementShader
                ? ShaderModulesPool.Get(desc.FragementShader)
                : ShaderModuleState.Null;
            var taskModule = desc.TaskShader
                ? ShaderModulesPool.Get(desc.TaskShader)
                : ShaderModuleState.Null;
            var meshModule = desc.MeshShader
                ? ShaderModulesPool.Get(desc.MeshShader)
                : ShaderModuleState.Null;

            HxDebug.Assert(vertModule || meshModule);
            HxDebug.Assert(fragModule);

            if (tescModule || teseModule || desc.PatchControlPoints > 0)
            {
                HxDebug.Assert(
                    tescModule && teseModule,
                    "Both tessellation control and evaluation shaders should be provided"
                );
                HxDebug.Assert(
                    desc.PatchControlPoints > 0
                        && desc.PatchControlPoints
                            <= _vkPhysicalDeviceProperties2
                                .properties
                                .limits
                                .maxTessellationPatchSize
                );
            }
            using var pBindings = MemoryMarshal
                .CreateFromPinnedArray(rps.VkBindings, 0, (int)rps.NumBindings)
                .Pin();

            using var pAttributes = MemoryMarshal
                .CreateFromPinnedArray(rps.VkAttributes, 0, (int)rps.NumAttributes)
                .Pin();

            VkPipelineVertexInputStateCreateInfo ciVertexInputState = new()
            {
                vertexBindingDescriptionCount = rps.NumBindings,
                pVertexBindingDescriptions =
                    rps.NumBindings > 0
                        ? (VkVertexInputBindingDescription*)pBindings.Pointer
                        : null,
                vertexAttributeDescriptionCount = rps.NumAttributes,
                pVertexAttributeDescriptions =
                    rps.NumAttributes > 0
                        ? (VkVertexInputAttributeDescription*)pAttributes.Pointer
                        : null,
            };

            var entries =
                stackalloc VkSpecializationMapEntry[Constants.SPECIALIZATION_CONSTANTS_MAX];
            using var pData = desc.SpecInfo.Data.Pin();
            VkSpecializationInfo si = HxVkUtils.GetPipelineShaderStageSpecializationInfo(
                desc.SpecInfo,
                entries,
                pData.Pointer
            );
            // create pipeline layout
            {
                uint32_t pushConstantsSize = 0;
                pushConstantsSize = vertModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = tescModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = teseModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = geomModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = fragModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = taskModule.GetMaxPushConstantsSize(pushConstantsSize);
                pushConstantsSize = meshModule.GetMaxPushConstantsSize(pushConstantsSize);

                // maxPushConstantsSize is guaranteed to be at least 128 bytes
                // https://www.khronos.org/registry/vulkan/specs/1.3/html/vkspec.html#features-limits
                // Table 32. Required Limits
                ref readonly var limits = ref GetVkPhysicalDeviceProperties().limits;
                if (!(pushConstantsSize <= limits.maxPushConstantsSize))
                {
                    logger.LogError(
                        "Push constants size exceeded {SIZE} (max {MAX_SIZE} bytes)",
                        pushConstantsSize,
                        limits.maxPushConstantsSize
                    );
                }

                // duplicate for MoltenVK
                var dsls =
                    stackalloc VkDescriptorSetLayout[4] {
                        VkDesSetLayout,
                        VkDesSetLayout,
                        VkDesSetLayout,
                        VkDesSetLayout,
                    };
                VkPushConstantRange range = new()
                {
                    stageFlags = rps.ShaderStageFlags,
                    offset = 0,
                    size = pushConstantsSize,
                };
                VkPipelineLayoutCreateInfo ci = new()
                {
                    setLayoutCount = 4,
                    pSetLayouts = dsls,
                    pushConstantRangeCount = pushConstantsSize > 0 ? 1u : 0u,
                    pPushConstantRanges = pushConstantsSize > 0 ? &range : null,
                };
                VK.vkCreatePipelineLayout(_vkDevice, &ci, null, &layout).CheckResult();
                if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(desc.DebugName))
                {
                    _vkDevice.SetDebugObjectName(
                        VK.VK_OBJECT_TYPE_PIPELINE_LAYOUT,
                        (nuint)layout.Handle,
                        $"[Vk.PipelineLayout]: {desc.DebugName}"
                    );
                }
            }

            VulkanPipelineBuilder
                .Create()
                .DynamicState(VK.VK_DYNAMIC_STATE_VIEWPORT)
                .DynamicState(VK.VK_DYNAMIC_STATE_SCISSOR)
                .DynamicState(VK.VK_DYNAMIC_STATE_DEPTH_BIAS)
                .DynamicState(VK.VK_DYNAMIC_STATE_BLEND_CONSTANTS)
                // from Vulkan 1.3 or VK_EXT_extended_dynamic_state
                .DynamicState(VK.VK_DYNAMIC_STATE_DEPTH_TEST_ENABLE)
                .DynamicState(VK.VK_DYNAMIC_STATE_DEPTH_WRITE_ENABLE)
                .DynamicState(VK.VK_DYNAMIC_STATE_DEPTH_COMPARE_OP)
                // from Vulkan 1.3 or VK_EXT_extended_dynamic_state2
                .DynamicState(VK.VK_DYNAMIC_STATE_DEPTH_BIAS_ENABLE)
                .PrimitiveTopology(desc.Topology.ToVk())
                .RasterizationSamples(
                    HxVkUtils.GetVulkanSampleCountFlags(
                        desc.SamplesCount,
                        GetFramebufferMSAABitMaskVK()
                    ),
                    desc.MinSampleShading
                )
                .PolygonMode(desc.PolygonMode.ToVk())
                .StencilStateOps(
                    VK.VK_STENCIL_FACE_FRONT_BIT,
                    desc.FrontFaceStencil.StencilFailureOp.ToVk(),
                    desc.FrontFaceStencil.DepthStencilPassOp.ToVk(),
                    desc.FrontFaceStencil.DepthFailureOp.ToVk(),
                    desc.FrontFaceStencil.StencilCompareOp.ToVk()
                )
                .StencilStateOps(
                    VK.VK_STENCIL_FACE_BACK_BIT,
                    desc.BackFaceStencil.StencilFailureOp.ToVk(),
                    desc.BackFaceStencil.DepthStencilPassOp.ToVk(),
                    desc.BackFaceStencil.DepthFailureOp.ToVk(),
                    desc.BackFaceStencil.StencilCompareOp.ToVk()
                )
                .StencilMasks(
                    VK.VK_STENCIL_FACE_FRONT_BIT,
                    0xFF,
                    desc.FrontFaceStencil.WriteMask,
                    desc.FrontFaceStencil.ReadMask
                )
                .StencilMasks(
                    VK.VK_STENCIL_FACE_BACK_BIT,
                    0xFF,
                    desc.BackFaceStencil.WriteMask,
                    desc.BackFaceStencil.ReadMask
                )
                .ShaderStage(
                    taskModule
                        ? HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_TASK_BIT_EXT,
                            taskModule!.ShaderModule,
                            desc.EntryPointTask.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                        : new VkPipelineShaderStageCreateInfo { module = VkShaderModule.Null }
                )
                .ShaderStage(
                    meshModule
                        ? HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_MESH_BIT_EXT,
                            meshModule!.ShaderModule,
                            desc.EntryPointMesh.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                        : HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_VERTEX_BIT,
                            vertModule!.ShaderModule,
                            desc.EntryPointVert.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                )
                .ShaderStage(
                    HxVkUtils.GetPipelineShaderStageCreateInfo(
                        VK.VK_SHADER_STAGE_FRAGMENT_BIT,
                        fragModule!.ShaderModule,
                        desc.EntryPointFrag.ToVkUtf8ReadOnlyString(),
                        &si
                    )
                )
                .ShaderStage(
                    tescModule
                        ? HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_TESSELLATION_CONTROL_BIT,
                            tescModule!.ShaderModule,
                            desc.EntryPointTesc.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                        : new VkPipelineShaderStageCreateInfo { module = VkShaderModule.Null }
                )
                .ShaderStage(
                    teseModule
                        ? HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_TESSELLATION_EVALUATION_BIT,
                            teseModule!.ShaderModule,
                            desc.EntryPointTese.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                        : new VkPipelineShaderStageCreateInfo { module = VkShaderModule.Null }
                )
                .ShaderStage(
                    geomModule
                        ? HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_GEOMETRY_BIT,
                            geomModule!.ShaderModule,
                            desc.EntryPointGeom.ToVkUtf8ReadOnlyString(),
                            &si
                        )
                        : new VkPipelineShaderStageCreateInfo { module = VkShaderModule.Null }
                )
                .CullMode(desc.CullMode.ToVk())
                .FrontFace(desc.FrontFaceWinding.ToVk())
                .VertexInputState(ciVertexInputState)
                .ViewMask(viewMask)
                .ColorAttachments(
                    colorBlendAttachmentStates,
                    colorAttachmentFormats,
                    numColorAttachments
                )
                .DepthAttachmentFormat(desc.DepthFormat.ToVk())
                .StencilAttachmentFormat(desc.StencilFormat.ToVk())
                .PatchControlPoints(desc.PatchControlPoints)
                .Build(_vkDevice, PipelineCache, layout, out pipeline, desc.DebugName);

            rps.Pipeline = pipeline;
            rps.PipelineLayout = layout;

            return pipeline;
        }
    }

    public VkPipeline GetVkPipeline(in ComputePipelineHandle handle)
    {
        var cps = ComputePipelinesPool.Get(handle);

        if (cps is null || !cps.Valid)
        {
            return VkPipeline.Null;
        }

        CheckAndUpdateDescriptorSets();

        if (cps.LastVkDescriptorSetLayout != VkDesSetLayout)
        {
            DeferredTask(
                () =>
                {
                    unsafe
                    {
                        VK.vkDestroyPipeline(VkDevice, cps.Pipeline, null);
                    }
                },
                SubmitHandle.Null
            );
            DeferredTask(
                () =>
                {
                    unsafe
                    {
                        VK.vkDestroyPipelineLayout(VkDevice, cps.PipelineLayout, null);
                    }
                },
                SubmitHandle.Null
            );
            cps.Pipeline = VkPipeline.Null;
            cps.PipelineLayout = VkPipelineLayout.Null;
            cps.LastVkDescriptorSetLayout = VkDesSetLayout;
        }

        if (cps.Pipeline == VkPipeline.Null)
        {
            var sm = ShaderModulesPool.Get(cps.Desc.ComputeShader);

            HxDebug.Assert(sm is not null && sm.Valid);
            unsafe
            {
                var entries =
                    stackalloc VkSpecializationMapEntry[Constants.SPECIALIZATION_CONSTANTS_MAX];
                using var pData = cps.Desc.SpecInfo.Data.Pin();
                var siComp = HxVkUtils.GetPipelineShaderStageSpecializationInfo(
                    cps.Desc.SpecInfo,
                    entries,
                    pData.Pointer
                );

                // create pipeline layout
                {
                    // duplicate for MoltenVK
                    var dsls =
                        stackalloc VkDescriptorSetLayout[4] {
                            VkDesSetLayout,
                            VkDesSetLayout,
                            VkDesSetLayout,
                            VkDesSetLayout,
                        };
                    VkPushConstantRange range = new()
                    {
                        stageFlags = VK.VK_SHADER_STAGE_COMPUTE_BIT,
                        offset = 0,
                        size = sm!.PushConstantsSize,
                    };
                    VkPipelineLayoutCreateInfo ci = new()
                    {
                        setLayoutCount = 4,
                        pSetLayouts = dsls,
                        pushConstantRangeCount = 1,
                        pPushConstantRanges = &range,
                    };
                    VK.vkCreatePipelineLayout(_vkDevice, &ci, null, out cps.PipelineLayout)
                        .CheckResult();
                    if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(cps.Desc.DebugName))
                    {
                        _vkDevice.SetDebugObjectName(
                            VK.VK_OBJECT_TYPE_PIPELINE_LAYOUT,
                            (nuint)cps.PipelineLayout,
                            $"[Vk.PipelineLayout]: {cps.Desc.DebugName}"
                        );
                    }
                }
                {
                    VkUtf8ReadOnlyString pEntryPoint = cps.Desc.EntryPoint.ToVkUtf8ReadOnlyString();
                    VkComputePipelineCreateInfo ci = new()
                    {
                        flags = 0,
                        stage = HxVkUtils.GetPipelineShaderStageCreateInfo(
                            VK.VK_SHADER_STAGE_COMPUTE_BIT,
                            sm.ShaderModule,
                            pEntryPoint,
                            &siComp
                        ),
                        layout = cps.PipelineLayout,
                        basePipelineHandle = VkPipeline.Null,
                        basePipelineIndex = -1,
                    };
                    VkPipeline pipeline = VkPipeline.Null;
                    VK.vkCreateComputePipelines(_vkDevice, PipelineCache, 1, &ci, null, &pipeline)
                        .CheckResult();
                    cps.Pipeline = pipeline;
                    if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(cps.Desc.DebugName))
                    {
                        _vkDevice.SetDebugObjectName(
                            VK.VK_OBJECT_TYPE_PIPELINE,
                            (nuint)cps.Pipeline,
                            $"[Vk.Pipline]: {cps.Desc.DebugName}"
                        );
                    }
                }
            }
        }
        return cps.Pipeline;
    }

    #region IDisposable Support

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                unsafe
                {
                    VK.vkDeviceWaitIdle(_vkDevice).CheckResult();
                    Disposer.DisposeAndRemove(ref _stagingDevice);
                    Disposer.DisposeAndRemove(ref _swapchain);
                    Destroy(_dummyTexture);
                    Destroy(_defaultSampler);

                    VK.vkDestroySemaphore(_vkDevice, TimelineSemaphore, null);

                    WaitDeferredTasks();

                    if (ShaderModulesPool.Count > 0)
                    {
                        HxDebug.Assert(false, $"Leaked {ShaderModulesPool.Count} shader modules");
                        logger.LogWarning("Leaked {COUNT} shader modules", ShaderModulesPool.Count);
                    }
                    if (RenderPipelinesPool.Count > 0)
                    {
                        HxDebug.Assert(
                            false,
                            $"Leaked {RenderPipelinesPool.Count} render pipelines"
                        );
                        logger.LogWarning(
                            "Leaked {COUNT} render pipelines",
                            RenderPipelinesPool.Count
                        );
                    }
                    if (ComputePipelinesPool.Count > 0)
                    {
                        HxDebug.Assert(
                            false,
                            $"Leaked {ComputePipelinesPool.Count} compute pipelines"
                        );
                        logger.LogWarning(
                            "Leaked {COUNT} compute pipelines",
                            ComputePipelinesPool.Count
                        );
                    }
                    if (SamplersPool.Count > 0)
                    {
                        HxDebug.Assert(false, $"Leaked {SamplersPool.Count} samplers");
                        // the dummy value is owned by the context
                        logger.LogWarning("Leaked {COUNT} samplers", SamplersPool.Count - 1);
                    }
                    if (TexturesPool.Count > 0)
                    {
                        HxDebug.Assert(false, $"Leaked {TexturesPool.Count} textures");
                        logger.LogWarning("Leaked {COUNT} textures", TexturesPool.Count);
                    }
                    if (BuffersPool.Count > 0)
                    {
                        HxDebug.Assert(false, $"Leaked {BuffersPool.Count} buffers");
                        logger.LogWarning("Leaked {COUNT} buffers", BuffersPool.Count);
                    }

                    SamplersPool.Clear();
                    ComputePipelinesPool.Clear();
                    RenderPipelinesPool.Clear();
                    ShaderModulesPool.Clear();
                    TexturesPool.Clear();

                    Disposer.DisposeAndRemove(ref _immediate);

                    VK.vkDestroyDescriptorSetLayout(_vkDevice, VkDesSetLayout, null);
                    VK.vkDestroyDescriptorPool(_vkDevice, VkDesPool, null);
                    VK.vkDestroySurfaceKHR(_vkInstance, _vkSurface, null);
                    VK.vkDestroyPipelineCache(_vkDevice, PipelineCache, null);
                    if (UseVmaAllocator)
                    {
                        Vma.vmaDestroyAllocator(VmaAllocator);
                    }

                    VK.vkDestroyDevice(_vkDevice, null);
                    Disposer.DisposeAndRemove(ref _validationSettings);
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~VulkanContext()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
    #endregion IDisposable Support
}
