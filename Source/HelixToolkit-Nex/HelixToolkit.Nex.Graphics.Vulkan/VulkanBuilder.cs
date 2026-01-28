namespace HelixToolkit.Nex.Graphics.Vulkan;

public static class VulkanBuilder
{
    /// <summary>
    /// Create the Vulkan graphics context.
    /// </summary>
    /// <param name="config"></param>
    /// <param name="window"></param>
    /// <param name="display"></param>
    /// <param name="surface"></param>
    /// <param name="initialize"></param>
    /// <returns></returns>
    public static IContext Create(
        VulkanContextConfig config,
        nint window,
        nint display,
        bool initialize = true
    )
    {
        var ctx = new VulkanContext(config, window, display);
        if (initialize)
        {
            if (ctx.Initialize().CheckResult() != ResultCode.Ok)
            {
                throw new InvalidOperationException("Failed to create Vulkan Context.");
            }
        }
        return ctx;
    }

    /// <summary>
    /// Create a headless Vulkan graphics context.
    /// </summary>
    public static IContext CreateHeadless(VulkanContextConfig config, bool initialize = true)
    {
        config.EnableHeadlessSurface = true;
        var ctx = new VulkanContext(config);
        if (initialize)
        {
            if (ctx.Initialize().CheckResult() != ResultCode.Ok)
            {
                throw new InvalidOperationException("Failed to create Vulkan Context.");
            }
        }
        return ctx;
    }
}
