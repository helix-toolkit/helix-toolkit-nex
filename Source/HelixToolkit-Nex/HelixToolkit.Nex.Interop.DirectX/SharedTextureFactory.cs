using Vortice.Direct3D11;
using Vortice.DXGI;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Creates D3D11 textures in shared mode for Vulkan interop.
/// </summary>
public static unsafe class SharedTextureFactory
{
    private static uint _sharedTextureId = 0;

    /// <summary>
    /// Creates a D3D11 shared texture for the WPF path (KMT handle).
    /// Opens a D3D9 shared texture on the D3D11 side via <c>OpenSharedResource</c>
    /// and queries <c>IDXGIResource</c> for the KMT shared handle.
    /// </summary>
    /// <param name="d3d11">The D3D11 device manager.</param>
    /// <param name="d3d9SharedHandle">
    /// The shared handle obtained from D3D9 <c>CreateTexture</c> (the <c>pSharedHandle</c> output).
    /// </param>
    /// <returns>
    /// A <see cref="SharedTextureResult"/> containing the D3D11 texture, KMT handle, and dimensions.
    /// </returns>
    public static SharedTextureResult CreateForWpf(D3D11DeviceManager d3d11, nint d3d9SharedHandle)
    {
        ArgumentNullException.ThrowIfNull(d3d11);
        if (d3d9SharedHandle == nint.Zero)
            throw new ArgumentException(
                "D3D9 shared handle must not be zero.",
                nameof(d3d9SharedHandle)
            );

        // 1. Open the D3D9 shared texture on the D3D11 side
        var d3d11Texture = d3d11.Device.OpenSharedResource<ID3D11Texture2D>(d3d9SharedHandle);

        // 2. Get texture dimensions from the D3D11 texture description
        var desc = d3d11Texture.Description;

        // 3. Query IDXGIResource1 to obtain the KMT shared handle.
        using var dxgiResource = d3d11Texture.QueryInterface<IDXGIResource1>();
        var kmtHandle = dxgiResource.SharedHandle;

        return new SharedTextureResult(
            d3d11Texture,
            kmtHandle,
            SharedHandleType.Kmt,
            desc.Width,
            desc.Height
        );
    }

    /// <summary>
    /// Creates a D3D11 shared texture for the WinUI path (NT handle).
    /// The texture is created with <c>SharedNthandle</c> and <c>SharedKeyedmutex</c> misc flags,
    /// and the NT handle is obtained via <c>IDXGIResource1.CreateSharedHandle</c>.
    /// </summary>
    /// <param name="d3d11">The D3D11 device manager.</param>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <returns>
    /// A <see cref="SharedTextureResult"/> containing the D3D11 texture, NT handle, and dimensions.
    /// </returns>
    public static SharedTextureResult CreateForWinUI(
        D3D11DeviceManager d3d11,
        uint width,
        uint height
    )
    {
        ArgumentNullException.ThrowIfNull(d3d11);
        if (width == 0)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Width must be greater than zero."
            );
        if (height == 0)
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Height must be greater than zero."
            );

        // 1. Describe the texture with shared NT handle + keyed mutex flags
        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            Format = Format.R8G8B8A8_UNorm,
            BindFlags = (BindFlags.RenderTarget | BindFlags.ShaderResource),
            MiscFlags = (ResourceOptionFlags.SharedNTHandle | ResourceOptionFlags.SharedKeyedMutex),
            SampleDescription = new(1u, 0u),
            Usage = ResourceUsage.Default,
            ArraySize = 1u,
            MipLevels = 1u,
        };

        // 2. Create the D3D11 texture
        var texture = d3d11.Device.CreateTexture2D(desc);

        // 3. Query IDXGIResource1 and create the NT shared handle
        using var dxgiResource = texture.QueryInterface<IDXGIResource1>();
        var id = Interlocked.Increment(ref _sharedTextureId);
        var ntHandle = dxgiResource.CreateSharedHandle(
            null,
            Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write,
            $"Dx11SharedTexture_{id}"
        );

        return new SharedTextureResult(texture, ntHandle, SharedHandleType.Nt, width, height);
    }
}
