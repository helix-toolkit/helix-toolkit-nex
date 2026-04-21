using Vortice.Direct3D9;

namespace HelixToolkit.Nex.Wpf;

/// <summary>
/// Manages the D3D9 context and device for WPF <see cref="System.Windows.Interop.D3DImage"/> interop.
/// Creates a D3D9Ex context and device so that shared-handle textures can be used
/// as back buffers for <c>D3DImage.SetBackBuffer</c>.
/// </summary>
public sealed class D3D9DeviceManager : IDisposable
{
    private IDirect3D9Ex _context;
    private IDirect3DDevice9Ex _device;
    private bool _disposed;

    /// <summary>
    /// The D3D9Ex context (IDirect3D9Ex).
    /// </summary>
    public IDirect3D9Ex Context => _context;

    /// <summary>
    /// The D3D9Ex device (IDirect3DDevice9Ex).
    /// </summary>
    public IDirect3DDevice9Ex Device => _device;

    public D3D9DeviceManager()
    {
        D3D9.Direct3DCreate9Ex(out _context).CheckError();

        var presentParameters = new PresentParameters
        {
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
            PresentationInterval = PresentInterval.Immediate,
            BackBufferFormat = Format.Unknown,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
        };

        _device = _context.CreateDeviceEx(
            0u,
            DeviceType.Hardware,
            nint.Zero,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded,
            presentParameters
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _device.Dispose();
        _context.Dispose();
    }
}
