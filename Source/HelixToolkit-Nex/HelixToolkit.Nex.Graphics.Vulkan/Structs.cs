namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// Represents a combination of pipeline stage and memory access flags for Vulkan synchronization.
/// </summary>
/// <remarks>
/// This structure is used for defining memory barriers and pipeline dependencies in Vulkan,
/// combining stage flags (when the operation occurs) with access flags (what type of memory access).
/// Uses Vulkan 1.3 synchronization2 flags for more granular control.
/// </remarks>
internal struct StageAccess2
{
    /// <summary>
    /// The pipeline stage flags indicating when the memory access occurs.
    /// </summary>
    public VkPipelineStageFlags2 Stage;

    /// <summary>
    /// The memory access flags indicating the type of memory access.
    /// </summary>
    public VkAccessFlags2 Access;
};
