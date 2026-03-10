using System.Linq;
using Xunit;

namespace HelixToolkit.Nex.CodeGen.Tests;

public class GlslStructParserTests
{
    [Fact]
    public void ParseStructs_PBRMaterial_ExtractsFieldsCorrectly()
    {
        // Arrange
        var glslCode =
            @"
@code_gen
struct PBRMaterial {
    vec3 albedo;           // Base color (sRGB)
    float metallic;        // Metallic factor [0..1]
    float roughness;       // Roughness factor [0..1]
    float ao;              // Ambient occlusion [0..1]
    vec3 normal;           // World-space normal (normalized)
    vec3 emissive;         // Emissive color
    float opacity;         // Opacity/alpha [0..1]
    float _padding;         // Padding test
};
";
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(glslCode);

        // Assert
        Assert.Single(structs);

        var pbrMaterial = structs[0];
        Assert.Equal("PBRMaterial", pbrMaterial.Name);
        Assert.Equal(8, pbrMaterial.Fields.Count);

        Assert.Equal("albedo", pbrMaterial.Fields[0].Name);
        Assert.Equal("vec3", pbrMaterial.Fields[0].GlslType);
        Assert.Contains("Base color", pbrMaterial.Fields[0].Comment);

        Assert.Equal("metallic", pbrMaterial.Fields[1].Name);
        Assert.Equal("float", pbrMaterial.Fields[1].GlslType);
    }

    [Fact]
    public void ParseStructs_Light_ExtractsAllFields()
    {
        // Arrange
        var glslCode =
            @"
@code_gen
struct Light {
    vec3 position;
    vec3 direction;
    vec3 color;
    float intensity;
    int type;
    float range;
    float innerConeAngle;
    float outerConeAngle;
};
";
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(glslCode);

        // Assert
        Assert.Single(structs);
        var light = structs[0];
        Assert.Equal("Light", light.Name);
        Assert.Equal(8, light.Fields.Count);
    }

    [Fact]
    public void ParseStructs_ArrayField_DetectsArray()
    {
        // Arrange
        var glslCode =
            @"
@code_gen
struct TestStruct {
    float values[16];
    vec3 positions[8];
};
";
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(glslCode);

        // Assert
        Assert.Single(structs);
        var testStruct = structs[0];
        Assert.Equal(2, testStruct.Fields.Count);

        Assert.True(testStruct.Fields[0].IsArray);
        Assert.Equal("16", testStruct.Fields[0].ArraySize);

        Assert.True(testStruct.Fields[1].IsArray);
        Assert.Equal("8", testStruct.Fields[1].ArraySize);
    }

    [Fact]
    public void Generate_PBRMaterial_CreatesValidCSharp()
    {
        // Arrange
        var structs = new System.Collections.Generic.List<GlslStruct>
        {
            new GlslStruct(
                "PBRMaterial",
                new System.Collections.Generic.List<GlslField>
                {
                    new GlslField("vec3", "albedo", null, "Base color"),
                    new GlslField("float", "metallic", null, "Metallic factor"),
                    new GlslField("float", "roughness", null, null),
                    new GlslField("float", "_padding", null, null),
                }
            ),
        };
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("PBRFunctions", structs);

        // Assert
        Assert.Contains("struct PBRMaterial", code);
        Assert.Contains("System.Numerics.Vector3 Albedo", code);
        Assert.Contains("public float Metallic", code);
        Assert.Contains("public float Roughness", code);
        Assert.Contains("private float _padding", code);
        Assert.Contains("[StructLayout(LayoutKind.Sequential, Pack = 16)]", code);
        Assert.Contains("namespace HelixToolkit.Nex.Shaders", code);
        Assert.Contains(
            "public static readonly unsafe uint SizeInBytes = (uint)sizeof(PBRMaterial);",
            code
        );
    }

    [Fact]
    public void Generate_IncludesSizeInBytes_ForAllStructs()
    {
        // Arrange
        var structs = new System.Collections.Generic.List<GlslStruct>
        {
            new GlslStruct(
                "SimpleStruct",
                new System.Collections.Generic.List<GlslField>
                {
                    new GlslField("float", "value", null, null),
                }
            ),
            new GlslStruct(
                "ComplexStruct",
                new System.Collections.Generic.List<GlslField>
                {
                    new GlslField("vec4", "color", null, null),
                    new GlslField("mat4", "transform", null, null),
                }
            ),
        };
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("TestStructs", structs);

        // Assert
        Assert.Contains(
            "public static readonly unsafe uint SizeInBytes = (uint)sizeof(SimpleStruct);",
            code
        );
        Assert.Contains(
            "public static readonly unsafe uint SizeInBytes = (uint)sizeof(ComplexStruct);",
            code
        );
        Assert.Contains("/// <summary>", code);
        Assert.Contains("/// The size of the <see cref=\"SimpleStruct\"/> struct, in bytes.", code);
        Assert.Contains(
            "/// The size of the <see cref=\"ComplexStruct\"/> struct, in bytes.",
            code
        );
    }

    [Fact]
    public void Generate_ArrayField_UnrollsAndCreatesAccessors()
    {
        // Arrange
        var structs = new System.Collections.Generic.List<GlslStruct>
        {
            new GlslStruct(
                "ArrayStruct",
                new System.Collections.Generic.List<GlslField>
                {
                    new GlslField("vec4", "planes", "4", "Frustum planes"),
                }
            ),
        };
        var generator = new CSharpStructGenerator();

        // Act
        var code = generator.Generate("ArrayStructFile", structs);

        // Assert
        Assert.Contains("public System.Numerics.Vector4 Planes_0;", code);
        Assert.Contains("public System.Numerics.Vector4 Planes_1;", code);
        Assert.Contains("public System.Numerics.Vector4 Planes_2;", code);
        Assert.Contains("public System.Numerics.Vector4 Planes_3;", code);

        Assert.Contains("public System.Numerics.Vector4 GetPlanes(int index)", code);
        Assert.Contains("public void SetPlanes(int index, System.Numerics.Vector4 value)", code);
        Assert.Contains(
            "public void SetPlanes(int index, ref System.Numerics.Vector4 value)",
            code
        );
        Assert.Contains("case 0: Planes_0 = value; break;", code);
        Assert.Contains("case 3: Planes_3 = value; break;", code);
        Assert.Contains("0 => Planes_0,", code);
        Assert.Contains("3 => Planes_3,", code);
    }

    [Fact]
    public void ParseStructs_MissingCodeGen_ReturnsEmpty()
    {
        // Arrange
        var glslCode =
            @"
struct NoGen {
    float val;
};
";
        var parser = new GlslStructParser();

        // Act
        var structs = parser.ParseStructs(glslCode);

        // Assert
        Assert.Empty(structs);
    }
}
