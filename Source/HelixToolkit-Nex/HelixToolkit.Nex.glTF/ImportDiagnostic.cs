namespace HelixToolkit.Nex.glTF;

/// <summary>
/// Severity level for import diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// Individual diagnostic entry with severity, message, and glTF element reference.
/// </summary>
/// <param name="Severity">The severity level of the diagnostic.</param>
/// <param name="Message">A human-readable description of the issue.</param>
/// <param name="ElementType">The glTF element type that caused the issue (e.g., "Mesh", "Material", "Accessor", "Node").</param>
/// <param name="ElementIndex">The glTF array index of the problematic element.</param>
public sealed record ImportDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string ElementType,
    int ElementIndex
);
