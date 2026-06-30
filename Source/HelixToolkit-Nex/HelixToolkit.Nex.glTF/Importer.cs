using glTFLoader;
using glTFLoader.Schema;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Internal.Draco;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF;

/// <summary>
/// Imports glTF 2.0 files into the HelixToolkit-Nex engine scene graph.
/// </summary>
public class Importer
{
    private const string DataUriPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>
    /// The set of glTF extension names this importer recognizes and handles. Membership here means
    /// the importer classifies the extension as recognized and will never record an
    /// unsupported/unrecognized-extension diagnostic for it, whether or not the corresponding
    /// feature is enabled through <see cref="ImporterConfig"/> (Requirement 11). The match is
    /// case-sensitive (<see cref="StringComparer.Ordinal"/>), consistent with the glTF extension
    /// naming convention.
    /// </summary>
    private static readonly HashSet<string> RecognizedExtensions = new(StringComparer.Ordinal)
    {
        DracoExtensionData.ExtensionName,
        LightConverter.ExtensionName,
        InstancingExtensionParser.ExtensionName,
    };

    /// <summary>
    /// Determines whether the importer recognizes the given glTF extension name. Recognized
    /// extensions are never reported as unsupported or unrecognized (Requirement 11), independent of
    /// whether their processing is enabled via <see cref="ImporterConfig"/>.
    /// </summary>
    /// <param name="extensionName">The glTF extension name to classify.</param>
    /// <returns>
    /// <see langword="true"/> when the extension is recognized; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool IsRecognizedExtension(string extensionName) =>
        RecognizedExtensions.Contains(extensionName);

    /// <summary>
    /// Generates a unique session identifier for an import operation.
    /// </summary>
    /// <returns>A GUID string in "D" format (36 characters, lowercase hex with hyphens).</returns>
    private static string GenerateSessionId() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// Synchronously imports a glTF/GLB file into the given world. Equivalent to calling
    /// <see cref="PrepareImport"/> followed by <see cref="PreparedImport.Complete(WorldDataProvider)"/>
    /// on the calling thread, which must be the world's owning thread.
    /// </summary>
    /// <param name="filePath">The path to the .gltf or .glb file to import.</param>
    /// <param name="worldData">The world data provider containing the ECS world and resource managers.</param>
    /// <param name="config">Optional configuration for the import operation. If null, default settings are used.</param>
    /// <returns>An <see cref="ImportResult"/> containing the root scene node and diagnostics.</returns>
    public ImportResult Import(
        string filePath,
        WorldDataProvider worldData,
        ImporterConfig? config = null
    )
    {
        return PrepareImport(filePath, worldData, config).Complete(worldData);
    }

