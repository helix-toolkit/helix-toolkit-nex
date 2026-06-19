using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal.Draco;
using Vertex = System.Numerics.Vector4;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Low-level utility that reads typed data from glTF accessors/buffer views into spans.
/// Handles component type conversion and byte stride calculations.
/// </summary>
internal sealed class AccessorReader
{
    private readonly Gltf _model;
    private readonly byte[][] _bufferData;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessorReader"/> class.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="bufferData">The raw binary buffer data arrays.</param>
    public AccessorReader(Gltf model, byte[][] bufferData)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _bufferData = bufferData ?? throw new ArgumentNullException(nameof(bufferData));
    }

    /// <summary>
    /// Resolves the accessor → bufferView → buffer chain and returns a span of the raw accessor data.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <returns>A read-only span of bytes containing the accessor's data.</returns>
    public ReadOnlySpan<byte> GetAccessorData(int accessorIndex)
    {
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.BufferView == null)
        {
            // Accessor without a buffer view (all zeros per glTF spec)
            return ReadOnlySpan<byte>.Empty;
        }

        var bufferView = _model.BufferViews[accessor.BufferView.Value];
        var buffer = _bufferData[bufferView.Buffer];

        int byteOffset = accessor.ByteOffset + bufferView.ByteOffset;
        int componentSize = GetComponentSize(accessor.ComponentType);
        int typeCount = GetTypeCount(accessor.Type);
        int elementSize = componentSize * typeCount;
        int stride = bufferView.ByteStride ?? elementSize;

        // Total bytes = (count - 1) * stride + elementSize
        // This accounts for the last element which doesn't need stride padding after it
        int totalBytes = accessor.Count > 0 ? (accessor.Count - 1) * stride + elementSize : 0;

        return buffer.AsSpan(byteOffset, totalBytes);
    }

    /// <summary>
    /// Returns the number of elements referenced by the specified accessor.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <returns>The element count of the accessor.</returns>
    public int GetAccessorCount(int accessorIndex)
    {
        return _model.Accessors[accessorIndex].Count;
    }

    /// <summary>
    /// Resolves a Draco extension's <c>bufferView</c> index to a concrete byte slice
    /// (<paramref name="buffer"/>, <paramref name="offset"/>, <paramref name="length"/>) using the
    /// already-loaded buffer data, without requiring an accessor. Delegates to
    /// <see cref="DracoBufferViewResolver"/> so the loaded buffer data stays encapsulated here.
    /// </summary>
    /// <param name="bufferViewIndex">The <c>bufferView</c> index declared by the Draco extension.</param>
    /// <param name="buffer">The backing buffer byte array on success; otherwise <see langword="null"/>.</param>
    /// <param name="offset">The byte offset of the slice within <paramref name="buffer"/> on success; otherwise 0.</param>
    /// <param name="length">The byte length of the slice on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when the slice resolves within bounds; otherwise <see langword="false"/>.</returns>
    public bool TryResolveDracoBufferView(
        int bufferViewIndex,
        out byte[]? buffer,
        out int offset,
        out int length
    )
    {
        return DracoBufferViewResolver.TryResolve(
            _model,
            _bufferData,
            bufferViewIndex,
            out buffer,
            out offset,
            out length
        );
    }

    /// <summary>
    /// Validates that the accessor's data range fits within the buffer bounds.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="error">When validation fails, contains a description of the error; otherwise null.</param>
    /// <returns>True if the accessor is valid; false if it references data beyond buffer bounds.</returns>
    public bool ValidateAccessor(int accessorIndex, out string? error)
    {
        error = null;

        if (
            _model.Accessors == null
            || accessorIndex < 0
            || accessorIndex >= _model.Accessors.Length
        )
        {
            error = $"Accessor index {accessorIndex} is out of range.";
            return false;
        }

        var accessor = _model.Accessors[accessorIndex];

        if (accessor.BufferView == null)
        {
            // Accessor without buffer view is valid (all zeros)
            return true;
        }

        int bufferViewIndex = accessor.BufferView.Value;
        if (
            _model.BufferViews == null
            || bufferViewIndex < 0
            || bufferViewIndex >= _model.BufferViews.Length
        )
        {
            error =
                $"Accessor {accessorIndex} references invalid buffer view index {bufferViewIndex}.";
            return false;
        }

        var bufferView = _model.BufferViews[bufferViewIndex];

        if (bufferView.Buffer < 0 || bufferView.Buffer >= _bufferData.Length)
        {
            error =
                $"Buffer view {bufferViewIndex} references invalid buffer index {bufferView.Buffer}.";
            return false;
        }

        var buffer = _bufferData[bufferView.Buffer];
        int byteOffset = accessor.ByteOffset + bufferView.ByteOffset;
        int componentSize = GetComponentSize(accessor.ComponentType);
        int typeCount = GetTypeCount(accessor.Type);
        int elementSize = componentSize * typeCount;
        int stride = bufferView.ByteStride ?? elementSize;

        // Check: accessor offset + (count - 1) * stride + elementSize ≤ buffer length
        long requiredBytes =
            accessor.Count > 0
                ? byteOffset + (long)(accessor.Count - 1) * stride + elementSize
                : byteOffset;

        if (requiredBytes > buffer.Length)
        {
            error =
                $"Accessor {accessorIndex} requires {requiredBytes} bytes but buffer {bufferView.Buffer} is only {buffer.Length} bytes.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the byte size of a single component for the given component type.
    /// </summary>
    /// <param name="componentType">The glTF component type enum value.</param>
    /// <returns>The size in bytes of one component.</returns>
    public static int GetComponentSize(Accessor.ComponentTypeEnum componentType)
    {
        return componentType switch
        {
            Accessor.ComponentTypeEnum.BYTE => 1,
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => 1,
            Accessor.ComponentTypeEnum.SHORT => 2,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => 2,
            Accessor.ComponentTypeEnum.UNSIGNED_INT => 4,
            Accessor.ComponentTypeEnum.FLOAT => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(componentType),
                componentType,
                "Unsupported component type."
            ),
        };
    }

    /// <summary>
    /// Gets the number of components for the given accessor type.
    /// </summary>
    /// <param name="type">The glTF accessor type enum value.</param>
    /// <returns>The number of components per element.</returns>
    public static int GetTypeCount(Accessor.TypeEnum type)
    {
        return type switch
        {
            Accessor.TypeEnum.SCALAR => 1,
            Accessor.TypeEnum.VEC2 => 2,
            Accessor.TypeEnum.VEC3 => 3,
            Accessor.TypeEnum.VEC4 => 4,
            Accessor.TypeEnum.MAT2 => 4,
            Accessor.TypeEnum.MAT3 => 9,
            Accessor.TypeEnum.MAT4 => 16,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported accessor type."
            ),
        };
    }

    /// <summary>
    /// Gets the byte stride for the given accessor, accounting for buffer view stride or tightly packed data.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <returns>The byte stride between consecutive elements.</returns>
    public int GetStride(int accessorIndex)
    {
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.BufferView == null)
        {
            return GetComponentSize(accessor.ComponentType) * GetTypeCount(accessor.Type);
        }

        var bufferView = _model.BufferViews[accessor.BufferView.Value];
        int componentSize = GetComponentSize(accessor.ComponentType);
        int typeCount = GetTypeCount(accessor.Type);
        int elementSize = componentSize * typeCount;
        return bufferView.ByteStride ?? elementSize;
    }

    /// <summary>
    /// Gets the byte offset into the buffer for the given accessor.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <returns>The combined byte offset (accessor offset + buffer view offset).</returns>
    public int GetByteOffset(int accessorIndex)
    {
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.BufferView == null)
        {
            return 0;
        }

        var bufferView = _model.BufferViews[accessor.BufferView.Value];
        return accessor.ByteOffset + bufferView.ByteOffset;
    }

    /// <summary>
    /// Gets the raw buffer data for the buffer referenced by the given accessor.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <returns>The raw buffer byte array, or null if the accessor has no buffer view.</returns>
    public byte[]? GetBuffer(int accessorIndex)
    {
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.BufferView == null)
        {
            return null;
        }

        var bufferView = _model.BufferViews[accessor.BufferView.Value];
        return _bufferData[bufferView.Buffer];
    }

    /// <summary>
    /// Reads position data (VEC3 FLOAT) into a FastList of Vertex (Vector4 with w=1.0).
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate with vertex positions.</param>
    public void ReadPositions(int accessorIndex, FastList<Vertex> output)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            // No buffer view — fill with zeros
            for (int i = 0; i < accessor.Count; i++)
            {
                output.Add(new Vertex(0, 0, 0, 1.0f));
            }
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float x = BitConverter.ToSingle(buffer, offset);
            float y = BitConverter.ToSingle(buffer, offset + 4);
            float z = BitConverter.ToSingle(buffer, offset + 8);
            output.Add(new Vertex(x, y, z, 1.0f));
        }
    }

    /// <summary>
    /// Reads normal data (VEC3 FLOAT) into a FastList of VertexProperties.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate or merge normals into.</param>
    /// <param name="merge">If true, updates existing entries' Normal field; if false, adds new entries.</param>
    public void ReadNormals(int accessorIndex, FastList<VertexProperties> output, bool merge)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            if (!merge)
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    output.Add(new VertexProperties(Vector3.Zero));
                }
            }
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float x = BitConverter.ToSingle(buffer, offset);
            float y = BitConverter.ToSingle(buffer, offset + 4);
            float z = BitConverter.ToSingle(buffer, offset + 8);
            var normal = new Vector3(x, y, z);

            if (merge && i < output.Count)
            {
                var existing = output[i];
                existing.Normal = normal;
                output[i] = existing;
            }
            else
            {
                output.Add(new VertexProperties(normal));
            }
        }
    }

    /// <summary>
    /// Reads texture coordinate data (VEC2 FLOAT) into a FastList of VertexProperties.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate or merge tex coords into.</param>
    /// <param name="merge">If true, updates existing entries' TexCoord field; if false, adds new entries.</param>
    public void ReadTexCoords(int accessorIndex, FastList<VertexProperties> output, bool merge)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            if (!merge)
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    output.Add(new VertexProperties());
                }
            }
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float u = BitConverter.ToSingle(buffer, offset);
            float v = BitConverter.ToSingle(buffer, offset + 4);
            var texCoord = new Vector2(u, v);

            if (merge && i < output.Count)
            {
                var existing = output[i];
                existing.TexCoord = texCoord;
                output[i] = existing;
            }
            else
            {
                output.Add(new VertexProperties(Vector3.Zero, texCoord));
            }
        }
    }

    /// <summary>
    /// Reads tangent data (VEC4 FLOAT) into a FastList of VertexProperties.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate or merge tangents into.</param>
    /// <param name="merge">If true, updates existing entries' Tangent field; if false, adds new entries.</param>
    public void ReadTangents(int accessorIndex, FastList<VertexProperties> output, bool merge)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            if (!merge)
            {
                for (int i = 0; i < accessor.Count; i++)
                {
                    output.Add(new VertexProperties());
                }
            }
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float x = BitConverter.ToSingle(buffer, offset);
            float y = BitConverter.ToSingle(buffer, offset + 4);
            float z = BitConverter.ToSingle(buffer, offset + 8);
            float w = BitConverter.ToSingle(buffer, offset + 12);
            var tangent = new Vector4(x, y, z, w);

            if (merge && i < output.Count)
            {
                var existing = output[i];
                existing.Tangent = tangent;
                output[i] = existing;
            }
            else
            {
                output.Add(new VertexProperties(Vector3.Zero, Vector2.Zero, tangent));
            }
        }
    }

    /// <summary>
    /// Reads index data (SCALAR, UNSIGNED_SHORT or UNSIGNED_INT) into a FastList of uint.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate with index values.</param>
    public void ReadIndices(int accessorIndex, FastList<uint> output)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            uint index = accessor.ComponentType switch
            {
                Accessor.ComponentTypeEnum.UNSIGNED_SHORT => BitConverter.ToUInt16(buffer, offset),
                Accessor.ComponentTypeEnum.UNSIGNED_INT => BitConverter.ToUInt32(buffer, offset),
                Accessor.ComponentTypeEnum.UNSIGNED_BYTE => buffer[offset],
                _ => throw new InvalidOperationException(
                    $"Unsupported index component type: {accessor.ComponentType}"
                ),
            };
            output.Add(index);
        }
    }

    /// <summary>
    /// Reads vertex color data (VEC3 or VEC4 FLOAT) into a FastList of Vector4.
    /// For VEC3 data, alpha is set to 1.0.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate with color values.</param>
    public void ReadColors(int accessorIndex, FastList<Vector4> output)
    {
        var accessor = _model.Accessors[accessorIndex];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            for (int i = 0; i < accessor.Count; i++)
            {
                output.Add(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            }
            return;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);
        bool isVec4 = accessor.Type == Accessor.TypeEnum.VEC4;

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float r = BitConverter.ToSingle(buffer, offset);
            float g = BitConverter.ToSingle(buffer, offset + 4);
            float b = BitConverter.ToSingle(buffer, offset + 8);
            float a = isVec4 ? BitConverter.ToSingle(buffer, offset + 12) : 1.0f;
            output.Add(new Vector4(r, g, b, a));
        }
    }
}
