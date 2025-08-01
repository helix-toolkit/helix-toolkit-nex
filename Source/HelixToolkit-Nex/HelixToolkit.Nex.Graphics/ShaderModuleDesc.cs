namespace HelixToolkit.Nex.Graphics;

public enum ShaderDataType
{
    Auto, // automatically detect the type based on the data
    Spirv,
    Glsl
}

public struct ShaderDefine(string name, string? value = null)
{
    public string Name = name;
    public string? Value = value;

    public override readonly string ToString()
    {
        return $"#define {Name} {Value ?? ""}";
    }
}

public struct ShaderModuleDesc()
{
    public ShaderStage Stage;
    public ShaderDataType DataType = ShaderDataType.Auto; // default is SPIR-V
    public nint Data;
    public size_t DataSize;

    public ShaderDefine[] Defines = [];
    public string DebugName = string.Empty;
}