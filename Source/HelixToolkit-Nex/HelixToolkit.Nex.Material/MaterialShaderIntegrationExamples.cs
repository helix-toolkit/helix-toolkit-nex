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
    public static void Example1_BasicPBRMaterial(IContext context)
    {
        // Create a PBR material
        var material = new PbrMaterial();
        material.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.8f, 0.2f, 0.1f),
            Metallic = 0.5f,
            Roughness = 0.3f,
            Ao = 1.0f,
            Opacity = 1.0f,
        };

        // Initialize with pipeline - shaders are generated automatically
        var pipelineDesc = new RenderPipelineDesc
        {
            Topology = Topology.Triangle,
            CullMode = CullMode.Back,
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        bool success = material.InitializePipeline(context, pipelineDesc);
        if (success)
        {
            // Material is ready to use with its pipeline
            var pipeline = material.Pipeline;
            // Use in rendering...
        }
    }

    /// <summary>
    /// Example 2: Create a textured PBR material
    /// </summary>
    public static void Example2_TexturedPBRMaterial(
        IContext context,
        TextureResource albedoTexture,
        TextureResource normalTexture,
        SamplerResource sampler
    )
    {
        var material = new PbrMaterial();

        // Set up material properties
        material.Properties.Variables = new PBRMaterial
        {
            Albedo = Vector3.One,
            Metallic = 0.0f,
            Roughness = 0.5f,
            Ao = 1.0f,
        };

        // Assign textures - shader will automatically enable texture sampling
        material.Properties.BaseColorTexture = albedoTexture;
        material.Properties.NormalTexture = normalTexture;
        material.Properties.BaseColorSampler = sampler;
        material.Properties.NormalSampler = sampler;

        // Initialize pipeline
        var pipelineDesc = new RenderPipelineDesc();
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
        material.InitializePipeline(context, pipelineDesc);
    }

    /// <summary>
    /// Example 3: Create a custom material with shader modifications
    /// </summary>
    public static void Example3_CustomMaterialShader(IContext context)
    {
        var material = new PbrMaterial();

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

            var pipeline = context.CreateRenderPipeline(pipelineDesc);
        }
    }

    /// <summary>
    /// Example 4: Using MaterialFactory to create materials
    /// </summary>
    public static void Example4_MaterialFactory(IContext context)
    {
        // Create a material by name using the factory
        var material = MaterialFactory.Create("PBR");

        if (material is PbrMaterial pbrMaterial)
        {
            pbrMaterial.Properties.Variables = new PBRMaterial
            {
                Albedo = new Vector3(0.5f, 0.5f, 0.8f),
                Metallic = 1.0f,
                Roughness = 0.2f,
                Ao = 1.0f,
            };

            var pipelineDesc = new RenderPipelineDesc();
            pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
            pbrMaterial.InitializePipeline(context, pipelineDesc);
        }
    }

    /// <summary>
    /// Example 5: Create a material library with different presets
    /// </summary>
    public static Dictionary<string, PbrMaterial> Example5_MaterialLibrary(IContext context)
    {
        var library = new Dictionary<string, PbrMaterial>();

        // Metallic material
        var metallic = new PbrMaterial();
        metallic.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.8f, 0.8f, 0.8f),
            Metallic = 1.0f,
            Roughness = 0.2f,
            Ao = 1.0f,
        };
        library["Metal"] = metallic;

        // Plastic material
        var plastic = new PbrMaterial();
        plastic.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.2f, 0.8f, 0.2f),
            Metallic = 0.0f,
            Roughness = 0.5f,
            Ao = 1.0f,
        };
        library["Plastic"] = plastic;

        // Rough metal
        var roughMetal = new PbrMaterial();
        roughMetal.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.7f, 0.6f, 0.5f),
            Metallic = 1.0f,
            Roughness = 0.8f,
            Ao = 1.0f,
        };
        library["RoughMetal"] = roughMetal;

        // Initialize all pipelines
        var basePipelineDesc = new RenderPipelineDesc();
        basePipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        foreach (var mat in library.Values)
        {
            mat.InitializePipeline(context, basePipelineDesc);
        }

        return library;
    }

    /// <summary>
    /// Example 6: Dynamic material updates
    /// </summary>
    public static void Example6_DynamicMaterialUpdate(IContext context, PbrMaterial material)
    {
        // Update material properties at runtime
        material.Properties.Variables = new PBRMaterial
        {
            Albedo = new Vector3(0.9f, 0.1f, 0.1f),
            Metallic = 0.8f,
            Roughness = 0.4f,
            Ao = 1.0f,
        };

        // If textures change, invalidate and rebuild pipeline
        if (material.Properties.BaseColorTexture.Valid)
        {
            material.InvalidatePipeline();

            var pipelineDesc = new RenderPipelineDesc();
            pipelineDesc.Colors[0].Format = Format.RGBA_UN8;
            material.InitializePipeline(context, pipelineDesc);
        }
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
