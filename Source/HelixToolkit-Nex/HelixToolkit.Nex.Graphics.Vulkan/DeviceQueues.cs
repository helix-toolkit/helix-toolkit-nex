namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class DeviceQueues()
{
    public const uint32_t INVALID = 0xFFFFFFFF;
    public uint32_t graphicsQueueFamilyIndex = INVALID;
    public uint32_t computeQueueFamilyIndex = INVALID;
    public VkQueue graphicsQueue = VkQueue.Null;
    public VkQueue computeQueue = VkQueue.Null;
}