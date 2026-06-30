using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using Newtonsoft.Json.Linq;
using GltfBuffer = glTFLoader.Schema.Buffer;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// Builds in-memory glTF models, backing buffers, and nodes for <c>EXT_mesh_gpu_instancing</c>
/// parser/reader property and unit tests. It mirrors <see cref="DracoModelBuilder"/> but targets the
/// instancing pipeline: it can register per-instance attribute accessors (<c>TRANSLATION</c> VEC3
/// FLOAT, <c>ROTATION</c> VEC4 FLOAT / normalized signed BYTE / normalized signed SHORT, <c>SCALE</c>
/// VEC3 FLOAT), back them with concrete bytes (including deliberately too-short buffers and
/// non-finite payloads), and assemble nodes carrying the extension <see cref="JObject"/>.
/// </summary>
/// <remarks>
/// The builder is stateful: register elements with the <c>Add*</c> methods (each returns its index),
/// then call <see cref="BuildModel"/> to materialize the <see cref="Gltf"/> and the matching
/// <c>byte[][]</c> buffer data that <see cref="AccessorReader"/> consumes. The typed encoders
/// (<see cref="AddTranslationAccessor"/>, <see cref="AddRotationFloatAccessor"/>,
/// <see cref="AddRotationNormalizedByteAccessor"/>, <see cref="AddRotationNormalizedShortAccessor"/>,
/// <see cref="AddScaleAccessor"/>) cover the common valid cases; <see cref="AddRawAccessor"/> exposes
/// the low-level escape hatch for malformed element/component types and undersized buffers.
/// </remarks>
internal sealed class InstancingModelBuilder
{
    /// <summary>The glTF extension name the builder targets.</summary>
    public const string ExtensionName = "EXT_mesh_gpu_instancing";

    /// <summary>The recognized per-instance attribute key for translation (VEC3 FLOAT).</summary>
    public const string TranslationKey = "TRANSLATION";

    /// <summary>The recognized per-instance attribute key for rotation (VEC4 quaternion).</summary>
    public const string RotationKey = "ROTATION";

    /// <summary>The recognized per-instance attribute key for scale (VEC3 FLOAT).</summary>
    public const string ScaleKey = "SCALE";

    private readonly List<byte[]> _buffers = [];
    private readonly List<BufferView> _bufferViews = [];
    private readonly List<Accessor> _accessors = [];
    private readonly List<Mesh> _meshes = [];
    private readonly List<Node> _nodes = [];
    private readonly List<string> _extensionsUsed = [];
    private readonly List<string> _extensionsRequired = [];

    // ──────────────────────────────────────────────────────────────────────────────
    // Low-level registration
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a buffer's raw bytes and a matching <see cref="GltfBuffer"/> entry.
    /// </summary>
    /// <param name="data">The buffer bytes.</param>
    /// <returns>The index of the registered buffer.</returns>
    public int AddBuffer(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _buffers.Add(data);
        return _buffers.Count - 1;
    }

    /// <summary>
    /// Registers a <see cref="BufferView"/> over a previously added buffer.
    /// </summary>
    /// <param name="buffer">The buffer index this view refers to.</param>
    /// <param name="byteOffset">The byte offset within the buffer.</param>
    /// <param name="byteLength">The byte length of the view.</param>
    /// <returns>The index of the registered bufferView.</returns>
    public int AddBufferView(int buffer, int byteOffset, int byteLength)
    {
        _bufferViews.Add(
            new BufferView
            {
                Buffer = buffer,
                ByteOffset = byteOffset,
                ByteLength = byteLength,
            }
        );
        return _bufferViews.Count - 1;
    }

