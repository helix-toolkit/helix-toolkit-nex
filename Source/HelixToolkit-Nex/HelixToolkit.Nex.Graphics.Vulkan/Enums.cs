namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// Defines the descriptor set binding points used in Vulkan pipelines.
/// </summary>
/// <remarks>
/// These binding indices correspond to the descriptor set layout bindings used
/// for different resource types in shaders.
/// </remarks>
public enum Bindings : uint8_t
{
    /// <summary>
    /// Binding point for sampled textures (set 0, binding 0).
    /// </summary>
    Textures = 0,

    /// <summary>
    /// Binding point for samplers (set 0, binding 1).
    /// </summary>
    Samplers = 1,

    /// <summary>
    /// Binding point for storage images (set 0, binding 2).
    /// </summary>
    StorageImages = 2,

    /// <summary>
    /// Binding point for YUV format images (set 0, binding 3).
    /// </summary>
    YUVImages = 3,

    /// <summary>
    /// Total number of descriptor bindings.
    /// </summary>
    NumBindings = 4,
};
