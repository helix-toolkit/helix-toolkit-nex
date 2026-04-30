namespace HelixToolkit.Nex.Graphics.Vulkan
{
    internal partial class VulkanContext
    {
        private unsafe ResultCode InitPhysicalDevice()
        {
            // Find physical device, setup queue's and create device.
            uint physicalDevicesCount = 0;
            VK.vkEnumeratePhysicalDevices(_vkInstance, &physicalDevicesCount, null).CheckResult();

            if (physicalDevicesCount == 0)
            {
                throw new Exception("Vulkan: Failed to find GPUs with Vulkan support");
            }

            var physicalDevices = stackalloc VkPhysicalDevice[(int)physicalDevicesCount];
            VK.vkEnumeratePhysicalDevices(_vkInstance, &physicalDevicesCount, physicalDevices)
                .CheckResult();

            var luidFilterActive = Config.RequiredDeviceLuid is { Length: 8 };
            _vkPhysicalDevice = FindBestDevice(
                _vkSurface,
                new ReadOnlySpan<VkPhysicalDevice>(physicalDevices, (int)physicalDevicesCount),
                Config,
                luidFilterActive ? Config.RequiredDeviceLuid : null
            );

            if (_vkPhysicalDevice.IsNull)
            {
                _logger.LogError("Vulkan: No suitable physical device found");
                return ResultCode.RuntimeError;
            }
            VK.vkGetPhysicalDeviceProperties(
                _vkPhysicalDevice,
                out VkPhysicalDeviceProperties properties
            );

            if (properties.apiVersion < Config.VulkanVersion)
            {
                _logger.LogError(
                    "Vulkan: The physical device does not support Vulkan 1.3 or higher."
                );
                return ResultCode.NotSupported;
            }
            if (properties.deviceName is not null)
            {
                DeviceName =
                    new VkUtf8ReadOnlyString(
                        new ReadOnlySpan<byte>(
                            properties.deviceName,
                            (int)VK.VK_MAX_PHYSICAL_DEVICE_NAME_SIZE
                        )
                    ).ToString() ?? string.Empty;
                DeviceName = DeviceName.TrimEnd('\0'); // Remove any trailing null characters
            }

            _logger.LogInformation(
                $"""
                Selected Graphics Card Info:
                ---------------------------
                Device ID: {properties.deviceID}
                API Version: {properties.apiVersion}
                Device Name: {DeviceName}
                Device Type: {properties.deviceType}
                ----------------------------
                """
            );
            DeviceQueues.GraphicsQueueFamilyIndex = HxVkUtils.FindQueueFamilyIndex(
                _vkPhysicalDevice,
                VkQueueFlags.Graphics
            );
            DeviceQueues.ComputeQueueFamilyIndex = HxVkUtils.FindQueueFamilyIndex(
                _vkPhysicalDevice,
                VkQueueFlags.Compute
            );
            DeviceQueues.TransferQueueFamilyIndex = HxVkUtils.FindQueueFamilyIndex(
                _vkPhysicalDevice,
                VkQueueFlags.Transfer
            );

            if (DeviceQueues.GraphicsQueueFamilyIndex == DeviceQueues.INVALID)
            {
                _logger.LogError("VK_QUEUE_GRAPHICS_BIT is not supported");
                return ResultCode.RuntimeError;
            }
            if (DeviceQueues.ComputeQueueFamilyIndex == DeviceQueues.INVALID)
            {
                _logger.LogError("VK_QUEUE_COMPUTE_BIT is not supported");
                return ResultCode.RuntimeError;
            }
            return ResultCode.Ok;
        }

        private unsafe ResultCode InitDevice()
        {
            var availableDeviceExtensions = VK.vkEnumerateDeviceExtensionProperties(
                _vkPhysicalDevice
            );
            if (!availableDeviceExtensions.IsEmpty)
            {
                _supportedExtensions.Clear();
                foreach (var ext in availableDeviceExtensions)
                {
                    if (ext.extensionName is null)
                    {
                        continue;
                    }
                    _supportedExtensions.Add(new VkUtf8String(ext.extensionName).ToString()!);
                }
            }

            //var supportPresent = vkGetPhysicalDeviceWin32PresentationSupportKHR(PhysicalDevice, queueFamilies.graphicsFamily);
            VkDeviceQueueCreateInfo* queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[3];
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
                pQueuePriorities = &queuePriority, // Priority for the compute queue
            };
            queueCreateInfos[2] = new VkDeviceQueueCreateInfo
            {
                queueFamilyIndex = DeviceQueues.TransferQueueFamilyIndex,
                queueCount = 1,
                pQueuePriorities = &queuePriority, // Priority for the transfer queue
            };

            // Deduplicate queue families - Vulkan requires unique queue family indices
            HashSet<uint32_t> uniqueQueueFamilies =
            [
                queueCreateInfos[0].queueFamilyIndex,
                queueCreateInfos[1].queueFamilyIndex,
            ];
            if (DeviceQueues.TransferQueueFamilyIndex != DeviceQueues.INVALID)
            {
                uniqueQueueFamilies.Add(queueCreateInfos[2].queueFamilyIndex);
            }
            // Rebuild queueCreateInfos with only unique families
            uint32_t numQueues = 0;
            foreach (var familyIndex in uniqueQueueFamilies)
            {
                queueCreateInfos[numQueues++] = new VkDeviceQueueCreateInfo
                {
                    queueFamilyIndex = familyIndex,
                    queueCount = 1,
                    pQueuePriorities = &queuePriority,
                };
            }

            // Get features and properties of the physical device and create the logical device
            // VkPhysicalDeviceMeshShaderFeaturesEXT meshShaderFeatures = new();
            VkPhysicalDeviceVulkan13Features features1_3 = new() { pNext = null };
            VkPhysicalDeviceVulkan12Features features1_2 = new() { pNext = &features1_3 };
            VkPhysicalDeviceVulkan11Features features1_1 = new() { pNext = &features1_2 };

            VkPhysicalDeviceFeatures2 feature_1_0 = new() { pNext = &features1_1 };

            void** features_chain = &features1_2.pNext;

            using var additionalExts = new VkStringArray(Config.ExtensionsDevice);
            byte** pAdditionalExts = additionalExts;
            for (int i = 0; i < additionalExts.Length; ++i)
            {
                _deviceExtensions.Add(new VkUtf8String(pAdditionalExts[i]));
            }

            // Conditionally enable VK_KHR_external_memory_win32 for DirectX interop
            if (Config.EnableExternalMemoryWin32)
            {
                _deviceExtensions.Add(VK.VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME);
            }

            VK.vkGetPhysicalDeviceFeatures2(_vkPhysicalDevice, &feature_1_0);
            feature_1_0 = new()
            {
                features = DeviceFeatures.CreateFeatures10(ref feature_1_0.features),
            };
            features1_1 = DeviceFeatures.CreateFeatures11(ref features1_1);
            features1_2 = DeviceFeatures.CreateFeatures12(ref features1_2);
            features1_3 = DeviceFeatures.CreateFeatures13(ref features1_3);

            feature_1_0.pNext = &features1_1;
            features1_1.pNext = &features1_2;
            features1_2.pNext = &features1_3;

            _vkFeatures10 = feature_1_0;
            _vkFeatures11 = features1_1;
            _vkFeatures12 = features1_2;
            _vkFeatures13 = features1_3;

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

            VerifyRequiredDeviceExtensions();

            using var deviceExtensionNames = new VkStringArray(_deviceExtensions);

            VkDeviceCreateInfo deviceCreateInfo = new()
            {
                pNext = &feature_1_0,
                queueCreateInfoCount = numQueues,
                pQueueCreateInfos = queueCreateInfos,
                enabledExtensionCount = deviceExtensionNames.Length,
                ppEnabledExtensionNames = deviceExtensionNames,
                pEnabledFeatures = null,
            };

            VK.vkCreateDevice(_vkPhysicalDevice, &deviceCreateInfo, null, out _vkDevice)
                .CheckResult("Failed to create Vulkan Logical Device");
            VK.vkLoadDevice(_vkDevice);
            return ResultCode.Ok;
        }

        private void InitImmediateCommands()
        {
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

            // Get the transfer queue if a dedicated one is available
            if (DeviceQueues.TransferQueueFamilyIndex != DeviceQueues.INVALID)
            {
                VK.vkGetDeviceQueue(
                    _vkDevice,
                    DeviceQueues.TransferQueueFamilyIndex,
                    0,
                    out DeviceQueues.TransferQueue
                );
            }

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
        }

        private unsafe void InitPipelineCache()
        {
            VkPipelineCacheCreateInfo ci = new()
            {
                flags = VkPipelineCacheCreateFlags.None,
                initialDataSize = (uint)Config.PipelineCacheDataSize,
                pInitialData = (void*)Config.PipelineCacheData,
            };
            VK.vkCreatePipelineCache(_vkDevice, &ci, null, out _pipelineCache);
        }

        private void InitVma()
        {
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
        }

        private unsafe ResultCode InitDefaultTextureSampler()
        {
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
                _defaultSampler = new SamplerResource(this, sampler);
                HxDebug.Assert(
                    SamplersPool.Count == 1,
                    "Default sampler should be created successfully"
                );
            }
            return ResultCode.Ok;
        }

        private unsafe void InitDescriptorSets()
        {
            GrowDescriptorPool(CurrentMaxTextures, CurrentMaxSamplers);
            QuerySurfaceCapabilities();
            var bindings = stackalloc VkDescriptorSetLayoutBinding[Constants.MAX_COLOR_ATTACHMENTS];
            for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
            {
                bindings[i] = HxVkUtils.GetDSLBinding(
                    i,
                    VK.VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT,
                    1,
                    VK.VK_SHADER_STAGE_FRAGMENT_BIT
                );
            }

            var limits = GetVkPhysicalDeviceProperties().limits;
            VkDescriptorSetLayoutCreateInfo dslci = new()
            {
                flags = VK.VK_DESCRIPTOR_SET_LAYOUT_CREATE_PUSH_DESCRIPTOR_BIT,
                bindingCount = Math.Min(
                    Constants.MAX_COLOR_ATTACHMENTS,
                    limits.maxPerStageDescriptorInputAttachments
                ),
                pBindings = bindings,
            };
            VkDescriptorSetLayout dslInputAttachmentLayout;
            VK.vkCreateDescriptorSetLayout(_vkDevice, &dslci, null, &dslInputAttachmentLayout)
                .CheckResult();
            _dslInputAttachments = dslInputAttachmentLayout;
            _vkDevice.SetDebugObjectName(
                VK.VK_OBJECT_TYPE_DESCRIPTOR_SET_LAYOUT,
                (nuint)dslInputAttachmentLayout.Handle,
                "Descriptor Set Layout: VulkanContext::dslInputAttachments"
            );
        }

        private static unsafe VkPhysicalDevice FindBestDevice(
            VkSurfaceKHR surface,
            ReadOnlySpan<VkPhysicalDevice> devices,
            VulkanContextConfig config,
            byte[]? requiredLuid = null
        )
        {
            List<string>? availableLuids = requiredLuid is not null ? new() : null;
            byte* luidCopyBuffer = stackalloc byte[8];

            Dictionary<VkPhysicalDeviceType, uint> bestDeviceByType = new((int)devices.Length);
            VkPhysicalDevice selectedDevice = VkPhysicalDevice.Null;

            for (uint i = 0; i < devices.Length; i++)
            {
                var candidate = devices[(int)i];

                if (!IsDeviceSuitable(candidate, surface))
                    continue;

                // When LUID filtering is active, query VkPhysicalDeviceIDProperties and skip non-matching devices
                if (requiredLuid is not null)
                {
                    VkPhysicalDeviceIDProperties idProps = new();
                    VkPhysicalDeviceProperties2 props2 = new() { pNext = &idProps };
                    VK.vkGetPhysicalDeviceProperties2(candidate, &props2);

                    byte* luidPtr = idProps.deviceLUID;
                    for (int j = 0; j < 8; j++)
                        luidCopyBuffer[j] = luidPtr[j];

                    ReadOnlySpan<byte> deviceLuidSpan = new(luidCopyBuffer, 8);
                    availableLuids!.Add(Convert.ToHexString(deviceLuidSpan));

                    if (!idProps.deviceLUIDValid)
                    {
                        continue;
                    }
                    if (LuidMatches(deviceLuidSpan, requiredLuid!))
                    {
                        selectedDevice = candidate;
                        break;
                    }
                    continue;
                }

                VK.vkGetPhysicalDeviceProperties(
                    candidate,
                    out VkPhysicalDeviceProperties checkProperties
                );

                bestDeviceByType[checkProperties.deviceType] = i;
            }
            if (selectedDevice.IsNotNull)
            {
                _logger.LogInformation(
                    $"Selected physical device based on LUID match. Device LUID: {Convert.ToHexString(requiredLuid!)}"
                );
                return selectedDevice;
            }
            if (requiredLuid is not null)
            {
                _logger.LogError(
                    $"Vulkan: No physical device matches the required LUID '{Convert.ToHexString(requiredLuid!)}'. "
                        + $"Available device LUIDs: [{string.Join(", ", availableLuids!)}]"
                );
                return VkPhysicalDevice.Null;
            }
            if (
                !config.ForceIntegratedGPU
                && bestDeviceByType.ContainsKey(VkPhysicalDeviceType.DiscreteGpu)
            )
            {
                _logger.LogInformation("Selected discrete GPU.");
                selectedDevice = devices[(int)bestDeviceByType[VkPhysicalDeviceType.DiscreteGpu]];
            }
            else if (bestDeviceByType.ContainsKey(VkPhysicalDeviceType.IntegratedGpu))
            {
                _logger.LogInformation("Selected integrated GPU.");
                selectedDevice = devices[(int)bestDeviceByType[VkPhysicalDeviceType.IntegratedGpu]];
            }
            else if (bestDeviceByType.ContainsKey(VkPhysicalDeviceType.VirtualGpu))
            {
                _logger.LogInformation("Selected virtual GPU.");
                selectedDevice = devices[(int)bestDeviceByType[VkPhysicalDeviceType.VirtualGpu]];
            }
            else if (bestDeviceByType.ContainsKey(VkPhysicalDeviceType.Cpu))
            {
                _logger.LogInformation("Selected CPU device.");
                selectedDevice = devices[(int)bestDeviceByType[VkPhysicalDeviceType.Cpu]];
            }

            return selectedDevice;
        }
    }
}
