using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Vulkan;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;
using VK = Vortice.Vulkan.Vulkan;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Feature: wpf-winui-integration, Property 6: ImportedVulkanTexture disposal clears resources
/// Validates: Requirements 1.6
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
public class ImportedVulkanTextureDisposalPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);
    private static VulkanContext? _ctx;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig { TerminateOnValidationError = true };
        var vkContext = VulkanBuilder.CreateHeadless(config);
        _ctx = vkContext as VulkanContext;
        Assert.IsNotNull(_ctx, "VulkanContext should not be null.");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _ctx?.Dispose();
    }

    /// <summary>
    /// Creates a real VkImage, VkDeviceMemory, VkImageView, and TextureHandle,
    /// wraps them in an ImportedVulkanTexture, and returns it for disposal testing.
    /// </summary>
    private static unsafe ImportedVulkanTexture CreateTestImportedTexture(
        VulkanContext ctx,
        uint width,
        uint height
    )
    {
        var device = ctx.VkDevice;
        var physicalDevice = ctx.VkPhysicalDevice;
        var format = VkFormat.B8G8R8A8Unorm;

        // Create a VkImage
        VkImageCreateInfo imageCreateInfo = new()
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(width, height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage =
                VkImageUsageFlags.ColorAttachment
                | VkImageUsageFlags.Sampled
                | VkImageUsageFlags.TransferSrc
                | VkImageUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        VkImage image;
        VK.vkCreateImage(device, &imageCreateInfo, null, &image)
            .CheckResult("Failed to create test image");

        // Get memory requirements and allocate
        VkMemoryRequirements memRequirements;
        VK.vkGetImageMemoryRequirements(device, image, &memRequirements);

        VkMemoryAllocateInfo memoryAllocateInfo = new()
        {
            allocationSize = memRequirements.size,
            memoryTypeIndex = HxVkUtils.FindMemoryType(
                physicalDevice,
                memRequirements.memoryTypeBits,
                VkMemoryPropertyFlags.DeviceLocal
            ),
        };

        VkDeviceMemory memory;
        VK.vkAllocateMemory(device, &memoryAllocateInfo, null, &memory)
            .CheckResult("Failed to allocate test memory");

        VK.vkBindImageMemory(device, image, memory, 0)
            .CheckResult("Failed to bind test memory to image");

        // Create image view
        VkImageViewCreateInfo imageViewCreateInfo = new()
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            components = new VkComponentMapping(
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity
            ),
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1,
            },
        };

        VkImageView imageView;
        VK.vkCreateImageView(device, &imageViewCreateInfo, null, &imageView)
            .CheckResult("Failed to create test image view");

        // Wrap in VulkanImage and register in TexturesPool
        var vulkanImage = new VulkanImage(
            ctx,
            image,
            usage: imageCreateInfo.usage,
            extent: imageCreateInfo.extent,
            type: VkImageType.Image2D,
            format: format,
            isDepthFormat: false,
            isStencilFormat: false,
            isSwapchainImage: false,
            isOwningVkImage: false,
            debugName: "Test Imported Texture"
        );
        vulkanImage.ImageView = imageView;

        TextureHandle textureHandle = ctx.TexturesPool.Create(vulkanImage);
        ctx.AwaitingCreation = true;

        return new ImportedVulkanTexture(ctx, image, memory, imageView, textureHandle);
    }

    /// <summary>
    /// Property 6: For any successfully created ImportedVulkanTexture, after Dispose(),
    /// Image is VkImage.Null, Memory is VkDeviceMemory.Null, ImageView is VkImageView.Null,
    /// and Handle is TextureHandle.Null.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [TestMethod]
    public void Disposal_ClearsAllResources()
    {
        Assert.IsNotNull(_ctx);

        // Generate random (width, height) pairs in [1..256] to create varied textures
        var genDimension = Gen.Choose(1, 256).Select(i => (uint)i);
        var genPair = genDimension.Two();

        Prop.ForAll(
                Arb.From(genPair),
                ((uint w, uint h) dims) =>
                {
                    var texture = CreateTestImportedTexture(_ctx, dims.w, dims.h);

                    // Pre-conditions: resources are valid before dispose
                    Assert.IsTrue(texture.Image.IsNotNull, "Image should be valid before dispose");
                    Assert.IsTrue(
                        texture.Memory.IsNotNull,
                        "Memory should be valid before dispose"
                    );
                    Assert.IsTrue(
                        texture.ImageView.IsNotNull,
                        "ImageView should be valid before dispose"
                    );
                    Assert.IsTrue(texture.Handle.Valid, "Handle should be valid before dispose");

                    texture.Dispose();

                    // Post-conditions: all resources cleared after dispose
                    bool imageCleared = texture.Image == VkImage.Null;
                    bool memoryCleared = texture.Memory == VkDeviceMemory.Null;
                    bool imageViewCleared = texture.ImageView == VkImageView.Null;
                    bool handleCleared = texture.Handle == TextureHandle.Null;

                    return imageCleared && memoryCleared && imageViewCleared && handleCleared;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 6 (double-dispose safety): Calling Dispose() twice does not throw.
    /// **Validates: Requirements 1.6**
    /// </summary>
    [TestMethod]
    public void Disposal_DoubleDispose_DoesNotThrow()
    {
        Assert.IsNotNull(_ctx);

        var genDimension = Gen.Choose(1, 256).Select(i => (uint)i);
        var genPair = genDimension.Two();

        Prop.ForAll(
                Arb.From(genPair),
                ((uint w, uint h) dims) =>
                {
                    var texture = CreateTestImportedTexture(_ctx, dims.w, dims.h);
                    texture.Dispose();
                    texture.Dispose(); // second dispose should be a no-op

                    bool imageCleared = texture.Image == VkImage.Null;
                    bool memoryCleared = texture.Memory == VkDeviceMemory.Null;
                    bool imageViewCleared = texture.ImageView == VkImageView.Null;
                    bool handleCleared = texture.Handle == TextureHandle.Null;

                    return imageCleared && memoryCleared && imageViewCleared && handleCleared;
                }
            )
            .Check(FsCheckConfig);
    }
}
