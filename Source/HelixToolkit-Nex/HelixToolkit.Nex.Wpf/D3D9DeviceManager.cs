using Silk.NET.Core.Native;
using Silk.NET.Direct3D9;
using static Silk.NET.Core.Native.SilkMarshal;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// Manages the D3D9 context and device for WPF <see cref="System.Windows.Interop.D3DImage"/> interop.
/// Creates a D3D9Ex context and device so that shared-handle textures can be used
/// as back buffers for <c>D3DImage.SetBackBuffer</c>.
/// </summary>
public sealed unsafe class D3D9DeviceManager : IDisposable
{
    private readonly D3D9 _d3d9;
    private ComPtr<IDirect3D9Ex> _context;
    private ComPtr<IDirect3DDevice9Ex> _device;
    private bool _disposed;

    /// <summary>
    /// The D3D9Ex context (IDirect3D9Ex).
    /// </summary>
    public ComPtr<IDirect3D9Ex> Context => _context;

    /// <summary>
    /// The D3D9Ex device (IDirect3DDevice9Ex).
    /// </summary>
    public ComPtr<IDirect3DDevice9Ex> Device => _device;

    public D3D9DeviceManager()
    {
        _d3d9 = D3D9.GetApi(null);

        ThrowHResult(_d3d9.Direct3DCreate9Ex(D3D9.SdkVersion, ref _context));

        var presentParameters = new PresentParameters
        {
            Windowed = true,
            SwapEffect = Swapeffect.Discard,
            PresentationInterval = D3D9.PresentIntervalImmediate,
            BackBufferFormat = Format.Unknown,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
        };

        ThrowHResult(
            _context.CreateDeviceEx(
                0u,
                Devtype.Hal,
                nint.Zero,
                D3D9.CreateHardwareVertexprocessing | D3D9.CreateMultithreaded,
                ref presentParameters,
                null,
                ref _device
            )
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _device.Dispose();
        _context.Dispose();
        _d3d9.Dispose();
    }
}
