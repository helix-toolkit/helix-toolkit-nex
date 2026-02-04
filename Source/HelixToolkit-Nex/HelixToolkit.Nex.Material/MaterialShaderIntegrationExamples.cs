using System.Numerics;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Examples demonstrating how to use the Material system with the Shader library
/// to create custom materials easily.
/// </summary>
public static class MaterialShaderIntegrationExamples
{
    /// <summary>
    /// Example 1: Create a basic PBR material with auto-generated shaders
    /// </summary>
    public static Material? Example1_BasicPBRMaterial(IContext context)
    {
        // Create a PBR material
        var builder = new MaterialShaderBuilder().WithPBRShading(true);
        var result = builder.BuildMaterialPipeline(context, "PbrMaterial");

        if (!result.Success)
        {
            return null;
        }

        // Update pipeline descriptor with generated shaders
        var pipelineDesc = new RenderPipelineDesc
        {
            Topology = Topology.Triangle,
            CullMode = CullMode.Back,
        };
        pipelineDesc.VertexShader = result.VertexShader;
        pipelineDesc.FragementShader = result.FragmentShader;
        pipelineDesc.DebugName = "PbrMaterial_Pipeline";
        var material = new Material();
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        if (material.CreatePipeline(context, pipelineDesc))
        {
            return material;
        }
        return null;
    }

    /// <summary>
    /// Example 3: Create a custom material with shader modifications
    /// </summary>
    public static Material? Example3_CustomMaterialShader(IContext context)
    {
        // Use the material shader builder directly for customization
        var builder = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithDefine("USE_CUSTOM_LIGHTING")
            .WithCustomCode(
                @"
                vec3 customLighting(PBRMaterial mat, vec3 viewDir) {
                    // Custom lighting calculation
                    return mat.albedo * 0.5;
                }
            "
            )
            .WithCustomMain(
                @"
                void main() {
                    PBRMaterial material;
                    material.albedo = vec3(0.8, 0.2, 0.2);
                    material.metallic = 0.5;
                    material.roughness = 0.3;
                    material.ao = 1.0;
                    material.opacity = 1.0;
                    material.emissive = vec3(0.0);
                    material.normal = normalize(fragNormal);
                    
                    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
                    vec3 color = customLighting(material, viewDir);
                    
                    outColor = vec4(color, 1.0);
                }
            "
            );

        var result = builder.BuildMaterialPipeline(context, "CustomMaterial");

        if (result.Success)
        {
            // Create pipeline manually
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = result.VertexShader,
                FragementShader = result.FragmentShader,
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
            var material = new Material();
            if (material.CreatePipeline(context, pipelineDesc))
            {
                return material;
            }
        }
        return null;
    }

    /// <summary>
    /// Example 7: Advanced shader generation with conditionals
    /// </summary>
    public static void Example7_ConditionalShaderGeneration(
        IContext context,
        bool useNormalMap,
        bool useEmissive
    )
    {
        var builder = new MaterialShaderBuilder().WithPBRShading(true);

        if (useNormalMap)
        {
            builder.WithDefine("USE_NORMAL_TEXTURE");
        }

        if (useEmissive)
        {
            builder.WithDefine("USE_EMISSIVE_TEXTURE");
            builder.WithCustomCode(
                @"
                vec3 getEmissive() {
                    return texture(sampler2D(kTextures2D[pc.emissiveTexIndex], kSamplers[pc.samplerIndex]), fragTexCoord).rgb;
                }
            "
            );
        }

        var result = builder.BuildFragmentShader();

        if (result.Success)
        {
            var fragmentModule = context.CreateShaderModuleGlsl(
                result.Source!,
                ShaderStage.Fragment,
                "ConditionalMaterial_Fragment"
            );
            // Use fragmentModule in pipeline...
        }
    }

    /// <summary>
    /// Example 8: Shader variants for different quality levels
    /// </summary>
    public static Dictionary<string, MaterialShaderResult> Example8_QualityVariants(
        IContext context
    )
    {
        var variants = new Dictionary<string, MaterialShaderResult>();

        // High quality - all features
        var highQuality = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithDefine("HIGH_QUALITY")
            .WithDefine("USE_BASE_COLOR_TEXTURE")
            .WithDefine("USE_NORMAL_TEXTURE")
            .WithDefine("USE_METALLIC_ROUGHNESS_TEXTURE")
            .BuildMaterialPipeline(context, "Material_HighQuality");
        variants["High"] = highQuality;

        // Medium quality - basic textures
        var mediumQuality = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithDefine("MEDIUM_QUALITY")
            .WithDefine("USE_BASE_COLOR_TEXTURE")
            .BuildMaterialPipeline(context, "Material_MediumQuality");
        variants["Medium"] = mediumQuality;

        // Low quality - no textures
        var lowQuality = new MaterialShaderBuilder()
            .WithPBRShading(true)
            .WithDefine("LOW_QUALITY")
            .BuildMaterialPipeline(context, "Material_LowQuality");
        variants["Low"] = lowQuality;

        return variants;
    }
}
