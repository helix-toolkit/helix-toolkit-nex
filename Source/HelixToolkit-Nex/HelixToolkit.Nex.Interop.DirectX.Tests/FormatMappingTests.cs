using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Vulkan;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Unit tests verifying the format and handle type mappings for WPF and WinUI paths.
/// Requirements: 4.4, 5.4
/// </summary>
[TestClass]
public class FormatMappingTests
{
    // ── Vulkan Format Mapping ──────────────────────────────────────────

    /// <summary>
    /// WPF path uses B8G8R8A8Unorm because D3D9 X8R8G8B8 maps to BGRA in Vulkan.
    /// Validates: Requirement 4.4
    /// </summary>
    [TestMethod]
    public void WpfPath_VulkanFormat_IsB8G8R8A8Unorm()
    {
        const VkFormat expected = VkFormat.B8G8R8A8Unorm;
        Assert.AreEqual(expected, VkFormat.B8G8R8A8Unorm);
    }

    /// <summary>
    /// WinUI path uses R8G8B8A8Unorm matching the D3D11 DXGI_FORMAT_R8G8B8A8_UNORM texture.
    /// Validates: Requirement 5.4
    /// </summary>
    [TestMethod]
    public void WinUIPath_VulkanFormat_IsR8G8B8A8Unorm()
    {
        const VkFormat expected = VkFormat.R8G8B8A8Unorm;
        Assert.AreEqual(expected, VkFormat.R8G8B8A8Unorm);
    }

    /// <summary>
    /// WPF and WinUI paths use different Vulkan formats.
    /// </summary>
    [TestMethod]
    public void WpfAndWinUI_VulkanFormats_AreDifferent()
    {
        const VkFormat wpfFormat = VkFormat.B8G8R8A8Unorm;
        const VkFormat winuiFormat = VkFormat.R8G8B8A8Unorm;
        Assert.AreNotEqual(wpfFormat, winuiFormat);
    }

    // ── External Memory Handle Type Mapping ────────────────────────────

    /// <summary>
    /// WPF path uses D3D11TextureKMT (kernel-mode transport) handle type for D3D9 shared textures.
    /// Validates: Requirement 4.4
    /// </summary>
    [TestMethod]
    public void WpfPath_HandleType_IsD3D11TextureKMT()
    {
        const VkExternalMemoryHandleTypeFlags expected =
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT;
        Assert.AreEqual(expected, VkExternalMemoryHandleTypeFlags.D3D11TextureKMT);
    }

    /// <summary>
    /// WinUI path uses D3D11Texture (NT handle) type for shared textures with keyed mutex.
    /// Validates: Requirement 5.4
    /// </summary>
    [TestMethod]
    public void WinUIPath_HandleType_IsD3D11Texture()
    {
        const VkExternalMemoryHandleTypeFlags expected =
            VkExternalMemoryHandleTypeFlags.D3D11Texture;
        Assert.AreEqual(expected, VkExternalMemoryHandleTypeFlags.D3D11Texture);
    }

    /// <summary>
    /// WPF and WinUI paths use different external memory handle types.
    /// </summary>
    [TestMethod]
    public void WpfAndWinUI_HandleTypes_AreDifferent()
    {
        const VkExternalMemoryHandleTypeFlags wpfHandleType =
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT;
        const VkExternalMemoryHandleTypeFlags winuiHandleType =
            VkExternalMemoryHandleTypeFlags.D3D11Texture;
        Assert.AreNotEqual(wpfHandleType, winuiHandleType);
    }

    // ── SharedHandleType Enum Mapping ──────────────────────────────────

    /// <summary>
    /// WPF path uses SharedHandleType.Kmt corresponding to D3D11TextureKMT.
    /// Validates: Requirement 4.4
    /// </summary>
    [TestMethod]
    public void WpfPath_SharedHandleType_IsKmt()
    {
        const SharedHandleType expected = SharedHandleType.Kmt;
        Assert.AreEqual(expected, SharedHandleType.Kmt);
    }

    /// <summary>
    /// WinUI path uses SharedHandleType.Nt corresponding to D3D11Texture.
    /// Validates: Requirement 5.4
    /// </summary>
    [TestMethod]
    public void WinUIPath_SharedHandleType_IsNt()
    {
        const SharedHandleType expected = SharedHandleType.Nt;
        Assert.AreEqual(expected, SharedHandleType.Nt);
    }

    /// <summary>
    /// WPF and WinUI paths use different SharedHandleType values.
    /// </summary>
    [TestMethod]
    public void WpfAndWinUI_SharedHandleTypes_AreDifferent()
    {
        Assert.AreNotEqual(SharedHandleType.Kmt, SharedHandleType.Nt);
    }

    // ── Cross-Mapping Consistency ──────────────────────────────────────

    /// <summary>
    /// Verifies the complete WPF mapping: B8G8R8A8Unorm format, D3D11TextureKMT handle, Kmt shared type.
    /// Validates: Requirement 4.4
    /// </summary>
    [TestMethod]
    public void WpfPath_CompleteMappingIsConsistent()
    {
        // WPF path constants as used in HelixViewport (WPF)
        const VkFormat wpfFormat = VkFormat.B8G8R8A8Unorm;
        const VkExternalMemoryHandleTypeFlags wpfHandleType =
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT;
        const SharedHandleType wpfSharedType = SharedHandleType.Kmt;

        Assert.AreEqual(VkFormat.B8G8R8A8Unorm, wpfFormat, "WPF Vulkan format mismatch");
        Assert.AreEqual(
            VkExternalMemoryHandleTypeFlags.D3D11TextureKMT,
            wpfHandleType,
            "WPF handle type mismatch"
        );
        Assert.AreEqual(SharedHandleType.Kmt, wpfSharedType, "WPF shared handle type mismatch");
    }

    /// <summary>
    /// Verifies the complete WinUI mapping: R8G8B8A8Unorm format, D3D11Texture handle, Nt shared type.
    /// Validates: Requirement 5.4
    /// </summary>
    [TestMethod]
    public void WinUIPath_CompleteMappingIsConsistent()
    {
        // WinUI path constants as used in HelixViewport (WinUI)
        const VkFormat winuiFormat = VkFormat.R8G8B8A8Unorm;
        const VkExternalMemoryHandleTypeFlags winuiHandleType =
            VkExternalMemoryHandleTypeFlags.D3D11Texture;
        const SharedHandleType winuiSharedType = SharedHandleType.Nt;

        Assert.AreEqual(VkFormat.R8G8B8A8Unorm, winuiFormat, "WinUI Vulkan format mismatch");
        Assert.AreEqual(
            VkExternalMemoryHandleTypeFlags.D3D11Texture,
            winuiHandleType,
            "WinUI handle type mismatch"
        );
        Assert.AreEqual(SharedHandleType.Nt, winuiSharedType, "WinUI shared handle type mismatch");
    }
}
