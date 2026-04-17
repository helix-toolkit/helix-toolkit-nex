using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Configuration options for shader building
/// </summary>
public sealed class ShaderBuildOptions
{
    /// <summary>
    /// Custom include directories (not yet implemented for embedded resources)
    /// </summary>
    public List<string> IncludeDirectories { get; set; } = new();

    /// <summary>
    /// Custom defines to add to the shader
    /// </summary>
    public Dictionary<string, string> Defines { get; set; } = new();

    /// <summary>
    /// Whether to strip comments from the shader
    /// </summary>
    public bool StripComments { get; set; } = false;

    /// <summary>
    /// Whether to enable debug information
    /// </summary>
    public bool EnableDebug { get; set; } = false;

    /// <summary>
    /// Gets or sets the default shader version string used when none is specified explicitly.
    /// </summary>
    public string DefaultVersion { set; get; } = GlslHeaders.DEFAULT_VERSION;

    /// <summary>
    /// Custom include source provider.
    /// Return null if the include file is not found.
    /// </summary>
    public Func<string, string?>? IncludeProvider { get; set; }
}

/// <summary>
/// Result of a shader build operation
/// </summary>
public sealed class ShaderBuildResult
{
    /// <summary>
    /// Whether the build was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The processed shader source code
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Any errors that occurred during building
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Any warnings generated during building
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// List of included files
    /// </summary>
    public List<string> IncludedFiles { get; set; } = new();
}

