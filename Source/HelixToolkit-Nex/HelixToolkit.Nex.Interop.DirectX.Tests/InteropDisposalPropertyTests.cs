using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Vulkan;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Feature: wpf-winui-integration, Property 7: Interop layer disposal releases all handles
/// Validates: Requirements 9.1
/// </summary>
[TestClass]
[TestCategory("GPU")]
public class InteropDisposalPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private static D3D11DeviceManager? _d3d11Manager;
    private static VulkanContext? _ctx;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        // 1. Create D3D11 device manager (shared across iterations)
        _d3d11Manager = new D3D11DeviceManager();

        // 2. Create headless VulkanContext with external memory enabled,
        //    matching the DXGI adapter LUID
        var config = new VulkanContextConfig
        {
            EnableExternalMemoryWin32 = true,
            RequiredDeviceLuid = _d3d11Manager.AdapterLuid,
            TerminateOnValidationError = true,
        };
        var vkContext = VulkanBuilder.CreateHeadless(config);
        _ctx = vkContext as VulkanContext;
        Assert.IsNotNull(_ctx, "VulkanContext should not be null.");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _ctx?.Dispose();
        _ctx = null;

        _d3d11Manager?.Dispose();
        _d3d11Manager = null;
    }

    /// <summary>
    /// Property 7: For any fully initialized interop layer
    /// (D3D11DeviceManager + SharedTextureResult + ImportedVulkanTexture),
    /// after calling Dispose() on each component, all Vulkan handles are null
    /// and the SharedTextureResult's SharedHandle type is correct.
    ///
    /// The D3D11DeviceManager and VulkanContext are shared across iterations
    /// (created once in ClassInitialize) since they are expensive to create.
    /// Only the SharedTextureResult and ImportedVulkanTexture are created/disposed
    /// per iteration.
    /// **Validates: Requirements 9.1**
    /// </summary>
    [TestMethod]
    public void Disposal_ReleasesAllHandles()
    {
        Assert.IsNotNull(_d3d11Manager);
        Assert.IsNotNull(_ctx);

        // Generate random (width, height) pairs in [1..256] to keep GPU allocation fast
        var genDimension = Gen.Choose(1, 256).Select(i => (uint)i);
        var genPair = genDimension.Two();

        Prop.ForAll(
                Arb.From(genPair),
                ((uint w, uint h) dims) =>
                {
                    // 1. Create shared texture via WinUI path (NT handle)
                    var sharedTexture = SharedTextureFactory.CreateForWinUI(
                        _d3d11Manager,
                        dims.w,
                        dims.h
                    );

                    // 2. Import into Vulkan
                    var importedTexture = VulkanExternalMemoryImporter.Import(
                        _ctx,
                        sharedTexture.SharedHandle,
                        VkExternalMemoryHandleTypeFlags.D3D11Texture,
                        VkFormat.R8G8B8A8Unorm,
                        dims.w,
                        dims.h
                    );

                    // Pre-conditions: resources are valid before dispose
                    Assert.IsTrue(
                        importedTexture.Image.IsNotNull,
                        "Image should be valid before dispose"
                    );
                    Assert.IsTrue(
                        importedTexture.Memory.IsNotNull,
                        "Memory should be valid before dispose"
                    );
                    Assert.IsTrue(
                        importedTexture.ImageView.IsNotNull,
                        "ImageView should be valid before dispose"
                    );
                    Assert.IsTrue(
                        importedTexture.Handle.Valid,
                        "Handle should be valid before dispose"
                    );
                    Assert.AreNotEqual(
                        nint.Zero,
                        sharedTexture.SharedHandle,
                        "SharedHandle should be non-zero before dispose"
                    );

                    // 3. Dispose ImportedVulkanTexture first (Vulkan resources)
                    importedTexture.Dispose();

                    bool imageCleared = importedTexture.Image == VkImage.Null;
                    bool memoryCleared = importedTexture.Memory == VkDeviceMemory.Null;
                    bool imageViewCleared = importedTexture.ImageView == VkImageView.Null;
                    bool handleCleared = importedTexture.Handle == TextureHandle.Null;

                    // 4. Dispose SharedTextureResult (D3D11 texture + handle)
                    sharedTexture.Dispose();

                    return imageCleared && memoryCleared && imageViewCleared && handleCleared;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 7 (double-dispose safety): Calling Dispose() twice on each component
    /// does not throw, ensuring safe disposal patterns.
    /// **Validates: Requirements 9.1**
    /// </summary>
    [TestMethod]
    public void Disposal_DoubleDispose_DoesNotThrow()
    {
        Assert.IsNotNull(_d3d11Manager);
        Assert.IsNotNull(_ctx);

        var genDimension = Gen.Choose(1, 256).Select(i => (uint)i);
        var genPair = genDimension.Two();

        Prop.ForAll(
                Arb.From(genPair),
                ((uint w, uint h) dims) =>
                {
                    var sharedTexture = SharedTextureFactory.CreateForWinUI(
                        _d3d11Manager,
                        dims.w,
                        dims.h
                    );

                    var importedTexture = VulkanExternalMemoryImporter.Import(
                        _ctx,
                        sharedTexture.SharedHandle,
                        VkExternalMemoryHandleTypeFlags.D3D11Texture,
                        VkFormat.R8G8B8A8Unorm,
                        dims.w,
                        dims.h
                    );

                    // First dispose
                    importedTexture.Dispose();
                    sharedTexture.Dispose();

                    // Second dispose — should be a no-op
                    importedTexture.Dispose();
                    sharedTexture.Dispose();

                    bool imageCleared = importedTexture.Image == VkImage.Null;
                    bool memoryCleared = importedTexture.Memory == VkDeviceMemory.Null;
                    bool imageViewCleared = importedTexture.ImageView == VkImageView.Null;
                    bool handleCleared = importedTexture.Handle == TextureHandle.Null;

                    return imageCleared && memoryCleared && imageViewCleared && handleCleared;
                }
            )
            .Check(FsCheckConfig);
    }
}
