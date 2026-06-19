using EvDraco = Evergine.Bindings.Draco.Draco;

namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Production <see cref="IDracoDecoder"/> implementation backed by the native Draco library
/// through the <c>Evergine.Bindings.Draco</c> package. Decodes a <c>KHR_draco_mesh_compression</c>
/// bitstream slice into a <see cref="DecodedMesh"/> by extracting each mapped attribute by its
/// Draco unique id and reading the decoded index data.
/// </summary>
/// <remarks>
/// All native handles (the decoded mesh and every attribute/index <c>Data</c> block) are released
/// in <c>finally</c> blocks so that neither a successful decode nor a failure leaks native memory.
/// Native exceptions thrown at the decode boundary are caught and converted into a
/// <see cref="DracoFailureReason.BitstreamDecodeFailed"/> outcome so that decode failures never
/// escape as exceptions.
/// </remarks>
internal sealed class DracoDecoder : IDracoDecoder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DracoDecoder"/> class and probes whether the
    /// native Draco library is loaded and usable, setting <see cref="IsAvailable"/> accordingly.
    /// </summary>
    public DracoDecoder()
    {
        IsAvailable = ProbeNativeAvailability();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public DracoDecodeOutcome Decode(
        ReadOnlySpan<byte> compressed,
        IReadOnlyDictionary<string, int> attributeMap
    )
    {
        ArgumentNullException.ThrowIfNull(attributeMap);

        if (compressed.IsEmpty)
        {
            return DracoDecodeOutcome.Failed(DracoFailureReason.BitstreamDecodeFailed);
        }

        EvDraco.Mesh mesh = default;
        var meshDecoded = false;
        try
        {
            mesh = Decompress(compressed);

            // A null mesh pointer indicates a corrupt or unsupported bitstream (Requirement 2.7).
            if (mesh.meshPtr == IntPtr.Zero)
            {
                return DracoDecodeOutcome.Failed(DracoFailureReason.BitstreamDecodeFailed);
            }

            meshDecoded = true;

            var vertexCount = checked((int)mesh.numVertices);
            var faceCount = checked((int)mesh.numFaces);

            // Extract each mapped attribute by its Draco unique id (Requirement 2.4).
            var attributes = new Dictionary<string, DecodedAttribute>(
                attributeMap.Count,
                StringComparer.Ordinal
            );

            foreach (var entry in attributeMap)
            {
                var attribute = mesh.GetAttributeByUniqueId((uint)entry.Value);

                // A mapped id absent from the bitstream is a hard failure naming the semantic
                // (Requirement 2.5). Check the native handle before touching any other member.
                if (
                    attribute.internalPtr == IntPtr.Zero
                    || attribute.attributeType == EvDraco.AttributeType.INVALID
                )
                {
                    return DracoDecodeOutcome.Failed(
                        DracoFailureReason.AttributeIdMissing,
                        entry.Key
                    );
                }

                var components = checked((int)attribute.numComponents);
                var values = ReadAttributeAsFloats(mesh, attribute, vertexCount, components);

                // Auxiliary attributes (extra UVs, JOINTS_0, WEIGHTS_0, ...) are decoded the same
                // way and retained without causing failure (Requirement 2.6).
                attributes[entry.Key] = new DecodedAttribute(values, components);
            }

            // Produce exactly one index array when faces exist; otherwise non-indexed
            // (Requirements 2.3, 3.5).
            uint[]? indices = faceCount > 0 ? ReadIndices(mesh, faceCount) : null;

            var decodedMesh = new DecodedMesh(vertexCount, attributes, indices);
            return DracoDecodeOutcome.Ok(decodedMesh);
        }
        catch (Exception)
        {
            // Any native or marshalling failure at the decode boundary is reported as a decode
            // failure rather than propagating (Requirement 2.7).
            return DracoDecodeOutcome.Failed(DracoFailureReason.BitstreamDecodeFailed);
        }
        finally
        {
            if (meshDecoded)
            {
                EvDraco.Release(mesh);
            }
        }
    }

    private static unsafe EvDraco.Mesh Decompress(ReadOnlySpan<byte> compressed)
    {
        fixed (byte* pCompressed = compressed)
        {
            return EvDraco.Decompress((IntPtr)pCompressed, (UIntPtr)(uint)compressed.Length);
        }
    }

    /// <summary>
    /// Reads a decoded attribute's per-vertex values into a flattened <see cref="float"/> array of
    /// length <c>vertexCount * components</c>, converting from the attribute's native data type.
    /// The number of scalars read is clamped to the size reported by the native data block so a
    /// malformed buffer can never cause an out-of-bounds read.
    /// </summary>
    private static unsafe float[] ReadAttributeAsFloats(
        EvDraco.Mesh mesh,
        EvDraco.Attribute attribute,
        int vertexCount,
        int components
    )
    {
        var expected = vertexCount * components;
        var values = new float[expected];

        if (expected == 0)
        {
            return values;
        }

        var data = EvDraco.GetData(mesh, attribute);
        try
        {
            if (data.data == IntPtr.Zero)
            {
                return values;
            }

            var count = SafeScalarCount(expected, (long)data.dataSize, data.dataType);
            ConvertToFloats((void*)data.data, data.dataType, values, count);
            return values;
        }
        finally
        {
            EvDraco.Release(data);
        }
    }

    /// <summary>
    /// Reads the decoded index data for a triangular mesh (<c>faceCount * 3</c> indices) into a
    /// <see cref="uint"/> array, converting from the native index data type. The number of indices
    /// read is clamped to the size reported by the native data block.
    /// </summary>
    private static unsafe uint[] ReadIndices(EvDraco.Mesh mesh, int faceCount)
    {
        var expected = faceCount * 3;
        var indices = new uint[expected];

        if (expected == 0)
        {
            return indices;
        }

        var data = mesh.GetIndices();
        try
        {
            if (data.data == IntPtr.Zero)
            {
                return indices;
            }

            var count = SafeScalarCount(expected, (long)data.dataSize, data.dataType);
            ConvertToUInts((void*)data.data, data.dataType, indices, count);
            return indices;
        }
        finally
        {
            EvDraco.Release(data);
        }
    }

    /// <summary>
    /// Computes how many scalars can be safely read from a native data block. The native
    /// <c>dataSize</c> is interpreted as either a byte count or an element count; whichever
    /// interpretation applies, this returns a count that never reads past the smaller of the two
    /// possible buffer sizes, capped at <paramref name="expected"/>.
    /// </summary>
    private static int SafeScalarCount(int expected, long dataSize, EvDraco.DataType dataType)
    {
        if (dataSize <= 0)
        {
            return 0;
        }

        // dataSize reported directly as an element count.
        if (dataSize >= expected)
        {
            return expected;
        }

        // Otherwise interpret dataSize as a byte count.
        var elementSize = Math.Max(1, (int)EvDraco.GetSize(dataType));
        var maxFromBytes = dataSize / elementSize;
        return (int)Math.Min(expected, maxFromBytes);
    }

    private static unsafe void ConvertToFloats(
        void* source,
        EvDraco.DataType dataType,
        float[] destination,
        int count
    )
    {
        switch (dataType)
        {
            case EvDraco.DataType.DT_FLOAT32:
            {
                var src = (float*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_FLOAT64:
            {
                var src = (double*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = (float)src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_INT8:
            {
                var src = (sbyte*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT8:
            case EvDraco.DataType.DT_BOOL:
            {
                var src = (byte*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_INT16:
            {
                var src = (short*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT16:
            {
                var src = (ushort*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_INT32:
            {
                var src = (int*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT32:
            {
                var src = (uint*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_INT64:
            {
                var src = (long*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT64:
            {
                var src = (ulong*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            default:
                // Unknown/invalid data type: leave the destination zero-initialized.
                break;
        }
    }

    private static unsafe void ConvertToUInts(
        void* source,
        EvDraco.DataType dataType,
        uint[] destination,
        int count
    )
    {
        switch (dataType)
        {
            case EvDraco.DataType.DT_UINT32:
            case EvDraco.DataType.DT_INT32:
            {
                var src = (uint*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT16:
            case EvDraco.DataType.DT_INT16:
            {
                var src = (ushort*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT8:
            case EvDraco.DataType.DT_INT8:
            {
                var src = (byte*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = src[i];
                }

                break;
            }
            case EvDraco.DataType.DT_UINT64:
            case EvDraco.DataType.DT_INT64:
            {
                var src = (ulong*)source;
                for (var i = 0; i < count; i++)
                {
                    destination[i] = (uint)src[i];
                }

                break;
            }
            default:
                // Unknown/invalid index type: leave the destination zero-initialized.
                break;
        }
    }

    /// <summary>
    /// Probes whether the native Draco library is loaded and callable by invoking a cheap native
    /// query. Returns <see langword="false"/> when the native library is missing or fails to
    /// initialize.
    /// </summary>
    private static bool ProbeNativeAvailability()
    {
        try
        {
            _ = EvDraco.GetSize(EvDraco.DataType.DT_FLOAT32);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
