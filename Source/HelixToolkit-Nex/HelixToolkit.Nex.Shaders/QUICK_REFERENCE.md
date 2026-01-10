# Shader Building System - Quick Reference Card

## Basic Usage

```csharp
using HelixToolkit.Nex.Shaders;
using HelixToolkit.Nex.Graphics;

// Method 1: Simple compilation with PBR
var compiler = new ShaderCompiler();
var result = compiler.CompileFragmentShaderWithPBR(shaderSource);
if (result.Success) UseShader(result.Source);

// Method 2: Fluent builder
var result = GlslHeaders.BuildShader()
    .WithStage(ShaderStage.Fragment)
    .WithSource(shaderSource)
    .WithStandardHeader()
    .WithPBRFunctions()
    .Build();
```

## Stage-Specific Methods

```csharp
var compiler = new ShaderCompiler();

// Each stage has a dedicated method
compiler.CompileVertexShader(source);
compiler.CompileFragmentShaderWithPBR(source);
compiler.CompileComputeShader(source);
compiler.CompileGeometryShader(source);
compiler.CompileTessControlShader(source);
compiler.CompileTessEvalShader(source);
compiler.CompileMeshShader(source);
compiler.CompileTaskShader(source);
```

## Custom Options

```csharp
var options = new ShaderBuildOptions
{
    IncludeStandardHeader = true,     // HeaderFrag.glsl, etc.
    IncludePBRFunctions = true,       // PBRFunctions.glsl
    StripComments = false,            // Remove comments
    EnableDebug = false,              // Debug info
    Defines = new Dictionary<string, string>
    {
        { "FEATURE_NAME", "1" },      // Add #define
        { "MAX_VALUE", "100" }
    }
};

var result = compiler.Compile(ShaderStage.Fragment, source, options);
```

## Caching

```csharp
// Use global cache (default)
var compiler = new ShaderCompiler(useGlobalCache: true);

// Use local cache
var cache = new ShaderCache(maxEntries: 50, expirationTime: TimeSpan.FromMinutes(30));
var compiler = new ShaderCompiler(useGlobalCache: false, localCache: cache);

// Disable caching
var builder = GlslHeaders.BuildShader().WithoutCache();

// Clear cache
compiler.ClearCache();

// Get statistics
var stats = compiler.GetCacheStatistics();
Console.WriteLine($"Cache entries: {stats.TotalEntries}");
```

## PBR Functions Available in Your Shader

```glsl
// After enabling IncludePBRFunctions = true

// Material structure
PBRMaterial material;
material.albedo = vec3(0.8, 0.2, 0.2);
material.metallic = 0.5;
material.roughness = 0.3;
material.ao = 1.0;
material.normal = normalize(fragNormal);
material.emissive = vec3(0.0);
material.opacity = 1.0;

// Simple PBR shading (single directional light)
vec3 color = pbrShadingSimple(
    material,
    lightDirection,
    lightColor,
    lightIntensity,
    viewDirection,
    ambientColor
);

// Advanced PBR shading (multiple lights)
Light lights[4];
// ... configure lights ...
vec3 color = pbrShading(material, fragPos, viewDir, lights, 4, ambientColor);
```

## Error Handling

```csharp
var result = compiler.CompileFragmentShaderWithPBR(source);

if (!result.Success)
{
    Console.WriteLine("Compilation failed!");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}

// Check warnings
foreach (var warning in result.Warnings)
{
    Console.WriteLine($"Warning: {warning}");
}

// Check what was included
Console.WriteLine($"Included: {string.Join(", ", result.IncludedFiles)}");
```

## Common Patterns

### Pattern 1: Shader with Variants
```csharp
public string GetShaderVariant(bool useShadows, int maxLights)
{
    return GlslHeaders.BuildShader()
        .WithStage(ShaderStage.Fragment)
        .WithSource(baseShader)
        .WithPBRFunctions()
        .WithDefine("USE_SHADOWS", useShadows ? "1" : "0")
        .WithDefine("MAX_LIGHTS", maxLights.ToString())
        .Build()
        .Source!;
}
```

### Pattern 2: Material Shader Generator
```csharp
public class MaterialShaderGenerator
{
    private readonly ShaderCompiler _compiler = new();
    
    public string GenerateFor(Material material)
    {
        var shader = BuildShaderSource(material);
        var result = _compiler.CompileFragmentShaderWithPBR(shader);
        return result.Source!;
    }
}
```

### Pattern 3: Shader Library
```csharp
public static class Shaders
{
    private static readonly ShaderCompiler Compiler = new();
    
    public static string PBR => Compile(Resources.PBRShader);
    public static string Unlit => Compile(Resources.UnlitShader);
    
    private static string Compile(string src) =>
        Compiler.CompileFragmentShaderWithPBR(src).Source!;
}
```

## Factory Methods

```csharp
// Create compiler
var compiler = GlslHeaders.CreateCompiler();
var compilerNoCache = GlslHeaders.CreateCompiler(useGlobalCache: false);

// Create builder
var builder = GlslHeaders.BuildShader();

// Get header directly
string fragmentHeader = GlslHeaders.GetShaderHeader(ShaderStage.Fragment);
string pbrFunctions = GlslHeaders.GetGlslShaderPBRFunction();
```

## Automatic Inclusions

When you enable options, these are automatically included:

1. **Version Directive**: Extracted from your shader or defaults to `#version 460`
2. **Custom Defines**: Added before all other code
3. **Standard Header**: Stage-appropriate header (HeaderFrag.glsl, etc.)
4. **PBR Functions**: Complete PBR library (PBRFunctions.glsl)
5. **User Code**: Your shader code with processed #includes

## Performance Tips

✅ Compile shaders at startup, not runtime
✅ Use the global cache for best performance
✅ Compile on background threads for long operations
✅ Clear cache during development for fresh builds
✅ Monitor cache statistics to optimize settings

## Debugging

```csharp
// Enable debug info
var options = new ShaderBuildOptions { EnableDebug = true };

// Print build result details
Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Source length: {result.Source?.Length}");
Console.WriteLine($"Errors: {result.Errors.Count}");
Console.WriteLine($"Warnings: {result.Warnings.Count}");
Console.WriteLine($"Included: {string.Join(", ", result.IncludedFiles)}");

// Don't strip comments during development
options.StripComments = false;
```

## Quick Tests

```bash
# Run all tests
dotnet test HelixToolkit.Nex.Shaders.Tests.csproj

# Run specific category
dotnet test --filter "TestCategory=PBR"
dotnet test --filter "TestCategory=Caching"
dotnet test --filter "TestCategory=FluentAPI"

# Run in Visual Studio Test Explorer
# Open Test Explorer and click "Run All"
```

Test Categories:
- `ShaderBuilding` - Core functionality
- `PBR` - PBR features
- `Preprocessor` - Preprocessing
- `Caching` - Cache tests
- `FluentAPI` - Builder pattern
- `ErrorHandling` - Error scenarios
- `Factory` - Factory methods
- `Headers` - Header access

````````
