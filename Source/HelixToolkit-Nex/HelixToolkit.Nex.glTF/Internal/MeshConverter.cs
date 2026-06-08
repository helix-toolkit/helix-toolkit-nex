using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using Vertex = System.Numerics.Vector4;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Converts glTF MeshPrimitives into engine Geometry objects.
/// Reads vertex attributes and indices via AccessorReader.
/// </summary>
internal sealed class MeshConverter
{
    private readonly IGeometryManager _geometryManager;
    private readonly AccessorReader _accessorReader;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly ResourceManifest _manifest;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshConverter"/> class.
    /// </summary>
    /// <param name="geometryManager">The geometry manager to register geometry resources with.</param>
    /// <param name="accessorReader">The accessor reader for reading vertex/index data from glTF buffers.</param>
    /// <param name="diagnostics">The diagnostics list to report errors and warnings to.</param>
    /// <param name="manifest">The resource manifest to track created geometry resources.</param>
    public MeshConverter(
        IGeometryManager geometryManager,
        AccessorReader accessorReader,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest manifest
    )
    {
        _geometryManager =
            geometryManager ?? throw new ArgumentNullException(nameof(geometryManager));
        _accessorReader = accessorReader ?? throw new ArgumentNullException(nameof(accessorReader));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
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
    /// <returns>A tuple of the created Geometry (or null if skipped) and the geometry handle.</returns>
    public async Task<(
        Geometry? geometry,
        Handle<GeometryResourceType> handle
    )> ConvertPrimitiveAsync(Gltf model, MeshPrimitive primitive, int meshIndex, int primIndex)
    {
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
}
