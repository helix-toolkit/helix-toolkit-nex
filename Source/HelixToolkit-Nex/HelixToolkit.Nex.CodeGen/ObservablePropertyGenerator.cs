using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HelixToolkit.Nex.CodeGen;

/// <summary>
/// Source generator that converts fields marked with [Observable] attribute
/// into properties with backing fields that notify property changes.
/// </summary>
[Generator]
public class ObservablePropertyGenerator : IIncrementalGenerator
{
    private const string AttributeNamespace = "HelixToolkit.Nex";
    private const string AttributeName = "ObservableAttribute";
    private const string AttributeShortName = "Observable";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all fields with Observable attribute
        var fieldDeclarations = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidateField(s),
                transform: static (ctx, _) => GetFieldToGenerate(ctx)
            )
            .Where(static m => m is not null);

        // Combine and generate
        var compilationAndFields = context.CompilationProvider.Combine(fieldDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndFields,
            static (spc, source) => Execute(source.Left, source.Right!, spc)
        );
    }

    private static bool IsCandidateField(SyntaxNode node)
    {
        return node is FieldDeclarationSyntax field && field.AttributeLists.Count > 0;
    }

    private static FieldInfo? GetFieldToGenerate(GeneratorSyntaxContext context)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;

        // Check if field has Observable attribute
        foreach (var attributeList in fieldDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(attribute).Symbol;
                if (symbol is IMethodSymbol attributeSymbol)
                {
                    var attributeClass = attributeSymbol.ContainingType;
                    var fullName = attributeClass.ToDisplayString();

                    if (
                        fullName == $"{AttributeNamespace}.{AttributeName}"
                        || attributeClass.Name == AttributeName
                        || attributeClass.Name == AttributeShortName
                    )
                    {
                        // Get the field symbol
                        var variable = fieldDeclaration.Declaration.Variables.FirstOrDefault();
                        if (variable == null)
                            return null;

                        var fieldSymbol =
                            context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                        if (fieldSymbol == null)
                            return null;

                        var containingType = fieldSymbol.ContainingType;

                        // Extract default value from attribute
                        string? defaultValue = null;
                        if (attribute.ArgumentList != null)
                        {
                            foreach (var arg in attribute.ArgumentList.Arguments)
                            {
                                if (
                                    arg.NameEquals?.Name.Identifier.Text == "Default"
                                    || arg.NameEquals?.Name.Identifier.Text == "default"
                                )
                                {
                                    var expression = arg.Expression;
                                    if (expression is LiteralExpressionSyntax literal)
                                    {
                                        defaultValue = literal.Token.ValueText;
                                    }
                                    else
                                    {
                                        defaultValue = expression.ToString();
                                    }
                                }
                            }
                        }

                        // Generate property name from field name (remove leading underscore and capitalize)
                        string propertyName = GeneratePropertyName(fieldSymbol.Name);

                        return new FieldInfo(
                            fieldSymbol.Name,
                            propertyName,
                            fieldSymbol.Type.ToDisplayString(),
                            containingType.Name,
                            containingType.ContainingNamespace.ToDisplayString(),
                            defaultValue
                        );
                    }
                }
            }
        }

        return null;
    }

    private static string GeneratePropertyName(string fieldName)
    {
        // Remove leading underscore if present
        string name = fieldName.StartsWith("_") ? fieldName.Substring(1) : fieldName;

        // Capitalize first letter
        if (name.Length > 0 && char.IsLower(name[0]))
        {
            name = char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        return name;
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<FieldInfo?> fields,
        SourceProductionContext context
    )
    {
        if (fields.IsDefaultOrEmpty)
            return;

        // Group fields by containing type
        var fieldsByType = fields
            .Where(p => p != null)
            .GroupBy(p => (p!.ContainingNamespace, p.ContainingType));

        foreach (var group in fieldsByType)
        {
            var (namespaceName, typeName) = group.Key;
            var typeFields = group.ToList();

            var source = GeneratePartialClass(namespaceName, typeName, typeFields!);
            context.AddSource(
                $"{typeName}.Observable.g.cs",
                SourceText.From(source, Encoding.UTF8)
            );
        }
    }

    private static string GeneratePartialClass(
        string namespaceName,
        string typeName,
        List<FieldInfo> fields
    )
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    partial class {typeName}");
        sb.AppendLine("    {");

        foreach (var field in fields)
        {
            // Generate property (field already exists in source code)
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Gets or sets the {field.PropertyName}.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public {field.FieldType} {field.PropertyName}");
            sb.AppendLine("        {");
            sb.AppendLine($"            get => {field.FieldName};");
            sb.AppendLine($"            set {{ Set(ref {field.FieldName}, value); }}");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed class FieldInfo(
        string fieldName,
        string propertyName,
        string fieldType,
        string containingType,
        string containingNamespace,
        string? defaultValue
    )
    {
        public string FieldName = fieldName;
        public string PropertyName = propertyName;
        public string FieldType = fieldType;
        public string ContainingType = containingType;
        public string ContainingNamespace = containingNamespace;
        public string? DefaultValue = defaultValue;
    }
}
