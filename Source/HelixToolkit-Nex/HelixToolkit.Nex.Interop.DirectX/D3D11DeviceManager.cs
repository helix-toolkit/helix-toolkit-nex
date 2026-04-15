using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Manages the D3D11 device and provides the DXGI adapter LUID.
/// Shared by both WPF and WinUI paths.
/// </summary>
public sealed unsafe class D3D11DeviceManager : IDisposable
{
    private ID3D11Device _device;
    private ID3D11DeviceContext _deviceContext;
    private bool _disposed;

    /// <summary>
    /// The D3D11 device.
    /// </summary>
    public ID3D11Device Device => _device;

    /// <summary>
    /// The immediate device context.
    /// </summary>
    public ID3D11DeviceContext DeviceContext => _deviceContext;

    /// <summary>
    /// The DXGI adapter LUID as an 8-byte array, matching the format used by
    /// <c>VulkanContextConfig.RequiredDeviceLuid</c>.
    /// </summary>
    public byte[] AdapterLuid { get; }

    public D3D11DeviceManager()
    {
        D3D11
            .D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0],
                out _device,
                out _deviceContext
            )
            .CheckError();

        AdapterLuid = RetrieveAdapterLuid();
    }

    private byte[] RetrieveAdapterLuid()
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        IDXGIAdapter? adapter = null;

        try
        {
            dxgiDevice.GetAdapter(out adapter).CheckError();
            // Convert Luid to 8-byte array (Low 4 bytes + High 4 bytes, little-endian)
            var luid = adapter.Description.Luid;
            var result = new byte[8];
            BitConverter.GetBytes(luid.LowPart).CopyTo(result, 0);
            BitConverter.GetBytes(luid.HighPart).CopyTo(result, 4);
            return result;
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _deviceContext.Dispose();
        _device.Dispose();
    }
}
