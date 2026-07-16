using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.glTF;

/// <summary>
/// The result of the off-thread phase of a glTF import: parsing, buffer loading, mesh/material/
/// texture/light conversion (via the async GPU-upload path), and recording the scene graph into a
/// <see cref="SceneCommandBuffer"/>. Holds everything needed to materialize the scene, deferred
/// until <see cref="Complete(WorldDataProvider)"/> is called on the world's owning (render) thread.
/// </summary>
/// <remarks>
/// <para>
/// The preparation phase (see <see cref="Importer.PrepareImportAsync"/>) uploads GPU resources
/// through the asynchronous resource path (<c>AddAsync</c>/<c>LoadTextureAsync</c>), which is safe
/// to run off the render thread, and records the scene structure into the command buffer without
/// mutating the ECS world. <see cref="Complete(WorldDataProvider)"/> only flushes the recorded
/// buffer, which constructs the engine nodes and sets their (already-uploaded) resources as ECS
/// components, so it MUST run on the world's owning thread.
/// </para>
/// <para>
/// GPU resources are created during preparation and tracked by <see cref="Resources"/>. If the
/// prepared import is never completed, call <see cref="Dispose"/> to release them. Once
/// <see cref="Complete(WorldDataProvider)"/> succeeds, ownership transfers to the returned
/// <see cref="ImportResult"/>.
/// </para>
/// </remarks>
public sealed class PreparedImport : IDisposable
{
    private readonly SceneRecording? _recording;
    private readonly List<ImportDiagnostic> _diagnostics;
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Diagnostics produced during the preparation phase (parsing, buffer loading, conversion, and
    /// recording). Flush-time diagnostics, if any, are appended to the returned
    /// <see cref="ImportResult"/> by <see cref="Complete(WorldDataProvider)"/>.
    /// </summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Manifest of all resources (textures, samplers, materials, geometries) created during the
    /// preparation phase.
    /// </summary>
    public ResourceManifest Resources { get; }

    /// <summary>
    /// <see langword="true"/> when the preparation phase produced a recorded scene that can be
    /// materialized; <see langword="false"/> when a critical error prevented recording.
    /// </summary>
    public bool CanComplete => _recording is not null;

    /// <summary>
    /// Returns a task that completes when all imported textures are fully GPU-ready, including any
    /// mipmap generation deferred to the render thread. See
    /// <see cref="ResourceManifest.WhenTexturesReadyAsync"/> for details. The mipmap pass is driven
    /// by the engine each frame, so this completes once the engine has rendered (begun) at least
    /// one frame after the textures were uploaded.
    /// </summary>
    public Task WhenTexturesReadyAsync() => Resources.WhenTexturesReadyAsync();

    private PreparedImport(
        SceneRecording? recording,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest resources
    )
    {
        _recording = recording;
        _diagnostics = diagnostics;
        Resources = resources;
    }

    /// <summary>
    /// Creates a prepared import for a successfully recorded scene.
    /// </summary>
    internal static PreparedImport ForRecording(
        SceneRecording recording,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest resources
    ) => new(recording, diagnostics, resources);

    /// <summary>
    /// Creates a prepared import representing a critical preparation failure.
    /// </summary>
    internal static PreparedImport ForFailure(
        List<ImportDiagnostic> diagnostics,
        ResourceManifest resources
    ) => new(null, diagnostics, resources);

    /// <summary>
    /// Materializes the recorded scene onto the world owned by <paramref name="worldData"/>. Must
    /// be called on the world's owning (render) thread.
    /// </summary>
    public ImportResult Complete(WorldDataProvider worldData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureNotCompleted();

        // A critical preparation failure has nothing to materialize and needs no world.
        if (_recording is not SceneRecording recording)
        {
            return FinishFailed();
        }

        ArgumentNullException.ThrowIfNull(worldData);
        return Flush(recording, worldData.World);
    }

    /// <summary>
    /// Materializes the recorded scene onto <paramref name="world"/> by flushing the recorded
    /// command buffer. Must be called on the world's owning (render) thread.
    /// </summary>
    public ImportResult Complete(World world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureNotCompleted();

        if (_recording is not SceneRecording recording)
        {
            return FinishFailed();
        }

        return Flush(recording, world);
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("This prepared import has already been completed.");
        }
    }

    private ImportResult FinishFailed()
    {
        _completed = true;
        return new ImportResult
        {
            RootNode = null,
            Diagnostics = _diagnostics,
            Resources = Resources,
        };
    }

    private ImportResult Flush(SceneRecording recording, World world)
    {
        _completed = true;

        var flushResult = recording.Buffer.Flush(world);
        if (!flushResult.Success)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to materialize scene: {flushResult.Message}",
                    "Scene",
                    flushResult.FailedCommandIndex
                )
            );

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = _diagnostics,
                Resources = Resources,
            };
        }

        var rootNode = recording.Buffer.MaterializedNodes[recording.Root];

        return new ImportResult
        {
            RootNode = rootNode,
            Diagnostics = _diagnostics,
            Resources = Resources,
        };
    }

    /// <summary>
    /// Releases the GPU resources created during preparation when the prepared import is not
    /// completed. Idempotent; a no-op once <see cref="Complete(WorldDataProvider)"/> has transferred
    /// ownership to an <see cref="ImportResult"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_completed)
        {
            Resources.DisposeAll();
        }
    }
}
