using System.Numerics;
using System.Runtime.InteropServices;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal.Draco;
using HelixToolkit.Nex.Shaders;
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
    /// Reads texture coordinate data (VEC2 FLOAT or normalized UNSIGNED_BYTE/UNSIGNED_SHORT) into a FastList of VertexProperties.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate or merge tex coords into.</param>
    /// <param name="merge">If true, updates existing entries' TexCoord field; if false, adds new entries.</param>
    /// <param name="secondSet">If true, writes <see cref="VertexProperties.TexCoord1"/> (TEXCOORD_1) instead of TexCoord.</param>
    public void ReadTexCoords(
        int accessorIndex,
        FastList<VertexProperties> output,
        bool merge,
        bool secondSet = false
    )
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
        var componentType = accessor.ComponentType;
        int componentSize = GetComponentSize(componentType);
        bool normalized = accessor.Normalized;

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            // glTF permits FLOAT and normalized UNSIGNED_BYTE/UNSIGNED_SHORT texture coordinates.
            float u = ReadComponent(buffer, offset, componentType, normalized);
            float v = ReadComponent(buffer, offset + componentSize, componentType, normalized);
            var texCoord = new Vector2(u, v);

            if (merge && i < output.Count)
            {
                var existing = output[i];
                if (secondSet)
                {
                    existing.TexCoord1 = texCoord;
                }
                else
                {
                    existing.TexCoord = texCoord;
                }
                output[i] = existing;
            }
            else
            {
                var added = new VertexProperties(Vector3.Zero, secondSet ? Vector2.Zero : texCoord);
                if (secondSet)
                {
                    added.TexCoord1 = texCoord;
                }
                output.Add(added);
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
    /// Reads skinning attributes (<c>JOINTS_0</c> and <c>WEIGHTS_0</c>, both VEC4) into joint influences.
    /// Joints may be UNSIGNED_BYTE or UNSIGNED_SHORT; weights may be FLOAT or normalized
    /// UNSIGNED_BYTE / UNSIGNED_SHORT. Weights are renormalized to sum to one.
    /// </summary>
    /// <param name="jointsAccessorIndex">The index of the <c>JOINTS_0</c> accessor.</param>
    /// <param name="weightsAccessorIndex">The index of the <c>WEIGHTS_0</c> accessor.</param>
    /// <param name="output">The output list to populate with joint influences.</param>
    /// <param name="error">A description of the problem when the attributes cannot be read.</param>
    /// <returns><see langword="true"/> if the influences were read; otherwise <see langword="false"/>.</returns>
    public bool ReadJointInfluences(
        int jointsAccessorIndex,
        int weightsAccessorIndex,
        FastList<GpuJointInfluence> output,
        out string? error
    )
    {
        var joints = _model.Accessors[jointsAccessorIndex];
        var weights = _model.Accessors[weightsAccessorIndex];
        if (joints.Type != Accessor.TypeEnum.VEC4 || weights.Type != Accessor.TypeEnum.VEC4)
        {
            error = "JOINTS_0 and WEIGHTS_0 must be VEC4.";
            return false;
        }
        if (joints.Count != weights.Count)
        {
            error = $"JOINTS_0 count {joints.Count} does not match WEIGHTS_0 count {weights.Count}.";
            return false;
        }
        if (
            joints.ComponentType
            is not (Accessor.ComponentTypeEnum.UNSIGNED_BYTE or Accessor.ComponentTypeEnum.UNSIGNED_SHORT)
        )
        {
            error = $"Unsupported JOINTS_0 component type {joints.ComponentType}.";
            return false;
        }
        if (
            weights.ComponentType
            is not (
                Accessor.ComponentTypeEnum.FLOAT
                or Accessor.ComponentTypeEnum.UNSIGNED_BYTE
                or Accessor.ComponentTypeEnum.UNSIGNED_SHORT
            )
        )
        {
            error = $"Unsupported WEIGHTS_0 component type {weights.ComponentType}.";
            return false;
        }

        var jointBuffer = GetBuffer(jointsAccessorIndex);
        var weightBuffer = GetBuffer(weightsAccessorIndex);
        int jointOffset = GetByteOffset(jointsAccessorIndex);
        int jointStride = GetStride(jointsAccessorIndex);
        int jointSize = GetComponentSize(joints.ComponentType);
        int weightOffset = GetByteOffset(weightsAccessorIndex);
        int weightStride = GetStride(weightsAccessorIndex);
        int weightSize = GetComponentSize(weights.ComponentType);

        Span<uint> j = stackalloc uint[4];
        Span<float> w = stackalloc float[4];
        for (int i = 0; i < joints.Count; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                j[c] =
                    jointBuffer == null ? 0u
                    : joints.ComponentType == Accessor.ComponentTypeEnum.UNSIGNED_BYTE
                        ? jointBuffer[jointOffset + i * jointStride + c * jointSize]
                    : BitConverter.ToUInt16(jointBuffer, jointOffset + i * jointStride + c * jointSize);

                int offset = weightOffset + i * weightStride + c * weightSize;
                w[c] =
                    weightBuffer == null ? 0f
                    : weights.ComponentType switch
                    {
                        Accessor.ComponentTypeEnum.FLOAT => BitConverter.ToSingle(weightBuffer, offset),
                        Accessor.ComponentTypeEnum.UNSIGNED_BYTE => weightBuffer[offset] / 255f,
                        _ => BitConverter.ToUInt16(weightBuffer, offset) / 65535f,
                    };
            }

            var weight = new Vector4(w[0], w[1], w[2], w[3]);
            float sum = weight.X + weight.Y + weight.Z + weight.W;
            // A vertex without weights is fully bound to its first joint.
            weight = sum > 0f ? weight / sum : Vector4.UnitX;
            output.Add(
                new GpuJointInfluence
                {
                    Joint0 = j[0],
                    Joint1 = j[1],
                    Joint2 = j[2],
                    Joint3 = j[3],
                    Weights = weight,
                }
            );
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Reads matrix data (MAT4 FLOAT, column-major per glTF) into a FastList of row-major
    /// <see cref="Matrix4x4"/>, matching the engine's row-vector convention.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The output list to populate with matrices.</param>
    /// <returns><see langword="true"/> if the matrices were read; otherwise <see langword="false"/>.</returns>
    public bool ReadMatrices(int accessorIndex, FastList<Matrix4x4> output)
    {
        var accessor = _model.Accessors[accessorIndex];
        if (
            accessor.Type != Accessor.TypeEnum.MAT4
            || accessor.ComponentType != Accessor.ComponentTypeEnum.FLOAT
        )
        {
            return false;
        }
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            for (int i = 0; i < accessor.Count; i++)
            {
                output.Add(Matrix4x4.Identity);
            }
            return true;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);
        for (int i = 0; i < accessor.Count; i++)
        {
            var m = MemoryMarshal.Read<Matrix4x4>(buffer.AsSpan(byteOffset + i * stride, 64));
            output.Add(m);
        }
        return true;
    }

    /// <summary>
    /// Reads animation keyframe times (SCALAR FLOAT).
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The keyframe times.</param>
    /// <param name="error">A description of the problem when the data cannot be read.</param>
    /// <returns><see langword="true"/> if the times were read; otherwise <see langword="false"/>.</returns>
    public bool ReadKeyframeTimes(int accessorIndex, out float[] output, out string? error)
    {
        output = [];
        var accessor = _model.Accessors[accessorIndex];
        if (
            accessor.Type != Accessor.TypeEnum.SCALAR
            || accessor.ComponentType != Accessor.ComponentTypeEnum.FLOAT
        )
        {
            error = $"Keyframe times must be SCALAR FLOAT, got {accessor.Type} {accessor.ComponentType}.";
            return false;
        }
        output = new float[accessor.Count];
        var buffer = GetBuffer(accessorIndex);
        if (buffer != null)
        {
            int byteOffset = GetByteOffset(accessorIndex);
            int stride = GetStride(accessorIndex);
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = BitConverter.ToSingle(buffer, byteOffset + i * stride);
            }
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Reads animation output values (VEC3 or VEC4) into <see cref="Vector4"/>s. Components may be
    /// FLOAT or normalized integers (as allowed for rotations). Missing components are zero.
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="type">The expected accessor type.</param>
    /// <param name="output">The values.</param>
    /// <param name="error">A description of the problem when the data cannot be read.</param>
    /// <returns><see langword="true"/> if the values were read; otherwise <see langword="false"/>.</returns>
    public bool ReadKeyframeValues(
        int accessorIndex,
        Accessor.TypeEnum type,
        out Vector4[] output,
        out string? error
    )
    {
        output = [];
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.Type != type)
        {
            error = $"Keyframe values must be {type}, got {accessor.Type}.";
            return false;
        }
        if (accessor.ComponentType != Accessor.ComponentTypeEnum.FLOAT && !accessor.Normalized)
        {
            error = $"Keyframe values must be FLOAT or normalized integers, got {accessor.ComponentType}.";
            return false;
        }
        output = new Vector4[accessor.Count];
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            error = null;
            return true;
        }

        int byteOffset = GetByteOffset(accessorIndex);
        int stride = GetStride(accessorIndex);
        int componentSize = GetComponentSize(accessor.ComponentType);
        int componentCount = GetTypeCount(type);
        Span<float> v = stackalloc float[4];
        for (int i = 0; i < output.Length; i++)
        {
            v.Clear();
            for (int c = 0; c < componentCount; c++)
            {
                int offset = byteOffset + i * stride + c * componentSize;
                v[c] = ReadNormalizedComponent(buffer, offset, accessor.ComponentType);
            }
            output[i] = new Vector4(v[0], v[1], v[2], v[3]);
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Reads VEC3 data into <see cref="Vector3"/>s, as used by morph target deltas. Components may be
    /// FLOAT, or normalized or plain integers (<c>KHR_mesh_quantization</c>). Sparse accessors are
    /// supported: the sparse values replace the base values (zeros when there is no buffer view).
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The list the values are appended to.</param>
    /// <param name="error">A description of the problem when the data cannot be read.</param>
    /// <returns><see langword="true"/> if the values were read; otherwise <see langword="false"/>.</returns>
    public bool ReadVector3s(int accessorIndex, FastList<Vector3> output, out string? error)
    {
        if (!ValidateAccessor(accessorIndex, out error))
        {
            return false;
        }
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.VEC3)
        {
            error = $"Accessor {accessorIndex} must be VEC3, got {accessor.Type}.";
            return false;
        }
        var componentType = accessor.ComponentType;
        int componentSize = GetComponentSize(componentType);
        int start = output.Count;
        var buffer = GetBuffer(accessorIndex);
        if (buffer == null)
        {
            for (int i = 0; i < accessor.Count; i++)
            {
                output.Add(Vector3.Zero);
            }
        }
        else
        {
            int byteOffset = GetByteOffset(accessorIndex);
            int stride = GetStride(accessorIndex);
            for (int i = 0; i < accessor.Count; i++)
            {
                output.Add(ReadVector3(buffer, byteOffset + i * stride, componentType, componentSize, accessor.Normalized));
            }
        }

        if (accessor.Sparse is { Count: > 0 })
        {
            if (!TryGetSparse(accessorIndex, accessor, componentSize * 3, out var sparse, out error))
            {
                return false;
            }
            for (int i = 0; i < sparse.Count; i++)
            {
                output[start + sparse.GetIndex(i)] = ReadVector3(
                    sparse.Values,
                    sparse.GetValueOffset(i),
                    componentType,
                    componentSize,
                    accessor.Normalized
                );
            }
        }
        return true;
    }

    /// <summary>
    /// Reads SCALAR data (FLOAT or normalized integers) into floats, as used by morph target weight
    /// animation outputs. Sparse accessors are supported: the sparse values replace the base values
    /// (zeros when there is no buffer view).
    /// </summary>
    /// <param name="accessorIndex">The index of the accessor in the glTF model.</param>
    /// <param name="output">The values.</param>
    /// <param name="error">A description of the problem when the data cannot be read.</param>
    /// <returns><see langword="true"/> if the values were read; otherwise <see langword="false"/>.</returns>
    public bool ReadScalars(int accessorIndex, out float[] output, out string? error)
    {
        output = [];
        if (!ValidateAccessor(accessorIndex, out error))
        {
            return false;
        }
        var accessor = _model.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.SCALAR)
        {
            error = $"Accessor {accessorIndex} must be SCALAR, got {accessor.Type}.";
            return false;
        }
        var componentType = accessor.ComponentType;
        if (componentType != Accessor.ComponentTypeEnum.FLOAT && !accessor.Normalized)
        {
            error = $"Accessor {accessorIndex} must be FLOAT or normalized integers, got {componentType}.";
            return false;
        }
        output = new float[accessor.Count];
        var buffer = GetBuffer(accessorIndex);
        if (buffer != null)
        {
            int byteOffset = GetByteOffset(accessorIndex);
            int stride = GetStride(accessorIndex);
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = ReadNormalizedComponent(buffer, byteOffset + i * stride, componentType);
            }
        }
        if (accessor.Sparse is { Count: > 0 })
        {
            if (!TryGetSparse(accessorIndex, accessor, GetComponentSize(componentType), out var sparse, out error))
            {
                output = [];
                return false;
            }
            for (int i = 0; i < sparse.Count; i++)
            {
                output[sparse.GetIndex(i)] = ReadNormalizedComponent(
                    sparse.Values,
                    sparse.GetValueOffset(i),
                    componentType
                );
            }
        }
        return true;
    }

    /// <summary>
    /// The resolved sparse substitution of an accessor: element indices and tightly packed values.
    /// </summary>
    private readonly record struct SparseData(
        int Count,
        byte[] Indices,
        int IndexOffset,
        int IndexSize,
        byte[] Values,
        int ValueOffset,
        int ElementSize
    )
    {
        public int GetIndex(int i)
        {
            int offset = IndexOffset + i * IndexSize;
            return IndexSize switch
            {
                1 => Indices[offset],
                2 => BitConverter.ToUInt16(Indices, offset),
                _ => (int)BitConverter.ToUInt32(Indices, offset),
            };
        }

        public int GetValueOffset(int i)
        {
            return ValueOffset + i * ElementSize;
        }
    }

    /// <summary>
    /// Resolves the sparse indices and values of an accessor, validating them against their buffer
    /// views (not only the backing buffers) and every index against the accessor count.
    /// </summary>
    private bool TryGetSparse(
        int accessorIndex,
        Accessor accessor,
        int elementSize,
        out SparseData sparse,
        out string? error
    )
    {
        sparse = default;
        error = null;
        var info = accessor.Sparse;
        int indexSize = info.Indices.ComponentType switch
        {
            AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_BYTE => 1,
            AccessorSparseIndices.ComponentTypeEnum.UNSIGNED_SHORT => 2,
            _ => 4,
        };
        if (
            !TryGetBufferViewRange(
                info.Indices.BufferView,
                info.Indices.ByteOffset,
                (long)info.Count * indexSize,
                out var indexBuffer,
                out var indexOffset
            )
            || !TryGetBufferViewRange(
                info.Values.BufferView,
                info.Values.ByteOffset,
                (long)info.Count * elementSize,
                out var valueBuffer,
                out var valueOffset
            )
        )
        {
            error = $"Sparse accessor {accessorIndex} references an invalid buffer view or exceeds its bounds.";
            return false;
        }
        sparse = new SparseData(
            info.Count,
            indexBuffer,
            indexOffset,
            indexSize,
            valueBuffer,
            valueOffset,
            elementSize
        );
        for (int i = 0; i < info.Count; i++)
        {
            long index = indexSize == 4
                ? BitConverter.ToUInt32(indexBuffer, indexOffset + i * indexSize)
                : sparse.GetIndex(i);
            if (index >= accessor.Count)
            {
                error = $"Sparse accessor {accessorIndex} index {index} is out of range.";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="byteLength"/> bytes at <paramref name="byteOffset"/> within a buffer
    /// view, failing when the range does not fit in the view or the view does not fit in its buffer.
    /// </summary>
    private bool TryGetBufferViewRange(
        int bufferViewIndex,
        int byteOffset,
        long byteLength,
        out byte[] buffer,
        out int bufferOffset
    )
    {
        buffer = [];
        bufferOffset = 0;
        var bufferViews = _model.BufferViews;
        if (bufferViews is null || bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Length)
        {
            return false;
        }
        var bufferView = bufferViews[bufferViewIndex];
        if (bufferView.Buffer < 0 || bufferView.Buffer >= _bufferData.Length)
        {
            return false;
        }
        var data = _bufferData[bufferView.Buffer];
        if (
            byteOffset < 0
            || byteOffset + byteLength > bufferView.ByteLength
            || (long)bufferView.ByteOffset + bufferView.ByteLength > data.Length
        )
        {
            return false;
        }
        buffer = data;
        bufferOffset = bufferView.ByteOffset + byteOffset;
        return true;
    }

    private static Vector3 ReadVector3(
        byte[] buffer,
        int offset,
        Accessor.ComponentTypeEnum componentType,
        int componentSize,
        bool normalized
    )
    {
        return new Vector3(
            ReadComponent(buffer, offset, componentType, normalized),
            ReadComponent(buffer, offset + componentSize, componentType, normalized),
            ReadComponent(buffer, offset + 2 * componentSize, componentType, normalized)
        );
    }

    /// <summary>
    /// Reads one component as a float: FLOAT as is, normalized integers as normalized values and
    /// other integers as their plain value.
    /// </summary>
    private static float ReadComponent(
        byte[] buffer,
        int offset,
        Accessor.ComponentTypeEnum componentType,
        bool normalized
    )
    {
        if (normalized || componentType == Accessor.ComponentTypeEnum.FLOAT)
        {
            return ReadNormalizedComponent(buffer, offset, componentType);
        }
        return componentType switch
        {
            Accessor.ComponentTypeEnum.BYTE => (sbyte)buffer[offset],
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => buffer[offset],
            Accessor.ComponentTypeEnum.SHORT => BitConverter.ToInt16(buffer, offset),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => BitConverter.ToUInt16(buffer, offset),
            _ => BitConverter.ToUInt32(buffer, offset),
        };
    }

    /// <summary>
    /// Reads one component as a float: FLOAT as is, integer types as normalized values
    /// (signed types clamped to -1, per the glTF normalization rules).
    /// </summary>
    private static float ReadNormalizedComponent(
        byte[] buffer,
        int offset,
        Accessor.ComponentTypeEnum componentType
    )
    {
        return componentType switch
        {
            Accessor.ComponentTypeEnum.FLOAT => BitConverter.ToSingle(buffer, offset),
            Accessor.ComponentTypeEnum.BYTE => MathF.Max((sbyte)buffer[offset] / 127f, -1f),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => buffer[offset] / 255f,
            Accessor.ComponentTypeEnum.SHORT => MathF.Max(
                BitConverter.ToInt16(buffer, offset) / 32767f,
                -1f
            ),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => BitConverter.ToUInt16(buffer, offset)
                / 65535f,
            _ => BitConverter.ToUInt32(buffer, offset) / (float)uint.MaxValue,
        };
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
    /// Reads vertex color data (VEC3 or VEC4; FLOAT, or normalized UNSIGNED_BYTE or UNSIGNED_SHORT)
    /// into a FastList of Vector4. For VEC3 data, alpha is set to 1.0.
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
        var componentType = accessor.ComponentType;
        int componentSize = GetComponentSize(componentType);

        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = byteOffset + i * stride;
            float r = ReadNormalizedComponent(buffer, offset, componentType);
            float g = ReadNormalizedComponent(buffer, offset + componentSize, componentType);
            float b = ReadNormalizedComponent(buffer, offset + 2 * componentSize, componentType);
            float a = isVec4
                ? ReadNormalizedComponent(buffer, offset + 3 * componentSize, componentType)
                : 1.0f;
            output.Add(new Vector4(r, g, b, a));
        }
    }
}
