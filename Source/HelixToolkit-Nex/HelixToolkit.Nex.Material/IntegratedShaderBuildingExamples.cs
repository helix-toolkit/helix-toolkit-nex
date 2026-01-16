using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Examples demonstrating integrated shader building and compilation
/// </summary>
public static class IntegratedShaderBuildingExamples
{
    /// <summary>
    /// Example 1: Build and compile in one step (extension method)
    /// </summary>
    public static void Example1_BuildAndCompileExtension(IContext context)
    {
        string fragmentShader =
            @"
layout(location = 0) in vec3 fragNormal;
layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    outColor = vec4(material.albedo, 1.0);
}";

        // Build and compile in one call
        var (buildResult, shaderModule) = context.BuildAndCompileFragmentShaderWithPBR(
            fragmentShader,
            debugName: "MyPBRShader"
        );

        if (buildResult.Success && shaderModule.Valid)
        {
            // Use shader module immediately
            // ... create pipeline ...
        }
        else
        {
            foreach (var error in buildResult.Errors)
            {
                Console.WriteLine($"Error: {error}");
            }
        }
    }

    /// <summary>
    /// Example 2: Fluent builder with context integration
    /// </summary>
    public static void Example2_FluentBuilderWithContext(IContext context)
    {
        string shader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragPosition, 1.0);
}";

        var (buildResult, shaderModule) = context
            .BuildAndCompileShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(shader)
            .WithStandardHeader()
            .WithPBRFunctions()
            .WithDefine("USE_ADVANCED_LIGHTING")
            .WithDebugName("AdvancedLightingShader")
            .Build();

        if (buildResult.Success)
        {
            Console.WriteLine($"Shader compiled successfully!");
            Console.WriteLine($"Module valid: {shaderModule.Valid}");
        }
    }

    /// <summary>
    /// Example 3: ShaderToyRenderer integration
    /// </summary>
    public static void Example3_ShaderToyIntegration(IContext context)
    {
        // This is how ShaderToyRenderer could be simplified
        string vertexShader =
            @"
layout(location = 0) out vec2 fragCoord;

void main() {
    vec2 positions[4] = vec2[](
        vec2(-1.0, -1.0), vec2( 1.0, -1.0),
        vec2(-1.0,  1.0), vec2( 1.0,  1.0)
    );
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    fragCoord = positions[gl_VertexIndex] * 0.5 + 0.5;
}";

        string fragmentShader =
            @"
layout(location = 0) in vec2 fragCoord;
layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(fragCoord, 0.0, 1.0);
}";

        // Build and compile both shaders
        var (vsBuild, vsModule) = context.BuildAndCompileVertexShader(
            vertexShader,
            debugName: "ShaderToy: Vertex"
        );

        var (fsBuild, fsModule) = context.BuildAndCompileShader(
            ShaderStage.Fragment,
            fragmentShader,
            new ShaderBuildOptions { IncludeStandardHeader = true },
            "ShaderToy: Fragment"
        );

        if (vsBuild.Success && fsBuild.Success)
        {
            // Create pipeline with both modules
            var pipelineDesc = new RenderPipelineDesc
            {
                VertexShader = vsModule,
                FragementShader = fsModule,
                DebugName = "ShaderToy Pipeline",
            };

            var pipeline = context.CreateRenderPipeline(pipelineDesc);
        }
    }

    /// <summary>
    /// Example 4: Error handling with detailed build information
    /// </summary>
    public static void Example4_ErrorHandling(IContext context)
    {
        string shaderWithError =
            @"
layout(location = 0) out vec4 outColor;

void main() {
    // This will fail because PBRMaterial is not included
    PBRMaterial mat;
    outColor = vec4(1.0);
}";

        var (buildResult, shaderModule) = context.BuildAndCompileShader(
            ShaderStage.Fragment,
            shaderWithError,
            new ShaderBuildOptions { IncludePBRFunctions = false } // Intentionally exclude PBR
        );

        if (!buildResult.Success)
        {
            Console.WriteLine("Build failed:");
            Console.WriteLine($"  Errors: {buildResult.Errors.Count}");
            foreach (var error in buildResult.Errors)
            {
                Console.WriteLine($"    - {error}");
            }

            Console.WriteLine($"  Included files: {string.Join(", ", buildResult.IncludedFiles)}");
        }

        // Module will be Null if compilation failed
        Console.WriteLine($"Shader module valid: {shaderModule.Valid}");
    }

    /// <summary>
    /// Example 5: Traditional two-step approach (still supported)
    /// </summary>
    public static void Example5_TwoStepApproach(IContext context)
    {
        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(1.0);
}";

        // Step 1: Build (preprocessing)
        var compiler = new ShaderCompiler();
        var buildResult = compiler.CompileFragmentShaderWithPBR(shader);

        if (!buildResult.Success)
        {
            Console.WriteLine("Preprocessing failed");
            return;
        }

        // Step 2: Compile to SPIR-V (can inspect/modify source in between)
        Console.WriteLine($"Preprocessed source has {buildResult.Source!.Length} characters");

        var shaderModule = context.CreateShaderModuleGlsl(
            buildResult.Source,
            ShaderStage.Fragment,
            "MyShader"
        );

        Console.WriteLine($"Module created: {shaderModule.Valid}");
    }

    /// <summary>
    /// Example 6: Batch compilation with error tracking
    /// </summary>
    public static void Example6_BatchCompilation(IContext context)
    {
        var shaderSources = new Dictionary<string, (ShaderStage stage, string source)>
        {
            ["Material"] = (ShaderStage.Fragment, "/* material shader */"),
            ["Sky"] = (ShaderStage.Fragment, "/* sky shader */"),
            ["Terrain"] = (ShaderStage.Fragment, "/* terrain shader */"),
        };

        var compiledShaders = new Dictionary<string, ShaderModuleResource>();
        var errors = new List<string>();

        foreach (var (name, (stage, source)) in shaderSources)
        {
            var (buildResult, module) = context.BuildAndCompileShader(
                stage,
                source,
                debugName: name
            );

            if (buildResult.Success && module.Valid)
            {
                compiledShaders[name] = module;
            }
            else
            {
                errors.Add($"{name}: {string.Join("; ", buildResult.Errors)}");
            }
        }

        Console.WriteLine($"Successfully compiled: {compiledShaders.Count}/{shaderSources.Count}");
        if (errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            errors.ForEach(Console.WriteLine);
        }
    }
}
