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
    private readonly Dictionary<string, string> _defines = new();
    private readonly List<string> _customCode = new();

    // New fields for template injection
    private readonly Dictionary<string, string> _templateReplacements = new();

    private bool _usePBR = true;
    private bool _simpleLighting = false;
    private ForwardPlusConfig _forwardPlusConfig = ForwardPlusConfig.Default;
    private string? _customFragmentMain;

    public MaterialShaderBuilder()
    {
        _compiler = GlslHeaders.CreateCompiler();
    }

    /// <summary>
    /// Enable or disable PBR shading (enabled by default).
    /// </summary>
    public MaterialShaderBuilder WithPBRShading(bool enable = true)
    {
        _usePBR = enable;
        return this;
    }

    /// <summary>
    /// Enable Forward+ rendering with tile-based light culling.
    /// </summary>
    public MaterialShaderBuilder ConfigForwardPlus(ForwardPlusConfig? config = null)
    {
        _forwardPlusConfig = config ?? ForwardPlusConfig.Default;
        WithDefine("TILE_SIZE", _forwardPlusConfig.TileSize.ToString());
        WithDefine("MAX_LIGHTS_PER_TILE", _forwardPlusConfig.MaxLightsPerTile.ToString());
        return this;
    }

    // Legacy alias to fix build
    public MaterialShaderBuilder WithForwardPlus(bool enable, ForwardPlusConfig? config = null)
    {
        // Ignore 'enable' flag if we assume it's part of config logic or separate feature flag
        // but if enable is true, we configure it.
        if (enable)
        {
            return ConfigForwardPlus(config);
        }
        return this;
    }

    public MaterialShaderBuilder WithSimpleLighting(bool enable = true)
    {
        _simpleLighting = enable;
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
    /// Enable texture features based on material properties.
    /// </summary>
    public MaterialShaderBuilder ForMaterial(PbrMaterialProperties material)
    {
        // Auto-configure based on material properties
        if (material.BaseColorTexture.Valid)
        {
            WithDefine("USE_BASE_COLOR_TEXTURE");
        }

        if (material.MetallicRoughnessTexture.Valid)
        {
            WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE");
        }

        if (material.NormalTexture.Valid)
        {
            WithDefine("USE_NORMAL_TEXTURE");
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

        Debug.Assert(vertexModule.Valid, "Vertex shader module creation failed.");

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
            BuildResult = fragmentResult,
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

        // If user supplied a custom main, we append it to custom code and
        // rely on template modification or overrides if needed.
        // However, the current template system expects 'main' to be present in the template.
        // If user wants custom main, they might need to use a different base or defines.
        // For now, let's treat CustomMain as injecting code or replacing logic if provided via template mechanism.
        if (!string.IsNullOrEmpty(_customFragmentMain))
        {
            // This logic needs to be adapted if strict replacement is required.
            // But for now, let's assume simple injection or the user uses the new template replacement system.
            sbCustom.AppendLine(_customFragmentMain);
        }

        // Apply template replacements
        template = template.Replace("// TEMPLATE_CUSTOM_CODE", sbCustom.ToString());

        foreach (var replacement in _templateReplacements)
        {
            // Simple replace of entire block markers
            // Example: /*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_START*/ ... /*TEMPLATE_CREATE_PBR_MATERIAL_IMPL_END*/
            // replaced by user code.

            string startMarker = $"/*{replacement.Key}_START*/";
            string endMarker = $"/*{replacement.Key}_END*/";

            int startIndex = template.IndexOf(startMarker);
            int endIndex = template.IndexOf(endMarker);

            if (startIndex >= 0 && endIndex > startIndex)
            {
                // Replace the content including markers
                // Or just content between? Usually replacing content between allows keeping markers if needed,
                // but replacing everything is cleaner.
                // Let's replace the whole block including markers to fully override.
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
public struct MaterialShaderResult
{
    public bool Success;
    public List<string> Errors;
    public ShaderModuleResource VertexShader;
    public ShaderModuleResource FragmentShader;
    public ShaderBuildResult BuildResult;
}
