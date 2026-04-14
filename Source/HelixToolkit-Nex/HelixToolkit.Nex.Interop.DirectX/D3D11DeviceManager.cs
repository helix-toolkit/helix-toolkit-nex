using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using static Silk.NET.Core.Native.SilkMarshal;

namespace HelixToolkit.Nex.Interop.DirectX;

/// <summary>
/// Manages the D3D11 device and provides the DXGI adapter LUID.
/// Shared by both WPF and WinUI paths.
/// </summary>
public sealed unsafe class D3D11DeviceManager : IDisposable
{
    private readonly D3D11 _d3d11;
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _deviceContext;
    private bool _disposed;

    /// <summary>
    /// The D3D11 device.
    /// </summary>
    public ComPtr<ID3D11Device> Device => _device;

    /// <summary>
    /// The immediate device context.
    /// </summary>
    public ComPtr<ID3D11DeviceContext> DeviceContext => _deviceContext;

    /// <summary>
    /// The DXGI adapter LUID as an 8-byte array, matching the format used by
    /// <c>VulkanContextConfig.RequiredDeviceLuid</c>.
    /// </summary>
    public byte[] AdapterLuid { get; }

    public D3D11DeviceManager()
    {
        _d3d11 = D3D11.GetApi(null);

        ThrowHResult(
            _d3d11.CreateDevice(
                default(ComPtr<IDXGIAdapter>),
                D3DDriverType.Hardware,
                nint.Zero,
                (uint)CreateDeviceFlag.BgraSupport,
                null,
                0u,
                D3D11.SdkVersion,
                ref _device,
                null,
                ref _deviceContext
            )
        );

        AdapterLuid = RetrieveAdapterLuid();
    }

    private byte[] RetrieveAdapterLuid()
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        var adapter = default(ComPtr<IDXGIAdapter>);

        try
        {
            ThrowHResult(dxgiDevice.GetAdapter(ref adapter));

            AdapterDesc desc = default;
            ThrowHResult(adapter.GetDesc(ref desc));

            // Convert Luid to 8-byte array (Low 4 bytes + High 4 bytes, little-endian)
            var luid = desc.AdapterLuid;
            var bytes = new byte[8];
            BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), (uint)luid.Low);
            BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), luid.High);
            return bytes;
        }
        finally
        {
            adapter.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _deviceContext.Dispose();
        _device.Dispose();
        _d3d11.Dispose();
    }
}
