using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.glTF;

/// <summary>
/// Return type containing the root Node, diagnostics list, and import metadata.
/// Implements <see cref="IDisposable"/> for deterministic cleanup of all import-created resources.
/// </summary>
public sealed class ImportResult : IDisposable
{
    /// <summary>Root node of the imported scene. Null if import failed critically.</summary>
    public Node? RootNode { get; init; }

    /// <summary>All diagnostic entries from the import process.</summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// Manifest of all GPU resources (textures, samplers, materials, geometries) created during import.
    /// Defaults to <see cref="ResourceManifest.Empty"/> for failed imports.
    /// </summary>
    public ResourceManifest Resources { get; init; } = ResourceManifest.Empty;

    /// <summary>True if the import completed (possibly with warnings) and produced no Error-severity diagnostics. False if a critical error occurred or any Error diagnostic was reported.</summary>
    public bool Success => RootNode is not null && !HasErrors;

    /// <summary>True if any diagnostics have Error severity.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>True if any diagnostics have Warning severity.</summary>
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

    private bool _disposed;

    /// <summary>
    /// Disposes the root node and all tracked resources.
    /// Idempotent: subsequent calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        RootNode?.Dispose();
        Resources.DisposeAll();

        GC.SuppressFinalize(this);
    }
}
