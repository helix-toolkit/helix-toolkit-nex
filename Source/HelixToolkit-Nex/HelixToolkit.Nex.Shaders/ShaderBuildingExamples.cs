using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Examples demonstrating how to use the shader building system.
/// For unit tests, see HelixToolkit.Nex.Shaders.Tests project.
/// </summary>
public static class ShaderBuildingExamples
{
    /// <summary>
    /// Example 1: Simple fragment shader with automatic PBR inclusion
    /// </summary>
    public static void Example1_SimplePBRShader()
    {
        string userShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
} pc;

void main() {
    // Create a simple PBR material
    PBRMaterial material;
    material.albedo = vec3(0.8, 0.2, 0.2);
    material.metallic = 0.5;
    material.roughness = 0.3;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 lightColor = vec3(1.0);
    
    vec3 color = pbrShadingSimple(material, lightDir, lightColor, 3.0, viewDir, vec3(0.03));
    
    outColor = vec4(color, 1.0);
}";

        // Method 1: Using ShaderCompiler directly
        var compiler = new ShaderCompiler();
        var result = compiler.CompileFragmentShaderWithPBR(userShader);

        if (result.Success)
        {
            Console.WriteLine("Shader compiled successfully!");
            Console.WriteLine($"Processed source length: {result.Source?.Length}");
            Console.WriteLine($"Included files: {string.Join(", ", result.IncludedFiles)}");
        }
        else
        {
            Console.WriteLine("Shader compilation failed:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }
    }

    /// <summary>
    /// Example 2: Using the fluent builder API
    /// </summary>
    public static void Example2_FluentBuilder()
    {
        string userShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(fragPosition, 1.0);
}";

        var result = GlslHeaders
            .BuildShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(userShader)
            .WithStandardHeader()
            .WithPBRFunctions()
            .WithDefine("USE_ADVANCED_LIGHTING", "1")
            .WithDefine("MAX_LIGHTS", "8")
            .Build();

        if (result.Success)
        {
            Console.WriteLine("Built with fluent API!");
            // Use result.Source...
        }
    }

    /// <summary>
    /// Example 3: Compile multiple shader stages
    /// </summary>
    public static void Example3_MultipleStages()
    {
        var compiler = GlslHeaders.CreateCompiler();

        // Vertex shader
        string vertexShader =
            @"
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

layout(location = 0) out vec3 fragPosition;
layout(location = 1) out vec3 fragNormal;

layout(push_constant) uniform PushConstants {
    mat4 modelViewProjection;
} pc;

void main() {
    fragPosition = inPosition;
    fragNormal = inNormal;
    gl_Position = pc.modelViewProjection * vec4(inPosition, 1.0);
}";

        // Fragment shader with PBR
        string fragmentShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;

layout(location = 0) out vec4 outColor;

void main() {
    PBRMaterial material;
    material.albedo = vec3(1.0);
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    // Simple ambient lighting for this example
    outColor = vec4(material.albedo * material.ao, 1.0);
}";

        var vsResult = compiler.CompileVertexShader(vertexShader);
        var fsResult = compiler.CompileFragmentShaderWithPBR(fragmentShader);

        Console.WriteLine($"Vertex shader: {(vsResult.Success ? "OK" : "FAILED")}");
        Console.WriteLine($"Fragment shader: {(fsResult.Success ? "OK" : "FAILED")}");
    }

    /// <summary>
    /// Example 4: Using custom options
    /// </summary>
    public static void Example4_CustomOptions()
    {
        string userShader =
            @"
// This is a comment that will be stripped
layout(location = 0) out vec4 outColor;

void main() {
    #ifdef USE_RED_COLOR
        outColor = vec4(1.0, 0.0, 0.0, 1.0);
    #else
        outColor = vec4(0.0, 1.0, 0.0, 1.0);
    #endif
}";

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = false, // Don't need PBR for this simple shader
            StripComments = true,
            Defines = new Dictionary<string, string> { { "USE_RED_COLOR", "" } },
        };

        var compiler = new ShaderCompiler();
        var result = compiler.Compile(ShaderStage.Fragment, userShader, options);

