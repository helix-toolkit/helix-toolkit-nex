using System.Text;
using System.Text.RegularExpressions;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Configuration options for shader building
/// </summary>
public class ShaderBuildOptions
{
    /// <summary>
    /// Whether to automatically include the standard header for the shader stage
    /// </summary>
    public bool IncludeStandardHeader { get; set; } = true;

    /// <summary>
    /// Whether to automatically include PBR functions
    /// </summary>
    public bool IncludePBRFunctions { get; set; } = false;

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
    public string DefaultVersion { set; get; } = "#version 460";
}

/// <summary>
/// Result of a shader build operation
/// </summary>
public class ShaderBuildResult
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
public class ShaderBuilder
{
    private readonly ShaderStage _stage;
    private readonly ShaderBuildOptions _options;
    private readonly HashSet<string> _processedIncludes = new();
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

            result.Source = processedSource;
            result.Warnings = new List<string>(_warnings);
            result.IncludedFiles = new List<string>(_processedIncludes);

            return result;
        }
        catch (Exception ex)
        {
            return new ShaderBuildResult
            {
                Success = false,
                Errors = new List<string> { $"Shader build failed: {ex.Message}" },
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

        // Include standard header if requested
        if (_options.IncludeStandardHeader)
        {
            builder.AppendLine("// Standard Header");
            string header = GlslHeaders.GetShaderHeader(_stage);
            builder.AppendLine(header);
            builder.AppendLine();
            _processedIncludes.Add($"StandardHeader_{_stage}");
        }

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

        // Include PBR functions if requested
        if (_options.IncludePBRFunctions)
        {
            builder.AppendLine("// PBR Functions");
            string pbrFunctions = GlslHeaders.GetGlslShaderPBRFunction();
            builder.AppendLine(pbrFunctions);
            builder.AppendLine();
            _processedIncludes.Add("PBRFunctions.glsl");
        }

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
                string includePath = match.Groups[1].Value;

                // Check if already processed (prevent infinite loops)
                if (_processedIncludes.Contains(includePath))
                {
                    builder.AppendLine($"// Already included: {includePath}");
                    continue;
                }

                _processedIncludes.Add(includePath);

                // Try to load the include file
                string? includeContent = LoadIncludeFile(includePath);
                if (includeContent != null)
                {
                    builder.AppendLine($"// Begin include: {includePath}");
                    builder.AppendLine(ProcessIncludes(includeContent)); // Recursive processing
                    builder.AppendLine($"// End include: {includePath}");
                }
                else
                {
                    _errors.Add($"Failed to load include file: {includePath}");
                    builder.AppendLine($"// ERROR: Failed to include: {includePath}");
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
        try
        {
            // Try to load from embedded resources
            var assembly = typeof(GlslHeaders).Assembly;
            var assemblyName =
                assembly.GetName().Name
                ?? throw new InvalidOperationException("Assembly name cannot be null.");

            // Handle special includes
            if (includePath == "PBRFunctions.glsl")
            {
                return GlslHeaders.GetGlslShaderPBRFunction();
            }

            // Try to load from Headers directory
            string resourceName = $"{assemblyName}.Headers.{includePath}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

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
}
