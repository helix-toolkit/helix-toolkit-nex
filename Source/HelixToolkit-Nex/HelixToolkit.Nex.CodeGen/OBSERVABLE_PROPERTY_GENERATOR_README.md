# ObservablePropertyGenerator - Usage Guide

## Overview

The `ObservablePropertyGenerator` is a C# source generator that automatically creates observable properties from private fields marked with the `[Observable]` attribute. It follows the pattern used by CommunityToolkit.Mvvm.

## How It Works

When you mark a private field with `[Observable]`, the generator:
1. Creates a public property with the same name (PascalCase)
2. Implements `INotifyPropertyChanged` using the `Set()` method from `ObservableObject`
3. Generates the property in a partial class file

### Example

**Source Code:**
```csharp
public partial class UnlitMaterialProperties : MaterialProperties
{
    [Observable(Default = "Vector4.One")]
    private Vector4 _albedo;

    [Observable(Default = "TextureResource.Null")]
    private TextureResource _albedoTexture;
}
```

**Generated Code:**
```csharp
// UnlitMaterialProperties.Observable.g.cs
partial class UnlitMaterialProperties
{
    public System.Numerics.Vector4 Albedo
    {
        get => _albedo;
        set { Set(ref _albedo, value); }
    }

    public TextureResource AlbedoTexture
    {
        get => _albedoTexture;
        set { Set(ref _albedoTexture, value); }
    }
}
```

## IntelliSense Support

To make IntelliSense recognize the generated properties, your project file should include:

```xml
<PropertyGroup>
  <!-- Enable source generator diagnostics and emit generated files -->
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<!-- Include generated files in the project for IntelliSense -->
<ItemGroup>
  <Compile Include="$(CompilerGeneratedFilesOutputPath)\**\*.cs" Visible="false" />
</ItemGroup>
```

### If IntelliSense Doesn't Recognize Generated Properties

1. **Build the project** to trigger the source generator:
   ```bash
   dotnet build
   ```

2. **Restart your IDE**:
   - Visual Studio: Close and reopen the solution
   - VS Code: Reload window (Ctrl+Shift+P ? "Developer: Reload Window")

3. **Check generated files** are created in:
   ```
   obj/Generated/HelixToolkit.Nex.CodeGen/HelixToolkit.Nex.CodeGen.ObservablePropertyGenerator/
   ```

4. **Clean and rebuild** if needed:
   ```bash
   dotnet clean
   dotnet build
   ```

## Naming Conventions

The generator converts field names to property names:

| Field Name | Property Name |
|------------|---------------|
| `_albedo` | `Albedo` |
| `_baseColor` | `BaseColor` |
| `myField` | `MyField` |
| `_metallicRoughnessTexture` | `MetallicRoughnessTexture` |

Rules:
1. Remove leading underscore (`_`)
2. Capitalize first letter
3. Keep the rest of the name as-is

## Default Values

You can specify default values for fields using the `Default` parameter:

```csharp
[Observable(Default = "Vector4.One")]
private Vector4 _albedo;

[Observable(Default = "1.0f")]
private float _roughness;

[Observable(Default = "TextureResource.Null")]
private TextureResource _texture;

[Observable]  // No default value
private float _metallic;
```

The default value should be a valid C# expression.

## Requirements

1. The containing class must be `partial`
2. The field must be marked with `[Observable]` attribute
3. The class must inherit from `ObservableObject` or provide a `Set<T>()` method
4. The field must be a field (not a property)

## Troubleshooting

### Error: "The type already contains a definition for 'PropertyName'"

This means you're trying to generate a property that already exists. Make sure:
- You're using **fields** (not properties) with the `[Observable]` attribute
- You haven't manually declared the property in your source code

### Error: "Attribute 'Observable' is not valid on this declaration type"

The `[Observable]` attribute targets fields only. Make sure you're applying it to a field:
```csharp
// ? Correct
[Observable]
private int _myField;

// ? Wrong - don't use on properties
[Observable]
public int MyProperty { get; set; }
```

### IntelliSense doesn't recognize generated properties

1. Build the project
2. Check that `EmitCompilerGeneratedFiles` is set to `true` in your `.csproj`
3. Restart your IDE
4. Verify generated files exist in `obj/Generated/`

### Generated files not visible in Solution Explorer

Generated files are intentionally hidden (`Visible="false"` in project file) but are included for compilation and IntelliSense. You can find them in:
```
obj/Generated/HelixToolkit.Nex.CodeGen/HelixToolkit.Nex.CodeGen.ObservablePropertyGenerator/
```

## Project Setup

To use the generator in a project, add these references to your `.csproj`:

```xml
<ItemGroup>
  <!-- Reference to ObservableObject and ObservableAttribute -->
  <ProjectReference Include="..\HelixToolkit.Nex\HelixToolkit.Nex.csproj" />
  
  <!-- Source generator as analyzer -->
  <ProjectReference Include="..\HelixToolkit.Nex.CodeGen\HelixToolkit.Nex.CodeGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Best Practices

1. **Use descriptive field names** - They become your public property names
2. **Group related fields** - Keep fields that generate related properties together
3. **Initialize fields with default values** - The `Default` parameter in the attribute is for documentation; actual initialization should be in the field declaration
4. **Make classes partial** - Required for the generator to add properties
5. **Inherit from ObservableObject** - Provides the `Set()` method needed for change notification

## Examples

### Simple Material Properties
```csharp
public partial class SimpleMaterialProperties : MaterialProperties
{
    [Observable(Default = "Color4.White")]
    private Color4 _color;

    [Observable]
    private float _opacity;
}
```

### PBR Material Properties
```csharp
public partial class PbrMaterialProperties : MaterialProperties
{
    [Observable(Default = "Vector4.One")]
    private Vector4 _baseColor;

    [Observable]
    private float _metallic;

    [Observable(Default = "1.0f")]
    private float _roughness;

    [Observable(Default = "TextureResource.Null")]
    private TextureResource _baseColorTexture;
}
```

## Generator Implementation Details

- **Target Framework**: .NET Standard 2.0 (for maximum compatibility)
- **Generator Type**: Incremental Generator (`IIncrementalGenerator`)
- **Performance**: Only processes fields with `[Observable]` attribute
- **Output**: Generates one `.g.cs` file per class with observable fields

## Related Files

- `ObservableObject.cs` - Base class providing `Set()` method
- `ObservableAttribute.cs` - Attribute definition
- `ObservablePropertyGenerator.cs` - Source generator implementation
- `ObservablePropertyGeneratorTests.cs` - Unit tests
