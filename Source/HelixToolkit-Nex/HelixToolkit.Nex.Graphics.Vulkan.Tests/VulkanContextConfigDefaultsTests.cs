namespace HelixToolkit.Nex.Tests.Vulkan;

/// <summary>
/// Unit tests for VulkanContextConfig default field values.
/// Validates: Requirements 3.1, 3.3
/// </summary>
[TestClass]
public class VulkanContextConfigDefaultsTests
{
    /// <summary>
    /// EnableExternalMemoryWin32 defaults to false, ensuring the extension
    /// is not loaded unless explicitly opted in.
    /// Validates: Requirements 3.1, 3.3
    /// </summary>
    [TestMethod]
    public void EnableExternalMemoryWin32_DefaultsToFalse()
    {
        var config = new VulkanContextConfig();
        Assert.IsFalse(config.EnableExternalMemoryWin32);
    }

    /// <summary>
    /// RequiredDeviceLuid defaults to null, meaning no LUID filtering
    /// is applied during physical device selection.
    /// Validates: Requirements 3.1, 3.3
    /// </summary>
    [TestMethod]
    public void RequiredDeviceLuid_DefaultsToNull()
    {
        var config = new VulkanContextConfig();
        Assert.IsNull(config.RequiredDeviceLuid);
    }
}
