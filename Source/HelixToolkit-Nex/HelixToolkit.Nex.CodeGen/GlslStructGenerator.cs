using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace HelixToolkit.Nex.CodeGen;

/// <summary>
/// Source generator that extracts struct definitions from GLSL files
/// and generates equivalent C# structs with proper memory layout.
/// </summary>
[Generator]
public class GlslStructGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get all additional files (GLSL files)
        var glslFiles = context
            .AdditionalTextsProvider.Where(static file =>
                file.Path.EndsWith(".glsl", System.StringComparison.OrdinalIgnoreCase)
            )
            .Select(
                static (text, cancellationToken) =>
                {
                    var content = text.GetText(cancellationToken)?.ToString() ?? string.Empty;
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(text.Path);
                    return (FileName: fileName, Content: content);
                }
            );

        // Parse structs from GLSL files
        var parsedStructs = glslFiles
            .Select(
                static (file, cancellationToken) =>
                {
                    var parser = new GlslStructParser();
                    var structs = parser.ParseStructs(file.Content);
                    return (file.FileName, Structs: structs);
                }
            )
            .Where(static result => result.Structs.Count > 0);

        // Generate C# code for each file
        context.RegisterSourceOutput(
            parsedStructs,
            static (spc, source) =>
            {
                var generator = new CSharpStructGenerator();
                var csharpCode = generator.Generate(source.FileName, source.Structs);

                spc.AddSource(
                    $"{source.FileName}.Structs.g.cs",
                    SourceText.From(csharpCode, Encoding.UTF8)
                );
            }
        );
    }
}
