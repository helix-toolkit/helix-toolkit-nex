using System.Runtime.InteropServices;
using HelixToolkit.Nex.CodeGen;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Unit tests for verifying C# struct generation from GLSL files.
/// </summary>
[TestClass]
public class CSharpStructGeneratorTests
{
    private const string PBRFunctionsGlsl =
        @"
// PBR Material Structure
struct PBRMaterial {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    vec3 normal;           // World-space normal (normalized)
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity/alpha [0..1]
};

// Light Structure
struct Light {
    vec3 position;         // Light position (world space)
    vec3 direction;        // Light direction (for directional/spot lights)
    vec3 color;            // Light color (linear RGB)
    float intensity;       // Light intensity
    int type;              // Light type: 0=directional, 1=point, 2=spot
    float range;           // Light range (for point/spot lights)
    float innerConeAngle;  // Inner cone angle (for spot lights)
    float outerConeAngle;  // Outer cone angle (for spot lights)
};
";

    [TestMethod]
    [TestCategory("CodeGen")]
    public void ParsePBRMaterialStruct_ShouldExtractAllFields()
    {
        // Arrange
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(PBRFunctionsGlsl);

        // Assert
        Assert.IsTrue(structs.Count >= 1, "Should find at least one struct (PBRMaterial)");

        var pbrMaterial = structs.FirstOrDefault(s => s.Name == "PBRMaterial");
        Assert.IsNotNull(pbrMaterial, "Should find PBRMaterial struct");
        Assert.AreEqual(7, pbrMaterial.Fields.Count, "PBRMaterial should have 7 fields");

        // Verify field names and types
        Assert.AreEqual("albedo", pbrMaterial.Fields[0].Name);
        Assert.AreEqual("vec3", pbrMaterial.Fields[0].GlslType);

        Assert.AreEqual("metallic", pbrMaterial.Fields[1].Name);
        Assert.AreEqual("float", pbrMaterial.Fields[1].GlslType);

        Assert.AreEqual("roughness", pbrMaterial.Fields[2].Name);
        Assert.AreEqual("float", pbrMaterial.Fields[2].GlslType);

        Assert.AreEqual("ao", pbrMaterial.Fields[3].Name);
        Assert.AreEqual("float", pbrMaterial.Fields[3].GlslType);

        Assert.AreEqual("normal", pbrMaterial.Fields[4].Name);
        Assert.AreEqual("vec3", pbrMaterial.Fields[4].GlslType);

        Assert.AreEqual("emissive", pbrMaterial.Fields[5].Name);
        Assert.AreEqual("vec3", pbrMaterial.Fields[5].GlslType);

        Assert.AreEqual("opacity", pbrMaterial.Fields[6].Name);
        Assert.AreEqual("float", pbrMaterial.Fields[6].GlslType);
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void ParseLightStruct_ShouldExtractAllFields()
    {
        // Arrange
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(PBRFunctionsGlsl);

        // Assert
        Assert.IsTrue(structs.Count >= 2, "Should find at least two structs");

        var light = structs.FirstOrDefault(s => s.Name == "Light");
        Assert.IsNotNull(light, "Should find Light struct");
        Assert.AreEqual(8, light.Fields.Count, "Light should have 8 fields");

        // Verify field names and types
        Assert.AreEqual("position", light.Fields[0].Name);
        Assert.AreEqual("vec3", light.Fields[0].GlslType);

        Assert.AreEqual("direction", light.Fields[1].Name);
        Assert.AreEqual("vec3", light.Fields[1].GlslType);

        Assert.AreEqual("color", light.Fields[2].Name);
        Assert.AreEqual("vec3", light.Fields[2].GlslType);

        Assert.AreEqual("intensity", light.Fields[3].Name);
        Assert.AreEqual("float", light.Fields[3].GlslType);

        Assert.AreEqual("type", light.Fields[4].Name);
        Assert.AreEqual("int", light.Fields[4].GlslType);

        Assert.AreEqual("range", light.Fields[5].Name);
        Assert.AreEqual("float", light.Fields[5].GlslType);

        Assert.AreEqual("innerConeAngle", light.Fields[6].Name);
        Assert.AreEqual("float", light.Fields[6].GlslType);

        Assert.AreEqual("outerConeAngle", light.Fields[7].Name);
        Assert.AreEqual("float", light.Fields[7].GlslType);
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GeneratePBRMaterial_ShouldCreateValidCSharpCode()
    {
        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var pbrMaterial = structs.FirstOrDefault(s => s.Name == "PBRMaterial");
        Assert.IsNotNull(pbrMaterial);

        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", new List<GlslStruct> { pbrMaterial });

        // Assert
        Assert.IsNotNull(code);
        Assert.IsTrue(code.Contains("struct PBRMaterial"), "Should contain struct declaration");
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Albedo"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(code.Contains("float Metallic"), "Should map float to float");
        Assert.IsTrue(code.Contains("float Roughness"), "Should map float to float");
        Assert.IsTrue(code.Contains("float Ao"), "Should convert 'ao' to 'Ao' (PascalCase)");
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Normal"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Emissive"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(code.Contains("float Opacity"), "Should map float to float");
        Assert.IsTrue(
            code.Contains("[StructLayout(LayoutKind.Sequential)]"),
            "Should have StructLayout attribute"
        );
        Assert.IsTrue(
            code.Contains("namespace HelixToolkit.Nex.Shaders"),
            "Should be in correct namespace"
        );
        Assert.IsTrue(
            code.Contains("public static readonly unsafe int SizeInBytes = sizeof(PBRMaterial)"),
            "Should include SizeInBytes constant"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GenerateLight_ShouldCreateValidCSharpCode()
    {
        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var light = structs.FirstOrDefault(s => s.Name == "Light");
        Assert.IsNotNull(light);

        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", new List<GlslStruct> { light });

        // Assert
        Assert.IsNotNull(code);
        Assert.IsTrue(code.Contains("struct Light"), "Should contain struct declaration");
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Position"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Direction"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(code.Contains("System.Numerics.Vector3 Color"), "Should map vec3 to Vector3");
        Assert.IsTrue(code.Contains("float Intensity"), "Should map float to float");
        Assert.IsTrue(code.Contains("int Type"), "Should map int to int");
        Assert.IsTrue(code.Contains("float Range"), "Should map float to float");
        Assert.IsTrue(code.Contains("float InnerConeAngle"), "Should convert to PascalCase");
        Assert.IsTrue(code.Contains("float OuterConeAngle"), "Should convert to PascalCase");
        Assert.IsTrue(
            code.Contains("[StructLayout(LayoutKind.Sequential)]"),
            "Should have StructLayout attribute"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GeneratedCode_ShouldIncludeComments()
    {
        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var pbrMaterial = structs.FirstOrDefault(s => s.Name == "PBRMaterial");
        Assert.IsNotNull(pbrMaterial);

        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", new List<GlslStruct> { pbrMaterial });

        // Assert
        Assert.IsTrue(
            code.Contains("/// <summary>Base color (sRGB)</summary>"),
            "Should include field comment for albedo"
        );
        Assert.IsTrue(
            code.Contains("/// <summary>Metallic factor [0..1]</summary>"),
            "Should include field comment for metallic"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GeneratedCode_ShouldIncludeFileHeader()
    {
        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", structs);

        // Assert
        Assert.IsTrue(
            code.StartsWith("// <auto-generated/>"),
            "Should start with auto-generated comment"
        );
        Assert.IsTrue(
            code.Contains("// This file was generated from PBRFunctions.glsl"),
            "Should include source file reference"
        );
        Assert.IsTrue(code.Contains("#nullable enable"), "Should enable nullable reference types");
        Assert.IsTrue(
            code.Contains("using System.Runtime.InteropServices;"),
            "Should include required using statements"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GenerateWithArrayField_ShouldCreateMarshalAsAttribute()
    {
        // Arrange
        var glslCode =
            @"
struct TestStruct {
    float values[16];
    vec3 positions[4];
};
";
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(glslCode);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("TestArrays", structs);

        // Assert
        Assert.IsTrue(
            code.Contains("[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]"),
            "Should include MarshalAs for array"
        );
        Assert.IsTrue(
            code.Contains("public float[]? Values;"),
            "Should create nullable array field"
        );
        Assert.IsTrue(
            code.Contains("[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]"),
            "Should include MarshalAs for vec3 array"
        );
        Assert.IsTrue(
            code.Contains("public System.Numerics.Vector3[]? Positions;"),
            "Should create Vector3 array"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GenerateMultipleStructs_ShouldIncludeAll()
    {
        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", structs);

        // Assert
        Assert.IsTrue(structs.Count >= 2, "Should parse at least 2 structs");
        Assert.IsTrue(code.Contains("struct PBRMaterial"), "Should include PBRMaterial");
        Assert.IsTrue(code.Contains("struct Light"), "Should include Light");
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void TypeMapping_ShouldMapAllGLSLTypes()
    {
        // Arrange
        var glslCode =
            @"
struct TypeTest {
    float scalarFloat;
    double scalarDouble;
    int scalarInt;
    uint scalarUint;
    bool scalarBool;
    vec2 vector2;
    vec3 vector3;
    vec4 vector4;
    mat2 matrix2;
    mat3 matrix3;
    mat4 matrix4;
};
";
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(glslCode);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("TypeMapping", structs);

        // Assert
        Assert.IsTrue(code.Contains("float ScalarFloat"), "Should map float");
        Assert.IsTrue(code.Contains("double ScalarDouble"), "Should map double");
        Assert.IsTrue(code.Contains("int ScalarInt"), "Should map int");
        Assert.IsTrue(code.Contains("uint ScalarUint"), "Should map uint");
        Assert.IsTrue(code.Contains("bool ScalarBool"), "Should map bool");
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector2 Vector2"),
            "Should map vec2 to Vector2"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector3 Vector3"),
            "Should map vec3 to Vector3"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Vector4 Vector4"),
            "Should map vec4 to Vector4"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Matrix3x2 Matrix2"),
            "Should map mat2 to Matrix3x2"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Matrix4x4 Matrix3"),
            "Should map mat3 to Matrix4x4"
        );
        Assert.IsTrue(
            code.Contains("System.Numerics.Matrix4x4 Matrix4"),
            "Should map mat4 to Matrix4x4"
        );
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void FieldNameConversion_ShouldConvertToPascalCase()
    {
        // Arrange
        var glslCode =
            @"
struct NamingTest {
    float camelCase;
    float PascalCase;
    float snake_case;
    float ao;
};
";
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(glslCode);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("Naming", structs);

        // Assert
        Assert.IsTrue(code.Contains("float CamelCase"), "Should convert camelCase to PascalCase");
        Assert.IsTrue(code.Contains("float PascalCase"), "Should keep PascalCase");
        Assert.IsTrue(
            code.Contains("float Snake_case"),
            "Should capitalize first letter of snake_case"
        );
        Assert.IsTrue(code.Contains("float Ao"), "Should capitalize single lowercase word");
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void ReadActualPBRFunctionsFile_ShouldParseSuccessfully()
    {
        // Arrange
        var glslFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "HelixToolkit.Nex.Shaders",
            "Headers",
            "PBRFunctions.glsl"
        );

        // Skip if file doesn't exist (for CI/CD environments)
        if (!File.Exists(glslFilePath))
        {
            Assert.Inconclusive($"PBRFunctions.glsl not found at {glslFilePath}");
            return;
        }

        var glslContent = File.ReadAllText(glslFilePath);
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(glslContent);

        // Assert
        Assert.IsTrue(structs.Count >= 2, "Should find at least 2 structs in PBRFunctions.glsl");
        Assert.IsTrue(structs.Any(s => s.Name == "PBRMaterial"), "Should find PBRMaterial struct");
        Assert.IsTrue(structs.Any(s => s.Name == "Light"), "Should find Light struct");
    }

    [TestMethod]
    [TestCategory("CodeGen")]
    public void GeneratedStruct_SizeInBytes_ShouldBeCorrect()
    {
        // This test verifies the generated code would produce correct struct sizes
        // We can't directly compile and check, but we verify the pattern is correct

        // Arrange
        var parser = new GlslStructParser();
        var structs = parser.ParseStructs(PBRFunctionsGlsl);
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", structs);

        // Assert
        // Verify each struct has a SizeInBytes member
        foreach (var glslStruct in structs)
        {
            Assert.IsTrue(
                code.Contains(
                    $"public static readonly unsafe int SizeInBytes = sizeof({glslStruct.Name})"
                ),
                $"Generated code should include SizeInBytes for {glslStruct.Name}"
            );
        }
    }
}
