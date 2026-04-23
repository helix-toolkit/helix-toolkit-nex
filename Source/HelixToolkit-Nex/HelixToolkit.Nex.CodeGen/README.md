```markdown
# HelixToolkit.Nex.CodeGen

`HelixToolkit.Nex.CodeGen` is a C# package designed to facilitate the generation of C# code from GLSL shader code within the HelixToolkit.Nex 3D graphics engine. This package provides tools for converting GLSL struct definitions into C# structs with appropriate memory layouts and for generating observable properties from fields marked with specific attributes.

## Overview

The `HelixToolkit.Nex.CodeGen` package plays a crucial role in the HelixToolkit.Nex engine by automating the conversion of GLSL shader code into C# code, ensuring that the data structures used in shaders are accurately represented in the engine's C# codebase. This package includes source generators that parse GLSL files to extract struct definitions and convert them into C# structs. It also includes a generator for creating observable properties from fields marked with an `[Observable]` attribute, enhancing data binding capabilities.

## Key Types

| Type                          | Description                                                                 |
|-------------------------------|-----------------------------------------------------------------------------|
| `CSharpStructGenerator`       | Generates C# struct code from parsed GLSL structs.                          |
| `GlslStructGenerator`         | Source generator that extracts struct definitions from GLSL files.          |
| `GlslStructParser`            | Parses GLSL code to extract struct definitions.                             |
| `GlslStruct`                  | Represents a GLSL struct definition.                                        |
| `GlslField`                   | Represents a field in a GLSL struct.                                        |
| `ObservablePropertyGenerator` | Source generator that converts fields marked with `[Observable]` attribute. |

## Usage Examples

### Generating C# Structs from GLSL

To generate C# structs from GLSL files, ensure your GLSL structs are annotated with `@code_gen`. The `GlslStructGenerator` will automatically process these files during the build.

```csharp
// Example GLSL file content
/*
@code_gen
struct Light {
    vec3 position;
    vec3 color;
    float intensity;
};
*/

// The above GLSL struct will be converted to a C# struct similar to:
public struct Light
{
    public System.Numerics.Vector3 Position;
    public System.Numerics.Vector3 Color;
    public float Intensity;

    public static readonly unsafe uint SizeInBytes = (uint)sizeof(Light);
}
```

### Creating Observable Properties

Fields marked with the `[Observable]` attribute will be converted into properties with change notification.

```csharp
// Original C# class
public partial class MyClass
{
    [Observable]
    private int _value;
}

// Generated code
public partial class MyClass
{
    public int Value
    {
        get => _value;
        set { Set(ref _value, value); }
    }
}
```

## Architecture Notes

- **Design Patterns**: The package leverages the source generator pattern to automate code generation during the build process.
- **Dependencies**: This package depends on the Roslyn compiler platform for source generation and integrates with other HelixToolkit.Nex packages to ensure seamless shader integration.
- **Memory Layout**: Generated structs use `[StructLayout(LayoutKind.Sequential, Pack = 16)]` to ensure proper memory alignment, crucial for GPU data transfer.
- **Type Mapping**: GLSL types are mapped to equivalent C# types using `System.Numerics` for vector and matrix operations.

This package is an integral part of the HelixToolkit.Nex engine, ensuring that shader code is efficiently and accurately integrated into the C# environment.
```
