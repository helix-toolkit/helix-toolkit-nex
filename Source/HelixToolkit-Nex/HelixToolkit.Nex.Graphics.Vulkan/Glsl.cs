using Glslang.NET;

namespace HelixToolkit.Nex.Graphics.Vulkan;

public static class GlslHeaders
{
    const string TASK_MESH_SHADER = "HeaderTask.glsl";

    const string VERTEX_COMPUTE_TESS_SHADER = "HeaderVertex.glsl";

    const string FRAGMENT_SHADER = "HeaderFrag.glsl";

    public static string GetShaderHeader(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Task or ShaderStage.Mesh => GetGlslShaderHeader(TASK_MESH_SHADER),
            ShaderStage.Vertex or ShaderStage.Compute or ShaderStage.TessellationControl or ShaderStage.TessellationEvaluation => GetGlslShaderHeader(VERTEX_COMPUTE_TESS_SHADER),
            ShaderStage.Fragment => GetGlslShaderHeader(FRAGMENT_SHADER),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), $"Unsupported shader stage: {stage}"),
        };
    }

    static string GetGlslShaderHeader(string shaderName)
    {
        var assembly = typeof(GlslHeaders).Assembly;
        var assemblyName = assembly.GetName().Name ?? throw new InvalidOperationException("Assembly name cannot be null.");
        using var stream = assembly.GetManifestResourceStream($"{assemblyName}.Shaders.{shaderName}") ?? throw new FileNotFoundException($"Shader file '{shaderName}' not found in embedded resources.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}