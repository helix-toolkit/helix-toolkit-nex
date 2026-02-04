# GLSL to C# Struct Source Generator

## Overview

This source generator automatically extracts struct definitions from GLSL shader files and generates equivalent C# structs with proper memory layout for interop with graphics APIs.

## Features

- **Automatic Type Mapping**: Converts GLSL types to appropriate C# types
  - `vec2`, `vec3`, `vec4` ? `System.Numerics.Vector2/3/4`
  - `mat4` ? `System.Numerics.Matrix4x4`
  - `float`, `int`, `uint` ? native C# types
  
- **Memory Layout**: Generates structs with `[StructLayout(LayoutKind.Sequential)]` for proper GPU buffer alignment

- **Documentation Preservation**: GLSL inline comments are converted to XML documentation comments

- **Field Name Conversion**: Converts GLSL camelCase field names to C# PascalCase conventions

- **Array Support**: Handles GLSL array fields with proper marshaling attributes

## How It Works

1. **Input**: GLSL files marked as `AdditionalFiles` in the project
2. **Processing**: The generator scans for `struct` definitions marked with `@code_gen` using regex patterns
3. **Output**: Generates C# struct files in the `obj/GeneratedFiles` directory

## Usage

### 1. Add GLSL Files

Add your GLSL shader files to the project and mark them as `AdditionalFiles`:

```xml
<ItemGroup>
    <AdditionalFiles Include="Headers\*.glsl" />
</ItemGroup>
```

### 2. Define GLSL Structs

Write standard GLSL struct definitions in your shader files and annotate them with `@code_gen`:

```glsl
@code_gen
struct PBRProperties {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
};
```

### 3. Build the Project

The generator runs automatically during build and creates C# structs:

```csharp
/// <summary>
/// C# representation of the GLSL struct 'PBRProperties'.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct PBRProperties
{
    /// <summary>Base color (sRGB)</summary>
    public System.Numerics.Vector3 Albedo;

    /// <summary>Metallic factor [0..1]</summary>
    public float Metallic;

    /// <summary>Roughness factor [0..1]</summary>
    public float Roughness;

    public static readonly unsafe uint SizeInBytes = (uint)sizeof(PBRMaterial);
}
```

### 4. Use Generated Structs

The generated structs are available in the `HelixToolkit.Nex.Shaders` namespace:

```csharp
using HelixToolkit.Nex.Shaders;

var material = new PBRProperties
{
    Albedo = new Vector3(1.0f, 0.0f, 0.0f),
    Metallic = 0.5f,
    Roughness = 0.3f,
    Normal = new Vector3(0, 1, 0)
};

// Upload to GPU buffer
CreateUniformBuffer(material);
```

## Example: PBRFunctions.glsl

The generator processes `PBRFunctions.glsl` and creates two structs:

### Generated Output
- `PBRProperties` - 6 fields for physically-based rendering material properties
- `Light` - 8 fields for light source definitions

### Type Mappings Applied
- `vec3` ? `System.Numerics.Vector3`
- `float` ? `float`
- `int` ? `int`

## Viewing Generated Files

Generated files are created during build in:
```
obj/GeneratedFiles/HelixToolkit.Nex.CodeGen/HelixToolkit.Nex.CodeGen.GlslStructGenerator/
```

To output generated files to a visible directory, use:
```bash
dotnet build /p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=obj/GeneratedFiles
```

## Supported GLSL Types

| GLSL Type | C# Type |
|-----------|---------|
| `float` | `float` |
| `int` | `int` |
| `uint` | `uint` |
| `bool` | `bool` |
| `vec2` | `System.Numerics.Vector2` |
| `vec3` | `System.Numerics.Vector3` |
| `vec4` | `System.Numerics.Vector4` |
| `mat4` | `System.Numerics.Matrix4x4` |
| User-defined | Same name in C# |

## Limitations

- Only struct definitions are extracted (no functions, uniforms, or other declarations)
- Nested structs are supported but must be defined in dependency order
- Complex array types may require manual marshaling
- Matrix types have limited mapping (no direct 3x3 matrix in System.Numerics)

## Testing

Unit tests are available in `HelixToolkit.Nex.CodeGen.Tests`:
- `GlslStructParserTests` - Validates parsing logic
- Tests cover single structs, multiple structs, arrays, and comments

Run tests:
```bash
dotnet test HelixToolkit.Nex.CodeGen.Tests
```

## Architecture

### Components

1. **GlslStructGenerator** - Main incremental source generator
2. **GlslStructParser** - Regex-based GLSL parser
3. **CSharpStructGenerator** - C# code generation
4. **Type Mapping** - GLSL to C# type conversion

### Parser Implementation

Uses regex patterns to extract:
- Struct name: `@code_gen\s+struct\s+(\w+)\s*\{([^}]+)\}`
- Field definitions: `(\w+)\s+(\w+)(?:\[(\d+)\])?\s*;(?:\s*//\s*(.*))?`

### Code Generation

- Adds `StructLayout` attribute for proper memory alignment
- Converts field names to PascalCase
- Preserves comments as XML documentation
- Handles arrays with `MarshalAs` attribute

## Future Enhancements

Potential improvements:
- Support for uniform blocks
- Custom type mapping configuration
- Better matrix type handling (custom Matrix3x3)
- Padding detection and insertion for alignment
- Support for vector component swizzling in comments
