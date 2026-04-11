using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Manages the GPU-side storage buffer for custom per-material properties of a specific
/// material type and exposes the device address that the shader reads via
/// <c>getCustomBufferAddress()</c>.
/// </summary>
/// <remarks>
/// Implement this interface (or derive from <see cref="CustomMaterialBuffer{T}"/>) for
/// every material type that declares a <see cref="MaterialTypeRegistration.CustomBufferGlsl"/>
/// block.  At render time, call <see cref="Update"/> to flush pending CPU writes to the
/// GPU buffer, then pass <see cref="GpuAddress"/> to the renderer so it can set
/// <c>FPConstants.customMaterialBufferAddress</c> before drawing with that material.
/// </remarks>
public interface ICustomMaterialBuffer : IDisposable
{
    /// <summary>
    /// GPU device address of the underlying storage buffer.
    /// Pass this value into <c>FPConstants.customMaterialBufferAddress</c> before issuing
    /// draw calls that use the owning material type.
    /// Returns <c>0</c> when the buffer is not yet initialized.
    /// </summary>
    ulong GpuAddress { get; }

    /// <summary>
    /// Returns <see langword="true"/> when the buffer has been successfully allocated and
    /// is ready to be referenced by the GPU.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Writes any pending CPU-side property changes to the GPU buffer.
    /// Call this once per frame (or whenever properties change) before submitting draw
    /// commands that use this material type.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the buffer was successfully synchronized;
    /// <see langword="false"/> if the buffer is not initialized or a GPU error occurred.
    /// </returns>
    bool Update();
}

/// <summary>
/// Base class for managing a strongly-typed custom material properties buffer that
/// backs a GLSL <c>buffer_reference</c> block declared in
/// <see cref="MaterialTypeRegistration.CustomBufferGlsl"/>.
/// </summary>
/// <typeparam name="T">
/// An unmanaged struct whose memory layout exactly matches the GLSL struct declared in
/// <see cref="MaterialTypeRegistration.CustomBufferGlsl"/>.  Annotate the struct with
/// <c>[StructLayout(LayoutKind.Sequential, Pack = N)]</c> to guarantee the layout matches
/// the <c>std430</c> GLSL layout rules.
/// </typeparam>
/// <example>
/// <code>
/// // 1. Declare the GLSL-matching C# struct
/// [StructLayout(LayoutKind.Sequential)]
/// public struct ToonProps
/// {
///     public Vector4 TintColor;   // vec4 tintColor
///     public float   Levels;      // float levels
///     private float  _pad0, _pad1, _pad2;
/// }
///
/// // 2. Create the buffer (typically during renderer init)
/// var toonBuffer = new CustomMaterialBuffer&lt;ToonProps&gt;(context, "ToonMaterial");
///
/// // 3. Modify properties on the CPU side
/// toonBuffer.Properties = new ToonProps { TintColor = new(1,0,0,1), Levels = 4 };
///
/// // 4. Each frame: flush + provide GPU address
/// toonBuffer.Update();
/// fpConstants.customMaterialBufferAddress = toonBuffer.GpuAddress;
/// </code>
/// </example>
public class CustomMaterialBuffer<T> : ICustomMaterialBuffer
    where T : unmanaged
{
    private readonly IContext _context;
    private readonly string _debugName;
    private BufferResource? _buffer;
    private T _properties;
    private bool _dirty = true;
    private bool _disposed;

    /// <summary>
    /// CPU-side copy of the custom material properties.
    /// Setting this property marks the buffer as dirty so the next call to
    /// <see cref="Update"/> uploads the new values to the GPU.
    /// </summary>
    public T Properties
    {
        get => _properties;
        set
        {
            _properties = value;
            _dirty = true;
        }
    }

    /// <inheritdoc/>
    public ulong GpuAddress =>
        _buffer is { Valid: true } ? (ulong)_context.GpuAddress(_buffer.Handle) : 0UL;

    /// <inheritdoc/>
    public bool IsValid => _buffer is { Valid: true };

    /// <summary>
    /// Initializes the buffer and allocates GPU memory immediately.
    /// </summary>
    /// <param name="context">The graphics context used to create and upload the buffer.</param>
    /// <param name="debugName">Optional label shown in GPU debug tools.</param>
    /// <param name="initialProperties">
    /// Optional initial value for the custom properties.
    /// If <see langword="null"/>, the struct is zero-initialized.
    /// </param>
    public CustomMaterialBuffer(
        IContext context,
        string debugName = "",
        T? initialProperties = null
    )
    {
        _context = context;
        _debugName = string.IsNullOrEmpty(debugName)
            ? $"CustomMaterial_{typeof(T).Name}"
            : debugName;

        _properties = initialProperties ?? default;

        AllocateBuffer();
    }

    private void AllocateBuffer()
    {
        uint sizeInBytes = (uint)Marshal.SizeOf<T>();

        var desc = new BufferDesc(
            BufferUsageBits.Storage,
            StorageType.HostVisible,
            IntPtr.Zero,
            sizeInBytes,
            _debugName
        );

        var result = _context.CreateBuffer(desc, out var buffer, _debugName);
        if (result == ResultCode.Ok)
        {
            _buffer = buffer;
            _dirty = true;
        }
    }

    /// <inheritdoc/>
    public bool Update()
    {
        if (_buffer is not { Valid: true })
            return false;

        if (!_dirty)
            return true;

        var result = _context.Upload(_buffer.Handle, 0, _properties);
        if (result == ResultCode.Ok)
        {
            _dirty = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the buffer as dirty so the next <see cref="Update"/> call re-uploads the
    /// current <see cref="Properties"/> value even when the struct has not changed.
    /// </summary>
    public void MarkDirty() => _dirty = true;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed and unmanaged resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        _disposed = true;
    }
}
