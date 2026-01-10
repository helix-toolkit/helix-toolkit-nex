# Integration Guide: Using the Shader Building System

This guide shows how to integrate the shader building system into your HelixToolkit.Nex application.

## Quick Start Integration

### Step 1: Add Reference

The shader building system is part of `HelixToolkit.Nex.Shaders`. If you're using other HelixToolkit.Nex projects, you likely already have this reference.

```xml
<ProjectReference Include="..\HelixToolkit.Nex.Shaders\HelixToolkit.Nex.Shaders.csproj" />
```

### Step 2: Basic Usage in Your Application

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

public class MyRenderer
{
    private readonly ShaderCompiler _shaderCompiler;

    public MyRenderer()
    {
        // Create compiler instance (uses global cache by default)
        _shaderCompiler = GlslHeaders.CreateCompiler();
    }

    public void InitializeShaders()
    {
        // Your custom PBR-enabled fragment shader
        string fragmentShaderSource = @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    vec3 lightDirection;
    vec3 lightColor;
} pc;

layout(set = 0, binding = 0) uniform texture2D kTextures2D[];
layout(set = 0, binding = 1) uniform sampler kSamplers[];

void main() {
    // Sample textures
    vec3 albedo = texture(sampler2D(kTextures2D[0], kSamplers[0]), fragTexCoord).rgb;
    float roughness = texture(sampler2D(kTextures2D[1], kSamplers[0]), fragTexCoord).r;
    float metallic = texture(sampler2D(kTextures2D[2], kSamplers[0]), fragTexCoord).r;
    
    // Setup PBR material
    PBRMaterial material;
    material.albedo = albedo;
    material.metallic = metallic;
    material.roughness = roughness;
    material.ao = 1.0;
    material.normal = normalize(fragNormal);
    material.emissive = vec3(0.0);
    material.opacity = 1.0;
    
    // Calculate lighting
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    vec3 color = pbrShadingSimple(
        material,
        pc.lightDirection,
        pc.lightColor,
        1.0,
        viewDir,
        vec3(0.03)
    );
    
    // Tone mapping and gamma correction
    color = color / (color + vec3(1.0));
    color = pow(color, vec3(1.0/2.2));
    
    outColor = vec4(color, 1.0);
}";

        // Compile with automatic PBR functions inclusion
        var result = _shaderCompiler.CompileFragmentShaderWithPBR(fragmentShaderSource);
        
        if (!result.Success)
        {
            // Handle compilation errors
            throw new Exception($"Shader compilation failed: {string.Join(", ", result.Errors)}");
        }
        
        // Use the compiled shader with your graphics context
        CreateVulkanShader(result.Source);
    }

    private void CreateVulkanShader(string processedSource)
    {
        // Your Vulkan shader creation code here
        // The processedSource contains the complete shader with all headers included
    }
}
```

## Advanced Integration Examples

### Example 1: Dynamic Shader Variants

Create shader variants with different feature sets:

```csharp
public class ShaderVariantManager
{
    private readonly ShaderCompiler _compiler = new();
    private readonly Dictionary<string, string> _shaderCache = new();

    public string GetShaderVariant(string baseShader, bool useShadows, bool useNormalMapping, int maxLights)
    {
        string variantKey = $"{useShadows}_{useNormalMapping}_{maxLights}";
        
        if (_shaderCache.TryGetValue(variantKey, out var cached))
        {
            return cached;
        }

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = true,
            Defines = new Dictionary<string, string>
            {
                { "USE_SHADOWS", useShadows ? "1" : "0" },
                { "USE_NORMAL_MAPPING", useNormalMapping ? "1" : "0" },
                { "MAX_LIGHTS", maxLights.ToString() }
            }
        };

        var result = _compiler.Compile(ShaderStage.Fragment, baseShader, options);
        
        if (result.Success)
        {
            _shaderCache[variantKey] = result.Source!;
            return result.Source!;
        }

        throw new Exception("Failed to compile shader variant");
    }
}
```

### Example 2: Material System Integration

Integrate with a material system:

```csharp
public class PBRMaterial
{
    public string Name { get; set; }
    public Texture2D? AlbedoMap { get; set; }
    public Texture2D? NormalMap { get; set; }
    public Texture2D? MetallicRoughnessMap { get; set; }
    public Texture2D? AOMap { get; set; }
    public Texture2D? EmissiveMap { get; set; }

