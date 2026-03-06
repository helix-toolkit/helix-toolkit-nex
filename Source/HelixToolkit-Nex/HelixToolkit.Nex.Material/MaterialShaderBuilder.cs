using System.Text;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Builds shader code for materials by generating appropriate GLSL based on material properties.
/// Integrates the Material system with the Shader library for easy custom material creation.
/// Supports bindless vertex buffers, light buffers, and Forward+ rendering.
/// </summary>
public class MaterialShaderBuilder
{
    private readonly ShaderCompiler _compiler;
    private readonly Dictionary<string, string> _defines = [];
    private readonly List<string> _customCode = [];

    // New fields for template injection
    private readonly Dictionary<string, string> _templateReplacements = [];

    private ForwardPlusLightCulling.Config _forwardPlusConfig = ForwardPlusLightCulling
        .Config
        .Default;
    private string? _customFragmentMain;

    // Material type selection
    private uint? _materialTypeId;
    private bool _buildUberShader = true;

    public MaterialShaderBuilder()
    {
        _compiler = GlslHeaders.CreateCompiler();
    }

    /// <summary>
    /// Enable Forward+ rendering with tile-based light culling.
    /// </summary>
    public MaterialShaderBuilder ConfigForwardPlus(ForwardPlusLightCulling.Config? config = null)
    {
        _forwardPlusConfig = config ?? ForwardPlusLightCulling.Config.Default;
        WithDefine("TILE_SIZE", _forwardPlusConfig.TileSize.ToString());
        WithDefine("MAX_LIGHTS_PER_TILE", _forwardPlusConfig.MaxLightsPerTile.ToString());
        return this;
    }

    // Legacy alias to fix build
    public MaterialShaderBuilder WithForwardPlus(
        bool enable,
        ForwardPlusLightCulling.Config? config = null
    )
    {
        // Ignore 'enable' flag if we assume it's part of config logic or separate feature flag
        // but if enable is true, we configure it.
        if (enable)
        {
            return ConfigForwardPlus(config);
        }
        return this;
    }

