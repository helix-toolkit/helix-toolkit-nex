namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Defines the type of shader data being provided to the shader module.
/// </summary>
public enum ShaderDataType
{
    /// <summary>
    /// Automatically detect the shader data type based on the content.
    /// </summary>
    Auto, // automatically detect the type based on the data

    /// <summary>
    /// SPIR-V binary format (pre-compiled shader bytecode).
    /// </summary>
    Spirv,

    /// <summary>
    /// GLSL source code format (will be compiled to SPIR-V).
    /// </summary>
    Glsl
}

/// <summary>
/// Represents a preprocessor define for shader compilation.
/// </summary>
/// <param name="name">The name of the define.</param>
/// <param name="value">Optional value for the define. If null, the define has no value.</param>
public struct ShaderDefine(string name, string? value = null)
{
    /// <summary>
    /// The name of the preprocessor define.
    /// </summary>
    public string Name = name;

    /// <summary>
    /// The value of the preprocessor define, or null if no value is specified.
    /// </summary>
    public string? Value = value;

    /// <summary>
    /// Returns a string representation of the shader define in #define format.
    /// </summary>
    /// <returns>A string like "#define NAME VALUE" or "#define NAME" if no value.</returns>
    public override readonly string ToString()
    {
        return $"#define {Name} {Value ?? ""}";
    }
}

/// <summary>
/// Describes the properties required to create a shader module.
/// </summary>
public struct ShaderModuleDesc()
{
    /// <summary>
    /// The shader stage this module represents (e.g., Vertex, Fragment, Compute).
    /// </summary>
    public ShaderStage Stage;

    /// <summary>
    /// The type of shader data provided. Defaults to <see cref="ShaderDataType.Auto"/>.
    /// </summary>
    public ShaderDataType DataType = ShaderDataType.Auto; // default is SPIR-V

    /// <summary>
    /// Pointer to the shader data (either SPIR-V bytecode or GLSL source code).
    /// </summary>
    public nint Data;

    /// <summary>
    /// Size of the shader data in bytes.
    /// </summary>
    public size_t DataSize;

    /// <summary>
    /// Array of preprocessor defines to apply during shader compilation.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="DataType"/> is <see cref="ShaderDataType.Glsl"/> or <see cref="ShaderDataType.Auto"/> with GLSL source.
    /// </remarks>
    public ShaderDefine[] Defines = [];

    /// <summary>
    /// Optional debug name for the shader module, used in debugging and profiling tools.
    /// </summary>
    public string DebugName = string.Empty;
}