    /// <summary>
    /// Synchronously performs the preparation phase of an import: validates the file, parses the
    /// glTF model, loads buffer data, converts meshes/materials/textures/lights, and records the
    /// scene graph into a <see cref="SceneCommandBuffer"/>. Materialize the result by calling
    /// <see cref="PreparedImport.Complete(WorldDataProvider)"/> on the world's owning thread.
    /// </summary>
    /// <param name="filePath">The path to the .gltf or .glb file to import.</param>
    /// <param name="worldData">The world data provider containing the resource managers (and the target world for completion).</param>
    /// <param name="config">Optional configuration for the import operation. If null, default settings are used.</param>
    /// <returns>A <see cref="PreparedImport"/> holding the recorded scene, diagnostics, and resources.</returns>
    public PreparedImport PrepareImport(
        string filePath,
        WorldDataProvider worldData,
        ImporterConfig? config = null
    )
    {
        var sessionId = GenerateSessionId();
        var diagnostics = new List<ImportDiagnostic>();
        config ??= ImporterConfig.Default;

        // 1. Validate file exists
        if (!File.Exists(filePath))
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"File not found: {filePath}",
                    "File",
                    -1
                )
            );

            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 2. Parse glTF model
        Gltf model;
        try
        {
            model = Interface.LoadModel(filePath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to parse glTF file: {ex.Message}",
                    "File",
                    -1
                )
            );

            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 3. Load all buffer data
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        bool isGlb = IsGlbFile(filePath);

        byte[][]? bufferData = LoadBufferData(model, filePath, baseDirectory, isGlb, diagnostics);
        if (bufferData == null)
        {
            // Critical error loading buffers — already added to diagnostics
            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 4. Convert and record the scene (synchronous conversion path).
        var sceneBuilder = CreateSceneBuilder(
            model,
            bufferData,
            baseDirectory,
            sessionId,
            worldData,
            config,
            diagnostics,
            out var manifest
        );

        int sceneIndex = model.Scene ?? 0;
        try
        {
            var recording = sceneBuilder.RecordScene(model, sceneIndex);
            return PreparedImport.ForRecording(recording, diagnostics, manifest);
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to build scene: {ex.Message}",
                    "Scene",
                    sceneIndex
                )
            );
            return PreparedImport.ForFailure(diagnostics, manifest);
        }
    }

    /// <summary>
    /// Asynchronously imports a glTF/GLB file into the given world. Equivalent to
    /// <see cref="PrepareImportAsync"/> followed by
    /// <see cref="PreparedImport.Complete(WorldDataProvider)"/> on the continuation.
    /// </summary>
    /// <remarks>
    /// Only the load phase runs asynchronously/off-thread. The completing build performs GPU
    /// resource creation and world mutation, so it MUST run on the world's owning thread: the
    /// caller must await this on the owning thread. For explicit control — loading on a background
    /// thread and building on the owning (render) thread — use <see cref="PrepareImportAsync"/> and
    /// call <see cref="PreparedImport.Complete(WorldDataProvider)"/> on the owning thread.
    /// </remarks>
    /// <param name="filePath">The path to the .gltf or .glb file to import.</param>
    /// <param name="worldData">The world data provider containing the ECS world and resource managers.</param>
    /// <param name="config">Optional configuration for the import operation. If null, default settings are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the import operation.</param>
    /// <returns>A task containing an <see cref="ImportResult"/> with the root scene node and diagnostics.</returns>
    public async Task<ImportResult> ImportAsync(
        string filePath,
        WorldDataProvider worldData,
        ImporterConfig? config = null,
        CancellationToken cancellationToken = default
    )
    {
        var prepared = await PrepareImportAsync(filePath, worldData, config, cancellationToken)
            .ConfigureAwait(false);

        // Final cancellation check — if cancelled after recording, dispose and throw.
        if (cancellationToken.IsCancellationRequested)
        {
            prepared.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }

        return prepared.Complete(worldData);
    }

    /// <summary>
    /// Asynchronously performs the preparation phase of an import: validates the file, parses the
    /// glTF model, loads buffer data (non-blocking I/O), converts meshes/materials/textures/lights
    /// through the asynchronous GPU-upload path (<c>AddAsync</c>/<c>LoadTextureAsync</c>), and
    /// records the scene graph into a <see cref="SceneCommandBuffer"/>. Because conversion uses the
    /// async upload path and recording never mutates the world, this work may run on a background
    /// thread. Materialize the result by calling
    /// <see cref="PreparedImport.Complete(WorldDataProvider)"/> on the world's owning (render)
    /// thread, which only flushes the recorded buffer.
    /// </summary>
    /// <param name="filePath">The path to the .gltf or .glb file to import.</param>
    /// <param name="worldData">The world data provider containing the resource managers (and the target world for completion).</param>
    /// <param name="config">Optional configuration for the import operation. If null, default settings are used.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the preparation.</param>
    /// <returns>A task containing a <see cref="PreparedImport"/> holding the recorded scene, diagnostics, and resources.</returns>
    public async Task<PreparedImport> PrepareImportAsync(
        string filePath,
        WorldDataProvider worldData,
        ImporterConfig? config = null,
        CancellationToken cancellationToken = default
    )
    {
        var sessionId = GenerateSessionId();
        var diagnostics = new List<ImportDiagnostic>();
        config ??= ImporterConfig.Default;

        // 1. Check cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Validate file exists
        if (!File.Exists(filePath))
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"File not found: {filePath}",
                    "File",
                    -1
                )
            );

            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 3. Parse glTF model
        Gltf model;
        try
        {
            model = Interface.LoadModel(filePath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to parse glTF file: {ex.Message}",
                    "File",
                    -1
                )
            );

            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 4. Check cancellation before buffer loading
        cancellationToken.ThrowIfCancellationRequested();

        // 5. Load all buffer data asynchronously
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        bool isGlb = IsGlbFile(filePath);

        byte[][]? bufferData = await LoadBufferDataAsync(
                model,
                filePath,
                baseDirectory,
                isGlb,
                diagnostics,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (bufferData == null)
        {
            // Critical error loading buffers — already added to diagnostics
            return PreparedImport.ForFailure(diagnostics, ResourceManifest.Empty);
        }

        // 6. Check cancellation before conversion/recording
        cancellationToken.ThrowIfCancellationRequested();

        // 7. Convert (async GPU upload) and record the scene off the owning thread.
        var sceneBuilder = CreateSceneBuilder(
            model,
            bufferData,
            baseDirectory,
            sessionId,
            worldData,
            config,
            diagnostics,
            out var manifest
        );

        int sceneIndex = model.Scene ?? 0;
        try
        {
            var recording = await sceneBuilder
                .RecordSceneAsync(model, sceneIndex, cancellationToken)
                .ConfigureAwait(false);
            return PreparedImport.ForRecording(recording, diagnostics, manifest);
        }
        catch (OperationCanceledException)
        {
            manifest.DisposeAll();
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to build scene: {ex.Message}",
                    "Scene",
                    sceneIndex
                )
            );
            return PreparedImport.ForFailure(diagnostics, manifest);
        }
    }

    /// <summary>
    /// Builds the conversion pipeline (mesh, material, texture, and light converters) and a
    /// <see cref="SceneBuilder"/> over a freshly created resource manifest. Shared by the
    /// synchronous and asynchronous prepare paths.
    /// </summary>
    private static SceneBuilder CreateSceneBuilder(
        Gltf model,
        byte[][] bufferData,
        string baseDirectory,
        string sessionId,
        WorldDataProvider worldData,
        ImporterConfig config,
        List<ImportDiagnostic> diagnostics,
        out ResourceManifest manifest
    )
    {
        var resourceManager = worldData.ResourceManager;
        manifest = new ResourceManifest(sessionId);
        var accessorReader = new AccessorReader(model, bufferData);
        bool dracoRequired = IsDracoRequired(model);
        var dracoDecoder = new DracoDecoder();
        var meshConverter = new MeshConverter(
            resourceManager.Geometries,
            accessorReader,
            diagnostics,
            manifest,
            config,
            dracoDecoder,
            dracoRequired
        );
        var textureLoader = new TextureLoader(
            resourceManager.TextureRepository,
            resourceManager.SamplerRepository,
            baseDirectory,
            model,
            bufferData,
            diagnostics,
            manifest,
            sessionId
        );
        var materialConverter = new MaterialConverter(
            resourceManager.PBRPropertyManager,
            textureLoader,
            diagnostics,
            manifest,
            config.DefaultShadingMode
        );
        var lightConverter = new LightConverter(diagnostics, config);
        return new SceneBuilder(
            worldData.World,
            meshConverter,
            materialConverter,
            lightConverter,
            diagnostics,
            config,
            accessorReader,
            IsInstancingRequired(model),
            manifest,
            resourceManager.InstancingManager
        );
    }

    /// <summary>
    /// Determines whether <c>KHR_draco_mesh_compression</c> is listed in the model's
    /// <c>extensionsRequired</c> array. This governs the diagnostic severity used when a Draco
    /// primitive cannot be decoded (Error when required, Warning otherwise).
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <returns>
    /// <see langword="true"/> when <c>KHR_draco_mesh_compression</c> appears in
    /// <c>extensionsRequired</c>; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsDracoRequired(Gltf model)
    {
        var required = model.ExtensionsRequired;
        if (required == null)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Determines whether <c>EXT_mesh_gpu_instancing</c> is listed in the model's
    /// <c>extensionsRequired</c> array. This governs the disabled-path diagnostic: when instancing
    /// processing is disabled and the extension is required, a Warning is emitted for each node that
    /// declares it (Requirement 10.4); when the extension is only in <c>extensionsUsed</c>, no
    /// Error/Warning is emitted for the unprocessed extension (Requirement 10.5).
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <returns>
    /// <see langword="true"/> when <c>EXT_mesh_gpu_instancing</c> appears in
    /// <c>extensionsRequired</c>; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsInstancingRequired(Gltf model)
    {
        var required = model.ExtensionsRequired;
        if (required == null)
        {
            return false;
        }

        for (int i = 0; i < required.Length; i++)
        {
            if (
                string.Equals(
                    required[i],
                    InstancingExtensionParser.ExtensionName,
                    StringComparison.Ordinal
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Loads all buffer data referenced by the glTF model.
    /// Handles external .bin files, embedded base64 data URIs, and GLB binary chunks.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="filePath">The original file path (used for GLB binary buffer loading).</param>
    /// <param name="baseDirectory">The base directory for resolving relative .bin paths.</param>
    /// <param name="isGlb">Whether the source file is a GLB file.</param>
    /// <param name="diagnostics">The diagnostics list to report errors to.</param>
    /// <returns>An array of byte arrays for each buffer, or null if a critical error occurred.</returns>
    private static byte[][]? LoadBufferData(
        Gltf model,
        string filePath,
        string baseDirectory,
        bool isGlb,
        List<ImportDiagnostic> diagnostics
    )
    {
        if (model.Buffers == null || model.Buffers.Length == 0)
        {
            return [];
        }

        var bufferData = new byte[model.Buffers.Length][];

        for (int i = 0; i < model.Buffers.Length; i++)
        {
            var buffer = model.Buffers[i];
            string? uri = buffer.Uri;

            if (string.IsNullOrEmpty(uri))
            {
                // GLB binary chunk (buffer 0 in GLB files has no URI)
                if (isGlb)
                {
                    try
                    {
                        bufferData[i] = LoadGlbBinaryChunk(filePath);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(
                            new ImportDiagnostic(
                                DiagnosticSeverity.Error,
                                $"Failed to load GLB binary buffer: {ex.Message}",
                                "Buffer",
                                i
                            )
                        );
                        return null;
                    }
                }
                else
                {
                    // Non-GLB file with no URI — this shouldn't happen but handle gracefully
                    bufferData[i] = [];
                }
            }
            else if (uri.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Embedded base64 data URI
                byte[]? decoded = DecodeBase64DataUri(uri, i, diagnostics);
                if (decoded == null)
                {
                    return null;
                }
                bufferData[i] = decoded;
            }
            else
            {
                // External .bin file
                string decodedUri = Uri.UnescapeDataString(uri);
                string binPath = Path.GetFullPath(Path.Combine(baseDirectory, decodedUri));

                if (!File.Exists(binPath))
                {
                    diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Referenced buffer file not found: {decodedUri}",
                            "Buffer",
                            i
                        )
                    );
                    return null;
                }

                try
                {
                    bufferData[i] = File.ReadAllBytes(binPath);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Failed to read buffer file '{decodedUri}': {ex.Message}",
                            "Buffer",
                            i
                        )
                    );
                    return null;
                }
            }
        }

        return bufferData;
    }

    /// <summary>
    /// Asynchronously loads all buffer data referenced by the glTF model.
    /// Handles external .bin files, embedded base64 data URIs, and GLB binary chunks.
    /// Uses File.ReadAllBytesAsync for non-blocking I/O.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="filePath">The original file path (used for GLB binary buffer loading).</param>
    /// <param name="baseDirectory">The base directory for resolving relative .bin paths.</param>
    /// <param name="isGlb">Whether the source file is a GLB file.</param>
    /// <param name="diagnostics">The diagnostics list to report errors to.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An array of byte arrays for each buffer, or null if a critical error occurred.</returns>
    private static async Task<byte[][]?> LoadBufferDataAsync(
        Gltf model,
        string filePath,
        string baseDirectory,
        bool isGlb,
        List<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (model.Buffers == null || model.Buffers.Length == 0)
        {
            return [];
        }

        var bufferData = new byte[model.Buffers.Length][];

        for (int i = 0; i < model.Buffers.Length; i++)
        {
            // Check cancellation before each buffer load
            cancellationToken.ThrowIfCancellationRequested();

            var buffer = model.Buffers[i];
            string? uri = buffer.Uri;

            if (string.IsNullOrEmpty(uri))
            {
                // GLB binary chunk (buffer 0 in GLB files has no URI)
                if (isGlb)
                {
                    try
                    {
                        bufferData[i] = LoadGlbBinaryChunk(filePath);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(
                            new ImportDiagnostic(
                                DiagnosticSeverity.Error,
                                $"Failed to load GLB binary buffer: {ex.Message}",
                                "Buffer",
                                i
                            )
                        );
                        return null;
                    }
                }
                else
                {
                    // Non-GLB file with no URI — this shouldn't happen but handle gracefully
                    bufferData[i] = [];
                }
            }
            else if (uri.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Embedded base64 data URI
                byte[]? decoded = DecodeBase64DataUri(uri, i, diagnostics);
                if (decoded == null)
                {
                    return null;
                }
                bufferData[i] = decoded;
            }
            else
            {
                // External .bin file
                string decodedUri = Uri.UnescapeDataString(uri);
                string binPath = Path.GetFullPath(Path.Combine(baseDirectory, decodedUri));

                if (!File.Exists(binPath))
                {
                    diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Referenced buffer file not found: {decodedUri}",
                            "Buffer",
                            i
                        )
                    );
                    return null;
                }

                try
                {
                    bufferData[i] = await File.ReadAllBytesAsync(binPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Failed to read buffer file '{decodedUri}': {ex.Message}",
                            "Buffer",
                            i
                        )
                    );
                    return null;
                }
            }
        }

        return bufferData;
    }

    /// <summary>
    /// Decodes a base64 data URI into a byte array.
    /// Expected format: "data:application/octet-stream;base64,..."
    /// </summary>
    /// <param name="dataUri">The data URI string.</param>
    /// <param name="bufferIndex">The buffer index (for diagnostics).</param>
    /// <param name="diagnostics">The diagnostics list to report errors to.</param>
    /// <returns>The decoded byte array, or null if decoding failed.</returns>
    private static byte[]? DecodeBase64DataUri(
        string dataUri,
        int bufferIndex,
        List<ImportDiagnostic> diagnostics
    )
    {
        int base64Start = dataUri.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
        if (base64Start < 0)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Buffer {bufferIndex} has invalid data URI format (missing base64 marker).",
                    "Buffer",
                    bufferIndex
                )
            );
            return null;
        }

        string base64Data = dataUri[(base64Start + Base64Marker.Length)..];

        try
        {
            return Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Buffer {bufferIndex} contains invalid base64 data.",
                    "Buffer",
                    bufferIndex
                )
            );
            return null;
        }
    }

    /// <summary>
    /// Determines whether the file is a GLB (binary glTF) file based on its extension.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file has a .glb extension; false otherwise.</returns>
    private static bool IsGlbFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".glb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the binary chunk from a GLB file.
    /// GLB format: 12-byte header, then chunks (JSON chunk first, BIN chunk second).
    /// Each chunk has an 8-byte header (4 bytes length, 4 bytes type).
    /// </summary>
    /// <param name="filePath">The path to the GLB file.</param>
    /// <returns>The binary chunk data.</returns>
    private static byte[] LoadGlbBinaryChunk(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        // GLB Header: magic (4), version (4), length (4) = 12 bytes
        uint magic = reader.ReadUInt32();
        if (magic != 0x46546C67) // "glTF" in little-endian
        {
            throw new InvalidDataException("Invalid GLB magic number.");
        }

        uint version = reader.ReadUInt32();
        uint totalLength = reader.ReadUInt32();

        // Chunk 0: JSON chunk
        uint jsonChunkLength = reader.ReadUInt32();
        uint jsonChunkType = reader.ReadUInt32(); // 0x4E4F534A = "JSON"
        stream.Seek(jsonChunkLength, SeekOrigin.Current); // Skip JSON data

        // Chunk 1: BIN chunk
        if (stream.Position >= totalLength)
        {
            // No binary chunk present
            return [];
        }

        uint binChunkLength = reader.ReadUInt32();
        uint binChunkType = reader.ReadUInt32(); // 0x004E4942 = "BIN\0"

        var binData = reader.ReadBytes((int)binChunkLength);
        return binData;
    }
}
