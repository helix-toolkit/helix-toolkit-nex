using HelixToolkit.Nex.Graphics.Vulkan;
using Vortice.Vulkan;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;
using VK = Vortice.Vulkan.Vulkan;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Result of importing a shared DirectX texture into Vulkan.
/// Owns the VkImage, VkDeviceMemory, VkImageView, and TextureHandle.
/// The VulkanImage wrapper is created with isOwningVkImage = false so that
/// Pool.Destroy only cleans up the wrapper's image views — the actual Vulkan
/// resources are freed here in Dispose().
/// </summary>
public sealed class ImportedVulkanTexture : IDisposable
{
    private readonly VulkanContext _ctx;
    private VkImage _image;
    private VkDeviceMemory _memory;
    private VkImageView _imageView;
    private TextureHandle _handle;
    private bool _disposed;

    /// <summary>
    /// The texture handle registered in the engine's TexturesPool.
    /// </summary>
    public TextureHandle Handle => _handle;

    /// <summary>
    /// The imported VkImage backed by shared DirectX memory.
    /// </summary>
    public VkImage Image => _image;

    /// <summary>
    /// The VkDeviceMemory allocated via ImportMemoryWin32HandleInfoKHR.
    /// </summary>
    public VkDeviceMemory Memory => _memory;

    /// <summary>
    /// The VkImageView created for the imported image.
    /// </summary>
    public VkImageView ImageView => _imageView;

    internal ImportedVulkanTexture(
        VulkanContext ctx,
        VkImage image,
        VkDeviceMemory memory,
        VkImageView imageView,
        TextureHandle handle
    )
    {
        _ctx = ctx;
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _handle = handle;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var device = _ctx.VkDevice;

        // Destroy the texture handle in the pool first (this disposes the VulkanImage wrapper,
        // which only destroys its own image views since isOwningVkImage = false).
        if (_handle.Valid)
        {
            _ctx.TexturesPool.Destroy(_handle);
            _handle = TextureHandle.Null;
        }

        // Now destroy the Vulkan resources we own.
        unsafe
        {
            if (_imageView.IsNotNull)
            {
                VK.vkDestroyImageView(device, _imageView, null);
                _imageView = VkImageView.Null;
            }

            if (_image.IsNotNull)
            {
                VK.vkDestroyImage(device, _image, null);
                _image = VkImage.Null;
            }

            if (_memory.IsNotNull)
            {
                VK.vkFreeMemory(device, _memory, null);
                _memory = VkDeviceMemory.Null;
            }
        }
    }
}