    /// <summary>
    /// Add a preprocessor define.
    /// </summary>
    public MaterialShaderBuilder WithDefine(string name, string? value = null)
    {
        _defines[name] = value ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Add custom GLSL code to be included in the shader.
    /// </summary>
    public MaterialShaderBuilder WithCustomCode(string glslCode)
    {
        _customCode.Add(glslCode);
        return this;
    }

    /// <summary>
    /// Set a custom fragment shader main function.
    /// If not set, a default PBR main will be generated.
    /// </summary>
    public MaterialShaderBuilder WithCustomMain(string fragmentMain)
    {
        _customFragmentMain = fragmentMain;
        return this;
    }

    /// <summary>
    /// Set a custom template replacement (e.g. for overriding template functions)
    /// </summary>
    public MaterialShaderBuilder WithTemplateReplacement(string key, string value)
    {
        _templateReplacements[key] = value;
        return this;
    }

    /// <summary>
    /// Select a specific material type by ID.
    /// When specified, only this material type will be compiled (not an uber shader).
    /// </summary>
    /// <param name="typeId">Material type ID from MaterialTypeRegistry.</param>
    public MaterialShaderBuilder WithMaterialType(uint typeId)
    {
        _materialTypeId = typeId;
        _buildUberShader = false;
        return this;
    }

    /// <summary>
    /// Select a specific material type by name.
    /// When specified, only this material type will be compiled (not an uber shader).
    /// </summary>
    /// <param name="typeName">Material type name from MaterialTypeRegistry.</param>
    public MaterialShaderBuilder WithMaterialType(string typeName)
    {
        var typeId = MaterialTypeRegistry.GetTypeId(typeName);
        if (typeId == null)
        {
            throw new ArgumentException(
                $"Material type '{typeName}' is not registered.",
                nameof(typeName)
            );
        }
        return WithMaterialType(typeId.Value);
    }

    /// <summary>
    /// Build an uber shader containing all registered material types.
    /// Material type is selected at runtime via specialization constant.
    /// This is the default mode.
    /// </summary>
    public MaterialShaderBuilder WithUberShader(bool enable = true)
    {
        _buildUberShader = enable;
        if (enable)
        {
            _materialTypeId = null;
        }
        return this;
    }

    /// <summary>
    /// Build the fragment shader for this material.
    /// </summary>
    public ShaderBuildResult BuildFragmentShader()
    {
        var shaderSource = GenerateFragmentShader();

        var options = new ShaderBuildOptions { Defines = _defines };

        return _compiler.Compile(ShaderStage.Fragment, shaderSource, options);
    }

    public ShaderBuildResult BuildVertexShader()
    {
        var shaderSource = GenerateVertexShader();
        var options = new ShaderBuildOptions { Defines = _defines };
        return _compiler.Compile(ShaderStage.Vertex, shaderSource, options);
    }

    /// <summary>
    /// Build a complete material pipeline with vertex and fragment shaders.
    /// </summary>
    public MaterialShaderResult BuildMaterialPipeline(IContext context, string? debugName = null)
    {
        // Build default vertex shader
        var vertexSource = GenerateVertexShader();
        var vertexOptions = new ShaderBuildOptions { Defines = _defines };
        var vertexResult = _compiler.Compile(ShaderStage.Vertex, vertexSource, vertexOptions);

        // Build fragment shader
        var fragmentResult = BuildFragmentShader();

        // Check for errors
        if (!vertexResult.Success || !fragmentResult.Success)
        {
            return new MaterialShaderResult
            {
                Success = false,
                Errors = vertexResult.Errors.Concat(fragmentResult.Errors).ToList(),
                VertexShader = ShaderModuleResource.Null,
                FragmentShader = ShaderModuleResource.Null,
            };
        }

        // Create shader modules
        var vertexModule = context.CreateShaderModuleGlsl(
            vertexResult.Source!,
            ShaderStage.Vertex,
            $"{debugName}_Vertex"
        );

        Debug.Assert(vertexModule.Valid, "VertexProperties shader module creation failed.");

        var fragmentModule = context.CreateShaderModuleGlsl(
            fragmentResult.Source!,
            ShaderStage.Fragment,
            $"{debugName}_Fragment"
        );

        Debug.Assert(fragmentModule.Valid, "Fragment shader module creation failed.");

        return new MaterialShaderResult
        {
            Success = true,
            VertexShader = vertexModule,
            FragmentShader = fragmentModule,
        };
    }

    private string GenerateFragmentShader()
    {
        // Load base template
        string template = LoadTemplate("psPBRTemplate.glsl");

        // Inject custom code
        var sbCustom = new StringBuilder();
        foreach (var code in _customCode)
        {
            sbCustom.AppendLine(code);
            sbCustom.AppendLine();
        }

        // Apply custom code injection
        template = template.Replace("// TEMPLATE_CUSTOM_CODE", sbCustom.ToString());

        // Generate outputColor function based on mode
        if (_buildUberShader)
        {
            // Build uber shader with all registered material types
            template = GenerateUberOutputColorFunction(template);
        }
        else if (_materialTypeId.HasValue)
        {
            // Build single material type
            template = GenerateSingleMaterialOutputColorFunction(template, _materialTypeId.Value);
        }
        else
        {
            // Legacy mode - keep existing outputColor function
            // No changes needed
        }

        // Apply template replacements
        foreach (var replacement in _templateReplacements)
        {
            string startMarker = $"/*{replacement.Key}_START*/";
            string endMarker = $"/*{replacement.Key}_END*/";

            int startIndex = template.IndexOf(startMarker);
            int endIndex = template.IndexOf(endMarker);

            if (startIndex >= 0 && endIndex > startIndex)
            {
                string before = template.Substring(0, startIndex);
                string after = template.Substring(endIndex + endMarker.Length);
                template = before + replacement.Value + after;
            }
            else
            {
                // Fallback: try simple string substitution if it's a direct placeholder
                template = template.Replace(replacement.Key, replacement.Value);
            }
        }

        // Handle custom main function
        if (!string.IsNullOrEmpty(_customFragmentMain))
        {
            // Replace the main function
            string startMarker = "/*TEMPLATE_CUSTOM_MAIN_START*/";
            string endMarker = "/*TEMPLATE_CUSTOM_MAIN_END*/";

            int startIndex = template.IndexOf(startMarker);
            int endIndex = template.IndexOf(endMarker);

            if (startIndex >= 0 && endIndex > startIndex)
            {
                string before = template.Substring(0, startIndex);
                string after = template.Substring(endIndex + endMarker.Length);
                template = before + _customFragmentMain + after;
            }
        }

        return template;
    }

    private string GenerateUberOutputColorFunction(string template)
    {
        var registrations = MaterialTypeRegistry
            .GetAllRegistrations()
            .OrderBy(r => r.TypeId)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// Template function to create final color");
        sb.AppendLine("vec4 outputColor()");
        sb.AppendLine("{");

        foreach (var reg in registrations)
        {
            sb.AppendLine($"    if (MATERIAL_TYPE == {(uint)reg.TypeId}u) {{");
            sb.AppendLine($"        // {reg.Name}");

            // Indent the implementation
            var lines = reg.OutputColorImplementation.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine($"    {line}");
                }
            }

            sb.AppendLine("    }");
        }

