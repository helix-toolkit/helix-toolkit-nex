namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Describes a single vertex attribute within a vertex buffer.
/// </summary>
/// <remarks>
/// A vertex attribute defines how vertex data is interpreted, including its location,
/// binding point, format, and offset within the vertex structure.
/// </remarks>
public struct VertexAttribute()
{
    /// <summary>
    /// The shader input location for this attribute.
    /// </summary>
    public uint32_t Location; // a buffer which contains this attribute stream

    /// <summary>
    /// The binding index of the buffer containing this attribute.
    /// </summary>
    public uint32_t Binding;

    /// <summary>
    /// The format of each element in this attribute stream.
    /// </summary>
    public VertexFormat Format; // per-element format

    /// <summary>
    /// The byte offset where the first element of this attribute starts within the vertex structure.
    /// </summary>
    public size_t Offset; // an offset where the first element of this attribute stream starts
}

/// <summary>
/// Describes a vertex buffer binding configuration.
/// </summary>
public struct VertexInputBinding()
{
    /// <summary>
    /// The stride in bytes between consecutive vertex elements in the buffer.
    /// </summary>
    public uint32_t Stride;
}

/// <summary>
/// Describes the complete vertex input configuration for a graphics pipeline.
/// </summary>
/// <remarks>
/// This structure defines how vertex data is organized and interpreted by the graphics pipeline,
/// including buffer bindings and attribute layouts.
/// </remarks>
public struct VertexInput()
{
    /// <summary>
    /// Maximum number of vertex buffer bindings supported.
    /// </summary>
    public const uint32_t MAX_VERTEX_BINDINGS = 16;

    /// <summary>
    /// Maximum number of vertex attributes supported.
    /// </summary>
    public const uint32_t MAX_VERTEX_ATTRIBUTES = 16;

    /// <summary>
    /// Array of vertex buffer bindings.
    /// </summary>
    public readonly VertexInputBinding[] Bindings = new VertexInputBinding[MAX_VERTEX_BINDINGS];

    /// <summary>
    /// Array of vertex attributes.
    /// </summary>
    public readonly VertexAttribute[] Attributes = new VertexAttribute[MAX_VERTEX_ATTRIBUTES];

    /// <summary>
    /// Gets the number of active vertex buffer bindings.
    /// </summary>
    /// <returns>The count of bindings with non-zero stride.</returns>
    public readonly uint32_t BindingCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_BINDINGS; i++)
        {
            if (Bindings[i].Stride == 0)
                return i;
        }
        return MAX_VERTEX_BINDINGS;
    }

    /// <summary>
    /// Gets the number of active vertex attributes.
    /// </summary>
    /// <returns>The count of attributes with valid (non-Invalid) format.</returns>
    public readonly uint32_t AttributeCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_ATTRIBUTES; i++)
        {
            if (Attributes[i].Format == VertexFormat.Invalid)
                return i;
        }
        return MAX_VERTEX_ATTRIBUTES;
    }

    /// <summary>
    /// Calculates the total size in bytes of a single vertex based on all attributes.
    /// </summary>
    /// <returns>The total vertex size in bytes.</returns>
    /// <remarks>
    /// This method assumes attributes are tightly packed in order. If attributes have gaps or
    /// are not sequential, an assertion will fail.
    /// </remarks>
    public readonly uint32_t GetVertexSize()
    {
        uint32_t vertexSize = 0;
        for (uint32_t i = 0; i < MAX_VERTEX_ATTRIBUTES && Attributes[i].Format != VertexFormat.Invalid; i++)
        {
            HxDebug.Assert(Attributes[i].Offset == vertexSize, "Unsupported vertex attributes format");
            vertexSize += Attributes[i].Format.GetVertexFormatSize();
        }
        return vertexSize;
    }

    /// <summary>
    /// A predefined null/empty vertex input configuration.
    /// </summary>
    public static readonly VertexInput Null = new();
}
