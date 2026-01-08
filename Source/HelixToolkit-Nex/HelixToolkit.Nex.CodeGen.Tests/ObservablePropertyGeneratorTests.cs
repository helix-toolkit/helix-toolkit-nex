using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HelixToolkit.Nex.CodeGen.Tests;

/// <summary>
/// Integration tests for ObservablePropertyGenerator that compile code and verify the output.
/// The generator now targets fields marked with [Observable] and generates public properties.
/// </summary>
public class ObservablePropertyGeneratorTests
{
    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>();

        // Add basic .NET references
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll"))
        );
        references.Add(
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Collections.dll"))
        );
        references.Add(
            MetadataReference.CreateFromFile(
                Path.Combine(assemblyPath, "System.ComponentModel.Primitives.dll")
            )
        );
        references.Add(
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "netstandard.dll"))
        );
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        // Add System.Numerics for Vector4
        references.Add(
            MetadataReference.CreateFromFile(typeof(System.Numerics.Vector4).Assembly.Location)
        );

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    private static (
        Compilation outputCompilation,
        ImmutableArray<Diagnostic> diagnostics
    ) RunGenerator(Compilation inputCompilation)
    {
        var generator = new ObservablePropertyGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)
            driver.RunGeneratorsAndUpdateCompilation(
                inputCompilation,
                out var outputCompilation,
                out var diagnostics
            );

        return (outputCompilation, diagnostics);
    }

    [Fact]
    public void Generator_GeneratesProperty_ForSimpleField()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int _testProperty;
    }
}";
        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        // Verify generated source contains expected code
        var generatedTrees = outputCompilation.SyntaxTrees.Skip(1).ToList();
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].ToString();
        Assert.Contains("public int TestProperty", generatedCode);
        Assert.Contains("get => _testProperty", generatedCode);
        Assert.Contains("set { Set(ref _testProperty, value); }", generatedCode);
        // Should NOT generate a duplicate field
        Assert.DoesNotContain("private int _testProperty", generatedCode);
    }

    [Fact]
    public void Generator_GeneratesProperty_WithDefaultValue()
    {
        var source =
            @"
using System.Numerics;

namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable(Default = ""Vector4.One"")]
        private Vector4 _property1;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        Assert.Contains("public System.Numerics.Vector4 Property1", generatedCode);
        Assert.Contains("get => _property1", generatedCode);
        Assert.Contains("set { Set(ref _property1, value); }", generatedCode);
    }

    [Fact]
    public void Generator_GeneratesProperties_ForMultipleFields()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable(Default = ""1.0f"")]
        private float _property1;

        [Observable(Default = ""2.0f"")]
        private float _property2;

        [Observable]
        private int _property3;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);
        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();

        // Verify all three properties are generated
        Assert.Contains("public float Property1", generatedCode);
        Assert.Contains("public float Property2", generatedCode);
        Assert.Contains("public int Property3", generatedCode);
        Assert.Contains("get => _property1", generatedCode);
        Assert.Contains("get => _property2", generatedCode);
        Assert.Contains("get => _property3", generatedCode);
    }

    [Fact]
    public void Generator_DoesNotGenerate_ForPropertyWithoutBackingField()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        // No [Observable] attribute on field, so no generation
        private int _regularField;
        
        public int ReadOnlyProperty { get; }
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        // Should only have the original source tree, no generated code
        var generatedTrees = outputCompilation.SyntaxTrees.Skip(1).ToList();
        Assert.Empty(generatedTrees);
    }

    [Fact]
    public void Generator_GeneratesCorrectFileName()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    },

    public partial class MyCustomClass : ObservableObject
    {
        [Observable]
        private int _value;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedTree = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault();
        Assert.NotNull(generatedTree);
        Assert.Contains("MyCustomClass.Observable.g.cs", generatedTree.FilePath);
    }

    [Fact]
    public void Generator_PreservesNamespace()
    {
        var source =
            @"
namespace My.Custom.Namespace.Test
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int _value;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        Assert.Contains("namespace My.Custom.Namespace.Test", generatedCode);
    }

    [Fact]
    public void Generator_GeneratesAutoGeneratedComment()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int _value;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        Assert.Contains("// <auto-generated/>", generatedCode);
        Assert.Contains("#nullable enable", generatedCode);
    }

    [Fact]
    public void Generator_ConvertsCamelCaseFieldToPascalCaseProperty()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int _myProperty;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        Assert.Contains("public int MyProperty", generatedCode);
        Assert.Contains("get => _myProperty", generatedCode);
    }

    [Fact]
    public void Generator_HandlesFieldWithoutUnderscore()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int myProperty;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        // Should capitalize first letter even without underscore
        Assert.Contains("public int MyProperty", generatedCode);
        Assert.Contains("get => myProperty", generatedCode);
    }

    [Fact]
    public void Generator_IgnoresFieldsWithoutAttribute()
    {
        var source =
            @"
namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class TestClass : ObservableObject
    {
        [Observable]
        private int _observableField;

        private int _regularField;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();
        Assert.Contains("public int ObservableField", generatedCode);
        Assert.DoesNotContain("public int RegularField", generatedCode);
        Assert.DoesNotContain("_regularField", generatedCode);
    }

    [Fact]
    public void Generator_WorksWithVector4AndDefaultValue()
    {
        var source =
            @"
using System.Numerics;

namespace TestNamespace
{
    public abstract class ObservableObject 
    {
        protected void Set<T>(ref T field, T value) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ObservableAttribute : System.Attribute
    {
        public string? Default { get; set; }
    }

    public partial class MaterialProperties : ObservableObject
    {
        [Observable(Default = ""Vector4.One"")]
        private Vector4 _albedo;

        [Observable]
        private float _metallic;

        [Observable(Default = ""1.0f"")]
        private float _roughness;
    }
}";

        var compilation = CreateCompilation(source);
        var (outputCompilation, diagnostics) = RunGenerator(compilation);

        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).First().ToString();

        // Verify properties are generated correctly
        Assert.Contains("public System.Numerics.Vector4 Albedo", generatedCode);
        Assert.Contains("get => _albedo", generatedCode);
        Assert.Contains("set { Set(ref _albedo, value); }", generatedCode);

        Assert.Contains("public float Metallic", generatedCode);
        Assert.Contains("get => _metallic", generatedCode);

        Assert.Contains("public float Roughness", generatedCode);
        Assert.Contains("get => _roughness", generatedCode);
    }
}
