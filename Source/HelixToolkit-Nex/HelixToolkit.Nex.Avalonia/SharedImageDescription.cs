using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Platform-neutral description of the shared engine output image handed to Avalonia's
/// <c>ICompositionGpuInterop</c> for import into the composition surface. It carries the image
/// dimensions and format together with a platform handle union: on Windows the shared D3D11
/// NT handle (<see cref="NtHandle"/>); on Linux the exported external-memory POSIX file descriptor
/// (<see cref="MemoryFd"/>) plus its allocation size (<see cref="MemorySize"/>) and an optional
/// dma-buf format modifier (<see cref="DmaBufModifier"/>).
/// </summary>
/// <remarks>
/// Only the fields relevant to the active platform path are populated. The Windows bridge sets
/// <see cref="NtHandle"/>; the Linux bridge sets <see cref="MemoryFd"/>, <see cref="MemorySize"/>,
/// and (when a dma-buf modifier applies) <see cref="DmaBufModifier"/>. <see cref="ExternalHandleType"/>
/// identifies which handle type the compositor should use when importing the image.
/// </remarks>
public sealed class SharedImageDescription
{
    /// <summary>Width of the shared image in pixels.</summary>
    public uint Width { get; init; }

    /// <summary>Height of the shared image in pixels.</summary>
    public uint Height { get; init; }

    /// <summary>Pixel format of the shared image.</summary>
    public Format Format { get; init; }

    /// <summary>
    /// Windows path: the shared D3D11 texture NT handle imported by the compositor. Unused
    /// (<see cref="nint.Zero"/>) on the Linux path.
    /// </summary>
    public nint NtHandle { get; init; }

    /// <summary>
    /// Linux path: the exported external-memory POSIX file descriptor (opaque-fd or dma-buf).
    /// Unused (<c>-1</c>) on the Windows path.
    /// </summary>
    public int MemoryFd { get; init; } = -1;

    /// <summary>
    /// Linux path: the size, in bytes, of the exported device memory allocation backing the image.
    /// Unused (<c>0</c>) on the Windows path.
    /// </summary>
    public ulong MemorySize { get; init; }

    /// <summary>
    /// Linux path: the dma-buf format modifier describing the memory layout, when the exported
    /// handle is a dma-buf. <c>null</c> for opaque-fd exports and on the Windows path.
    /// </summary>
    public ulong? DmaBufModifier { get; init; }

    /// <summary>
    /// Identifier of the external image handle type the compositor should use when importing the
    /// image (for example a D3D11 shared texture handle on Windows or a Vulkan opaque-fd/dma-buf
    /// handle on Linux). Matches the Avalonia known external-image handle-type name.
    /// </summary>
    public string ExternalHandleType { get; init; } = string.Empty;
}
