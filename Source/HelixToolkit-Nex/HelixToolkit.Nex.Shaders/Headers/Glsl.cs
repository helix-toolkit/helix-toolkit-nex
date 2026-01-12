using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Shaders;

public static class GlslHeaders
{
    public const string DEFAULT_VERSION = "#version 460";
    private const string TASK_MESH_SHADER = "HeaderTask.glsl";

    private const string VERTEX_COMPUTE_TESS_SHADER = "HeaderVertex.glsl";

    private const string FRAGMENT_SHADER = "HeaderFrag.glsl";

    private const string PBR_FUNCTIONS = "PBRFunctions.glsl";

    public static string GetShaderHeader(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Task or ShaderStage.Mesh => GetGlslShaderHeader(TASK_MESH_SHADER),
            ShaderStage.Vertex
            or ShaderStage.Geometry
            or ShaderStage.Compute
            or ShaderStage.TessellationControl
            or ShaderStage.TessellationEvaluation => GetGlslShaderHeader(
                VERTEX_COMPUTE_TESS_SHADER
            ),
            ShaderStage.Fragment => GetGlslShaderHeader(FRAGMENT_SHADER),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                $"Unsupported shader stage: {stage}"
            ),
        };
    }

    private static string GetGlslShaderHeader(string shaderName)
    {
        var assembly = typeof(GlslHeaders).Assembly;
        var assemblyName =
            assembly.GetName().Name
            ?? throw new InvalidOperationException("Assembly name cannot be null.");
        using var stream =
            assembly.GetManifestResourceStream($"{assemblyName}.Headers.{shaderName}")
            ?? throw new FileNotFoundException(
                $"Shader file '{shaderName}' not found in embedded resources."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string GetGlslShaderPBRFunction()
    {
        return GetGlslShaderHeader(PBR_FUNCTIONS);
    }

    /// <summary>
    /// Create a new shader compiler instance
    /// </summary>
    public static ShaderCompiler CreateCompiler(bool useGlobalCache = true)
    {
        return new ShaderCompiler(useGlobalCache);
    }

    /// <summary>
    /// Create a fluent shader compilation builder
    /// </summary>
    public static ShaderCompilationBuilder BuildShader()
    {
        return new ShaderCompilationBuilder();
    }
}
