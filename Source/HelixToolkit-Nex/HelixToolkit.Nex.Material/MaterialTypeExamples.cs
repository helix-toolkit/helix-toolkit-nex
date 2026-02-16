using System.Numerics;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Examples demonstrating the new material type system with uber shader generation.
/// </summary>
public static class MaterialTypeExamples
{
    /// <summary>
    /// Example 1: Build an uber shader containing all registered material types.
    /// Select material type at runtime using specialization constants.
    /// </summary>
    public static MaterialShaderResult Example1_UberShader(IContext context)
    {
        // Build uber shader with all registered material types
        var builder = new MaterialShaderBuilder().WithUberShader(true); // This is the default

        var result = builder.BuildMaterialPipeline(context, "UberMaterial");

        if (!result.Success)
        {
            Console.WriteLine("Failed to build uber shader:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  {error}");
            }
            return result;
        }

        // Now you can create multiple pipelines from this shader,
        // each using a different material type via specialization constants
        return result;
    }

    /// <summary>
    /// Example 2: Create a pipeline for a specific material type using the uber shader.
    /// </summary>
    public static Material? Example2_CreatePBRMaterial(
        IContext context,
        MaterialShaderResult uberShader
    )
    {
        if (!uberShader.Success)
        {
            return null;
        }

        var pipelineDesc = new RenderPipelineDesc
        {
            Topology = Topology.Triangle,
            CullMode = CullMode.Back,
            VertexShader = uberShader.VertexShader,
            FragementShader = uberShader.FragmentShader,
            DebugName = "PBR_Material_Pipeline",
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        var material = new Material(PBRShadingMode.PBR.ToString());
        if (material.CreatePipeline(context, pipelineDesc))
        {
            return material;
        }

        return null;
    }

    /// <summary>
    /// Example 3: Create multiple material variants from a single uber shader.
    /// </summary>
    public static Dictionary<string, Material> Example3_MultipleVariants(
        IContext context,
        MaterialShaderResult uberShader
    )
    {
        var materials = new Dictionary<string, Material>();

        if (!uberShader.Success)
        {
            return materials;
        }

        // Get all registered material types
        var materialTypes = new[]
        {
            PBRShadingMode.PBR,
            PBRShadingMode.Unlit,
            PBRShadingMode.DebugTileLightCount,
            PBRShadingMode.Normal,
        };

        foreach (var typeName in materialTypes)
        {
            var pipelineDesc = new RenderPipelineDesc
            {
                Topology = Topology.Triangle,
                CullMode = CullMode.Back,
                VertexShader = uberShader.VertexShader,
                FragementShader = uberShader.FragmentShader,
                DebugName = $"{typeName}_Pipeline",
            };
            pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

            // Set the material type
            pipelineDesc.SetMaterialType(typeName.ToString());

            var material = new Material(typeName.ToString());
            if (material.CreatePipeline(context, pipelineDesc))
            {
                materials[typeName.ToString()] = material;
            }
        }

        return materials;
    }

    /// <summary>
    /// Example 4: Register a custom material type.
    /// </summary>
    public static void Example4_RegisterCustomMaterialType()
    {
        // Register a custom toon shading material
        var toonTypeId = MaterialTypeRegistry.Register(
            name: "Toon",
            outputColorImpl: @"
    PBRMaterial material = createPBRMaterial();
    vec3 normal = material.normal;
    vec3 viewDir = normalize(fpConst.cameraPosition - fragWorldPos);
    
    // Simple toon shading
    float intensity = dot(normal, -viewDir);
    vec3 color;
    if (intensity > 0.95) 
        color = material.albedo * 1.0;
    else if (intensity > 0.5) 
        color = material.albedo * 0.6;
    else if (intensity > 0.25) 
        color = material.albedo * 0.4;
    else 
        color = material.albedo * 0.2;
    
    finalColor = vec4(color + material.emissive, material.opacity);
    return;",
            additionalCode: @"
// Helper function for toon shading
vec3 toonShade(vec3 baseColor, float intensity, int levels) {
    float step = 1.0 / float(levels);
    float level = floor(intensity / step) * step;
    return baseColor * level;
}"
        );

        Console.WriteLine($"Registered Toon material type with ID: {toonTypeId}");
    }

    /// <summary>
    /// Example 5: Build a single material type shader (not an uber shader).
    /// Useful for optimization when you only need one material type.
    /// </summary>
    public static MaterialShaderResult Example5_SingleMaterialType(IContext context)
    {
        // Build a shader for only the PBR material type
        var builder = new MaterialShaderBuilder().WithMaterialType("PBR"); // Specify exact type

        var result = builder.BuildMaterialPipeline(context, "PBR_Only_Material");

        // This shader will only contain PBR shading code,
        // reducing shader complexity and potentially improving performance
        return result;
    }

    /// <summary>
    /// Example 6: Override the default createPBRMaterial implementation.
    /// </summary>
    public static void Example6_CustomMaterialCreation()
    {
        // Register a material type with custom material creation logic
        MaterialTypeRegistry.Register(
            new MaterialTypeRegistration
            {
                TypeId = 100, // Custom ID
                Name = "CustomPBR",
                CreateMaterialImplementation =
                    @"
PBRProperties props = getPBRMaterial();
PBRMaterial material;
material.albedo = pow(props.albedo, vec3(2.2)); // Custom gamma correction
material.roughness = props.roughness * props.roughness; // Squared roughness
material.metallic = props.metallic;
material.ao = props.ao;
material.emissive = props.emissive;
material.opacity = props.opacity;
material.ambient = props.ambient;
material.normal = normalize(fragNormal);
return material;",
                OutputColorImplementation =
                    @"
PBRMaterial material = createPBRMaterial();
forwardPlusLighting(material, finalColor);
return;",
            }
        );
    }

    /// <summary>
    /// Example 7: Debug - List all registered material types.
    /// </summary>
    public static void Example7_ListMaterialTypes()
    {
        Console.WriteLine("Registered Material Types:");
        Console.WriteLine("===========================");

        foreach (var registration in MaterialTypeRegistry.GetAllRegistrations())
        {
            Console.WriteLine($"ID: {registration.TypeId}");
            Console.WriteLine($"Name: {registration.Name}");
            Console.WriteLine(
                $"Has Custom Material Creation: {registration.CreateMaterialImplementation != null}"
            );
            Console.WriteLine($"Has Additional Code: {registration.AdditionalCode != null}");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Example 8: Complete workflow with custom material type.
    /// </summary>
    public static Material? Example8_CompleteWorkflow(IContext context)
    {
        // Step 1: Register custom material type
        MaterialTypeRegistry.Register(
            name: "Wireframe",
            outputColorImpl: @"
vec3 wireColor = vec3(0.0, 1.0, 0.0);
float edgeWidth = 0.02;
vec3 baryCentric = vec3(1.0); // Would need geometry shader for real barycentrics
finalColor = vec4(wireColor, 1.0);
return;",
            additionalCode: @"
// Wireframe helper functions
float edgeFactor(vec3 bary, float width) {
    vec3 d = fwidth(bary);
    vec3 a3 = smoothstep(vec3(0.0), d * width, bary);
    return min(min(a3.x, a3.y), a3.z);
}"
        );

        // Step 2: Build uber shader
        var builder = new MaterialShaderBuilder().WithUberShader(true);

        var shaderResult = builder.BuildMaterialPipeline(context, "UberShader");

        if (!shaderResult.Success)
        {
            return null;
        }

        // Step 3: Create pipeline with wireframe material type
        var pipelineDesc = new RenderPipelineDesc
        {
            Topology = Topology.Triangle,
            CullMode = CullMode.None,
            PolygonMode = PolygonMode.Line,
            VertexShader = shaderResult.VertexShader,
            FragementShader = shaderResult.FragmentShader,
            DebugName = "Wireframe_Pipeline",
        };
        pipelineDesc.Colors[0].Format = Format.RGBA_UN8;

        var material = new Material("Wireframe");
        return material.CreatePipeline(context, pipelineDesc) ? material : null;
    }
}
