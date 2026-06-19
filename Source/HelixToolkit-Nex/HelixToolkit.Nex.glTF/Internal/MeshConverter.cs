using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal.Draco;
using HelixToolkit.Nex.Graphics;
using Newtonsoft.Json.Linq;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Converts glTF MeshPrimitives into engine Geometry objects.
/// Reads vertex attributes and indices via AccessorReader, or decodes
/// <c>KHR_draco_mesh_compression</c> primitives through the <see cref="IDracoDecoder"/>.
/// </summary>
internal sealed class MeshConverter
{
    private readonly IGeometryManager _geometryManager;
    private readonly AccessorReader _accessorReader;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly ResourceManifest _manifest;
    private readonly ImporterConfig _config;
    private readonly IDracoDecoder _dracoDecoder;
    private readonly bool _dracoRequired;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshConverter"/> class.
    /// </summary>
    /// <param name="geometryManager">The geometry manager to register geometry resources with.</param>
    /// <param name="accessorReader">The accessor reader for reading vertex/index data from glTF buffers.</param>
    /// <param name="diagnostics">The diagnostics list to report errors and warnings to.</param>
    /// <param name="manifest">The resource manifest to track created geometry resources.</param>
    /// <param name="config">The importer configuration (controls Draco decompression).</param>
    /// <param name="dracoDecoder">The Draco decoder used to decode <c>KHR_draco_mesh_compression</c> primitives.</param>
    /// <param name="dracoRequired">
    /// <see langword="true"/> when <c>KHR_draco_mesh_compression</c> is listed in the model's
    /// <c>extensionsRequired</c>; controls the diagnostic severity used for fallback skips.
    /// </param>
    public MeshConverter(
        IGeometryManager geometryManager,
        AccessorReader accessorReader,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest manifest,
        ImporterConfig config,
        IDracoDecoder dracoDecoder,
        bool dracoRequired
    )
    {
        _geometryManager =
            geometryManager ?? throw new ArgumentNullException(nameof(geometryManager));
        _accessorReader = accessorReader ?? throw new ArgumentNullException(nameof(accessorReader));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dracoDecoder = dracoDecoder ?? throw new ArgumentNullException(nameof(dracoDecoder));
        _dracoRequired = dracoRequired;
    }

    /// <summary>
    /// Converts a glTF mesh primitive into an engine Geometry object and registers it with the geometry manager.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="primitive">The mesh primitive to convert.</param>
    /// <param name="meshIndex">The index of the mesh in the glTF model (for diagnostics).</param>
    /// <param name="primIndex">The index of the primitive within the mesh (for diagnostics).</param>
    /// <returns>A tuple of the created Geometry (or null if skipped) and the geometry handle.</returns>
    public (Geometry? geometry, Handle<GeometryResourceType> handle) ConvertPrimitive(
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex
    )
    {
        // Route KHR_draco_mesh_compression primitives through the Draco path. When the
        // primitive declares the extension but Draco is disabled or unavailable, this skips the
        // primitive via the fallback policy without reading attributes through the AccessorReader.
        if (TryRouteDracoPrimitive(model, primitive, meshIndex, primIndex, out var dracoResult))
        {
            return dracoResult;
        }

        // Validate topology mode
        if (!TryMapTopology(primitive.Mode, out var topology))
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} uses unsupported topology mode {(int)primitive.Mode}.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Check for required POSITION attribute
        if (
            primitive.Attributes == null
            || !primitive.Attributes.TryGetValue("POSITION", out int positionAccessorIndex)
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} is missing required POSITION attribute.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Create geometry with the mapped topology
        var geometry = new Geometry(topology);

        // Read positions (required)
        _accessorReader.ReadPositions(positionAccessorIndex, geometry.Vertices);

        // Read normals (optional)
        bool hasVertexProps = false;
        if (primitive.Attributes.TryGetValue("NORMAL", out int normalAccessorIndex))
        {
            _accessorReader.ReadNormals(normalAccessorIndex, geometry.VertexProps, merge: false);
            hasVertexProps = true;
        }

