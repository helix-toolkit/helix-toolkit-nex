namespace HelixToolkit.Nex.Graphics.Vulkan;

public static class VulkanBuilder
{
    /// <summary>
    /// Create the Vulkan graphics context.
    /// </summary>
    /// <param name="config">The configuration for the Vulkan context.</param>
    /// <returns>A new instance of <see cref="VulkanContext"/>.</returns>
    public static IContext Create(VulkanContextConfig config, nint window, nint display, in VkSurfaceKHR surface, bool initialize = true)
    {
        var ctx = new VulkanContext(config, window, display, surface);
        if (initialize)
        {
            ctx.Initialize().CheckResult();
        }
        return ctx;
    }

    public static IContext CreateHeadless(VulkanContextConfig config, bool initialize = true)
    {
        config.EnableHeadlessSurface = true;
        var ctx = new VulkanContext(config);
        if (initialize)
        {
            ctx.Initialize().CheckResult();
        }
        return ctx;
    }
}
