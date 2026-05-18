namespace HelixToolkit.Nex.Geometries;

[Flags]
public enum BillboardVertType : uint
{
    None,
    UV = 1,
}

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
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    /// <param name="uvRect">The UV rectangle defining the billboard's texture coordinates.</param>
    /// <param name="color">The per-billboard color.</param>
    public void Add(Vector3 position, float width, float height, Vector4 uvRect, Vector4 color)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = uvRect,
                Color = color,
                Type = (uint)BillboardVertType.UV,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Adds a billboard instance without per-billboard color (uses component uniform color).
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    /// <param name="uvRect">The UV rectangle defining the billboard's texture coordinates.</param>
    public void Add(Vector3 position, float width, float height, Vector4 uvRect)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = uvRect,
                Color = Vector4.Zero, // alpha=0 signals "use uniform color"
                Type = (uint)BillboardVertType.UV,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Adds a billboard instance without per-billboard color (uses component uniform color).
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    public void Add(Vector3 position, float width, float height)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = Vector4.Zero,
                Color = Vector4.Zero, // alpha=0 signals "use uniform color"
                Type = (uint)BillboardVertType.None,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Inserts a billboard instance without per-billboard color (uses component uniform color) at the specified index.
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    /// <param name="index">The index at which to insert the billboard.</param>
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    public void Insert(int index, Vector3 position, float width, float height)
    {
        Vertices.Insert(
            index,
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = Vector4.Zero,
                Color = Vector4.Zero, // alpha=0 signals "use uniform color"
                Type = (uint)BillboardVertType.None,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Adds a billboard instance with per-billboard color, but without UV.
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    /// <param name="color">The per-billboard color.</param>
    public void Add(Vector3 position, float width, float height, Color4 color)
    {
        Vertices.Add(
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = Vector4.Zero,
                Color = color, // alpha=0 signals "use uniform color"
                Type = (uint)BillboardVertType.None,
            }
        );
        _dirty = true;
    }

    /// <summary>
    /// Inserts a billboard instance with per-billboard color, but without UV, at the specified index.
    /// The color field is set to (0,0,0,0) which signals the compute shader to use the uniform color.
    /// </summary>
    /// <param name="index">The index at which to insert the billboard.</param>
    /// <param name="position">The local-space position of the billboard.</param>
    /// <param name="width">The width of the billboard.</param>
    /// <param name="height">The height of the billboard.</param>
    /// <param name="color">The per-billboard color.</param>
    public void Insert(int index, Vector3 position, float width, float height, Color4 color)
    {
        Vertices.Insert(
            index,
            new BillboardVertex
            {
                Position = new Vector4(position, 1f),
                Size = new Vector2(width, height),
                UvRect = Vector4.Zero,
                Color = color, // alpha=0 signals "use uniform color"
                Type = (uint)BillboardVertType.None,
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
