namespace HelixToolkit.Nex.Shaders;

public static class GlslUtils
{
    public static string GetEmbeddedGlslShader(string shaderPath)
    {
        if (string.IsNullOrEmpty(shaderPath))
        {
            throw new ArgumentException("Shader path cannot be null or empty.", nameof(shaderPath));
        }
        shaderPath = shaderPath.Replace('\\', '.').Replace('/', '.');
        if (!shaderPath.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase))
        {
            shaderPath += ".glsl";
        }
        var assembly = typeof(GlslHeaders).Assembly;
        var assemblyName =
            assembly.GetName().Name
            ?? throw new InvalidOperationException("Assembly name cannot be null.");
        using var stream =
            assembly.GetManifestResourceStream($"{assemblyName}.{shaderPath}")
            ?? throw new FileNotFoundException(
                $"Shader file '{shaderPath}' not found in embedded resources."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public static class GlslHeaders
{
    public const string DEFAULT_VERSION = "#version 460";
    private const string TASK_MESH_SHADER = "HeaderTask.glsl";

    private const string VERTEX_TESS_SHADER = "HeaderVertex.glsl";

    private const string COMPUTE_SHADER = "HeaderCompute.glsl";

    private const string FRAGMENT_SHADER = "HeaderFrag.glsl";

    private const string PBR_FUNCTIONS = "PBRFunctions.glsl";

    public static string GetShaderHeader(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Task or ShaderStage.Mesh => GetGlslShaderHeader(TASK_MESH_SHADER),
            ShaderStage.Vertex
            or ShaderStage.Geometry
            or ShaderStage.TessellationControl
            or ShaderStage.TessellationEvaluation => GetGlslShaderHeader(VERTEX_TESS_SHADER),
            ShaderStage.Fragment => GetGlslShaderHeader(FRAGMENT_SHADER),
            ShaderStage.Compute => GetGlslShaderHeader(COMPUTE_SHADER),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                $"Unsupported shader stage: {stage}"
            ),
        };
    }

    public static string GetGlslShaderHeader(string shaderName)
    {
        return GlslUtils.GetEmbeddedGlslShader($"HxHeaders.{shaderName}");
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
