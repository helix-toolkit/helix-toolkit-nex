

namespace HelixToolkit.Nex.Tests.Vulkan;

[TestClass]
[TestCategory("GPURequired")]
public class Initialize
{
    [TestMethod]
    public void ContextInit()
    {
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true
        };
        using var context = VulkanBuilder.CreateHeadless(config);
        Assert.IsNotNull(context, "Vulkan context should not be null.");
    }
}
