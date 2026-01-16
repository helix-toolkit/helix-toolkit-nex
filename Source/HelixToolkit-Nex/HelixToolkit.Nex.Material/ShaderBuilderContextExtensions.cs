using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Extension methods for integrating shader building with graphics context
/// </summary>
public static class ShaderBuilderContextExtensions
{
    /// <summary>
    /// Builds and compiles a shader module from GLSL source in one step
    /// </summary>
    /// <param name="context">The graphics context</param>
    /// <param name="stage">Shader stage</param>
    /// <param name="source">GLSL source code</param>
    /// <param name="options">Build options (null = defaults with PBR)</param>
    /// <param name="debugName">Debug name for the shader module</param>
    /// <returns>Tuple of (build result, shader module resource)</returns>
    public static (
        ShaderBuildResult BuildResult,
        ShaderModuleResource Module
    ) BuildAndCompileShader(
        this IContext context,
        ShaderStage stage,
        string source,
        ShaderBuildOptions? options = null,
        string? debugName = null
    )
    {
        // Build the shader
        var compiler = new ShaderCompiler();
        var buildResult = compiler.Compile(stage, source, options);

        if (!buildResult.Success)
        {
            return (buildResult, ShaderModuleResource.Null);
        }

        // Compile to SPIR-V via context
        var module = context.CreateShaderModuleGlsl(buildResult.Source!, stage, debugName);

        return (buildResult, module);
    }

    /// <summary>
    /// Builds and compiles a fragment shader with PBR functions
    /// </summary>
    public static (
        ShaderBuildResult BuildResult,
        ShaderModuleResource Module
    ) BuildAndCompileFragmentShaderWithPBR(
        this IContext context,
        string source,
        ShaderBuildOptions? options = null,
        string? debugName = null
    )
    {
        options ??= new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
        };

        options.IncludeStandardHeader = true;
        options.IncludePBRFunctions = true;

        return context.BuildAndCompileShader(ShaderStage.Fragment, source, options, debugName);
    }

    /// <summary>
    /// Builds and compiles a vertex shader
    /// </summary>
    public static (
        ShaderBuildResult BuildResult,
        ShaderModuleResource Module
    ) BuildAndCompileVertexShader(
        this IContext context,
        string source,
        ShaderBuildOptions? options = null,
        string? debugName = null
    )
    {
        return context.BuildAndCompileShader(ShaderStage.Vertex, source, options, debugName);
    }

    /// <summary>
    /// Fluent builder for building and compiling shaders with context
    /// </summary>
    public static ShaderCompilationWithContextBuilder BuildAndCompileShader(this IContext context)
    {
        return new ShaderCompilationWithContextBuilder(context);
    }
}

/// <summary>
/// Fluent builder for shader compilation with context integration
/// </summary>
public class ShaderCompilationWithContextBuilder
{
    private readonly IContext _context;
    private ShaderStage _stage = ShaderStage.Fragment;
    private string _source = string.Empty;
    private readonly ShaderBuildOptions _options = new();
    private string? _debugName;

    internal ShaderCompilationWithContextBuilder(IContext context)
    {
        _context = context;
    }

    public ShaderCompilationWithContextBuilder WithStage(ShaderStage stage)
    {
        _stage = stage;
        return this;
    }

    public ShaderCompilationWithContextBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    public ShaderCompilationWithContextBuilder WithStandardHeader(bool include = true)
    {
        _options.IncludeStandardHeader = include;
        return this;
    }

    public ShaderCompilationWithContextBuilder WithPBRFunctions(bool include = true)
    {
        _options.IncludePBRFunctions = include;
        return this;
    }

    public ShaderCompilationWithContextBuilder WithDefine(string name, string? value = null)
    {
        _options.Defines[name] = value ?? string.Empty;
        return this;
    }

    public ShaderCompilationWithContextBuilder WithDebugName(string debugName)
    {
        _debugName = debugName;
        return this;
    }

    public (ShaderBuildResult BuildResult, ShaderModuleResource Module) Build()
    {
        return _context.BuildAndCompileShader(_stage, _source, _options, _debugName);
    }
}
