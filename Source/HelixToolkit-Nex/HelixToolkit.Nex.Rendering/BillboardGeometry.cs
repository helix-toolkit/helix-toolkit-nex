namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Stores per-billboard instance data in a single interleaved <see cref="BillboardVertex"/> buffer.
/// Each entry represents a camera-facing quad with its own position, dimensions, UV coordinates,
/// and optional color. The GPU compute shader reads all fields from one contiguous buffer,
/// providing better cache locality and fewer buffer bindings than separate per-property buffers.
/// </summary>
public partial class BillboardGeometry : HxObservableObject
{
    /// <summary>
    /// The interleaved per-billboard vertex data.
    /// </summary>
    [Observable]
    private FastList<BillboardVertex> _vertices = [];

    /// <summary>
    /// Gets the number of billboards in this geometry.
    /// </summary>
    public int Count => Vertices.Count;

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
    /// Applies a position offset to all billboard instances.
    /// Adds the given offset to the xyz components of each vertex's Position, preserving w.
    /// </summary>
    /// <param name="offset">The offset to add to each billboard position.</param>
    public void ApplyPositionOffset(Vector3 offset)
    {
        var offset4 = new Vector4(offset, 0f);
        for (int i = 0; i < Vertices.Count; i++)
        {
            var v = Vertices[i];
            v.Position += offset4;
            Vertices[i] = v;
        }
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
}
