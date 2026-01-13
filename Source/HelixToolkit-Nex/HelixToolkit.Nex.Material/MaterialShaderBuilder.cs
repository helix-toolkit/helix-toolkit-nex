using System.Text;
using static HelixToolkit.Nex.Shaders.Templates;

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
    private bool _usePBR = true;
    private bool _useBindlessVertices = false;
    private bool _useForwardPlus = false;
    private ForwardPlusConfig _forwardPlusConfig = ForwardPlusConfig.Default;
    private string? _customFragmentMain;

    public MaterialShaderBuilder()
    {
        _compiler = GlslHeaders.CreateCompiler();
    }

    /// <summary>
    /// Enable or disable PBR shading (enabled by default).
    /// </summary>
    public MaterialShaderBuilder WithPBRShading(bool enable)
    {
        _usePBR = enable;
        return this;
    }

    /// <summary>
    /// Enable bindless vertex buffer access using buffer_reference.
    /// </summary>
    public MaterialShaderBuilder WithBindlessVertices(bool enable = true)
    {
        _useBindlessVertices = enable;
        if (enable)
        {
            WithDefine("USE_BINDLESS_VERTICES");
        }
        else
        {
            _defines.Remove("USE_BINDLESS_VERTICES");
        }
        return this;
    }

    /// <summary>
    /// Enable Forward+ rendering with tile-based light culling.
    /// </summary>
    public MaterialShaderBuilder WithForwardPlus(
        bool enable = true,
        ForwardPlusConfig? config = null
    )
    {
        _useForwardPlus = enable;
        if (enable)
        {
            WithDefine("USE_FORWARD_PLUS");
            _forwardPlusConfig = config ?? ForwardPlusConfig.Default;
            WithDefine("TILE_SIZE", _forwardPlusConfig.TileSize.ToString());
            WithDefine("MAX_LIGHTS_PER_TILE", _forwardPlusConfig.MaxLightsPerTile.ToString());
        }
        else
        {
            _defines.Remove("USE_FORWARD_PLUS");
            _defines.Remove("TILE_SIZE");
            _defines.Remove("MAX_LIGHTS_PER_TILE");
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

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = _usePBR,
            Defines = _defines,
        };

        return _compiler.Compile(ShaderStage.Fragment, shaderSource, options);
    }

    /// <summary>
    /// Build a complete material pipeline with vertex and fragment shaders.
    /// </summary>
    public MaterialShaderResult BuildMaterialPipeline(IContext context, string? debugName = null)
    {
        // Build default vertex shader
        var vertexSource = GenerateVertexShader();
        var vertexOptions = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false,
            Defines = _defines,
        };
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

        var fragmentModule = context.CreateShaderModuleGlsl(
            fragmentResult.Source!,
            ShaderStage.Fragment,
            $"{debugName}_Fragment"
        );

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
        var sb = new StringBuilder();

        // Buffer reference declarations for bindless access
        if (_useBindlessVertices)
        {
            sb.Append(BindlessVertexStruct);
        }

        if (_useForwardPlus)
        {
            sb.Append(ForwardPlusStructs);
        }

        // Input attributes
        if (!_useBindlessVertices)
        {
            sb.Append(StandardInputs);
        }
        else
        {
            sb.Append(BindlessInputs);
        }

        // Output
        sb.Append(OutputDef);

        // Push constants
        if (_useForwardPlus)
        {
            sb.AppendFormat(
                ForwardPlusPushConstant,
                _useBindlessVertices ? "uint vertexBufferAddress;" : ""
            );
        }
        else
        {
            sb.Append(StandardPushConstant);
        }

        // Custom code sections
        foreach (var code in _customCode)
        {
            sb.AppendLine(code);
            sb.AppendLine();
        }

        // Main function
        if (_customFragmentMain != null)
        {
            sb.AppendLine(_customFragmentMain);
        }
        else
        {
            sb.Append(GenerateDefaultMain());
        }

        return sb.ToString();
    }

    private string GenerateDefaultMain()
    {
        var sb = new StringBuilder();

        sb.AppendFormat(DefaultMainHeader, _useBindlessVertices ? BindlessVertexFetch : "");

        if (_useForwardPlus)
        {
            sb.Append(ForwardPlusLighting);
        }
        else
        {
            sb.Append(SimpleLighting);
        }

        sb.Append(DefaultMainFooter);
        return sb.ToString();
    }

    private string GenerateVertexShader()
    {
        if (_useBindlessVertices)
        {
            string pushConstants = _useForwardPlus
                ? VertexShaderBindlessForwardPlusPushConstants
                : VertexShaderBindlessStandardPushConstants;
            string mainBody = _useForwardPlus
                ? VertexShaderBindlessForwardPlusMain
                : VertexShaderBindlessStandardMain;

            return string.Format(VertexShaderBindlessTemplate, pushConstants, mainBody);
        }
        else
        {
            // Traditional vertex shader
            return VertexShaderLegacy;
        }
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
