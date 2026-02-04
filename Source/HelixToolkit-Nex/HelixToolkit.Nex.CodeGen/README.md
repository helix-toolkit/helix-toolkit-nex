# HelixToolkit.Nex.CodeGen

## Overview

HelixToolkit.Nex.CodeGen is a C# source generator library that provides automated code generation tools for the HelixToolkit.Nex graphics framework. This library contains Roslyn-based incremental source generators that reduce boilerplate code and improve developer productivity.

## Features

This library includes two main source generators:

### 1. **GLSL to C# Struct Generator**
Automatically converts GLSL shader struct definitions into equivalent C# structs with proper memory layout for GPU buffer interoperability.

**Key capabilities:**
- Automatic type mapping from GLSL to C# types
- Proper memory layout with `[StructLayout(LayoutKind.Sequential, Pack = 16)]`
- Automatic `SizeInBytes` constant generation
- Documentation preservation from GLSL comments
- Array support with generated index accessors

**[Read the full GLSL Struct Generator documentation](GLSL_STRUCT_GENERATOR_README.md)**

### 2. **Observable Property Generator**
Automatically generates observable properties from fields marked with the `[Observable]` attribute, following the CommunityToolkit.Mvvm pattern.

**Key capabilities:**
- Automatic `INotifyPropertyChanged` implementation
- Convention-based property naming
- Default value support
- IntelliSense integration

?? **[Read the full Observable Property Generator documentation](OBSERVABLE_PROPERTY_GENERATOR_README.md)**

## Target Framework

- **.NET Standard 2.0** - Ensures maximum compatibility with various .NET runtimes and IDEs

## Technology Stack

- **Roslyn Compiler Platform** - Microsoft.CodeAnalysis.CSharp 4.14.0
- **Incremental Generators** - For optimal build performance
- **C# 11+ Language Features** - With LangVersion set to latest

## Installation

Reference this project as an analyzer in your `.csproj` file:

```xml
<ItemGroup>
  <ProjectReference Include="..\HelixToolkit.Nex.CodeGen\HelixToolkit.Nex.CodeGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Quick Start

### Using the GLSL Struct Generator

1. Add your GLSL shader files to the project as `AdditionalFiles`:
```xml
<ItemGroup>
    <AdditionalFiles Include="Shaders\*.glsl" />
</ItemGroup>
```

2. Define structs in your GLSL files and mark them with `@code_gen`:
```glsl
@code_gen
struct Material {
    vec3 color;
    float roughness;
};
```

3. Build the project - C# structs are automatically generated!

### Using the Observable Property Generator

1. Mark fields with `[Observable]` attribute:
```csharp
public partial class MyViewModel : ObservableObject
{
    [Observable]
    private string _title;
    
    [Observable(Default = "0")]
    private int _count;
}
```

2. Build the project - Observable properties are automatically generated!

## Generated Files Location

By default, generated files are created in:
```
obj/Generated/HelixToolkit.Nex.CodeGen/
```

To emit generated files to a visible directory for debugging:
```bash
dotnet build /p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=obj/GeneratedFiles
```

Or add to your `.csproj`:
```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
  <Compile Include="$(CompilerGeneratedFilesOutputPath)\**\*.cs" Visible="false" />
</ItemGroup>
```

## Project Structure

```
HelixToolkit.Nex.CodeGen/
- README.md                                    # This file
- GLSL_STRUCT_GENERATOR_README.md              # GLSL generator documentation
- OBSERVABLE_PROPERTY_GENERATOR_README.md      # Observable generator documentation
- HelixToolkit.Nex.CodeGen.csproj             # Project file
- GlslStructGenerator.cs                       # GLSL source generator
- GlslStructParser.cs                          # GLSL parsing logic
- CSharpStructGenerator.cs                     # C# code generation
- ObservablePropertyGenerator.cs               # Observable property generator
```

## Testing

Unit tests are available in the companion test project:
```bash
dotnet test HelixToolkit.Nex.CodeGen.Tests
```

The test project includes:
- `GlslStructParserTests` - GLSL parser validation
- `ObservablePropertyGeneratorTests` - Observable property generation tests

## Requirements

- **.NET SDK 8.0 or higher** (for building)
- **C# compiler with source generator support** (Visual Studio 2022, VS Code with C# extension, or Rider)

## Documentation

For detailed usage instructions, examples, and troubleshooting:

- **[GLSL Struct Generator Guide](GLSL_STRUCT_GENERATOR_README.md)** - Complete documentation for shader struct generation
- **[Observable Property Generator Guide](OBSERVABLE_PROPERTY_GENERATOR_README.md)** - Complete documentation for observable property generation

## Architecture

### Source Generator Pipeline

```
Input Files ? Syntax Analysis ? Code Generation ? Compilation
```

Both generators implement the `IIncrementalGenerator` interface for optimal performance:
- Only process changed files
- Cache intermediate results
- Minimize allocations
- Support incremental compilation

### Key Components

1. **GlslStructGenerator** - Main entry point for GLSL processing
2. **GlslStructParser** - Regex-based GLSL struct parser
3. **CSharpStructGenerator** - C# code emission for structs
4. **ObservablePropertyGenerator** - Property generation from fields

## Benefits

? **Reduces Boilerplate** - Eliminates hundreds of lines of repetitive code  
? **Type Safety** - Compile-time validation of generated code  
? **IDE Support** - Full IntelliSense for generated members  
? **Performance** - Incremental generation only processes changes  
? **Maintainability** - Single source of truth for definitions  
? **Consistency** - Enforces naming and pattern conventions  

## Troubleshooting

### Generator not running
- Ensure the project is referenced as an analyzer (`OutputItemType="Analyzer"`)
- Clean and rebuild the solution
- Check for errors in the build output

### IntelliSense not showing generated code
- Build the project to trigger code generation
- Restart your IDE
- Verify `EmitCompilerGeneratedFiles` is enabled
- Check that generated files exist in `obj/Generated/`

### Generated code has errors
- Review the generator documentation for correct usage patterns
- Ensure your source code meets the generator requirements
- Check the build output for generator diagnostics

For detailed troubleshooting, see the individual generator documentation files.

## Contributing

When contributing to this project:

1. **Follow the coding conventions** used in existing generators
2. **Add unit tests** for new functionality
3. **Update documentation** in the relevant README files
4. **Test with incremental builds** to ensure performance
5. **Validate in multiple IDEs** (Visual Studio, VS Code, Rider)

## License

This project is part of the HelixToolkit.Nex graphics framework. See the root repository for license information.

## Related Projects

- **HelixToolkit.Nex** - Main graphics framework library
- **HelixToolkit.Nex.Shaders** - Shader compilation and management
- **HelixToolkit.Nex.Material** - Material system (uses Observable properties)
- **HelixToolkit.Nex.CodeGen.Tests** - Unit tests for this project

## Support

For questions, issues, or feature requests:
- Visit the [HelixToolkit GitHub repository](https://github.com/helix-toolkit/helix-toolkit-nex)
- Check the detailed documentation in the markdown files listed above
- Review the test project for usage examples

---

**Version:** Compatible with .NET Standard 2.0 and higher  
**Generator API:** Microsoft.CodeAnalysis 4.14.0  
**Last Updated:** 2026
