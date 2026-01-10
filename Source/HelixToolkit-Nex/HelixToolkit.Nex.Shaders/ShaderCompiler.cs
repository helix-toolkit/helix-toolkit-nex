using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// High-level API for building shaders with automatic header inclusion
/// </summary>
public class ShaderCompiler
{
    private static readonly Lazy<ShaderCache> _globalCache = new(() =>
        new ShaderCache(maxEntries: 200)
    );

    /// <summary>
    /// Global shader cache shared across all compiler instances
    /// </summary>
    public static ShaderCache GlobalCache => _globalCache.Value;

    private readonly ShaderCache? _localCache;
    private readonly bool _useCache;

    /// <summary>
    /// Creates a new shader compiler
    /// </summary>
    /// <param name="useGlobalCache">Whether to use the global cache</param>
    /// <param name="localCache">Optional local cache instance</param>
    public ShaderCompiler(bool useGlobalCache = true, ShaderCache? localCache = null)
    {
        _useCache = useGlobalCache || localCache != null;
        _localCache = localCache;
    }

    /// <summary>
    /// Compile a shader with automatic header inclusion
    /// </summary>
    /// <param name="stage">Shader stage</param>
    /// <param name="source">User shader source code</param>
    /// <param name="options">Build options (null = defaults with PBR functions included)</param>
    /// <returns>Build result with processed source</returns>
    public ShaderBuildResult Compile(
        ShaderStage stage,
        string source,
        ShaderBuildOptions? options = null
    )
    {
        // Use default options if not provided
        options ??= new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
        };

        // Check cache first
        if (_useCache)
        {
            var cacheKey = ShaderCache.GenerateCacheKey(source, options, stage);
            var cache = _localCache ?? GlobalCache;

            if (cache.TryGet(cacheKey, out var cachedEntry) && cachedEntry != null)
            {
                return new ShaderBuildResult
                {
                    Success = true,
                    Source = cachedEntry.ProcessedSource,
                    Warnings = new List<string> { "Using cached shader" },
                };
            }

            // Build the shader
            var builder = new ShaderBuilder(stage, options);
            var result = builder.Build(source);

            // Cache the result if successful
            if (result.Success && result.Source != null)
            {
                var sourceHash = ComputeHash(source);
                cache.Set(cacheKey, result.Source, sourceHash);
            }

            return result;
        }
        else
        {
            // Build without caching
            var builder = new ShaderBuilder(stage, options);
            return builder.Build(source);
        }
    }

    /// <summary>
    /// Compile a fragment shader with PBR support
    /// </summary>
    public ShaderBuildResult CompileFragmentShaderWithPBR(
        string source,
        ShaderBuildOptions? options = null
    )
    {
        options ??= new ShaderBuildOptions();
        options.IncludeStandardHeader = true;
        options.IncludePBRFunctions = true;

        return Compile(ShaderStage.Fragment, source, options);
    }

    /// <summary>
    /// Compile a vertex shader
    /// </summary>
    public ShaderBuildResult CompileVertexShader(string source, ShaderBuildOptions? options = null)
    {
        return Compile(ShaderStage.Vertex, source, options);
    }

    /// <summary>
    /// Compile a compute shader
    /// </summary>
    public ShaderBuildResult CompileComputeShader(string source, ShaderBuildOptions? options = null)
    {
        return Compile(ShaderStage.Compute, source, options);
    }

    /// <summary>
    /// Compile a geometry shader
    /// </summary>
    public ShaderBuildResult CompileGeometryShader(
        string source,
        ShaderBuildOptions? options = null
    )
    {
        return Compile(ShaderStage.Geometry, source, options);
    }

    /// <summary>
    /// Compile a tessellation control shader
    /// </summary>
    public ShaderBuildResult CompileTessControlShader(
        string source,
        ShaderBuildOptions? options = null
    )
    {
        return Compile(ShaderStage.TessellationControl, source, options);
    }

    /// <summary>
    /// Compile a tessellation evaluation shader
    /// </summary>
    public ShaderBuildResult CompileTessEvalShader(
        string source,
        ShaderBuildOptions? options = null
    )
    {
        return Compile(ShaderStage.TessellationEvaluation, source, options);
    }

    /// <summary>
    /// Compile a mesh shader
    /// </summary>
    public ShaderBuildResult CompileMeshShader(string source, ShaderBuildOptions? options = null)
    {
        return Compile(ShaderStage.Mesh, source, options);
    }

    /// <summary>
    /// Compile a task shader
    /// </summary>
    public ShaderBuildResult CompileTaskShader(string source, ShaderBuildOptions? options = null)
    {
        return Compile(ShaderStage.Task, source, options);
    }

    /// <summary>
    /// Clear the cache
    /// </summary>
    public void ClearCache()
    {
        if (_localCache != null)
        {
            _localCache.Clear();
        }
        else
        {
            GlobalCache.Clear();
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetCacheStatistics()
    {
        return (_localCache ?? GlobalCache).GetStatistics();
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

/// <summary>
/// Builder pattern for shader compilation with fluent API
/// </summary>
public class ShaderCompilationBuilder
{
    private ShaderStage _stage = ShaderStage.Fragment;
    private string _source = string.Empty;
    private readonly ShaderBuildOptions _options = new();
    private bool _useGlobalCache = true;
    private ShaderCache? _localCache;

    /// <summary>
    /// Set the shader stage
    /// </summary>
    public ShaderCompilationBuilder WithStage(ShaderStage stage)
    {
        _stage = stage;
        return this;
    }

    /// <summary>
    /// Set the shader source code
    /// </summary>
    public ShaderCompilationBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    /// <summary>
    /// Include standard header for the shader stage
    /// </summary>
    public ShaderCompilationBuilder WithStandardHeader(bool include = true)
    {
        _options.IncludeStandardHeader = include;
        return this;
    }

    /// <summary>
    /// Include PBR functions
    /// </summary>
    public ShaderCompilationBuilder WithPBRFunctions(bool include = true)
    {
        _options.IncludePBRFunctions = include;
        return this;
    }

    /// <summary>
    /// Add a preprocessor define
    /// </summary>
    public ShaderCompilationBuilder WithDefine(string name, string? value = null)
    {
        _options.Defines[name] = value ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Strip comments from the shader
    /// </summary>
    public ShaderCompilationBuilder StripComments(bool strip = true)
    {
        _options.StripComments = strip;
        return this;
    }

    /// <summary>
    /// Enable debug mode
    /// </summary>
    public ShaderCompilationBuilder WithDebug(bool enable = true)
    {
        _options.EnableDebug = enable;
        return this;
    }

    /// <summary>
    /// Use a local cache instead of the global cache
    /// </summary>
    public ShaderCompilationBuilder WithLocalCache(ShaderCache cache)
    {
        _localCache = cache;
        _useGlobalCache = false;
        return this;
    }

    /// <summary>
    /// Disable caching
    /// </summary>
    public ShaderCompilationBuilder WithoutCache()
    {
        _useGlobalCache = false;
        _localCache = null;
        return this;
    }

    /// <summary>
    /// Build the shader
    /// </summary>
    public ShaderBuildResult Build()
    {
        var compiler = new ShaderCompiler(_useGlobalCache, _localCache);
        return compiler.Compile(_stage, _source, _options);
    }
}
