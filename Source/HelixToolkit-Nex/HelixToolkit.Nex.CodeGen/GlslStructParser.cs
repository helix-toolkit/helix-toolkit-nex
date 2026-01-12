using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HelixToolkit.Nex.CodeGen;

/// <summary>
/// Parses GLSL code to extract struct definitions.
/// </summary>
public class GlslStructParser
{
    private static readonly Regex StructPattern = new Regex(
        @"struct\s+(\w+)\s*\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline
    );

    private static readonly Regex FieldPattern = new Regex(
        @"^\s*(\w+)\s+(\w+)(?:\[(\d+)\])?\s*;(?:\s*//\s*(.*))?$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public List<GlslStruct> ParseStructs(string glslCode)
    {
        var structs = new List<GlslStruct>();

        // Remove multi-line comments first
        glslCode = Regex.Replace(glslCode, @"/\*.*?\*/", "", RegexOptions.Singleline);

        var matches = StructPattern.Matches(glslCode);

        foreach (Match match in matches)
        {
            var structName = match.Groups[1].Value;
            var structBody = match.Groups[2].Value;

            var fields = ParseFields(structBody);

            if (fields.Count > 0)
            {
                structs.Add(new GlslStruct(structName, fields));
            }
        }

        return structs;
    }

    private List<GlslField> ParseFields(string structBody)
    {
        var fields = new List<GlslField>();
        var lines = structBody.Split(
            new[] { '\n', '\r' },
            System.StringSplitOptions.RemoveEmptyEntries
        );

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//"))
                continue;

            var match = FieldPattern.Match(trimmedLine);
            if (match.Success)
            {
                var glslType = match.Groups[1].Value;
                var fieldName = match.Groups[2].Value;
                var arraySize = match.Groups[3].Success ? match.Groups[3].Value : null;
                var comment = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null;

                fields.Add(new GlslField(glslType, fieldName, arraySize, comment));
            }
        }

        return fields;
    }
}

/// <summary>
/// Represents a GLSL struct definition.
/// </summary>
public class GlslStruct
{
    public string Name { get; }
    public List<GlslField> Fields { get; }

    public GlslStruct(string name, List<GlslField> fields)
    {
        Name = name;
        Fields = fields;
    }
}

/// <summary>
/// Represents a field in a GLSL struct.
/// </summary>
public class GlslField
{
    public string GlslType { get; }
    public string Name { get; }
    public string? ArraySize { get; }
    public string? Comment { get; }

    public GlslField(string glslType, string name, string? arraySize, string? comment)
    {
        GlslType = glslType;
        Name = name;
        ArraySize = arraySize;
        Comment = comment;
    }

    public bool IsArray => !string.IsNullOrEmpty(ArraySize);
}