        // Read texture coordinates (optional)
        if (primitive.Attributes.TryGetValue("TEXCOORD_0", out int texCoordAccessorIndex))
        {
            if (hasVertexProps)
            {
                _accessorReader.ReadTexCoords(
                    texCoordAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }
            else
            {
                _accessorReader.ReadTexCoords(
                    texCoordAccessorIndex,
                    geometry.VertexProps,
                    merge: false
                );
                hasVertexProps = true;
            }
        }

        // Read tangents (optional)
        if (primitive.Attributes.TryGetValue("TANGENT", out int tangentAccessorIndex))
        {
            if (hasVertexProps)
            {
                _accessorReader.ReadTangents(
                    tangentAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }
            else
            {
                _accessorReader.ReadTangents(
                    tangentAccessorIndex,
                    geometry.VertexProps,
                    merge: false
                );
                hasVertexProps = true;
            }
        }

        // Read vertex colors (optional)
        if (primitive.Attributes.TryGetValue("COLOR_0", out int colorAccessorIndex))
        {
            _accessorReader.ReadColors(colorAccessorIndex, geometry.VertexColors);
        }

        // Read indices (optional)
        if (primitive.Indices.HasValue)
        {
            _accessorReader.ReadIndices(primitive.Indices.Value, geometry.Indices);
        }

        // Generate tangents when the glTF asset does not supply them
        if (!primitive.Attributes.ContainsKey("TANGENT"))
        {
            TangentGenerator.ComputeTangents(geometry);
        }

        // Compute bounding volumes from vertex positions
        geometry.UpdateBounds();

        // Register with geometry manager
        var handle = _geometryManager.Add(geometry);

        if (!handle.Valid)
        {
            return (null, handle);
        }

        _manifest.AddGeometry(geometry);
        return (geometry, handle);
    }

    /// <summary>
    /// Asynchronously converts a glTF mesh primitive into an engine Geometry object and registers it with the geometry manager.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="primitive">The mesh primitive to convert.</param>
    /// <param name="meshIndex">The index of the mesh in the glTF model (for diagnostics).</param>
    /// <param name="primIndex">The index of the primitive within the mesh (for diagnostics).</param>
    /// <param name="cancellationToken">
    /// A token used to cancel Draco decoding and geometry registration. Cancellation during Draco
    /// processing surfaces as an <see cref="OperationCanceledException"/> (Requirement 7.4).
    /// </param>
    /// <returns>A tuple of the created Geometry (or null if skipped) and the geometry handle.</returns>
    public async Task<(
        Geometry? geometry,
        Handle<GeometryResourceType> handle
    )> ConvertPrimitiveAsync(
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        CancellationToken cancellationToken = default
    )
    {
        // Route KHR_draco_mesh_compression primitives through the Draco path. The CPU-bound decode
        // runs on a background thread (Requirement 7.2) and cancellation is honored before decode
        // and before registration (Requirement 7.4). When the primitive declares the extension but
        // Draco is disabled or unavailable, this skips the primitive via the fallback policy without
        // reading attributes through the AccessorReader.
        var (dracoHandled, dracoResult) = await TryRouteDracoPrimitiveAsync(
                model,
                primitive,
                meshIndex,
                primIndex,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (dracoHandled)
        {
            return dracoResult;
        }

        // Validate topology mode
        if (!TryMapTopology(primitive.Mode, out var topology))
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} uses unsupported topology mode {(int)primitive.Mode}.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Check for required POSITION attribute
        if (
            primitive.Attributes == null
            || !primitive.Attributes.TryGetValue("POSITION", out int positionAccessorIndex)
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} is missing required POSITION attribute.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Create geometry with the mapped topology
        var geometry = new Geometry(topology);

        // Read positions (required)
        _accessorReader.ReadPositions(positionAccessorIndex, geometry.Vertices);

        // Read normals (optional)
        bool hasVertexProps = false;
        if (primitive.Attributes.TryGetValue("NORMAL", out int normalAccessorIndex))
        {
            _accessorReader.ReadNormals(normalAccessorIndex, geometry.VertexProps, merge: false);
            hasVertexProps = true;
        }

        // Read texture coordinates (optional)
        if (primitive.Attributes.TryGetValue("TEXCOORD_0", out int texCoordAccessorIndex))
        {
            if (hasVertexProps)
            {
                _accessorReader.ReadTexCoords(
                    texCoordAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }
            else
            {
                _accessorReader.ReadTexCoords(
                    texCoordAccessorIndex,
                    geometry.VertexProps,
                    merge: false
                );
                hasVertexProps = true;
            }
        }

        // Read tangents (optional)
        if (primitive.Attributes.TryGetValue("TANGENT", out int tangentAccessorIndex))
        {
            if (hasVertexProps)
            {
                _accessorReader.ReadTangents(
                    tangentAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }
            else
            {
                _accessorReader.ReadTangents(
                    tangentAccessorIndex,
                    geometry.VertexProps,
                    merge: false
                );
                hasVertexProps = true;
            }
        }

        // Read vertex colors (optional)
        if (primitive.Attributes.TryGetValue("COLOR_0", out int colorAccessorIndex))
        {
            _accessorReader.ReadColors(colorAccessorIndex, geometry.VertexColors);
        }

        // Read indices (optional)
        if (primitive.Indices.HasValue)
        {
            _accessorReader.ReadIndices(primitive.Indices.Value, geometry.Indices);
        }

        // Generate tangents when the glTF asset does not supply them
        if (!primitive.Attributes.ContainsKey("TANGENT"))
        {
            TangentGenerator.ComputeTangents(geometry);
        }

        // Compute bounding volumes from vertex positions
        geometry.CreateBoundingBox();
        geometry.CreateBoundingSphere();

        // Register with geometry manager asynchronously
        var (success, handle) = await _geometryManager.AddAsync(geometry);

        if (!success || !handle.Valid)
        {
            return (null, handle);
        }

        _manifest.AddGeometry(geometry);
        return (geometry, handle);
    }

    /// <summary>
    /// Detects whether a mesh primitive declares the <c>KHR_draco_mesh_compression</c> extension.
    /// </summary>
    /// <param name="primitive">The mesh primitive to inspect.</param>
    /// <param name="raw">
    /// The extension value as a <see cref="JObject"/> when present and well-formed; otherwise
    /// <see langword="null"/> (the value is missing, <see langword="null"/>, or not a JObject).
    /// </param>
    /// <param name="present">
    /// <see langword="true"/> when the primitive's extensions map contains a
    /// <c>KHR_draco_mesh_compression</c> key (regardless of the value's shape); otherwise
    /// <see langword="false"/>.
    /// </param>
    /// <returns><see langword="true"/> when the extension is present and its value is a JObject.</returns>
    private static bool TryGetDracoExtension(
        MeshPrimitive primitive,
        out JObject? raw,
        out bool present
    )
    {
        raw = null;
        present =
            primitive.Extensions != null
            && primitive.Extensions.ContainsKey(DracoExtensionData.ExtensionName);

        if (!present)
        {
            return false;
        }

        raw = primitive.Extensions![DracoExtensionData.ExtensionName] as JObject;
        return raw is not null;
    }

    /// <summary>
    /// Routes a primitive that declares <c>KHR_draco_mesh_compression</c>. Decides between the
    /// existing accessor path (extension absent) and the Draco path, applies the
    /// disabled/unavailable fallback gating, and orchestrates parse → resolve → decode, mapping
    /// every failure to exactly one diagnostic and skipping the primitive.
    /// </summary>
    /// <param name="model">The deserialized glTF model (used to resolve the bufferView slice).</param>
    /// <param name="primitive">The mesh primitive being converted.</param>
    /// <param name="meshIndex">The index of the mesh (for diagnostics).</param>
    /// <param name="primIndex">The index of the primitive within the mesh (for diagnostics).</param>
    /// <param name="result">The conversion result when the Draco path handled the primitive.</param>
    /// <returns>
    /// <see langword="true"/> when the Draco path handled (or skipped) the primitive and the caller
    /// should return <paramref name="result"/>; <see langword="false"/> when the primitive has no
    /// Draco extension and the caller should use the accessor-based path.
    /// </returns>
    private bool TryRouteDracoPrimitive(
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        out (Geometry? geometry, Handle<GeometryResourceType> handle) result
    )
    {
        result = (null, Handle<GeometryResourceType>.Null);

        // Pre-decode: gate, parse, validate, and resolve the bufferView slice (shared with the
        // asynchronous path). Skips already recorded their single diagnostic.
        DracoRouteStage stage = PrepareDracoDecode(
            primitive,
            meshIndex,
            primIndex,
            out byte[]? compressed,
            out DracoExtensionData? data
        );

        switch (stage)
        {
            case DracoRouteStage.UseAccessorPath:
                return false;
            case DracoRouteStage.Skipped:
                // Diagnostic already recorded; the primitive is excluded from the Geometry.
                return true;
            default:
                // Requirement 7.1: the synchronous path decodes inline on the calling thread.
                DracoDecodeOutcome outcome = _dracoDecoder.Decode(
                    compressed!.AsSpan(),
                    data!.Attributes
                );
                result = CompleteDracoDecode(model, primitive, outcome, data, meshIndex, primIndex);
                return true;
        }
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="TryRouteDracoPrimitive"/>. Shares the same
    /// pre-decode gating/parse/resolve and post-decode build logic, but offloads the CPU-bound
    /// Draco decode to a background thread via <see cref="Task.Run(Func{DracoDecodeOutcome}, CancellationToken)"/>
    /// so the calling thread does not execute decode work (Requirement 7.2). The cancellation token
    /// is checked before the decode and again before geometry registration, and is passed into
    /// <see cref="Task.Run(Func{DracoDecodeOutcome}, CancellationToken)"/>, so cancellation during
    /// Draco processing surfaces as an <see cref="OperationCanceledException"/> rather than a
    /// completed result (Requirement 7.4).
    /// </summary>
    /// <returns>
    /// A tuple whose <c>handled</c> flag is <see langword="true"/> when the Draco path handled (or
    /// skipped) the primitive and the caller should return <c>result</c>; <see langword="false"/>
    /// when the primitive has no Draco extension and the caller should use the accessor-based path.
    /// </returns>
    private async Task<(
        bool handled,
        (Geometry? geometry, Handle<GeometryResourceType> handle) result
    )> TryRouteDracoPrimitiveAsync(
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        CancellationToken cancellationToken
    )
    {
        // Pre-decode: gate, parse, validate, and resolve the bufferView slice (shared with the
        // synchronous path). Skips already recorded their single diagnostic.
        DracoRouteStage stage = PrepareDracoDecode(
            primitive,
            meshIndex,
            primIndex,
            out byte[]? compressed,
            out DracoExtensionData? data
        );

        switch (stage)
        {
            case DracoRouteStage.UseAccessorPath:
                return (false, (null, Handle<GeometryResourceType>.Null));
            case DracoRouteStage.Skipped:
                // Diagnostic already recorded; the primitive is excluded from the Geometry.
                return (true, (null, Handle<GeometryResourceType>.Null));
            default:
                // Requirement 7.4: honor cancellation before the (potentially expensive) decode.
                cancellationToken.ThrowIfCancellationRequested();

                // Requirement 7.2: run the CPU-bound decode on a background thread so the calling
                // thread does not execute decode work. The resolved bufferView slice is materialized
                // to a byte[] (see PrepareDracoDecode) so it can be captured by the lambda; a
                // ReadOnlySpan<byte> cannot be captured in a closure.
                byte[] localCompressed = compressed!;
                IReadOnlyDictionary<string, int> localAttributes = data!.Attributes;
                DracoDecodeOutcome outcome = await Task.Run(
                        () => _dracoDecoder.Decode(localCompressed.AsSpan(), localAttributes),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                // Requirement 7.4: honor cancellation again before building/registering geometry.
                cancellationToken.ThrowIfCancellationRequested();

                var result = CompleteDracoDecode(
                    model,
                    primitive,
                    outcome,
                    data,
                    meshIndex,
                    primIndex
                );
                return (true, result);
        }
    }

    /// <summary>
    /// Identifies how a primitive should be routed after Draco pre-decode processing.
    /// </summary>
    private enum DracoRouteStage
    {
        /// <summary>The primitive has no Draco extension; use the existing accessor path.</summary>
        UseAccessorPath,

        /// <summary>
        /// The primitive declares the extension but was skipped (a single diagnostic has already
        /// been recorded); the caller should return a null geometry result.
        /// </summary>
        Skipped,

        /// <summary>The primitive is ready to decode; <c>compressed</c> and <c>data</c> are set.</summary>
        Decode,
    }

    /// <summary>
    /// Performs the shared, thread-agnostic pre-decode work for a Draco primitive: extension
    /// detection (Requirements 1.1, 1.2), the disabled/unavailable fallback gate
    /// (Requirements 6.2, 6.3, 9.3, 9.4), malformed-value and parse validation
    /// (Requirements 1.3, 1.4, 1.5, 4.1), and bufferView resolution (Requirement 2.2). Every skip
    /// records exactly one diagnostic. On <see cref="DracoRouteStage.Decode"/> the resolved
    /// bufferView slice is materialized into <paramref name="compressed"/> (a standalone
    /// <see cref="byte"/> array) so it can be safely offloaded to a background thread by the
    /// asynchronous path, and <paramref name="data"/> holds the parsed extension.
    /// </summary>
    private DracoRouteStage PrepareDracoDecode(
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        out byte[]? compressed,
        out DracoExtensionData? data
    )
    {
        compressed = null;
        data = null;

        // Requirement 1.2: no KHR_draco_mesh_compression key → use the existing accessor path.
        bool isJObject = TryGetDracoExtension(primitive, out JObject? raw, out bool present);
        if (!present)
        {
            return DracoRouteStage.UseAccessorPath;
        }

        // Requirements 6.2, 6.3, 9.3, 9.4: when Draco decompression is disabled or the decoder is
        // unavailable, skip the primitive via the fallback policy without invoking the decoder and
        // without reading the primitive's attributes through the AccessorReader. Severity is Error
        // when the extension is required, otherwise Warning.
        if (!_config.EnableDracoDecompression || !_dracoDecoder.IsAvailable)
        {
            string reason = !_config.EnableDracoDecompression ? "disabled" : "unavailable";
            _diagnostics.Add(
                new ImportDiagnostic(
                    FallbackSeverity(),
                    $"Mesh {meshIndex} primitive {primIndex} declares {DracoExtensionData.ExtensionName} "
                        + $"but Draco decompression is {reason}; skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );

            return DracoRouteStage.Skipped;
        }

        // Requirement 1.3: the extension value is present but null or not a JObject → skip with
        // exactly one Error diagnostic.
        if (!isJObject || raw is null)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} declares {DracoExtensionData.ExtensionName} "
                        + "but its value is null or not an object; skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );

            return DracoRouteStage.Skipped;
        }

        // Requirements 1.4, 1.5, 4.1: parse and validate the extension object. Any parse error
        // (missing/invalid bufferView, missing/empty attributes, or missing POSITION) results in a
        // single Error diagnostic and a skip.
        if (!DracoExtensionData.TryParse(raw, out DracoExtensionData? parsed, out var parseError))
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} has an invalid {DracoExtensionData.ExtensionName} "
                        + $"extension ({DescribeParseError(parseError)}); skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );

            return DracoRouteStage.Skipped;
        }

        // Requirement 2.2: resolve the bufferView slice through the bufferView → buffer chain. When
        // the slice cannot be resolved (invalid bufferView/buffer index or unloaded buffer), skip
        // with a single Error diagnostic.
        if (
            !_accessorReader.TryResolveDracoBufferView(
                parsed!.BufferView,
                out byte[]? buffer,
                out int offset,
                out int length
            ) || buffer is null
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} references an unresolvable {DracoExtensionData.ExtensionName} "
                        + $"bufferView {parsed.BufferView}; skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );

            return DracoRouteStage.Skipped;
        }

        // Materialize the resolved slice into a standalone array. A ReadOnlySpan<byte> cannot be
        // captured by the lambda the asynchronous path passes to Task.Run, so the slice is copied
        // here once and reused by both the synchronous and asynchronous decode paths.
        compressed = buffer.AsSpan(offset, length).ToArray();
        data = parsed;
        return DracoRouteStage.Decode;
    }

    /// <summary>
    /// Performs the shared post-decode work for a Draco primitive: maps a failed
    /// <see cref="DracoDecodeOutcome"/> to exactly one diagnostic
    /// (Requirements 2.5, 2.7, 6.2, 6.3, 9.3), validates decoded counts against the accessor
    /// metadata (Requirements 5.1–5.5, Warnings only), and builds + registers the geometry through
    /// the same <see cref="BuildGeometryFromDecodedMesh"/> logic used by both the synchronous and
    /// asynchronous paths, guaranteeing identical geometry (Requirement 7.3).
    /// </summary>
    private (Geometry? geometry, Handle<GeometryResourceType> handle) CompleteDracoDecode(
        Gltf model,
        MeshPrimitive primitive,
        DracoDecodeOutcome outcome,
        DracoExtensionData data,
        int meshIndex,
        int primIndex
    )
    {
        if (!outcome.Success || outcome.Mesh is null)
        {
            // Requirements 2.5, 2.7, 6.2, 6.3, 9.3: map the decode failure to exactly one
            // diagnostic. AttributeIdMissing and BitstreamDecodeFailed are always Error; a generic
            // decode failure uses the requiredness-based fallback severity.
            (DiagnosticSeverity severity, string message) = outcome.Reason switch
            {
                DracoFailureReason.AttributeIdMissing => (
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex}: Draco bitstream is missing the attribute "
                        + $"mapped to semantic '{outcome.Semantic}'; skipping primitive."
                ),
                DracoFailureReason.BitstreamDecodeFailed => (
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex}: failed to decode the Draco bitstream "
                        + "(corrupt or unsupported); skipping primitive."
                ),
                _ => (
                    FallbackSeverity(),
                    $"Mesh {meshIndex} primitive {primIndex}: Draco decoding failed; skipping primitive."
                ),
            };

            _diagnostics.Add(new ImportDiagnostic(severity, message, "Mesh", meshIndex));
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Requirements 5.1–5.5: validate the decoded vertex/index counts against the primitive's
        // accessor metadata. Mismatches are recorded as Warnings only (at most one vertex-count
        // Warning and one index-count Warning per primitive); the decoded data is always retained
        // and the primitive is still built and registered.
        ValidateDecodedCounts(model, primitive, outcome.Mesh, meshIndex, primIndex);

        // Successful decode: build the engine Geometry from the decoded mesh using the same build
        // logic as the synchronous path, so both paths produce identical geometry (Requirement 7.3).
        return BuildGeometryFromDecodedMesh(
            model,
            primitive,
            outcome.Mesh,
            data,
            meshIndex,
            primIndex
        );
    }

    /// <summary>
    /// Computes the diagnostic severity for a Draco fallback/skip: <see cref="DiagnosticSeverity.Error"/>
    /// when <c>KHR_draco_mesh_compression</c> is listed in <c>extensionsRequired</c>, otherwise
    /// <see cref="DiagnosticSeverity.Warning"/> (Requirements 6.2, 6.3, 9.3).
    /// </summary>
    private DiagnosticSeverity FallbackSeverity() =>
        _dracoRequired ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

    /// <summary>
    /// Returns a short human-readable description of a <see cref="DracoParseError"/> for diagnostics.
    /// </summary>
    private static string DescribeParseError(DracoParseError error) =>
        error switch
        {
            DracoParseError.MissingOrInvalidBufferView =>
                "missing or invalid bufferView (must be an integer >= 0)",
            DracoParseError.MissingOrEmptyAttributes => "missing or empty attributes map",
            DracoParseError.MissingPosition => "attributes map does not include POSITION",
            _ => "malformed extension object",
        };

    /// <summary>
    /// Validates a successfully produced <see cref="DecodedMesh"/> against the primitive's glTF
    /// accessor metadata (Requirement 5). Mismatches are reported as
    /// <see cref="DiagnosticSeverity.Warning"/> diagnostics only; the decoded data is always
    /// retained and the primitive is still built and registered (Requirement 5.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requirements 5.1–5.3: the decoded vertex count is compared against the count declared by each
    /// of the primitive's standard attribute accessors. If the decoded vertex count differs from the
    /// count declared by any attribute accessor, exactly one Warning is recorded (identifying the
    /// mesh and primitive index) and the remaining attribute accessors are not checked, so at most
    /// one vertex-count Warning is emitted per primitive.
    /// </para>
    /// <para>
    /// Requirements 5.4–5.5: when the decoded mesh contains indices, the decoded index count is
    /// compared against the count declared by the primitive's index accessor; a mismatch records
    /// exactly one Warning identifying the mesh and primitive index.
    /// </para>
    /// </remarks>
    private void ValidateDecodedCounts(
        Gltf model,
        MeshPrimitive primitive,
        DecodedMesh decoded,
        int meshIndex,
        int primIndex
    )
    {
        // Requirements 5.1, 5.2, 5.3: compare the decoded vertex count against the count declared by
        // each of the primitive's attribute accessors. A mismatch against any attribute accessor
        // yields exactly one Warning; the decoded data is retained and still built.
        if (primitive.Attributes != null)
        {
            int decodedVertexCount = decoded.VertexCount;
            foreach (var attribute in primitive.Attributes)
            {
                if (!TryGetAccessorCount(model, attribute.Value, out int accessorCount))
                {
                    continue;
                }

                if (accessorCount != decodedVertexCount)
                {
                    _diagnostics.Add(
                        new ImportDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Mesh {meshIndex} primitive {primIndex}: decoded Draco vertex count "
                                + $"{decodedVertexCount} does not match the count {accessorCount} declared by the "
                                + $"'{attribute.Key}' attribute accessor; retaining decoded data.",
                            "Mesh",
                            meshIndex
                        )
                    );

                    // Property 14: emit at most one vertex-count Warning per primitive.
                    break;
                }
            }
        }

        // Requirements 5.4, 5.5: when the decoded mesh contains indices, compare the decoded index
        // count against the count declared by the primitive's index accessor. A mismatch yields
        // exactly one Warning identifying the mesh and primitive index.
        if (
            decoded.Indices is { } indices
            && primitive.Indices.HasValue
            && TryGetAccessorCount(model, primitive.Indices.Value, out int indexAccessorCount)
            && indexAccessorCount != indices.Length
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Mesh {meshIndex} primitive {primIndex}: decoded Draco index count {indices.Length} "
                        + $"does not match the count {indexAccessorCount} declared by the index accessor; "
                        + "retaining decoded data.",
                    "Mesh",
                    meshIndex
                )
            );
        }
    }

    /// <summary>
    /// Safely resolves the element count declared by a glTF accessor, bounds-checking the index
    /// against the model's accessor table. Returns <see langword="false"/> when the index is
    /// negative or out of range so callers can skip the comparison rather than throw.
    /// </summary>
    private bool TryGetAccessorCount(Gltf model, int accessorIndex, out int count)
    {
        count = 0;
        if (accessorIndex < 0 || model.Accessors is null || accessorIndex >= model.Accessors.Length)
        {
            return false;
        }

        count = _accessorReader.GetAccessorCount(accessorIndex);
        return true;
    }

    // Engine attribute semantic names consumed by the Geometry (besides POSITION).
    private const string NormalSemantic = "NORMAL";
    private const string TexCoordSemantic = "TEXCOORD_0";
    private const string TangentSemantic = "TANGENT";
    private const string ColorSemantic = "COLOR_0";
    private const string PositionSemantic = "POSITION";

    /// <summary>
    /// Builds an engine <see cref="Geometry"/> from a successfully decoded Draco mesh and registers
    /// it with the geometry manager. This is the synchronous build path shared by both
    /// <see cref="ConvertPrimitive"/> and the asynchronous conversion path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Populates positions from the decoded <c>POSITION</c> attribute (Requirement 3.1), then the
    /// engine-stored optional attributes (<c>NORMAL</c>, <c>TEXCOORD_0</c>, <c>TANGENT</c>,
    /// <c>COLOR_0</c>) when their decoded element count matches the POSITION vertex count
    /// (Requirement 3.2). Auxiliary attributes are ignored without diagnostics (Requirement 3.3).
    /// </para>
    /// <para>
    /// An engine semantic declared in the primitive's standard <c>attributes</c> map but absent from
    /// the Draco <c>attributes</c> map is read through the <see cref="AccessorReader"/>
    /// (Requirement 5.6). Indices are populated when present, otherwise the geometry is built as
    /// non-indexed (Requirements 3.4, 3.5). Tangents are generated when no <c>TANGENT</c> attribute
    /// is available (Requirement 3.6). Bounds are computed and the geometry is registered with the
    /// geometry manager (Requirement 3.7).
    /// </para>
    /// <para>
    /// The primitive is skipped with exactly one <see cref="DiagnosticSeverity.Error"/> diagnostic
    /// (and never partially registered) when there is no usable POSITION (Requirements 3.8, 4.2) or
    /// when a consumed engine attribute's element count differs from the POSITION vertex count
    /// (Requirement 3.9).
    /// </para>
    /// </remarks>
    private (Geometry? geometry, Handle<GeometryResourceType> handle) BuildGeometryFromDecodedMesh(
        Gltf model,
        MeshPrimitive primitive,
        DecodedMesh decoded,
        DracoExtensionData data,
        int meshIndex,
        int primIndex
    )
    {
        // Map the primitive's topology mode (reuse the same helper as the accessor path).
        if (!TryMapTopology(primitive.Mode, out var topology))
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex} uses unsupported topology mode {(int)primitive.Mode}; skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        // Requirements 3.8, 4.2: a usable POSITION attribute is required. POSITION must be decoded,
        // expose at least 3 components, and contain at least one vertex.
        if (
            !decoded.Attributes.TryGetValue(PositionSemantic, out var positionAttr)
            || positionAttr.Components < 3
            || positionAttr.ElementCount == 0
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Mesh {meshIndex} primitive {primIndex}: Draco mesh has no usable POSITION attribute; skipping primitive.",
                    "Mesh",
                    meshIndex
                )
            );
            return (null, Handle<GeometryResourceType>.Null);
        }

        int vertexCount = positionAttr.ElementCount;

        // Requirement 3.9: before building, verify every consumed engine attribute that is present in
        // the decoded mesh shares the POSITION vertex count. A mismatch skips the primitive with a
        // single Error and never registers a partially-populated geometry.
        foreach (
            string semantic in new[]
            {
                NormalSemantic,
                TexCoordSemantic,
                TangentSemantic,
                ColorSemantic,
            }
        )
        {
            if (
                decoded.Attributes.TryGetValue(semantic, out var consumed)
                && consumed.ElementCount != vertexCount
            )
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Mesh {meshIndex} primitive {primIndex}: decoded {semantic} attribute has "
                            + $"{consumed.ElementCount} elements but POSITION has {vertexCount}; skipping primitive.",
                        "Mesh",
                        meshIndex
                    )
                );
                return (null, Handle<GeometryResourceType>.Null);
            }
        }

        var geometry = new Geometry(topology);

        // Requirement 3.1: populate positions from the decoded POSITION attribute.
        int posComponents = positionAttr.Components;
        float[] posValues = positionAttr.Values;
        for (int i = 0; i < vertexCount; i++)
        {
            int baseIndex = i * posComponents;
            geometry.Vertices.Add(
                new Vector4(
                    posValues[baseIndex],
                    posValues[baseIndex + 1],
                    posValues[baseIndex + 2],
                    1.0f
                )
            );
        }

        // Determine the source (decoded vs accessor) for each engine vertex-property semantic.
        bool normalDecoded = decoded.Attributes.ContainsKey(NormalSemantic);
        bool texCoordDecoded = decoded.Attributes.ContainsKey(TexCoordSemantic);
        bool tangentDecoded = decoded.Attributes.ContainsKey(TangentSemantic);

        bool normalAccessor = TryGetAccessorFallback(
            primitive,
            data,
            NormalSemantic,
            out int normalAccessorIndex
        );
        bool texCoordAccessor = TryGetAccessorFallback(
            primitive,
            data,
            TexCoordSemantic,
            out int texCoordAccessorIndex
        );
        bool tangentAccessor = TryGetAccessorFallback(
            primitive,
            data,
            TangentSemantic,
            out int tangentAccessorIndex
        );

        bool hasNormal = normalDecoded || normalAccessor;
        bool hasTexCoord = texCoordDecoded || texCoordAccessor;
        bool hasTangent = tangentDecoded || tangentAccessor;

        // Only populate VertexProps when at least one vertex-property semantic is present, mirroring
        // the accessor path (when none are present, VertexProps stays empty and tangent generation
        // is a no-op).
        if (hasNormal || hasTexCoord || hasTangent)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                geometry.VertexProps.Add(new VertexProperties());
            }

            // NORMAL (Requirement 3.2 / 5.6).
            if (normalDecoded)
            {
                WriteDecodedNormals(decoded.Attributes[NormalSemantic], geometry.VertexProps);
            }
            else if (normalAccessor)
            {
                _accessorReader.ReadNormals(normalAccessorIndex, geometry.VertexProps, merge: true);
            }

            // TEXCOORD_0 (Requirement 3.2 / 5.6).
            if (texCoordDecoded)
            {
                WriteDecodedTexCoords(decoded.Attributes[TexCoordSemantic], geometry.VertexProps);
            }
            else if (texCoordAccessor)
            {
                _accessorReader.ReadTexCoords(
                    texCoordAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }

            // TANGENT (Requirement 3.2 / 5.6).
            if (tangentDecoded)
            {
                WriteDecodedTangents(decoded.Attributes[TangentSemantic], geometry.VertexProps);
            }
            else if (tangentAccessor)
            {
                _accessorReader.ReadTangents(
                    tangentAccessorIndex,
                    geometry.VertexProps,
                    merge: true
                );
            }
        }

        // COLOR_0 (Requirement 3.2 / 5.6).
        if (decoded.Attributes.TryGetValue(ColorSemantic, out var colorAttr))
        {
            WriteDecodedColors(colorAttr, geometry.VertexColors);
        }
        else if (TryGetAccessorFallback(primitive, data, ColorSemantic, out int colorAccessorIndex))
        {
            _accessorReader.ReadColors(colorAccessorIndex, geometry.VertexColors);
        }

        // Requirements 3.4, 3.5: populate indices when present, otherwise build non-indexed.
        if (decoded.Indices is { } indices)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                geometry.Indices.Add(indices[i]);
            }
        }

        // Requirement 3.6: generate tangents when no TANGENT attribute is available from either source.
        if (!hasTangent)
        {
            TangentGenerator.ComputeTangents(geometry);
        }

        // Requirement 3.7: compute bounding volumes and register with the geometry manager.
        geometry.UpdateBounds();

        var handle = _geometryManager.Add(geometry);
        if (!handle.Valid)
        {
            return (null, handle);
        }

        _manifest.AddGeometry(geometry);
        return (geometry, handle);
    }

    /// <summary>
    /// Determines whether an engine semantic should be read through the <see cref="AccessorReader"/>:
    /// it is declared in the primitive's standard <c>attributes</c> map but absent from the Draco
    /// <c>attributes</c> map (Requirement 5.6).
    /// </summary>
    private static bool TryGetAccessorFallback(
        MeshPrimitive primitive,
        DracoExtensionData data,
        string semantic,
        out int accessorIndex
    )
    {
        accessorIndex = 0;
        if (data.Attributes.ContainsKey(semantic))
        {
            // The semantic is carried by the Draco bitstream; never use the accessor path for it.
            return false;
        }

        return primitive.Attributes != null
            && primitive.Attributes.TryGetValue(semantic, out accessorIndex);
    }

    /// <summary>
    /// Writes decoded NORMAL values (VEC3) into the supplied vertex-property list by index.
    /// </summary>
    private static void WriteDecodedNormals(
        DecodedAttribute attribute,
        FastList<VertexProperties> output
    )
    {
        int components = attribute.Components;
        float[] values = attribute.Values;
        int count = Math.Min(attribute.ElementCount, output.Count);
        for (int i = 0; i < count; i++)
        {
            int baseIndex = i * components;
            var existing = output[i];
            existing.Normal = new Vector3(
                values[baseIndex],
                values[baseIndex + 1],
                values[baseIndex + 2]
            );
            output[i] = existing;
        }
    }

    /// <summary>
    /// Writes decoded TEXCOORD_0 values (VEC2) into the supplied vertex-property list by index.
    /// </summary>
    private static void WriteDecodedTexCoords(
        DecodedAttribute attribute,
        FastList<VertexProperties> output
    )
    {
        int components = attribute.Components;
        float[] values = attribute.Values;
        int count = Math.Min(attribute.ElementCount, output.Count);
        for (int i = 0; i < count; i++)
        {
            int baseIndex = i * components;
            var existing = output[i];
            existing.TexCoord = new Vector2(values[baseIndex], values[baseIndex + 1]);
            output[i] = existing;
        }
    }

    /// <summary>
    /// Writes decoded TANGENT values into the supplied vertex-property list by index. A VEC4 tangent
    /// supplies handedness in <c>W</c>; a VEC3 tangent uses the default handedness.
    /// </summary>
    private static void WriteDecodedTangents(
        DecodedAttribute attribute,
        FastList<VertexProperties> output
    )
    {
        int components = attribute.Components;
        float[] values = attribute.Values;
        int count = Math.Min(attribute.ElementCount, output.Count);
        for (int i = 0; i < count; i++)
        {
            int baseIndex = i * components;
            float w = components >= 4 ? values[baseIndex + 3] : VertexProperties.DefaultTangent.W;
            var existing = output[i];
            existing.Tangent = new Vector4(
                values[baseIndex],
                values[baseIndex + 1],
                values[baseIndex + 2],
                w
            );
            output[i] = existing;
        }
    }

    /// <summary>
    /// Writes decoded COLOR_0 values into the supplied color list. A VEC4 color supplies alpha; a
    /// VEC3 color uses an alpha of 1.0.
    /// </summary>
    private static void WriteDecodedColors(DecodedAttribute attribute, FastList<Vector4> output)
    {
        int components = attribute.Components;
        float[] values = attribute.Values;
        int count = attribute.ElementCount;
        for (int i = 0; i < count; i++)
        {
            int baseIndex = i * components;
            float a = components >= 4 ? values[baseIndex + 3] : 1.0f;
            output.Add(
                new Vector4(values[baseIndex], values[baseIndex + 1], values[baseIndex + 2], a)
            );
        }
    }

    /// <summary>
    /// Maps a glTF primitive mode to the engine's Topology enum.
    /// </summary>
    /// <param name="mode">The glTF primitive mode.</param>
    /// <param name="topology">The mapped engine topology.</param>
    /// <returns>True if the mode is supported; false otherwise.</returns>
    private static bool TryMapTopology(MeshPrimitive.ModeEnum mode, out Topology topology)
    {
        topology = mode switch
        {
            MeshPrimitive.ModeEnum.POINTS => Topology.Point,
            MeshPrimitive.ModeEnum.LINES => Topology.Line,
            MeshPrimitive.ModeEnum.TRIANGLES => Topology.Triangle,
            MeshPrimitive.ModeEnum.TRIANGLE_STRIP => Topology.TriangleStrip,
            _ => Topology.Triangle, // placeholder, will be rejected below
        };

        // Only support modes 0, 1, 4, 5
        return mode
            is MeshPrimitive.ModeEnum.POINTS
                or MeshPrimitive.ModeEnum.LINES
                or MeshPrimitive.ModeEnum.TRIANGLES
                or MeshPrimitive.ModeEnum.TRIANGLE_STRIP;
    }

    private Geometry? _sphere;

    public Geometry GetSphereMesh()
    {
        if (_sphere == null)
        {
            var builder = new MeshBuilder();
            builder.AddSphere(Vector3.Zero, 0.5f);
            _sphere = builder.ToMesh().ToGeometry();
            _manifest.AddGeometry(_sphere);
            _geometryManager.Add(_sphere);
        }
        return _sphere;
    }
}
