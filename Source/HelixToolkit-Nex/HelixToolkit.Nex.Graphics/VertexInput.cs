namespace HelixToolkit.Nex.Graphics;

public struct VertexAttribute()
{
    public uint32_t Location; // a buffer which contains this attribute stream
    public uint32_t Binding;
    public VertexFormat Format; // per-element format
    public size_t Offset; // an offset where the first element of this attribute stream starts
}

public struct VertexInputBinding()
{
    public uint32_t Stride;
}

public struct VertexInput()
{
    public const uint32_t MAX_VERTEX_BINDINGS = 16;
    public const uint32_t MAX_VERTEX_ATTRIBUTES = 16;

    public readonly VertexInputBinding[] Bindings = new VertexInputBinding[MAX_VERTEX_BINDINGS];

    public readonly VertexAttribute[] Attributes = new VertexAttribute[MAX_VERTEX_ATTRIBUTES];

    public readonly uint32_t BindingCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_BINDINGS; i++)
        {
            if (Bindings[i].Stride == 0)
                return i;
        }
        return MAX_VERTEX_BINDINGS;
    }

    public readonly uint32_t AttributeCount()
    {
        for (uint32_t i = 0; i < MAX_VERTEX_ATTRIBUTES; i++)
        {
            if (Attributes[i].Format == VertexFormat.Invalid)
                return i;
        }
        return MAX_VERTEX_ATTRIBUTES;
    }

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

    public static readonly VertexInput Null = new();
}
