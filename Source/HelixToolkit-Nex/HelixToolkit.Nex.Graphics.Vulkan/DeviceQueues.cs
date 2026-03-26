namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class DeviceQueues()
{
    public const uint32_t INVALID = 0xFFFFFFFF;
    public uint32_t GraphicsQueueFamilyIndex = INVALID;
    public uint32_t ComputeQueueFamilyIndex = INVALID;
    public uint32_t TransferQueueFamilyIndex = INVALID;
    public VkQueue GraphicsQueue = VkQueue.Null;
    public VkQueue ComputeQueue = VkQueue.Null;
    public VkQueue TransferQueue = VkQueue.Null;

    /// <summary>
    /// Gets a value indicating whether a dedicated transfer queue exists
    /// (i.e., the transfer queue family is different from the graphics queue family).
    /// </summary>
    public bool HasDedicatedTransferQueue =>
        TransferQueueFamilyIndex != INVALID
        && TransferQueueFamilyIndex != GraphicsQueueFamilyIndex;
}
