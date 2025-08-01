namespace HelixToolkit.Nex.Graphics;

[Flags]
public enum BufferUsageBits : uint8_t
{
    None = 0,
    Index = 1 << 0,
    Vertex = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Indirect = 1 << 4,
    // ray tracing
    ShaderBindingTable = 1 << 5,
    AccelStructBuildInputReadOnly = 1 << 6,
    AccelStructStorage = 1 << 7
}

public struct BufferDesc(BufferUsageBits usage, StorageType storage, nint data, size_t dataSize, string? debugName = null)
{
    public BufferUsageBits Usage = usage;
    public StorageType Storage = storage;
    public nint Data = data;
    public size_t DataSize = dataSize;
    public string DebugName = debugName ?? string.Empty;
}