/// <summary>
/// Shader builder that processes shader source code and automatically includes necessary headers
/// </summary>
internal sealed class ShaderBuilder
{
    private static readonly ILogger _logger = LogManager.Create<ShaderBuilder>();
    private readonly ShaderStage _stage;
    private readonly ShaderBuildOptions _options;
    private readonly HashSet<string> _processedIncludes = new();
    private readonly Stack<string> _includeStack = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    // Regex patterns for preprocessing
    private static readonly Regex IncludeDirectiveRegex = new(
        @"^\s*#\s*include\s+[""<]([^"">]+)["">]",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex VersionDirectiveRegex = new(
        @"^\s*#\s*version\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex SingleLineCommentRegex = new(
        @"//.*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex MultiLineCommentRegex = new(
        @"/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex UnrecognizedPlaceholderRegex = new(
        @"LIMITS_[A-Z_]+",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Creates a new shader builder for the specified stage
    /// </summary>
    public ShaderBuilder(ShaderStage stage, ShaderBuildOptions? options = null)
    {
        _stage = stage;
        _options = options ?? new ShaderBuildOptions();
    }

    /// <summary>
    /// Build a shader from source code
    /// </summary>
    public ShaderBuildResult Build(string userShaderSource)
    {
        _processedIncludes.Clear();
        _includeStack.Clear();
        _errors.Clear();
        _warnings.Clear();

        try
        {
            var result = new ShaderBuildResult { Success = true };

            // Process the shader source
            string processedSource = ProcessShader(userShaderSource);

            if (_errors.Count > 0)
            {
                result.Success = false;
                result.Errors = new List<string>(_errors);
            }

            processedSource = processedSource
                .Replace("{{", "{")
                .Replace("}}", "}")
                .Replace("@code_gen", "");

            result.Source = processedSource;
            result.Warnings = [.. _warnings];
            result.IncludedFiles = [.. _processedIncludes];

            return result;
        }
        catch (Exception ex)
        {
            return new ShaderBuildResult
            {
                Success = false,
                Errors = [$"Shader build failed: {ex.Message}"],
            };
        }
    }

    private string ProcessShader(string source)
    {
        var builder = new StringBuilder();

        // Extract version directive if present
        string? versionDirective = null;
        var versionMatch = VersionDirectiveRegex.Match(source);
        if (versionMatch.Success)
        {
            versionDirective = versionMatch.Value.Trim();
            // Remove version directive from source
            source = VersionDirectiveRegex.Replace(source, string.Empty);
        }
        else
        {
            // Default to version 460 if not specified
            versionDirective = _options.DefaultVersion;
            _warnings.Add("No #version directive found, using default: #version 460");
        }

        // Add version directive first
        builder.AppendLine(versionDirective);
        builder.AppendLine();

        // Add custom defines
        if (_options.Defines.Count > 0)
        {
            builder.AppendLine("// Custom Defines");
            foreach (var define in _options.Defines)
            {
                if (string.IsNullOrEmpty(define.Value))
                {
                    builder.AppendLine($"#define {define.Key}");
                }
                else
                {
                    builder.AppendLine($"#define {define.Key} {define.Value}");
                }
            }
            builder.AppendLine();
        }

        // Replace LIMITS_ placeholders with derived constants from LimitsShaderConstants
        source = ReplaceLimitsPlaceholders(source);

        // Process user shader (handle includes, strip comments, etc.)
        string processedUserShader = ProcessIncludes(source);

        if (_options.StripComments)
        {
            processedUserShader = StripComments(processedUserShader);
        }

        builder.AppendLine("// User Shader Code");
        builder.AppendLine(processedUserShader);

        return builder.ToString();
    }

    private string ProcessIncludes(string source)
    {
        var lines = source.Split('\n');
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var match = IncludeDirectiveRegex.Match(line);
            if (match.Success)
            {
                string includePath = match.Groups[1].Value.Trim();
                string includeKey = includePath;

                // Normalize path for default embedded resource loader to ensure correct deduplication
                if (_options.IncludeProvider == null)
                {
                    includeKey = includePath.Replace("../", "").Replace("./", "").Replace("/", ".");
                }

                // Check for circular dependency
                if (_includeStack.Contains(includeKey))
                {
                    _errors.Add(
                        $"Circular dependency detected: {string.Join(" -> ", _includeStack.Reverse())} -> {includePath}"
                    );
                    builder.AppendLine($"// ERROR: Circular dependency detected: {includePath}");
                    continue;
                }

                // Check if already processed (prevent infinite loops / double inclusion)
                if (_processedIncludes.Contains(includeKey))
                {
                    builder.AppendLine($"// Already included: {includePath}");
                    continue;
                }

                _processedIncludes.Add(includeKey);
                _includeStack.Push(includeKey);

                try
                {
                    // Try to load the include file
                    // Use the original includePath for loading, as the loader might need the raw path
                    // (though for default loader, they are compatible)
                    string? includeContent = LoadIncludeFile(includePath);
                    if (includeContent != null)
                    {
                        // Replace LIMITS_ placeholders in include content
                        includeContent = ReplaceLimitsPlaceholders(includeContent);

                        builder.AppendLine($"// Begin include: {includePath}");
                        builder.AppendLine(ProcessIncludes(includeContent)); // Recursive processing
                        builder.AppendLine($"// End include: {includePath}");
                    }
                    else
                    {
                        _errors.Add($"Failed to load include file: {includePath}");
                        _logger.LogError($"Failed to load include file: {includePath}");
                        builder.AppendLine($"// ERROR: Failed to include: {includePath}");
                    }
                }
                finally
                {
                    _includeStack.Pop();
                }
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private string? LoadIncludeFile(string includePath)
    {
        if (_options.IncludeProvider != null)
        {
            var content = _options.IncludeProvider(includePath);
            if (content != null)
            {
                return content;
            }
        }

        try
        {
            // Try to load from embedded resources
            var assembly = typeof(GlslHeaders).Assembly;
            var assemblyName =
                assembly.GetName().Name
                ?? throw new InvalidOperationException("Assembly name cannot be null.");

            // Normalize path separators to dots for resource lookup
            // e.g. "Headers/HeaderFrag.glsl" -> "Headers.HeaderFrag.glsl"
            // Also handle relative paths like "../Headers/HeaderFrag.glsl" by cleaning them up

            // Simple path cleaning: remove "../" and "./"
            string cleanPath = includePath.Replace("../", "").Replace("./", "").Replace("/", ".");

            // Try to load from Headers directory
            string includeNamespace = "HelixToolkit.Nex.Shaders";
            string resourceName = $"{includeNamespace}.{cleanPath}";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

            // If not found, try varying known namespaces or locations if needed
            // E.g. try adding .glsl if missing or check root namespace

            // If not found in embedded resources, return null
            return null;
        }
        catch (Exception ex)
        {
            _errors.Add($"Error loading include '{includePath}': {ex.Message}");
            return null;
        }
    }

    private string StripComments(string source)
    {
        // Remove single-line comments
        source = SingleLineCommentRegex.Replace(source, string.Empty);

        // Remove multi-line comments
        source = MultiLineCommentRegex.Replace(source, string.Empty);

        return source;
    }

    private string ReplaceLimitsPlaceholders(string source)
    {
        var placeholders = LimitsShaderConstants.GetGlslPlaceholders();
        foreach (var (token, value) in placeholders)
        {
            source = source.Replace(token, value);
        }

        WarnUnrecognizedPlaceholders(source);
        return source;
    }

    private void WarnUnrecognizedPlaceholders(string source)
    {
        var knownTokens = LimitsShaderConstants.GetGlslPlaceholders().Keys;
        var matches = UnrecognizedPlaceholderRegex.Matches(source);
        foreach (Match match in matches)
        {
            string token = match.Value;
            if (!knownTokens.Contains(token))
            {
                _warnings.Add($"Unrecognized LIMITS_ placeholder token: {token}");
            }
        }
    }
}
