namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Identifies the type of shared handle used for DirectX-Vulkan interop.
/// </summary>
public enum SharedHandleType
{
    /// <summary>
    /// Kernel-mode transport handle, used by the WPF D3D9-to-D3D11 path.
    /// Corresponds to <c>D3D11TextureKmtBit</c> in Vulkan.
    /// </summary>
    Kmt,

    /// <summary>
    /// Windows NT handle, used by the WinUI D3D11 path with keyed mutex synchronization.
    /// Corresponds to <c>D3D11TextureBit</c> in Vulkan.
    /// </summary>
    Nt,
}
