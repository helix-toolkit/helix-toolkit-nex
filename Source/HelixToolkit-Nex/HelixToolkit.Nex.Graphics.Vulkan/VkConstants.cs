namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// Provides constant values used throughout the Vulkan implementation.
/// </summary>
public static class VkConstants
{
    /// <summary>
    /// The Vulkan API version used by VMA (Vulkan Memory Allocator).
    /// Corresponds to Vulkan 1.2.0.
    /// </summary>
    public const uint64_t kVmaVulkanVersion = 1002000;

    /// <summary>
    /// Flag indicating VMA should not use Vulkan functions (all static).
    /// </summary>
  public const uint8_t kVmaStaticVulkanFunctions = 0;

    /// <summary>
    /// Flag indicating VMA should use dynamically loaded Vulkan functions.
    /// </summary>
 public const uint8_t kVmaDynamicVulkanFunctions = 1;

    /// <summary>
    /// Maximum number of custom Vulkan extensions that can be enabled.
    /// </summary>
    public const uint32_t kMaxCustomExtensions = 32;
}