namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class DeviceQueues()
{
    public const uint32_t INVALID = 0xFFFFFFFF;
    public uint32_t GraphicsQueueFamilyIndex = INVALID;
    public uint32_t ComputeQueueFamilyIndex = INVALID;
    public VkQueue GraphicsQueue = VkQueue.Null;
    public VkQueue ComputeQueue = VkQueue.Null;
}