    /// <summary>
    /// Registers an accessor of the given element/component type backed by <paramref name="bufferBytes"/>.
    /// </summary>
    /// <param name="count">The declared element count of the accessor (may be zero).</param>
    /// <param name="type">The accessor element type.</param>
    /// <param name="componentType">The accessor component type.</param>
    /// <param name="bufferBytes">
    /// The concrete bytes to back the accessor. Pass tightly packed element data for a valid accessor,
    /// or a deliberately undersized array to exercise the out-of-range read path (Requirement 4.4).
    /// </param>
    /// <param name="normalized">Whether the accessor data is normalized (for signed BYTE/SHORT rotations).</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddRawAccessor(
        int count,
        Accessor.TypeEnum type,
        Accessor.ComponentTypeEnum componentType,
        byte[] bufferBytes,
        bool normalized = false
    )
    {
        ArgumentNullException.ThrowIfNull(bufferBytes);

        int buffer = AddBuffer(bufferBytes);
        int bufferView = AddBufferView(buffer, 0, bufferBytes.Length);
        _accessors.Add(
            new Accessor
            {
                BufferView = bufferView,
                ByteOffset = 0,
                ComponentType = componentType,
                Type = type,
                Count = count,
                Normalized = normalized,
            }
        );
        return _accessors.Count - 1;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Typed attribute encoders
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a <c>TRANSLATION</c> accessor (VEC3 FLOAT) for the supplied per-instance vectors.
    /// </summary>
    /// <param name="translations">The per-instance translation vectors.</param>
    /// <param name="bufferByteOverride">
    /// When non-null, backs the accessor with these bytes instead of the encoded translations (use a
    /// shorter array to trigger an out-of-range read).
    /// </param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddTranslationAccessor(
        IReadOnlyList<Vector3> translations,
        byte[]? bufferByteOverride = null
    )
    {
        ArgumentNullException.ThrowIfNull(translations);
        byte[] bytes = bufferByteOverride ?? EncodeVec3Float(translations);
        return AddRawAccessor(
            translations.Count,
            Accessor.TypeEnum.VEC3,
            Accessor.ComponentTypeEnum.FLOAT,
            bytes
        );
    }

    /// <summary>
    /// Registers a <c>SCALE</c> accessor (VEC3 FLOAT) for the supplied per-instance vectors.
    /// </summary>
    /// <param name="scales">The per-instance scale vectors (X is used as the uniform scale).</param>
    /// <param name="bufferByteOverride">Optional explicit backing bytes (e.g. an undersized buffer).</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddScaleAccessor(IReadOnlyList<Vector3> scales, byte[]? bufferByteOverride = null)
    {
        ArgumentNullException.ThrowIfNull(scales);
        byte[] bytes = bufferByteOverride ?? EncodeVec3Float(scales);
        return AddRawAccessor(
            scales.Count,
            Accessor.TypeEnum.VEC3,
            Accessor.ComponentTypeEnum.FLOAT,
            bytes
        );
    }

    /// <summary>
    /// Registers a <c>ROTATION</c> accessor (VEC4 FLOAT) for the supplied per-instance quaternions
    /// stored as <c>(x, y, z, w)</c> vectors.
    /// </summary>
    /// <param name="rotations">The per-instance quaternion components in <c>(x, y, z, w)</c> order.</param>
    /// <param name="bufferByteOverride">Optional explicit backing bytes (e.g. an undersized buffer).</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddRotationFloatAccessor(
        IReadOnlyList<Vector4> rotations,
        byte[]? bufferByteOverride = null
    )
    {
        ArgumentNullException.ThrowIfNull(rotations);
        byte[] bytes = bufferByteOverride ?? EncodeVec4Float(rotations);
        return AddRawAccessor(
            rotations.Count,
            Accessor.TypeEnum.VEC4,
            Accessor.ComponentTypeEnum.FLOAT,
            bytes
        );
    }

    /// <summary>
    /// Registers a <c>ROTATION</c> accessor (VEC4 normalized signed BYTE) for the supplied encoded
    /// quaternion components.
    /// </summary>
    /// <param name="encoded">The per-instance quaternion components as signed bytes in <c>(x, y, z, w)</c> order.</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddRotationNormalizedByteAccessor(IReadOnlyList<(sbyte X, sbyte Y, sbyte Z, sbyte W)> encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        byte[] bytes = EncodeVec4SignedByte(encoded);
        return AddRawAccessor(
            encoded.Count,
            Accessor.TypeEnum.VEC4,
            Accessor.ComponentTypeEnum.BYTE,
            bytes,
            normalized: true
        );
    }

    /// <summary>
    /// Registers a <c>ROTATION</c> accessor (VEC4 normalized signed SHORT) for the supplied encoded
    /// quaternion components.
    /// </summary>
    /// <param name="encoded">The per-instance quaternion components as signed shorts in <c>(x, y, z, w)</c> order.</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddRotationNormalizedShortAccessor(IReadOnlyList<(short X, short Y, short Z, short W)> encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        byte[] bytes = EncodeVec4SignedShort(encoded);
        return AddRawAccessor(
            encoded.Count,
            Accessor.TypeEnum.VEC4,
            Accessor.ComponentTypeEnum.SHORT,
            bytes,
            normalized: true
        );
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Mesh and node registration
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a mesh from the supplied primitives.
    /// </summary>
    /// <param name="primitives">The primitives that make up the mesh.</param>
    /// <returns>The index of the registered mesh.</returns>
    public int AddMesh(params MeshPrimitive[] primitives)
    {
        _meshes.Add(new Mesh { Primitives = primitives });
        return _meshes.Count - 1;
    }

    /// <summary>
    /// Registers a mesh with <paramref name="primitiveCount"/> identical primitives, each referencing
    /// <paramref name="positionAccessor"/> as its <c>POSITION</c> attribute. Handy for multi-primitive
    /// instancing tests (Properties 18 and 20).
    /// </summary>
    /// <param name="primitiveCount">The number of primitives to create (minimum 1).</param>
    /// <param name="positionAccessor">The accessor index used for each primitive's <c>POSITION</c>.</param>
    /// <returns>The index of the registered mesh.</returns>
    public int AddSimpleMesh(int primitiveCount, int positionAccessor)
    {
        var primitives = new MeshPrimitive[Math.Max(1, primitiveCount)];
        for (int i = 0; i < primitives.Length; i++)
        {
            primitives[i] = new MeshPrimitive
            {
                Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                Attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor },
            };
        }

        return AddMesh(primitives);
    }

    /// <summary>
    /// Registers a node, optionally attaching a value under the <c>EXT_mesh_gpu_instancing</c>
    /// extension key, a mesh reference, child node indices, and a local transform.
    /// </summary>
    /// <param name="extensionValue">
    /// The value to store under the extension key. Pass a <see cref="JObject"/> for a well-formed
    /// extension, an arbitrary <see cref="JToken"/> to exercise malformed-value paths, or
    /// <see langword="null"/> to omit the extension entirely (no key added).
    /// </param>
    /// <param name="mesh">The node's mesh index, or <see langword="null"/> for no mesh.</param>
    /// <param name="children">The node's child indices, or <see langword="null"/>.</param>
    /// <param name="translation">Optional TRS translation (defaults to identity when all TRS omitted).</param>
    /// <param name="rotation">Optional TRS rotation as <c>(x, y, z, w)</c>.</param>
    /// <param name="scale">Optional TRS scale.</param>
    /// <param name="matrix">Optional explicit 16-element column-major matrix (takes precedence over TRS).</param>
    /// <param name="name">Optional node name.</param>
    /// <returns>The index of the registered node.</returns>
    public int AddNode(
        object? extensionValue = null,
        int? mesh = null,
        int[]? children = null,
        Vector3? translation = null,
        Vector4? rotation = null,
        Vector3? scale = null,
        float[]? matrix = null,
        string? name = null
    )
    {
        var node = new Node { Name = name };

        if (mesh.HasValue)
        {
            node.Mesh = mesh.Value;
        }

        if (children is { Length: > 0 })
        {
            node.Children = children;
        }

        if (matrix is not null)
        {
            node.Matrix = matrix;
        }
        else
        {
            if (translation.HasValue)
            {
                node.Translation = [translation.Value.X, translation.Value.Y, translation.Value.Z];
            }

            if (rotation.HasValue)
            {
                node.Rotation =
                [
                    rotation.Value.X,
                    rotation.Value.Y,
                    rotation.Value.Z,
                    rotation.Value.W,
                ];
            }

            if (scale.HasValue)
            {
                node.Scale = [scale.Value.X, scale.Value.Y, scale.Value.Z];
            }
        }

        if (extensionValue is not null)
        {
            node.Extensions = new Dictionary<string, object>
            {
                [ExtensionName] = extensionValue,
            };
        }

        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    /// <summary>
    /// Marks an extension name as used and (optionally) required, populating
    /// <c>extensionsUsed</c>/<c>extensionsRequired</c> for the disabled-path severity tests.
    /// </summary>
    /// <param name="required">When <see langword="true"/>, the name is added to <c>extensionsRequired</c>.</param>
    /// <param name="name">The extension name; defaults to <c>EXT_mesh_gpu_instancing</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public InstancingModelBuilder DeclareExtension(bool required, string name = ExtensionName)
    {
        if (!_extensionsUsed.Contains(name))
        {
            _extensionsUsed.Add(name);
        }

        if (required && !_extensionsRequired.Contains(name))
        {
            _extensionsRequired.Add(name);
        }

        return this;
    }

    /// <summary>
    /// Produces the assembled <see cref="Gltf"/> model and matching per-buffer byte arrays.
    /// </summary>
    /// <returns>A tuple of the model and the buffer data arrays (as <see cref="AccessorReader"/> expects).</returns>
    public (Gltf model, byte[][] buffers) BuildModel()
    {
        var gltfBuffers = new GltfBuffer[_buffers.Count];
        for (int i = 0; i < _buffers.Count; i++)
        {
            gltfBuffers[i] = new GltfBuffer { ByteLength = _buffers[i].Length };
        }

        var model = new Gltf
        {
            Buffers = gltfBuffers.Length > 0 ? gltfBuffers : null,
            BufferViews = _bufferViews.Count > 0 ? _bufferViews.ToArray() : null,
            Accessors = _accessors.Count > 0 ? _accessors.ToArray() : null,
            Meshes = _meshes.Count > 0 ? _meshes.ToArray() : null,
            Nodes = _nodes.Count > 0 ? _nodes.ToArray() : null,
            ExtensionsUsed = _extensionsUsed.Count > 0 ? _extensionsUsed.ToArray() : null,
            ExtensionsRequired =
                _extensionsRequired.Count > 0 ? _extensionsRequired.ToArray() : null,
        };

        return (model, _buffers.ToArray());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Static byte encoders (reusable by generators and tests)
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Encodes a sequence of VEC3 FLOAT elements into a tightly packed byte buffer.</summary>
    public static byte[] EncodeVec3Float(IReadOnlyList<Vector3> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var bytes = new byte[elements.Count * 3 * sizeof(float)];
        for (int i = 0; i < elements.Count; i++)
        {
            int offset = i * 3 * sizeof(float);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), elements[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 4, 4), elements[i].Y);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 8, 4), elements[i].Z);
        }

        return bytes;
    }

    /// <summary>Encodes a sequence of VEC4 FLOAT elements into a tightly packed byte buffer.</summary>
    public static byte[] EncodeVec4Float(IReadOnlyList<Vector4> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var bytes = new byte[elements.Count * 4 * sizeof(float)];
        for (int i = 0; i < elements.Count; i++)
        {
            int offset = i * 4 * sizeof(float);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), elements[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 4, 4), elements[i].Y);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 8, 4), elements[i].Z);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 12, 4), elements[i].W);
        }

        return bytes;
    }

    /// <summary>Encodes a sequence of VEC4 signed BYTE elements into a tightly packed byte buffer.</summary>
    public static byte[] EncodeVec4SignedByte(IReadOnlyList<(sbyte X, sbyte Y, sbyte Z, sbyte W)> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var bytes = new byte[elements.Count * 4];
        for (int i = 0; i < elements.Count; i++)
        {
            int offset = i * 4;
            bytes[offset] = unchecked((byte)elements[i].X);
            bytes[offset + 1] = unchecked((byte)elements[i].Y);
            bytes[offset + 2] = unchecked((byte)elements[i].Z);
            bytes[offset + 3] = unchecked((byte)elements[i].W);
        }

        return bytes;
    }

    /// <summary>Encodes a sequence of VEC4 signed SHORT elements into a tightly packed byte buffer.</summary>
    public static byte[] EncodeVec4SignedShort(IReadOnlyList<(short X, short Y, short Z, short W)> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var bytes = new byte[elements.Count * 4 * sizeof(short)];
        for (int i = 0; i < elements.Count; i++)
        {
            int offset = i * 4 * sizeof(short);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset, 2), elements[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 2, 2), elements[i].Y);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 4, 2), elements[i].Z);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 6, 2), elements[i].W);
        }

        return bytes;
    }
}
