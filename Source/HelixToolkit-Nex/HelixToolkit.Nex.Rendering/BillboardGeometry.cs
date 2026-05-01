using System.Numerics;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Stores per-billboard instance data in a single interleaved <see cref="BillboardVertex"/> buffer.
/// Each entry represents a camera-facing quad with its own position, dimensions, UV coordinates,
/// and optional color. The GPU compute shader reads all fields from one contiguous buffer,
/// providing better cache locality and fewer buffer bindings than separate per-property buffers.
/// </summary>
public class BillboardGeometry : IDisposable
{
    /// <summary>
    /// The interleaved per-billboard vertex data.
    /// </summary>
    public FastList<BillboardVertex> Vertices { get; } = [];

    // GPU buffer
    private ElementBuffer<BillboardVertex>? _vertexBuffer;

    /// <summary>
    /// Gets the GPU buffer containing the interleaved billboard vertex data.
    /// </summary>
    public BufferResource VertexBuffer => _vertexBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the number of billboards in this geometry.
    /// </summary>
    public int Count => Vertices.Count;

    /// <summary>
    /// Gets or sets whether this geometry uses dynamic GPU buffers (expected to change frequently).
    /// </summary>
    public bool IsDynamic { get; set; }

    private bool _dirty = true;

    /// <summary>
    /// Gets or sets whether the GPU buffer needs to be re-uploaded.
    /// </summary>
    public bool BufferDirty
    {
        get => _dirty;
        set => _dirty = value;
    }

    /// <summary>
    /// Marks the GPU buffer as needing re-upload.
    /// </summary>
    public void MarkDirty() => _dirty = true;

    public BillboardGeometry(bool isDynamic = false)
    {
        IsDynamic = isDynamic;
    }

    /// <summary>
    /// Adds a billboard instance with per-billboard color.
    /// </summary>
    public void Add(Vector3 position, float width, float height, Vector4 uvRect, Vector4 color)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = uvRect,
                Color = color,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Adds a billboard instance without per-billboard color (uses component uniform color).
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    public void Add(Vector3 position, float width, float height, Vector4 uvRect)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = uvRect,
                Color = Vector4.Zero, // alpha=0 signals "use uniform color"
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Removes all billboard instances.
    /// </summary>
    public void Clear()
    {
        Vertices.Clear();
        _dirty = true;
    }

    /// <summary>
    /// Creates or updates the GPU buffer from the CPU-side vertex data.
    /// </summary>
    public ResultCode UpdateBuffers(IContext context)
    {
        if (!_dirty)
            return ResultCode.Ok;

        if (Vertices.Count == 0)
        {
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;
        }
        else
        {
            _vertexBuffer ??= new ElementBuffer<BillboardVertex>(
                context,
                Vertices.Count,
                BufferUsageBits.Storage,
                IsDynamic,
                debugName: "BillboardVertices"
            );
            _vertexBuffer.Upload(Vertices);
        }

        _dirty = false;
        return ResultCode.Ok;
    }

    // --- Convenience read-only accessors for CPU-side data ---
    // These project from the interleaved Vertices list for backward compatibility.

    /// <summary>
    /// Gets a read-only view of billboard positions (projected from Vertices).
    /// </summary>
    public VertexProjection<Vector4> Positions => new(Vertices, v => v.Position);

    /// <summary>
    /// Gets a read-only view of billboard sizes (projected from Vertices).
    /// </summary>
    public VertexProjection<Vector2> Sizes => new(Vertices, v => v.Size);

    /// <summary>
    /// Gets a read-only view of billboard UV rects (projected from Vertices).
    /// </summary>
    public VertexProjection<Vector4> UVRects => new(Vertices, v => v.UvRect);

    /// <summary>
    /// Gets a read-only view of billboard colors (projected from Vertices).
    /// </summary>
    public VertexProjection<Vector4> Colors => new(Vertices, v => v.Color);

    /// <summary>
    /// Gets whether any billboard has a non-zero per-billboard color (alpha > 0).
    /// </summary>
    public bool HasPerBillboardColors
    {
        get
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i].Color.W > 0f)
                    return true;
            }
            return false;
        }
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _vertexBuffer?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Lightweight read-only projection over a <see cref="FastList{T}"/> that extracts
/// a single field from each element via a delegate. Used by <see cref="BillboardGeometry"/>
/// to provide backward-compatible indexed access to individual vertex fields.
/// </summary>
public readonly struct VertexProjection<TResult>(
    FastList<BillboardVertex> source,
    Func<BillboardVertex, TResult> selector
)
{
    public int Count => source.Count;
    public TResult this[int index] => selector(source[index]);
}