    public string GenerateFragmentShader()
    {
        // Generate custom shader based on available textures
        var shaderCode = new StringBuilder();
        
        shaderCode.AppendLine(@"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
} pc;

void main() {
    PBRMaterial material;
");

        if (AlbedoMap != null)
        {
            shaderCode.AppendLine("    material.albedo = texture(sampler2D(kTextures2D[0], kSamplers[0]), fragTexCoord).rgb;");
        }
        else
        {
            shaderCode.AppendLine("    material.albedo = vec3(0.8);");
        }

        if (MetallicRoughnessMap != null)
        {
            shaderCode.AppendLine("    vec2 mr = texture(sampler2D(kTextures2D[1], kSamplers[0]), fragTexCoord).bg;");
            shaderCode.AppendLine("    material.metallic = mr.r;");
            shaderCode.AppendLine("    material.roughness = mr.g;");
        }
        else
        {
            shaderCode.AppendLine("    material.metallic = 0.0;");
            shaderCode.AppendLine("    material.roughness = 0.5;");
        }

        shaderCode.AppendLine(@"
    material.ao = 1.0;
    material.opacity = 1.0;
    material.emissive = vec3(0.0);
    material.normal = normalize(fragNormal);
    
    vec3 viewDir = normalize(pc.cameraPosition - fragPosition);
    vec3 color = pbrShadingSimple(material, normalize(vec3(-0.5, -1.0, -0.3)), vec3(1.0), 3.0, viewDir, vec3(0.03));
    
    outColor = vec4(color, 1.0);
}");

        // Compile with PBR functions
        var compiler = GlslHeaders.CreateCompiler();
        var result = compiler.CompileFragmentShaderWithPBR(shaderCode.ToString());
        
        return result.Success ? result.Source! : throw new Exception("Shader compilation failed");
    }
}
```

### Example 3: Hot-Reload Development Workflow

Support shader hot-reloading during development:

```csharp
public class ShaderHotReloader
{
    private readonly ShaderCompiler _compiler;
    private readonly FileSystemWatcher _watcher;
    private readonly string _shaderDirectory;
    private Action<string>? _onShaderReloaded;

    public ShaderHotReloader(string shaderDirectory)
    {
        _shaderDirectory = shaderDirectory;
        _compiler = new ShaderCompiler(useGlobalCache: false); // Don't cache during development
        
        _watcher = new FileSystemWatcher(shaderDirectory)
        {
            Filter = "*.glsl",
            NotifyFilter = NotifyFilters.LastWrite
        };
        
        _watcher.Changed += OnShaderFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public void OnShaderReloaded(Action<string> callback)
    {
        _onShaderReloaded = callback;
    }

    private void OnShaderFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce multiple file change events
        Task.Delay(100).ContinueWith(_ =>
        {
            try
            {
                var shaderCode = File.ReadAllText(e.FullPath);
                var result = _compiler.CompileFragmentShaderWithPBR(shaderCode);
                
                if (result.Success)
                {
                    _onShaderReloaded?.Invoke(result.Source!);
                    Console.WriteLine($"Shader {e.Name} reloaded successfully");
                }
                else
                {
                    Console.WriteLine($"Shader {e.Name} compilation failed:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reloading shader: {ex.Message}");
            }
        });
    }
}
```

### Example 4: Uber Shader System

Create an uber shader with multiple rendering paths:

```csharp
public class UberShaderBuilder
{
    public enum RenderingPath
    {
        Forward,
        Deferred,
        ForwardPlus
    }

    public enum LightingModel
    {
        PBR,
        Phong,
        Unlit
    }

