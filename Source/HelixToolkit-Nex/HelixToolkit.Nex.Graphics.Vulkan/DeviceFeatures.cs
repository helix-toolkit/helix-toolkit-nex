namespace HelixToolkit.Nex.Graphics.Vulkan;

internal static class DeviceFeatures
{
    public static VkPhysicalDeviceFeatures CreateFeatures10(ref VkPhysicalDeviceFeatures supported)
    {
        return new VkPhysicalDeviceFeatures()
        {
            geometryShader = supported.geometryShader,
            tessellationShader = supported.tessellationShader,
            multiDrawIndirect = VkBool32.True,
            drawIndirectFirstInstance = supported.drawIndirectFirstInstance,
            depthClamp = VkBool32.True,
            depthBiasClamp = supported.depthBiasClamp,
            fillModeNonSolid = supported.fillModeNonSolid,
            samplerAnisotropy = supported.samplerAnisotropy,
            vertexPipelineStoresAndAtomics = supported.vertexPipelineStoresAndAtomics,
            fragmentStoresAndAtomics = supported.fragmentStoresAndAtomics,
            shaderSampledImageArrayDynamicIndexing = VkBool32.True,
            shaderImageGatherExtended = supported.shaderImageGatherExtended,
            shaderInt64 = VkBool32.True,
            textureCompressionBC = supported.textureCompressionBC,
            shaderStorageImageMultisample = supported.shaderStorageImageMultisample,
            independentBlend = supported.independentBlend,
        };
    }

    public static VkPhysicalDeviceVulkan11Features CreateFeatures11(ref VkPhysicalDeviceVulkan11Features supported)
    {
        return new VkPhysicalDeviceVulkan11Features()
        {
            multiview = supported.multiview,
            samplerYcbcrConversion = supported.samplerYcbcrConversion,
            shaderDrawParameters = VkBool32.True,
        };
    }

    public static VkPhysicalDeviceVulkan12Features CreateFeatures12(ref VkPhysicalDeviceVulkan12Features supported)
    {
        return new VkPhysicalDeviceVulkan12Features()
        {
            samplerMirrorClampToEdge = VkBool32.True,
            drawIndirectCount = supported.drawIndirectCount, // enable if supported
            descriptorIndexing = VkBool32.True,
            shaderSampledImageArrayNonUniformIndexing = VkBool32.True,
            descriptorBindingSampledImageUpdateAfterBind = VkBool32.True,
            descriptorBindingStorageImageUpdateAfterBind = VkBool32.True,
            descriptorBindingUpdateUnusedWhilePending = VkBool32.True,
            descriptorBindingPartiallyBound = VkBool32.True,
            descriptorBindingVariableDescriptorCount = VkBool32.True,
            runtimeDescriptorArray = VkBool32.True,
            scalarBlockLayout = VkBool32.True,
            uniformBufferStandardLayout = VkBool32.True,
            hostQueryReset = supported.hostQueryReset, // enable if supported
            timelineSemaphore = VkBool32.True,
            bufferDeviceAddress = VkBool32.True,
            vulkanMemoryModel = supported.vulkanMemoryModel, // enable if supported
            vulkanMemoryModelDeviceScope = supported.vulkanMemoryModelDeviceScope, // enable if supported
        };
    }

    public static VkPhysicalDeviceVulkan13Features CreateFeatures13(ref VkPhysicalDeviceVulkan13Features supported)
    {
        return new VkPhysicalDeviceVulkan13Features()
        {
            shaderDemoteToHelperInvocation = supported.shaderDemoteToHelperInvocation, // enable if supported
            subgroupSizeControl = VkBool32.True,
            synchronization2 = VkBool32.True,
            dynamicRendering = VkBool32.True,
            maintenance4 = VkBool32.True,
        };
    }
}
