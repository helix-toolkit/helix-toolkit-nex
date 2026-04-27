using Microsoft.Extensions.Logging;
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
    private static readonly ILogger _logger = LogManager.Create<D3D11DeviceManager>();
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
        DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
        using var f = factory;
        if (factory == null)
        {
            throw new InvalidOperationException("Failed to create DXGI Factory.");
        }
        using var adapter = FindDiscreteGraphicCard(factory);
        if (adapter is null)
        {
            throw new NotSupportedException("No suitable discrete graphics card found.");
        }
        _logger.LogInformation(
            "Using adapter: {Description}, Dedicated Memory: {DedicatedMemory} MB",
            adapter.Description.Description,
            adapter.Description.DedicatedVideoMemory / (1024 * 1024)
        );
        D3D11
            .D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0],
                out _device,
                out _deviceContext
            )
            .CheckError();

        AdapterLuid = RetrieveAdapterLuid();
    }

    private static IDXGIAdapter? FindDiscreteGraphicCard(IDXGIFactory1 factory)
    {
        IDXGIAdapter? bestAdapter = null;
        ulong bestDedicatedMemory = 0;

        for (int i = 0; factory!.EnumAdapters((uint)i, out IDXGIAdapter? adapter).Success; i++)
        {
            var desc = adapter!.Description;

            // Skip software/basic render adapters
            if (desc.VendorId == 0x1414 && desc.DeviceId == 0x8c) // Microsoft Basic Render Driver
            {
                adapter.Dispose();
                continue;
            }

            ulong dedicatedMemory = (ulong)desc.DedicatedVideoMemory;
            if (dedicatedMemory > bestDedicatedMemory)
            {
                bestAdapter?.Dispose();
                bestAdapter = adapter;
                bestDedicatedMemory = dedicatedMemory;
            }
            else
            {
                adapter.Dispose();
            }
        }
        return bestAdapter;
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