    public string BuildUberShader(RenderingPath path, LightingModel lighting, bool castShadows)
    {
        var baseShader = @"
layout(location = 0) in vec3 fragPosition;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec2 fragTexCoord;

#if DEFERRED_PATH
layout(location = 0) out vec4 gPosition;
layout(location = 1) out vec4 gNormal;
layout(location = 2) out vec4 gAlbedo;
layout(location = 3) out vec4 gMetallicRoughness;
#else
layout(location = 0) out vec4 outColor;
#endif

void main() {
    vec3 albedo = texture(sampler2D(kTextures2D[0], kSamplers[0]), fragTexCoord).rgb;
    
#if LIGHTING_PBR
    PBRMaterial material;
    material.albedo = albedo;
    material.metallic = 0.0;
    material.roughness = 0.5;
    material.ao = 1.0;
    material.normal = normalize(fragNormal);
    material.emissive = vec3(0.0);
    material.opacity = 1.0;
    
    vec3 color = pbrShadingSimple(material, normalize(vec3(-0.5, -1.0, -0.3)), vec3(1.0), 3.0, vec3(0.0, 0.0, 1.0), vec3(0.03));
#elif LIGHTING_UNLIT
    vec3 color = albedo;
#endif

#if DEFERRED_PATH
    gPosition = vec4(fragPosition, 1.0);
    gNormal = vec4(normalize(fragNormal), 1.0);
    gAlbedo = vec4(albedo, 1.0);
#else
    outColor = vec4(color, 1.0);
#endif
}";

        var defines = new Dictionary<string, string>
        {
            { path == RenderingPath.Deferred ? "DEFERRED_PATH" : "FORWARD_PATH", "" },
            { $"LIGHTING_{lighting.ToString().ToUpper()}", "" }
        };

        if (castShadows)
        {
            defines["CAST_SHADOWS"] = "";
        }

        var options = new ShaderBuildOptions
        {
            IncludeStandardHeader = true,
            IncludePBRFunctions = lighting == LightingModel.PBR,
            Defines = defines
        };

        var compiler = new ShaderCompiler();
        var result = compiler.Compile(ShaderStage.Fragment, baseShader, options);

        return result.Success ? result.Source! : throw new Exception("Uber shader compilation failed");
    }
}
```

## Best Practices

1. **Compile Once, Use Many Times**: Compile shaders during initialization, not every frame.

2. **Use the Global Cache**: Unless you have specific requirements, the global cache provides excellent performance.

3. **Handle Errors Gracefully**: Always check `result.Success` and provide meaningful error messages.

4. **Use Fluent API for Readability**: The builder pattern makes shader configuration more maintainable.

5. **Separate Shader Logic**: Keep shader source code in separate files or resources when possible.

6. **Version Your Shaders**: Include version information in shader names or comments for debugging.

## Performance Tips

1. **Batch Shader Compilation**: Compile all shaders at startup rather than on-demand.

2. **Use Asynchronous Compilation**: Compile shaders on background threads:
   ```csharp
   var task = Task.Run(() => compiler.CompileFragmentShaderWithPBR(source));
   ```

3. **Monitor Cache Statistics**: Use `GetCacheStatistics()` to optimize cache settings.

4. **Clear Cache Periodically**: In development, clear the cache when shaders change frequently.

## Debugging

Enable detailed output:

```csharp
var options = new ShaderBuildOptions
{
    EnableDebug = true
};

var result = compiler.Compile(ShaderStage.Fragment, source, options);

// Print included files
Console.WriteLine("Included files:");
foreach (var file in result.IncludedFiles)
{
    Console.WriteLine($"  - {file}");
}

// Print warnings
foreach (var warning in result.Warnings)
{
    Console.WriteLine($"Warning: {warning}");
}
```

## Common Patterns

### Pattern 1: Shader Library

```csharp
public static class ShaderLibrary
{
    private static readonly ShaderCompiler Compiler = new();
    
    public static string GetPBRShader() => 
        Compiler.CompileFragmentShaderWithPBR(Resources.PBRShader).Source!;
    
    public static string GetUnlitShader() => 
        Compiler.Compile(ShaderStage.Fragment, Resources.UnlitShader).Source!;
}
```

### Pattern 2: Shader Factory

```csharp
public class ShaderFactory
{
    public IShader CreateShader(ShaderDescriptor descriptor)
    {
        var result = GlslHeaders.BuildShader()
            .WithStage(descriptor.Stage)
            .WithSource(descriptor.Source)
            .WithStandardHeader()
            .WithPBRFunctions(descriptor.UsePBR)
            .Build();
            
        return new CompiledShader(result.Source!);
    }
}
```

## See Also

- [SHADER_BUILDING_README.md](SHADER_BUILDING_README.md) - Complete API documentation
- [PBR_README.md](PBR_README.md) - PBR functions documentation
- [ShaderBuildingExamples.cs](ShaderBuildingExamples.cs) - Working examples
- [ShaderBuildingTests.cs](ShaderBuildingTests.cs) - Test suite