        if (result.Success)
        {
            Console.WriteLine("Custom options applied successfully!");
        }
    }

    /// <summary>
    /// Example 5: Using local cache
    /// </summary>
    public static void Example5_LocalCache()
    {
        // Create a local cache with custom settings
        var localCache = new ShaderCache(maxEntries: 50, expirationTime: TimeSpan.FromMinutes(30));

        var compiler = new ShaderCompiler(useGlobalCache: false, localCache: localCache);

        string shader =
            @"
layout(location = 0) out vec4 outColor;
void main() { outColor = vec4(1.0); }";

        // First compilation
        var result1 = compiler.Compile(ShaderStage.Fragment, shader);
        Console.WriteLine($"First compile - Cache count: {localCache.Count}");

        // Second compilation (will use cache)
        var result2 = compiler.Compile(ShaderStage.Fragment, shader);
        Console.WriteLine($"Second compile - Cache count: {localCache.Count}");

        // Get cache statistics
        var stats = compiler.GetCacheStatistics();
        Console.WriteLine($"Total entries: {stats.TotalEntries}");
        Console.WriteLine($"Average access count: {stats.AverageAccessCount}");
    }

    /// <summary>
    /// Example 6: Processing the example PBR shader from the file
    /// </summary>
    public static void Example6_ProcessExampleShader()
    {
        // This is the shader code from ExamplePBRShader.frag (without version and includes)
        string exampleShader =
            @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;
layout(location = 3) in vec3 fragTangent;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    float time;
} pc;

layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

layout(constant_id = 0) const uint albedoTexId = 0;
layout(constant_id = 1) const uint normalTexId = 1;
layout(constant_id = 2) const uint metallicRoughnessTexId = 2;
layout(constant_id = 3) const uint aoTexId = 3;
layout(constant_id = 4) const uint emissiveTexId = 4;
layout(constant_id = 5) const uint samplerId = 0;

void main() {
    vec4 albedoSample = texture(sampler2D(kTextures2D[albedoTexId], kSamplers[samplerId]), fragTexCoord);
    vec3 normalSample = texture(sampler2D(kTextures2D[normalTexId], kSamplers[samplerId]), fragTexCoord).xyz;
    vec2 metallicRoughness = texture(sampler2D(kTextures2D[metallicRoughnessTexId], kSamplers[samplerId]), fragTexCoord).bg;
    float ao = texture(sampler2D(kTextures2D[aoTexId], kSamplers[samplerId]), fragTexCoord).r;
    vec3 emissive = texture(sampler2D(kTextures2D[emissiveTexId], kSamplers[samplerId]), fragTexCoord).rgb;
    
    PBRMaterial material;
    material.albedo = albedoSample.rgb;
    material.metallic = metallicRoughness.r;
    material.roughness = metallicRoughness.g;
    material.ao = ao;
    material.opacity = albedoSample.a;
    material.emissive = emissive;
    
    vec3 N = normalize(fragNormal);
    vec3 normalMap = normalSample * 2.0 - 1.0;
    vec3 T = normalize(fragTangent);
    vec3 B = cross(N, T);
    mat3 TBN = mat3(T, B, N);
    material.normal = normalize(TBN * normalMap);
    
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    
    vec3 lightDirection = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 lightColor = vec3(1.0, 0.95, 0.9);
    float lightIntensity = 3.0;
    
    vec3 ambientColor = vec3(0.03);
    
    vec3 finalColor = pbrShadingSimple(
        material,
        lightDirection,
        lightColor,
        lightIntensity,
        viewDir,
        ambientColor
    );
    
    finalColor = finalColor / (finalColor + vec3(1.0));
    finalColor = pow(finalColor, vec3(1.0/2.2));
    
    outColor = vec4(finalColor, material.opacity);
}";

        var result = GlslHeaders
            .BuildShader()
            .WithStage(ShaderStage.Fragment)
            .WithSource(exampleShader)
            .WithStandardHeader()
            .WithPBRFunctions()
            .Build();

        if (result.Success)
        {
            Console.WriteLine("Example shader processed successfully!");
            Console.WriteLine($"Final shader has {result.Source?.Split('\n').Length} lines");
            Console.WriteLine($"Warnings: {result.Warnings.Count}");
            Console.WriteLine($"Included: {string.Join(", ", result.IncludedFiles)}");
        }
    }
}
