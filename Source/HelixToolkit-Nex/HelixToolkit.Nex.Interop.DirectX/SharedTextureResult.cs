using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Result of creating a shared D3D11 texture for Vulkan interop.
/// Owns the D3D11 texture COM pointer and releases it on dispose.
/// </summary>
public sealed unsafe class SharedTextureResult : IDisposable
{
    private ComPtr<ID3D11Texture2D> _texture;
    private bool _disposed;

    /// <summary>
    /// The D3D11 texture opened/created in shared mode.
    /// </summary>
    public ComPtr<ID3D11Texture2D> Texture => _texture;

    /// <summary>
    /// The shared handle (KMT or NT) for Vulkan external memory import.
    /// </summary>
    public nint SharedHandle { get; }

    /// <summary>
    /// The type of shared handle.
    /// </summary>
    public SharedHandleType HandleType { get; }

    /// <summary>
    /// Texture width in pixels.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Texture height in pixels.
    /// </summary>
    public uint Height { get; }

    internal SharedTextureResult(
        ComPtr<ID3D11Texture2D> texture,
        nint sharedHandle,
        SharedHandleType handleType,
        uint width,
        uint height
    )
    {
        _texture = texture;
        SharedHandle = sharedHandle;
        HandleType = handleType;
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _texture.Dispose();
    }
}
