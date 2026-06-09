using glTFLoader;
using glTFLoader.Schema;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.glTF.Internal;
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
    /// Generates a unique session identifier for an import operation.
    /// </summary>
    /// <returns>A GUID string in "D" format (36 characters, lowercase hex with hyphens).</returns>
    private static string GenerateSessionId() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// Synchronously imports a glTF/GLB file into the given world.
    /// </summary>
    /// <param name="filePath">The path to the .gltf or .glb file to import.</param>
    /// <param name="worldData">The world data provider containing the ECS world and resource managers.</param>
    /// <param name="config">Optional configuration for the import operation. If null, default settings are used.</param>
    /// <returns>An <see cref="ImportResult"/> containing the root scene node and diagnostics.</returns>
    public ImportResult Import(string filePath, WorldDataProvider worldData, ImporterConfig? config = null)
    {
        var sessionId = GenerateSessionId();
        var diagnostics = new List<ImportDiagnostic>();

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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
        }

        // 3. Load all buffer data
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        bool isGlb = IsGlbFile(filePath);

        byte[][]? bufferData = LoadBufferData(model, filePath, baseDirectory, isGlb, diagnostics);
        if (bufferData == null)
        {
            // Critical error loading buffers — already added to diagnostics
            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
        }

        // 4. Determine scene index
        int sceneIndex = model.Scene ?? 0;

        // 5. Apply default configuration if not provided
        config ??= ImporterConfig.Default;

        // 6. Create pipeline components
        var resourceManager = worldData.ResourceManager;
        var manifest = new ResourceManifest(sessionId);
        var accessorReader = new AccessorReader(model, bufferData);
        var meshConverter = new MeshConverter(
            resourceManager.Geometries,
            accessorReader,
            diagnostics,
            manifest
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
        var sceneBuilder = new SceneBuilder(
            worldData.World,
            meshConverter,
            materialConverter,
            diagnostics
        );

        // 7. Build scene
        Node rootNode;
        try
        {
            rootNode = sceneBuilder.BuildScene(model, sceneIndex);
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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = manifest,
            };
        }

        // 8. Return ImportResult
        return new ImportResult
        {
            RootNode = rootNode,
            Diagnostics = diagnostics,
            Resources = manifest,
        };
    }

    /// <summary>
    /// Asynchronously imports a glTF/GLB file into the given world.
    /// </summary>
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
        var sessionId = GenerateSessionId();
        var diagnostics = new List<ImportDiagnostic>();

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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
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
            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = ResourceManifest.Empty,
            };
        }

        // 6. Check cancellation before scene building
        cancellationToken.ThrowIfCancellationRequested();

        // 7. Determine scene index
        int sceneIndex = model.Scene ?? 0;

        // 8. Apply default configuration if not provided
        config ??= ImporterConfig.Default;

        // 9. Create pipeline components
        var resourceManager = worldData.ResourceManager;
        var manifest = new ResourceManifest(sessionId);
        var accessorReader = new AccessorReader(model, bufferData);
        var meshConverter = new MeshConverter(
            resourceManager.Geometries,
            accessorReader,
            diagnostics,
            manifest
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
        var sceneBuilder = new SceneBuilder(
            worldData.World,
            meshConverter,
            materialConverter,
            diagnostics
        );

        // 10. Build scene (sync — the heavy async work was in buffer loading)
        Node rootNode;
        try
        {
            rootNode = sceneBuilder.BuildScene(model, sceneIndex);
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

            return new ImportResult
            {
                RootNode = null,
                Diagnostics = diagnostics,
                Resources = manifest,
            };
        }

        // 11. Final cancellation check — if cancelled after scene build, dispose and throw
        if (cancellationToken.IsCancellationRequested)
        {
            rootNode.Dispose();
            manifest.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }

        // 12. Return ImportResult
        return new ImportResult
        {
            RootNode = rootNode,
            Diagnostics = diagnostics,
            Resources = manifest,
        };
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