        // Default fallback
        sb.AppendLine("  // Fallback for unknown material types");
        sb.AppendLine("  return vec4(1.0, 0.0, 1.0, 1.0); // Magenta");
        sb.AppendLine("}");

        // Replace the existing outputColor function
        int outputColorStart = template.IndexOf("vec4 outputColor()");
        if (outputColorStart < 0)
        {
            // Append before main if not found
            int mainStart = template.IndexOf("/*TEMPLATE_CUSTOM_MAIN_START*/");
            if (mainStart >= 0)
            {
                template = template.Insert(mainStart, sb.ToString() + "\n");
            }
        }
        else
        {
            // Find the end of the function
            int braceCount = 0;
            int i = outputColorStart;
            bool foundStart = false;

            while (i < template.Length)
            {
                if (template[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (template[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                    {
                        i++; // Include the closing brace
                        break;
                    }
                }
                i++;
            }

            if (i < template.Length)
            {
                string before = template.Substring(0, outputColorStart);
                string after = template.Substring(i);
                template = before + sb.ToString() + after;
            }
        }

        return template;
    }

    private string GenerateSingleMaterialOutputColorFunction(string template, uint typeId)
    {
        if (!MaterialTypeRegistry.TryGetById(typeId, out var registration) || registration == null)
        {
            throw new InvalidOperationException($"Material type ID {typeId} is not registered.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"// Material type: {registration.Name} (ID: {typeId})");
        sb.AppendLine("vec4 outputColor()");
        sb.AppendLine("{");

        // Add the implementation
        var lines = registration.OutputColorImplementation.Split('\n');
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine($"    {line}");
            }
        }

        sb.AppendLine("}");

        // Replace the existing outputColor function (same logic as uber shader)
        int outputColorStart = template.IndexOf("vec4 outputColor()");
        if (outputColorStart < 0)
        {
            int mainStart = template.IndexOf("/*TEMPLATE_CUSTOM_MAIN_START*/");
            if (mainStart >= 0)
            {
                template = template.Insert(mainStart, sb.ToString() + "\n");
            }
        }
        else
        {
            int braceCount = 0;
            int i = outputColorStart;
            bool foundStart = false;

            while (i < template.Length)
            {
                if (template[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (template[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                    {
                        i++;
                        break;
                    }
                }
                i++;
            }

            if (i < template.Length)
            {
                string before = template.Substring(0, outputColorStart);
                string after = template.Substring(i);
                template = before + sb.ToString() + after;
            }
        }

        return template;
    }

    private string GenerateVertexShader()
    {
        string template = LoadTemplate("vsMainTemplate.glsl");

        // Inject custom code if any meant for vertex shader (currently shared _customCode might be issue?)
        // Assuming _customCode is fragment only based on usage.
        // If we want vertex custom code, we need separate list.
        // For now, leaving empty or specific injection if needed.

        foreach (var replacement in _templateReplacements)
        {
            string startMarker = $"/*{replacement.Key}_START*/";
            string endMarker = $"/*{replacement.Key}_END*/";

            int startIndex = template.IndexOf(startMarker);
            int endIndex = template.IndexOf(endMarker);

            if (startIndex >= 0 && endIndex > startIndex)
            {
                string before = template.Substring(0, startIndex);
                string after = template.Substring(endIndex + endMarker.Length);
                template = before + replacement.Value + after;
            }
        }

        return template;
    }

    private string LoadTemplate(string templateName)
    {
        var assembly = typeof(MaterialShaderBuilder).Assembly; // Actually it's in Shaders assembly?
        // No, based on file context, templates are in HelixToolkit.Nex.Shaders project.
        // We need to access them from there.

        // Assuming HelixToolkit.Nex.Shaders assembly is loaded or referenced.
        // We can use a type from that assembly to locate it.
        var shaderAssembly = typeof(HelixToolkit.Nex.Shaders.GlslHeaders).Assembly;
        var resourceName = $"HelixToolkit.Nex.Shaders.Vert.{templateName}";

        if (templateName.StartsWith("ps"))
            resourceName = $"HelixToolkit.Nex.Shaders.Frag.{templateName}";

        using var stream = shaderAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException(
                $"Shader template '{templateName}' not found in resources: {resourceName}"
            );

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Result of building a material pipeline with vertex and fragment shaders.
/// </summary>
public sealed class MaterialShaderResult : IDisposable
{
    public bool Success;
    public List<string> Errors = [];
    public ShaderModuleResource VertexShader = ShaderModuleResource.Null;
    public ShaderModuleResource FragmentShader = ShaderModuleResource.Null;
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                VertexShader.Dispose();
                FragmentShader.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MaterialShaderResult()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
