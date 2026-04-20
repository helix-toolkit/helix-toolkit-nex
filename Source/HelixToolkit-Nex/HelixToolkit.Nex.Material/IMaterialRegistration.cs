using System.Text;

namespace HelixToolkit.Nex.Material;

internal interface IMaterialRegistration
{
    /// <summary>
    /// Gets the name associated with the current registration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the unique ID of this material type. Used for shader pipeline selection.
    /// </summary>
    MaterialTypeId TypeId { get; }
    /// <summary>
    /// Gets a value indicating whether pointer ring rendering is supported.
    /// </summary>
    bool SupportPointerRing { get; }
    /// <summary>
    /// Generates the implementation code for color output.
    /// </summary>
    /// <returns>A string containing the generated implementation code for color output.</returns>
    string? GetColorOutputImplCode();
}

internal static class MaterialRegistrationExtensions
{
    public static string BuildColorOutputImpl(
        this IEnumerable<IMaterialRegistration> registrations,
        string fragTemplate
    )
    {
        var regs = registrations
            .Where(x => x.GetColorOutputImplCode() is not null)
            .OrderBy(r => r.TypeId)
            .ToList();

        var sb = new StringBuilder();

        // Generate individual color output functions per material type
        foreach (var reg in regs)
        {
            sb.AppendLine($"// Color output for material type: {reg.Name} (ID: {(uint)reg.TypeId})");
            sb.AppendLine($"vec4 outputColor_{reg.Name}()");
            sb.AppendLine("{");

            var lines = reg.GetColorOutputImplCode()!.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine($"    {line}");
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Generate the dispatch function that calls individual functions
        sb.AppendLine("// Template function to create final color");
        sb.AppendLine("vec4 outputColor()");
        sb.AppendLine("{");

        foreach (var reg in regs)
        {
            sb.AppendLine($"    if (MATERIAL_TYPE == {(uint)reg.TypeId}u) {{");
            sb.AppendLine($"        vec4 color = outputColor_{reg.Name}();");
            if (reg.SupportPointerRing)
            {
                sb.AppendLine("        // Additional logic for pointer ring rendering can be added here");
                sb.AppendLine("        color = mixWithPointerRing(color);");
            }
            sb.AppendLine($"        return color;");
            sb.AppendLine("    }");
        }

        // Default fallback
        sb.AppendLine("    // Fallback for unknown material types");
        sb.AppendLine("    return vec4(1.0, 0.0, 1.0, 1.0); // Magenta");
        sb.AppendLine("}");

        // Replace the existing outputColor function
        int outputColorStart = fragTemplate.IndexOf("vec4 outputColor()");
        if (outputColorStart < 0)
        {
            // Append before main if not found
            int mainStart = fragTemplate.IndexOf("/*TEMPLATE_CUSTOM_MAIN_START*/");
            if (mainStart >= 0)
            {
                fragTemplate = fragTemplate.Insert(mainStart, sb.ToString() + "\n");
            }
        }
        else
        {
            // Find the end of the function
            int braceCount = 0;
            int i = outputColorStart;
            bool foundStart = false;

            while (i < fragTemplate.Length)
            {
                if (fragTemplate[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (fragTemplate[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                    {
                        i++; // Include the closing brace
                        break;
                    }
                }
                i++;
            }

            if (i < fragTemplate.Length)
            {
                string before = fragTemplate.Substring(0, outputColorStart);
                string after = fragTemplate.Substring(i);
                fragTemplate = before + sb.ToString() + after;
            }
        }

        return fragTemplate;
    }
}
