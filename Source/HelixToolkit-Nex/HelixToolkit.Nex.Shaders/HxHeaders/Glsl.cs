using System.Text;

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

    private const string HEADER_DEFINES = "HeaderDefines.glsl";

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

    private static string ResolveIncludes(string src, int depth = 0)
    {
        if (depth > 16)
        {
            return src;
        }

        var lines = src.Replace("\r\n", "\n").Split('\n');
        StringBuilder sb = new();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include"))
            {
                var start = trimmed.IndexOfAny(['\"', '<']);
                var end = trimmed.LastIndexOfAny(['\"', '>']);
                if (start >= 0 && end > start)
                {
                    var includeName = trimmed.Substring(start + 1, end - start - 1);
                    includeName = includeName.Replace('\\', '.').Replace('/', '.');
                    var includeContent = GlslHeaders.GetGlslShaderHeader(includeName);
                    if (!string.IsNullOrEmpty(includeContent))
                    {
                        sb.AppendLine(ResolveIncludes(includeContent, depth + 1));
                        continue;
                    }
                }
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    public static string GetGlslShaderHeader(string shaderName)
    {
        shaderName = shaderName.StartsWith("HxHeaders.") ? shaderName : $"HxHeaders.{shaderName}";
        var header = GlslUtils.GetEmbeddedGlslShader(shaderName);
        if (header.Contains("#include") && header.Contains(HEADER_DEFINES))
        {
            header = ResolveIncludes(header);
        }
        return header;
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
